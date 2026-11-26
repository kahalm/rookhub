using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using static RookHub.Api.Services.League.ClubPgnText;
using static RookHub.Api.Services.League.ClubSideResolver;

namespace RookHub.Api.Services.League;

/// <summary>Eine fertige Partie einer Lichess-Übertragung, so wie <see cref="LeagueBroadcastImport.CleanGames"/> sie liefert:
/// die Kopfzeilen der Übertragung (mit <c>WhiteTeam</c>/<c>BlackTeam</c> und <c>GameURL</c>) und die Hauptvariante als SAN.</summary>
public sealed record LeagueBroadcastGame(IReadOnlyDictionary<string, string> Headers, IReadOnlyList<string> Sans)
{
    public string? H(string key) => Headers.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) && v.Trim() != "?" ? v.Trim() : null;
    public int? Elo(string key) => int.TryParse(H(key), out var e) ? e : null;

    /// <summary>Der Signal-Königszug, der am Ende weggelassen wurde (1.022.0); <c>null</c> = keiner.</summary>
    public Games.SignalKingMove.Finding? DroppedSignal { get; init; }
}

/// <summary>
/// Übertragene Ligapartien der EIGENEN Mannschaft in die Vereins-Datenbank (1.017.0, Wunsch 2026-11-25: „live übertragene
/// Ligapartien sofort einspielen“). <see cref="LeagueBroadcastImport"/> reicht jede fertige Partie einer Übertragung herein;
/// übernommen wird sie, wenn ALLES gilt:
/// <list type="bullet">
/// <item>Eine Seite spielt für eine Mannschaft des Vereins: <c>WhiteTeam</c>/<c>BlackTeam</c> über
/// <see cref="LeagueClub.OwnsTeam"/> — Vorsilbe („Schwaz“, „Schwaz 1“, „SK Schwaz“) und in Tirol der kanonische Verein
/// (<see cref="LeagueNames.Club"/>: die Bundesliga schreibt „Schachklub Schwaz“). Ohne Mannschaftsangabe (Einzelturnier) nie.</item>
/// <item>Sie hat ein Ergebnis, einen vollen Spieltag in der laufenden Saison (ab <see cref="SeasonStart"/>, 1. Juli) und eine
/// <c>GameURL</c>.</item>
/// <item>Sie steht noch nicht da: weder mit derselben <c>GameURL</c> (<see cref="LeagueClubGame.SourceUrl"/>, auch archiviert —
/// so legt auch das zehnte Einlesen nichts doppelt an) noch als hochgeladene Fassung (gleiche Züge im gleichen Jahr wie beim
/// Import, oder dieselben Spieler mit demselben Anfang, <see cref="ClubSideResolver.SameVersion"/>).</item>
/// </list>
/// <para><b>Echte Namen</b>: die Partie ist öffentlich übertragen — deshalb wird HIER niemand durch „Schwaz“ ersetzt; die
/// eigenen Spieler stehen wie Gegner mit Namen (aus der Meldeliste, wenn sie dort stehen), FIDE-ID und Elo da, mit Veranstaltung,
/// Runde und Tag der Übertragung. Herkunft <see cref="LeagueBroadcastImport.Source"/>. Die Ersetzungsregel der Uploads bleibt
/// unberührt.</para>
/// <para>Danach derselbe Weg wie jede Vereinspartie: Ligapaarung (<see cref="ClubGameWriter.ApplyPairingsAsync"/>), Speichern mit
/// Spielerkarten (<see cref="ClubGameWriter.SaveAsync"/>) — die Stockfish-Analyse legt der Meisterpartien-Takt an, die zweite auf
/// Lc0 der <c>ClubSecondEngineScheduler</c> —, und für jede Partie genau EINE Zusammenfassung
/// (<see cref="ClubGameAutoSummary.RequestBroadcastAsync"/>).</para>
/// </summary>
internal sealed class ClubBroadcastGames
{
    private readonly AppDbContext _db;
    private readonly ILogger _log;
    private readonly Func<DateTime> _now;
    private readonly ClubGameReader _reader;
    private readonly ClubGameWriter _writer;

    public ClubBroadcastGames(AppDbContext db, ILogger log, Func<DateTime> now, ClubGameReader reader, ClubGameWriter writer)
    {
        _db = db; _log = log; _now = now; _reader = reader; _writer = writer;
    }

    /// <summary>Beginn der Saison, in der <paramref name="now"/> liegt (1. Juli; Saison 2026/27 = ab 01.07.2026).</summary>
    public static DateOnly SeasonStart(DateTime now) => new(now.Month >= 7 ? now.Year : now.Year - 1, 7, 1);

    /// <summary>Spielt eine Seite der Partie für eine Mannschaft dieses Vereins?</summary>
    public static bool Involves(LeagueClub club, LeagueBroadcastGame g) => club.OwnsTeam(g.H("WhiteTeam")) || club.OwnsTeam(g.H("BlackTeam"));

    /// <summary>Die übernehmbaren Partien in die Vereins-Datenbank. → die neu angelegten.</summary>
    public async Task<List<LeagueClubGame>> ImportAsync(LeagueClub club, IReadOnlyList<LeagueBroadcastGame> games, CancellationToken ct)
    {
        var now = _now();
        var since = SeasonStart(now);
        var mine = games.Where(g => Involves(club, g) && g.Sans.Count > 0 && g.H("GameURL") is { Length: <= 300 }
                && g.H("Result") is "1-0" or "0-1" or "1/2-1/2" && FullDateOf(g.H("Date")) is { } d && d >= since)
            .DistinctBy(g => g.H("GameURL")).ToList();
        if (mine.Count == 0) return new();

        var urls = mine.Select(g => g.H("GameURL")!).ToList();
        var known = (await _db.LeagueClubGames.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.ClubId == club.Id && c.SourceUrl != null && urls.Contains(c.SourceUrl)).Select(c => c.SourceUrl!).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        mine = mine.Where(g => !known.Contains(g.H("GameURL")!)).ToList();
        if (mine.Count == 0) return new();

        var lk = await _reader.LookupsAsync(await _reader.RosterAsync(club, ct), mine.SelectMany(g => new[] { g.H("White"), g.H("Black") }),
            mine.SelectMany(g => new[] { g.H("WhiteFideId"), g.H("BlackFideId") }), ct);
        var built = new List<(LeagueClubGame Game, DateOnly Date, IReadOnlyList<string> Sans)>();
        foreach (var g in mine)
        {
            // Kanonische SAN wie bei jedem Upload (gleicher Zug-Schlüssel), und nur eine nachspielbare Partie.
            List<string> sans;
            try { sans = Games.SavedGameService.LegalSans(g.Sans.ToList()); }
            catch (ArgumentException) { _log.LogInformation("Vereins-Datenbank {Club}: übertragene Partie {Url} nicht nachspielbar", club.Id, g.H("GameURL")); continue; }
            if (sans.Count > MaxPlies) continue;
            var (game, reason) = Build(club, SideOf(g.H("White"), g.H("WhiteFideId"), g.Elo("WhiteElo"), lk),
                SideOf(g.H("Black"), g.H("BlackFideId"), g.Elo("BlackElo"), lk), sans, YearOf(g.H("Date"), now), g.H("Result"), g.H("Event"));
            if (game is null)
            {
                _log.LogInformation("Vereins-Datenbank {Club}: übertragene Partie {Url} nicht übernommen ({Reason})", club.Id, g.H("GameURL"), reason);
                continue;
            }
            var date = FullDateOf(g.H("Date"))!.Value;
            game.Source = LeagueBroadcastImport.Source;
            game.SourceUrl = g.H("GameURL");
            game.PlayedOn = date;
            game.SourceRound = g.H("Round") is { Length: <= 20 } r ? r : null;
            game.CreatedAt = now;
            game.Pgn = PgnOf(game, sans);
            built.Add((game, date, sans));
        }
        var covered = await CoveredByAsync(_db, club, built.Select(b => b.Game), uploadsOnly: true, ct);
        var pending = built.Where(b => !covered.Contains(b.Game)).ToList();
        // Auch innerhalb eines Laufs nur einmal (gleiche Züge zweimal in einer Übertragung, z. B. Runde doppelt angelegt).
        pending = pending.DistinctBy(b => (b.Game.MovesHash, b.Game.White, b.Game.Black)).ToList();
        foreach (var b in built.Where(b => covered.Contains(b.Game)))
            _log.LogInformation("Vereins-Datenbank {Club}: übertragene Partie {Url} steht schon hochgeladen da — nicht doppelt", club.Id, b.Game.SourceUrl);
        if (pending.Count == 0) return new();

        await _writer.ApplyPairingsAsync(club, pending.Select(b => (b.Game, (int?)null, (DateOnly?)b.Date, b.Sans)).ToList(), ct);
        var list = pending.Select(b => b.Game).ToList();
        try
        {
            await _writer.SaveAsync(list, ct, uploaderUserId: null, dateOf: g => g.PlayedOn);
        }
        catch (DbUpdateException ex)
        {
            // Der eindeutige Schlüssel (Verein, GameURL): ein gleichzeitiger Lauf war schneller — beim nächsten Takt steht sie da.
            _log.LogWarning(ex, "Vereins-Datenbank {Club}: übertragene Partien nicht gespeichert (gleichzeitig eingespielt?)", club.Id);
            _db.ChangeTracker.Clear();
            return new();
        }
        await _writer.RebalanceAsync(club, list, ct);
        await _writer.RequestBroadcastSummariesAsync(list, since, ct);
        _log.LogInformation("Vereins-Datenbank {Club}: {Count} übertragene Partie(n) der eigenen Mannschaft übernommen ({Ids})", club.Id,
            list.Count, string.Join(",", list.Select(g => g.Id)));
        return list;
    }

    /// <summary>Eine Seite mit echtem Namen: abgeglichen wie beim Import (Meldeliste, gemerkte Zuordnung, Megabase), aber nie
    /// ersetzt; wer nirgends steht, gilt trotzdem als bekannt — Name und FIDE-ID kommen aus der öffentlichen Übertragung.</summary>
    private static Side SideOf(string? name, string? fide, int? elo, Lookups lk)
    {
        var (hit, mega, alias) = Resolve(name, fide, lk);
        var side = new Side(name, hit, elo, Replace: false, Fide: fide, Mega: mega, Alias: alias);
        return side.Known ? side : side with { Mega = new LeagueMegaPlayers.Hit(LeagueNames.Clean(name), fide) };
    }

    /// <summary>
    /// Welche dieser (noch nicht gespeicherten) Partien stehen schon als ANDERE Fassung im Verein? Gleiche Züge im gleichen Jahr
    /// (die Regel des Imports) oder dieselben Spieler mit demselben Anfang (<see cref="ClubSideResolver.SameVersion"/>, FIDE-ID
    /// auch hinter „Schwaz“). <paramref name="uploadsOnly"/>: nur gegen hochgeladene Partien (für das Einspielen der
    /// Übertragung); sonst nur gegen übertragene (für einen Upload: ein Formular derselben Partie wird kein zweiter Eintrag und
    /// keine zweite Zusammenfassung, und es archiviert die übertragene Fassung nicht).
    /// </summary>
    internal static async Task<HashSet<LeagueClubGame>> CoveredByAsync(AppDbContext db, LeagueClub club, IEnumerable<LeagueClubGame> games,
        bool uploadsOnly, CancellationToken ct)
    {
        var list = games.ToList();
        var covered = new HashSet<LeagueClubGame>(ReferenceEqualityComparer.Instance);
        if (list.Count == 0) return covered;
        var years = list.Select(g => g.Year).Distinct().ToList();
        var q = ClubGameReader.GamesOf(db, club).AsNoTracking().Where(c => years.Contains(c.Year));
        q = uploadsOnly ? q.Where(c => c.Source == null) : q.Where(c => c.Source != null);
        if (uploadsOnly)
        {
            // Hochgeladene sind viele — nur die mit denselben Zügen oder einer der beteiligten FIDE-IDs.
            var hashes = list.Select(g => g.MovesHash).Distinct().ToList();
            var fides = list.SelectMany(g => new[] { g.WhiteFide, g.BlackFide }).Where(f => f != null).Distinct().ToList();
            q = q.Where(c => hashes.Contains(c.MovesHash) || fides.Contains(c.WhiteFide) || fides.Contains(c.BlackFide)
                || fides.Contains(c.WhiteRealFide) || fides.Contains(c.BlackRealFide));
        }
        var stored = await q.Select(c => new LeagueClubGame
        {
            Id = c.Id, Year = c.Year, White = c.White, Black = c.Black, WhiteFide = c.WhiteFide, BlackFide = c.BlackFide,
            WhiteRealFide = c.WhiteRealFide, BlackRealFide = c.BlackRealFide, MovesHash = c.MovesHash, Plies = c.Plies, Pgn = c.Pgn,
        }).ToListAsync(ct);
        if (stored.Count == 0) return covered;
        var storedSans = stored.ToDictionary(c => c.Id, c => SansOf(c.Pgn));
        foreach (var g in list)
        {
            var sans = SansOf(g.Pgn);
            if (stored.Any(c => c.Year == g.Year && (c.MovesHash == g.MovesHash && (g.Plies >= ShortGamePlies || c.White == g.White && c.Black == g.Black)
                    || SameVersion(c, null, g, null, storedSans[c.Id], sans))))
                covered.Add(g);
        }
        return covered;
    }
}
