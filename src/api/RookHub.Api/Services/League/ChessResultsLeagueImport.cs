using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// EINE chess-results-Liga einer Saison über den Crawler einspielen (0.720.0, Wunsch 2026-10-08 „ergänz LeagueHub in Österreich um
/// die höheren Ligen (Bundesliga 1 &amp; 2)") — spiegelbildlich zum Ligamanager-Import, aber ohne eigenen Leser: der Crawler liefert
/// dieselben vier Seiten wie beim „Daten aktualisieren" (<c>GET {Crawler}/api/league/{tnr}</c>: Paarungen, Brettpaarungen, Meldeliste,
/// Statistik). Die Turnier-Zeile (Saison, Stufe, Liga, Gruppe) kommt aus der Anfrage — chess-results nennt sie nicht maschinenlesbar.
/// Ersetzt wird über <see cref="LeagueRefresh.ReplaceAsync(AppDbContext, LeagueRefresh.Pages, DateTime, CancellationToken)"/> (stabile
/// Brettpaarungs-Ids, <see cref="LeagueGameLinks.RelinkAsync"/>) in EINER Transaktion mit der Turnier-Zeile. Damit lassen sich
/// laufende Saison UND Vorsaisonen (Merkmale der Prognose) ohne Python-Bündel einspielen; danach holt „Daten aktualisieren" die Ligen
/// der laufenden Saison wie jede Tiroler Liga.
/// </summary>
public sealed partial class ChessResultsLeagueImport
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Anfrage: chess-results-Nummer, Saison „2026/27", Tiroler Stufe (<see cref="LeagueLevels"/>: 1 = 1. Bundesliga …
    /// 6 = Gebietsklasse), Liga („1. Bundesliga"), Gruppe („West", leer) und Phase („Liga" | „Playoff"); <c>Name</c> leer =
    /// „{Liga} {Gruppe} {Saison}".</summary>
    public sealed record Request(int Tnr, string Season, int Level, string League, string? Grp = null, string? Stage = null, string? Name = null);

    /// <summary>Was die Seiten hergeben — im Probelauf wie im Import.</summary>
    public sealed record Counts(int Rounds, int RoundsPlayed, int Matches, int Teams, int BoardGames, int BoardGamesPlayed,
        int BoardPlayersUnmatched, int Boards, int Players, int PlayersWithFide, int FideNotInLeagues, int FideWithoutCard,
        string? FirstRound, string? LastRound, int RoundBlocks);

    public sealed record ImportResult(int Tnr, string Name, string Season, int Level, bool DryRun, Counts Counts, int? Views);

    /// <summary>Anfrage unvollständig/unzulässig → 400 <c>invalidLeague</c>.</summary>
    public sealed class InvalidException(string message) : Exception(message);
    /// <summary>Der Crawler liefert für die Nummer nichts (vier leere Seiten) → 404 <c>notFound</c>.</summary>
    public sealed class NotFoundException(string message) : Exception(message);
    /// <summary>Unter der Nummer steht eine Liga FREMDER Quelle (Ligamanager/Zugspitze) → 409 <c>conflict</c>.</summary>
    public sealed class ConflictException(string message) : Exception(message);
    /// <summary>Eine leere Seite bei vorhandenem Bestand (Drosselseite) — nichts ersetzt → 502 <c>incomplete</c>.</summary>
    public sealed class IncompleteException(string message) : Exception(message);

    private readonly AppDbContext _db;
    private readonly LeagueService _league;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<ChessResultsLeagueImport> _log;
    private readonly Func<DateTime> _now;

    public ChessResultsLeagueImport(AppDbContext db, LeagueService league, IHttpClientFactory http, ILogger<ChessResultsLeagueImport> log,
        Func<DateTime>? now = null)
    {
        _db = db; _league = league; _http = http; _log = log; _now = now ?? (() => DateTime.UtcNow);
    }

    [GeneratedRegex(@"^(\d{4})/(\d{2})$")]
    private static partial Regex SeasonRe();

    /// <summary>Prüft die Anfrage und baut die Turnier-Zeile (ohne Termine).</summary>
    public static LeagueTournament Validate(Request r)
    {
        // chess-results ist 7-stellig; ab 900 000 000 liegen Ligamanager und Schachkreis Zugspitze.
        if (r.Tnr <= 0 || r.Tnr >= LigamanagerSource.TnrOffset) throw new InvalidException($"Turniernummer {r.Tnr} ist keine chess-results-Nummer");
        var m = SeasonRe().Match(r.Season?.Trim() ?? "");
        if (!m.Success || (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + 1) % 100 != int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))
            throw new InvalidException($"Saison „{r.Season}“ — erwartet „2026/27“");
        if (r.Level < LeagueLevels.Bundesliga || r.Level > LeagueLevels.TirolGebietsklasse)
            throw new InvalidException($"Stufe {r.Level} — Tirol/Österreich kennt 1 (1. Bundesliga) … 6 (Gebietsklasse)");
        var league = LeagueNames.Clean(r.League);
        if (league.Length is 0 or > 100) throw new InvalidException("Liga fehlt");
        var grp = LeagueNames.Clean(r.Grp);
        if (grp.Length > 50) throw new InvalidException("Gruppe zu lang");
        var stage = string.IsNullOrWhiteSpace(r.Stage) ? "Liga" : r.Stage.Trim();
        if (stage is not ("Liga" or "Playoff")) throw new InvalidException($"Phase „{r.Stage}“ — „Liga“ oder „Playoff“");
        var season = m.Value;
        var name = LeagueNames.Clean(r.Name);
        if (name.Length == 0) name = $"{league}{(grp.Length > 0 ? " " + grp : "")} {season}";
        if (name.Length > 200) throw new InvalidException("Name zu lang");
        return new LeagueTournament { Tnr = r.Tnr, Name = name, Season = season, Level = r.Level, League = league, Grp = grp, Stage = stage };
    }

    /// <summary>Holt die Liga beim Crawler und spielt sie — ohne <paramref name="dryRun"/> — ein; danach die Ansichten der laufenden
    /// Saison (<paramref name="rebuildViews"/>).</summary>
    public async Task<ImportResult> ImportAsync(Request req, bool dryRun, CancellationToken ct, bool rebuildViews = true)
    {
        var t = Validate(req);
        var existing = await _db.LeagueTournaments.AsNoTracking().FirstOrDefaultAsync(x => x.Tnr == t.Tnr, ct);
        if (existing is not null && existing.Source is not null)
            throw new ConflictException($"Nummer {t.Tnr} gehört schon der Liga „{existing.Name}“ ({existing.Source})");

        var client = _http.CreateClient(LeagueRefresh.CrawlerClient);
        var pages = await client.GetFromJsonAsync<LeagueRefresh.Pages>($"api/league/{t.Tnr}", Web, ct)
                    ?? throw new NotFoundException($"Crawler lieferte nichts für {t.Tnr}");
        if (pages.Tnr != t.Tnr) throw new InvalidOperationException($"Crawler lieferte Liga {pages.Tnr} statt {t.Tnr}");
        if (pages.Matches.Count == 0 && pages.Games.Count == 0 && pages.Roster.Count == 0)
            throw new NotFoundException($"chess-results zeigt unter {t.Tnr} keine Mannschaftsmeisterschaft (vier leere Seiten)");

        var dates = pages.RoundDates.OrderBy(kv => kv.Key).Select(kv => (kv.Key, Date: LeagueDates.Parse(kv.Value))).ToList();
        t.Rounds = dates.Count > 0 ? dates.Max(d => d.Key) : pages.Matches.Select(x => x.Round).DefaultIfEmpty().Max();
        t.Start = dates.Select(d => d.Date).FirstOrDefault(d => d is not null)?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        t.End = dates.Select(d => d.Date).LastOrDefault(d => d is not null)?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var counts = await CountAsync(t, pages, dates, ct);
        if (dryRun) return new ImportResult(t.Tnr, t.Name, t.Season, t.Level, true, counts, null);

        try
        {
            await InTransactionAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                var row = await _db.LeagueTournaments.FirstOrDefaultAsync(x => x.Tnr == t.Tnr, ct);
                if (row is null) _db.LeagueTournaments.Add(row = new LeagueTournament { Tnr = t.Tnr });
                row.Name = t.Name; row.Season = t.Season; row.Level = t.Level; row.League = t.League; row.Grp = t.Grp; row.Stage = t.Stage;
                row.Start = t.Start; row.End = t.End; row.Rounds = t.Rounds; row.Source = null; row.SourceRef = null; row.UpdatedAt = _now();
                await _db.SaveChangesAsync(ct);   // die Turnier-Zeile vor dem Ersetzen (ReplaceAsync setzt UpdatedAt über FindAsync)
                await LeagueRefresh.ReplaceAsync(_db, pages, _now(), ct);
            }, ct);
        }
        catch (InvalidOperationException e) when (e.Message.Contains("leere Seite", StringComparison.Ordinal))
        {
            throw new IncompleteException(e.Message);
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
        int? views = rebuildViews ? await _league.RebuildViewsAsync(ct) : null;
        _log.LogInformation("LeagueHub: chess-results-Liga {Tnr} ({Name}, Stufe {Level}) eingespielt — {Matches} Begegnungen, {Boards} Bretter, {Players} Gemeldete",
            t.Tnr, t.Name, t.Level, counts.Matches, counts.BoardGames, counts.Players);
        return new ImportResult(t.Tnr, t.Name, t.Season, t.Level, false, counts, views);
    }

    private async Task<Counts> CountAsync(LeagueTournament t, LeagueRefresh.Pages p, List<(int Round, DateOnly? Date)> dates, CancellationToken ct)
    {
        static bool Seat(string? n) => !string.IsNullOrWhiteSpace(n) && n is not ("Brett nicht besetzt" or "spielfrei");
        var roster = p.Roster.Select(r => (r.Team, Key: LeagueNames.NameKey(r.Name))).ToHashSet();
        var played = p.Games.Where(g => g.HomeScore is not null && g.Forfeit < 2).ToList();
        var unmatched = played.Sum(g => (Seat(g.HomePlayer) && !roster.Contains((g.HomeTeam, LeagueNames.NameKey(g.HomePlayer))) ? 1 : 0)
                                        + (Seat(g.AwayPlayer) && !roster.Contains((g.AwayTeam, LeagueNames.NameKey(g.AwayPlayer))) ? 1 : 0));
        var fides = p.Roster.Where(r => !string.IsNullOrEmpty(r.FideId)).Select(r => r.FideId!).Distinct().ToList();
        var known = new HashSet<string>(StringComparer.Ordinal);
        var cards = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in fides.Chunk(500))
        {
            known.UnionWith(await _db.LeaguePlayers.AsNoTracking().Where(x => x.Tnr != t.Tnr && x.FideId != null && chunk.Contains(x.FideId))
                .Select(x => x.FideId!).Distinct().ToListAsync(ct));
            cards.UnionWith(await _db.LeaguePlayerProfiles.AsNoTracking().Where(x => chunk.Contains(x.FideId)).Select(x => x.FideId).ToListAsync(ct));
        }
        var blocks = 0;
        for (var i = 0; i < dates.Count; i++)
            if (i == 0 || !LeagueLevels.Consecutive(null, t.Level, dates[i - 1].Date, dates[i].Date)) blocks++;
        var teams = p.Matches.SelectMany(m => new[] { m.Home, m.Away }).Where(x => x != "spielfrei").Distinct().Count();
        return new Counts(
            Rounds: t.Rounds ?? 0,
            RoundsPlayed: played.Select(g => g.Round).Distinct().Count(),
            Matches: p.Matches.Count(m => m.Away != "spielfrei"),
            Teams: teams,
            BoardGames: p.Games.Count,
            BoardGamesPlayed: played.Count,
            BoardPlayersUnmatched: unmatched,
            Boards: p.Games.Select(g => g.Board).DefaultIfEmpty().Max(),
            Players: p.Roster.Count,
            PlayersWithFide: p.Roster.Count(r => !string.IsNullOrEmpty(r.FideId)),
            FideNotInLeagues: fides.Count(f => !known.Contains(f)),
            FideWithoutCard: fides.Count(f => !cards.Contains(f)),
            FirstRound: t.Start, LastRound: t.End,
            RoundBlocks: blocks);
    }

    /// <summary>Selbst geöffnete Transaktion IN der Execution-Strategy (CLAUDE.md); InMemory kennt keine Transaktionen.</summary>
    private async Task InTransactionAsync(Func<Task> work, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            await work();
            await _db.SaveChangesAsync(ct);
            return;
        }
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await work();
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }
}
