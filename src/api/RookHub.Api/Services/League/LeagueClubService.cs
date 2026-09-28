using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Vereins-Datenbank in LeagueHub (Wunsch 2026-09-28): Mitglieder von SK Schwaz laden Partien hoch — viele auf einmal als
/// PGN oder einzeln aus einem eingelesenen Partieformular, angemeldet ODER ohne Konto über einen Teilen-Link — und die
/// Spielerkarten der Gegner zeigen sie mit.
///
/// <para>Regeln, alle vom Nutzer vorgegeben:</para>
/// <list type="bullet">
/// <item><b>Beide Namen gegen die Meldelisten</b> (<see cref="LeagueRosterIndex"/>), und wer dort fehlt, gegen das
/// Spielerverzeichnis der Megabase (<see cref="LeagueMegaPlayers"/>, eindeutig über Name oder FIDE-ID — Wunsch
/// 2026-09-28: „standardmäßig auf Megabase matchen, wenn in Tirol kein Treffer"). Ist keine Seite bekannt, wird die
/// Partie abgelehnt (<c>noLeaguePlayer</c>); bleibt nach dem Ersetzen keine bekannte übrig, ebenso (<c>onlyOwnClub</c>).
/// Eine Partie, deren Gegner nur die Megabase kennt, ist übernehmbar, die Übersicht wählt sie aber nicht vor.</item>
/// <item><b>Spieler von Schwaz werden durch „Schwaz" ersetzt</b> (Vorgabe: jeder, der in seiner jüngsten Saison für
/// Schwaz gemeldet ist, dazu der Hochladende laut Profil) — ohne Elo und FIDE-ID, die Veranstaltung fällt weg, und es
/// wird WEDER gespeichert, wer dahinter steht, NOCH wer hochgeladen hat oder wann (auch nicht versteckt). Beim PGN-Import
/// zeigt eine Übersicht (<see cref="PreviewAsync"/>) je Partie wer gegen wen, die Vorgaben und ob sie übernommen würde;
/// der Nutzer korrigiert Spieler und Ersetzen, übernommen wird mit seinen Entscheidungen (<see cref="ImportPgnAsync"/>).</item>
/// <item><b>Nur das JAHR</b> des Datums.</item>
/// <item>Gespeichert wird nur die Hauptvariante ohne Kommentare (Kommentare tragen oft Namen und Uhrzeiten), und nur ab
/// der Grundstellung (<c>fromPosition</c>) — die Karten zählen Eröffnungen.</item>
/// <item><b>Zweimal hochgeladen = einmal da</b>: gleiche Züge (<see cref="LeagueClubGame.MovesHash"/>) im gleichen Jahr;
/// bei kurzen Partien (unter <see cref="ShortGamePlies"/> Halbzügen) zusätzlich gleiche Namen — dieselben zwölf
/// Eröffnungszüge spielen viele.</item>
/// </list>
/// </summary>
public sealed class LeagueClubService
{
    public const string AnonymousName = LeagueRefresh.OwnTeam;
    public const int MaxImportGames = 500;
    public const int MaxImportChars = 5_000_000;
    public const int MaxPlies = 600;
    public const int ShortGamePlies = 20;
    public const int PageSize = 50;
    private const string StartPosition = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -";
    private static readonly HashSet<string> Results = new(StringComparer.Ordinal) { "1-0", "0-1", "1/2-1/2", "*" };

    private readonly AppDbContext _db;
    private readonly ILogger<LeagueClubService> _log;
    private readonly Func<DateTime> _now;

    public LeagueClubService(AppDbContext db, ILogger<LeagueClubService> log, Func<DateTime>? now = null)
    {
        _db = db; _log = log; _now = now ?? (() => DateTime.UtcNow);
    }

    public async Task<LeagueRosterIndex> RosterAsync(CancellationToken ct)
    {
        var seasons = await _db.LeagueTournaments.AsNoTracking().Select(t => new { t.Tnr, t.Season })
            .ToDictionaryAsync(t => t.Tnr, t => t.Season, ct);
        var rows = await _db.LeaguePlayers.AsNoTracking().Select(p => new { p.Tnr, p.Team, p.Name, p.NameKey, p.FideId }).ToListAsync(ct);
        return new(rows.Select(r => new LeagueRosterIndex.Row(r.Tnr, r.Team, r.Name, r.NameKey, r.FideId,
            seasons.GetValueOrDefault(r.Tnr) ?? "")));
    }

    /// <summary>Das Megabase-Verzeichnis für diese Namen und FIDE-IDs (ein Abgleich = eine Abfrage je 500).</summary>
    private Task<LeagueMegaPlayers.Lookup> MegaAsync(IEnumerable<string?> names, IEnumerable<string?> fides, CancellationToken ct) =>
        new LeagueMegaPlayers(_db).LookupAsync(names, fides, ct);

    // ── Hochladen ───────────────────────────────────────────────────

    private sealed record Owner(string? Fide, string?[] Names)
    {
        public static readonly Owner Nobody = new(null, Array.Empty<string?>());
    }

    /// <summary>Wer hochlädt (Profil) — ohne Konto (Teilen-Link) niemand.</summary>
    private async Task<Owner> OwnerAsync(int? userId, CancellationToken ct)
    {
        if (userId is not int uid) return Owner.Nobody;
        var p = await _db.UserProfiles.AsNoTracking().Where(x => x.UserId == uid)
            .Select(x => new { x.FideId, x.LastName, x.DisplayName, x.FirstName }).FirstOrDefaultAsync(ct);
        return new Owner(string.IsNullOrWhiteSpace(p?.FideId) ? null : p!.FideId!.Trim(),
            new[] { p?.LastName, p?.DisplayName, p?.FirstName });
    }

    /// <summary>Welche Seite spielt der Hochladende? FIDE-ID vor Name; bei Gleichstand keine.</summary>
    private static string? OwnerSideOf(Owner o, string? white, string? black, string? whiteFide, string? blackFide,
        LeagueRosterIndex.Hit hw, LeagueRosterIndex.Hit hb, LeagueMegaPlayers.Hit? mw, LeagueMegaPlayers.Hit? mb)
    {
        if (o.Fide != null)
        {
            var w = o.Fide == whiteFide || o.Fide == hw.Person?.Fide || o.Fide == mw?.Fide;
            var b = o.Fide == blackFide || o.Fide == hb.Person?.Fide || o.Fide == mb?.Fide;
            if (w != b) return w ? "white" : "black";
        }
        return o.Names.Length == 0 ? null : ScoresheetScanService.GuessOwnerSide(white, black, o.Names);
    }

    /// <summary>Eine Partie des PGN-Textes: die Kopfzeilen und die geprüfte Hauptvariante — oder warum sie sich gar nicht
    /// übernehmen lässt (<c>illegal</c>, <c>noMoves</c>, <c>tooLong</c>, <c>fromPosition</c>).</summary>
    private sealed record Parsed(int Index, Dictionary<string, string> Headers, List<string>? Sans, string? Error)
    {
        public string? H(string key) => Headers.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) && v.Trim() != "?" ? v.Trim() : null;
        public int? Elo(string key) => int.TryParse(H(key), out var e) ? e : null;
    }

    /// <summary>Die Partien in Reihenfolge, 1-basiert nummeriert — Übersicht und Übernahme zählen gleich.</summary>
    private static List<Parsed> ParseAll(string pgn, out bool truncated)
    {
        var games = PgnParser.SplitGames(pgn).Where(g => !string.IsNullOrWhiteSpace(g.MoveText)).ToList();
        truncated = games.Count > MaxImportGames;
        var start = new Chess.ChessBoard().ToFen();
        var result = new List<Parsed>();
        var index = 0;
        foreach (var (rawHeaders, moveText) in games.Take(MaxImportGames))
        {
            index++;
            var h = rawHeaders ?? new Dictionary<string, string>();
            string? Error()
            {
                if (h.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen)
                    && string.Join(' ', fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4)) != StartPosition)
                    return "fromPosition";
                return null;
            }
            var err = Error();
            List<string>? sans = null;
            if (err == null)
            {
                try
                {
                    // Nur ein Ergebnis („1-0", „*") ist KEINE Partie mit illegalem Zug — TryExtractUciMainline meldet beides null.
                    var uci = PgnParser.ExtractMainlineSans(moveText).Count == 0 ? new List<string>()
                        : PgnParser.TryExtractUciMainline(start, moveText);
                    if (uci == null) err = "illegal";
                    else if (uci.Count == 0) err = "noMoves";
                    else if (uci.Count > MaxPlies) err = "tooLong";
                    else
                    {
                        sans = SavedGameService.SansOf(start, uci);
                        if (sans.Count != uci.Count) { err = "illegal"; sans = null; }
                    }
                }
                catch (Exception) { err = "illegal"; sans = null; }
            }
            result.Add(new Parsed(index, h, sans, err));
        }
        return result;
    }

    /// <summary>Eine Seite, wie sie gespeichert würde. <see cref="Fide"/> = gewählte FIDE-ID eines Spielers, der KEIN
    /// Ligaspieler ist; <see cref="Mega"/> = so ein Spieler, im Megabase-Verzeichnis gefunden — dann ist die Seite
    /// „bekannt", auch ohne Liga.</summary>
    private sealed record Side(string? Name, LeagueRosterIndex.Hit Hit, int? Elo, bool Replace, string? Fide = null,
        LeagueMegaPlayers.Hit? Mega = null)
    {
        public bool Known => Hit.League || Mega != null;
    }

    /// <summary>Wer kein Ligaspieler ist, wird im Megabase-Verzeichnis gesucht: über die FIDE-ID, sonst den Namen. Eine
    /// FIDE-ID, die das Verzeichnis nicht kennt, lässt den Namen nur gelten, wenn er ihr nicht widerspricht.</summary>
    private static LeagueMegaPlayers.Hit? MegaOf(LeagueRosterIndex.Hit hit, string? name, string? fide, LeagueMegaPlayers.Lookup mega)
    {
        if (hit.League) return null;
        if (string.IsNullOrWhiteSpace(fide)) return mega.ByName(name);
        return mega.ByFide(fide) ?? (mega.ByName(name) is { } n && (n.Fide is null || n.Fide == fide.Trim())
            ? n with { Fide = fide.Trim() is { Length: <= 16 } f ? f : n.Fide } : null);
    }

    /// <summary>Die Vorgabe der Übersicht: Abgleich über Kopfzeile, ersetzt wird ein Spieler von Schwaz und der
    /// Hochladende selbst (Wunsch 2026-09-28: „alle Spieler vom Verein Schwaz").</summary>
    private static (Side White, Side Black, string? OwnerSide) Defaults(Parsed p, LeagueRosterIndex roster, Owner owner,
        LeagueMegaPlayers.Lookup mega)
    {
        var hw = roster.Match(p.H("White"), p.H("WhiteFideId"));
        var hb = roster.Match(p.H("Black"), p.H("BlackFideId"));
        var mw = MegaOf(hw, p.H("White"), p.H("WhiteFideId"), mega);
        var mb = MegaOf(hb, p.H("Black"), p.H("BlackFideId"), mega);
        var ownerSide = OwnerSideOf(owner, p.H("White"), p.H("Black"), p.H("WhiteFideId"), p.H("BlackFideId"), hw, hb, mw, mb);
        return (new Side(p.H("White"), hw, p.Elo("WhiteElo"), hw.OwnClub || ownerSide == "white", null, mw),
            new Side(p.H("Black"), hb, p.Elo("BlackElo"), hb.OwnClub || ownerSide == "black", null, mb), ownerSide);
    }

    /// <summary>Was der Nutzer für eine Seite festgelegt hat: ein gewählter Ligaspieler (FIDE-ID) schlägt den Namen, ein
    /// getippter Name wird neu abgeglichen, ohne Angabe gilt die Kopfzeile.</summary>
    private static Side Decided(LeagueClubSideDecision? d, string? raw, string? rawFide, int? elo, LeagueRosterIndex roster,
        LeagueMegaPlayers.Lookup mega)
    {
        d ??= new LeagueClubSideDecision();
        if (roster.ByFide(d.Fide) is { } p) return new Side(p.Name, new LeagueRosterIndex.Hit(true, p, new[] { p }), elo, d.Replace);
        var typed = !string.IsNullOrWhiteSpace(d.Name);
        var name = typed ? d.Name!.Trim() : raw;
        // Eine FIDE-ID, die kein Ligaspieler trägt (aus dem Megabase-Verzeichnis gewählt): bleibt an der Partie stehen.
        var chosen = string.IsNullOrWhiteSpace(d.Fide) ? null : d.Fide.Trim();
        var fide = chosen ?? (typed ? null : rawFide);
        var hit = roster.Match(name, fide);
        return new Side(name, hit, elo, d.Replace, chosen is { Length: <= 16 } ? chosen : null, MegaOf(hit, name, fide, mega));
    }

    /// <summary>Aus einer geprüften Zugfolge + den Seiten die zu speichernde Zeile (oder den Ablehnungsgrund).</summary>
    private static (LeagueClubGame? Game, string? Reason) Build(Side w, Side b, IReadOnlyList<string> sans, int? year,
        string? result, string? evt)
    {
        if (!w.Known && !b.Known) return (null, "noLeaguePlayer");
        if (!(w.Known && !w.Replace) && !(b.Known && !b.Replace)) return (null, "onlyOwnClub");
        (string Name, string? Fide, int? Elo) Out(Side s) => s.Replace ? (AnonymousName, null, null)
            : (Clip(s.Hit.Person?.Name ?? s.Mega?.Name ?? LeagueNames.Clean(s.Name), 120) is { Length: > 0 } n ? n : "?",
                s.Hit.Person?.Fide ?? s.Fide ?? s.Mega?.Fide, Elo(s.Elo));
        var (wn, wf, we) = Out(w);
        var (bn, bf, be) = Out(b);
        var anonymized = w.Replace || b.Replace;
        var game = new LeagueClubGame
        {
            Year = year,
            White = wn, Black = bn, WhiteFide = wf, BlackFide = bf, WhiteElo = we, BlackElo = be,
            Result = result is { } r && Results.Contains(r) ? r : "*",
            Event = anonymized ? null : Clip(LeagueNames.Clean(evt), 200) is { Length: > 0 } e && e != "?" ? e : null,
            Plies = sans.Count,
            MovesHash = HashOf(sans),
            Anonymized = anonymized,
        };
        game.Pgn = PgnOf(game, sans);
        return (game, null);
    }

    internal static string HashOf(IReadOnlyList<string> sans) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(' ', sans)))).ToLowerInvariant();

    internal static string PgnOf(LeagueClubGame g, IReadOnlyList<string> sans)
    {
        var sb = new StringBuilder();
        sb.Append(PgnWriter.Tag("Event", g.Event ?? "?"));
        sb.Append(PgnWriter.Tag("Site", "?"));
        sb.Append(PgnWriter.Tag("Date", g.Year is { } y ? $"{y}.??.??" : "????.??.??"));
        sb.Append(PgnWriter.Tag("Round", "?"));
        sb.Append(PgnWriter.Tag("White", g.White));
        sb.Append(PgnWriter.Tag("Black", g.Black));
        sb.Append(PgnWriter.Tag("Result", g.Result));
        if (g.WhiteElo is { } we) sb.Append(PgnWriter.Tag("WhiteElo", we.ToString()));
        if (g.BlackElo is { } be) sb.Append(PgnWriter.Tag("BlackElo", be.ToString()));
        // Dieselben Namen wie bei Lumbra — die Spielerkarte erkennt die Farbe daran (LeagueProfileBuilder.ColorOf).
        if (g.WhiteFide != null) sb.Append(PgnWriter.Tag("WhiteFideId", g.WhiteFide));
        if (g.BlackFide != null) sb.Append(PgnWriter.Tag("BlackFideId", g.BlackFide));
        sb.Append('\n').Append(PgnWriter.MoveText(sans, null, null, g.Result)).Append('\n');
        return sb.ToString();
    }

    private static int? Elo(int? e) => e is >= 500 and <= 3000 ? e : null;

    private static string Clip(string? s, int max) => s is null ? string.Empty : s.Length <= max ? s : s[..max];

    /// <summary>Jahr aus einem PGN-Datum („2024.05.12", „2024.??.??"); unplausibel = unbekannt.</summary>
    internal static int? YearOf(string? date, DateTime now) =>
        date is { Length: >= 4 } && int.TryParse(date[..4], out var y) && y >= 1900 && y <= now.Year + 1 ? y : null;

    /// <summary>Gleiche Partie schon da (Datenbank oder derselbe Upload)?</summary>
    private async Task<bool> IsDuplicateAsync(LeagueClubGame g, List<LeagueClubGame> pending, CancellationToken ct)
    {
        bool Same(LeagueClubGame x) => x.MovesHash == g.MovesHash && x.Year == g.Year
            && (g.Plies >= ShortGamePlies || (x.White == g.White && x.Black == g.Black));
        if (pending.Any(Same)) return true;
        var candidates = await _db.LeagueClubGames.AsNoTracking().Where(x => x.MovesHash == g.MovesHash && x.Year == g.Year)
            .Select(x => new LeagueClubGame { MovesHash = x.MovesHash, Year = x.Year, White = x.White, Black = x.Black })
            .ToListAsync(ct);
        return candidates.Any(Same);
    }

    private static LeagueClubPreviewSideDto SideDto(Side s, bool owner) => new()
    {
        Raw = s.Name, Elo = s.Elo, Match = MatchDto(s.Hit, s.Mega), Owner = owner, Replace = s.Replace,
    };

    internal static LeagueClubSideMatchDto MatchDto(LeagueRosterIndex.Hit h, LeagueMegaPlayers.Hit? mega = null) => new()
    {
        League = h.League, Ambiguous = h.Ambiguous, Mega = !h.League && mega != null,
        Name = h.Person?.Name ?? (h.League ? null : mega?.Name), Fide = h.Person?.Fide ?? (h.League ? null : mega?.Fide),
        Club = h.OwnClub, LastNameOnly = h.LastNameOnly,
        Candidates = h.Ambiguous ? h.Candidates.Take(8).Select(PersonDto).ToList() : new(),
    };

    internal static LeagueRosterPersonDto PersonDto(LeagueRosterIndex.Person p) =>
        new() { Name = p.Name, Fide = p.Fide, Teams = p.Teams.Take(3).ToList(), Club = p.OwnClub };

    /// <summary>
    /// Übersicht vor dem Import (Wunsch 2026-09-28: „nach Import Übersicht wer gegen wen, unerkannte/falsche Spieler
    /// korrigieren, je Partie markieren, ob sie importiert wird"): liest und gleicht ab, speichert NICHTS. Je Partie die
    /// Seiten mit Abgleich und der Vorgabe „ersetzen", ob sie schon da ist, und was sie unübernehmbar macht. Ob eine
    /// Partie übernommen wird, entscheidet die Seite danach aus den Seiten (dieselbe Regel wie <see cref="Build"/>).
    /// </summary>
    public async Task<LeagueClubPreviewDto> PreviewAsync(int? userId, string pgn, CancellationToken ct = default)
    {
        var parsed = ParseAll(pgn, out var truncated);
        var roster = await RosterAsync(ct);
        var mega = await MegaAsync(parsed.SelectMany(p => new[] { p.H("White"), p.H("Black") }),
            parsed.SelectMany(p => new[] { p.H("WhiteFideId"), p.H("BlackFideId") }), ct);
        var owner = await OwnerAsync(userId, ct);
        var now = _now();
        var pending = new List<LeagueClubGame>();
        var dto = new LeagueClubPreviewDto { Truncated = truncated };
        foreach (var p in parsed)
        {
            var (w, b, ownerSide) = Defaults(p, roster, owner, mega);
            var g = new LeagueClubPreviewGameDto
            {
                Index = p.Index, Year = YearOf(p.H("Date"), now), Result = p.H("Result") is { } r && Results.Contains(r) ? r : "*",
                Event = p.H("Event"), Plies = p.Sans?.Count ?? 0, Opening = p.Sans is null ? "" : OpeningOfSans(p.Sans),
                Error = p.Error, White = SideDto(w, ownerSide == "white"), Black = SideDto(b, ownerSide == "black"),
            };
            if (p.Sans != null && Build(w, b, p.Sans, g.Year, p.H("Result"), p.H("Event")).Game is { } built)
            {
                g.Duplicate = await IsDuplicateAsync(built, pending, ct);
                pending.Add(built);
            }
            dto.Games.Add(g);
        }
        return dto;
    }

    /// <summary>PGN-Import nach der Übersicht: je Partie in <paramref name="decisions"/> die festgelegten Seiten
    /// (<c>null</c> = alle Partien mit den Vorgaben der Übersicht). Wirft nicht bei einzelnen Partien — die stehen mit
    /// Grund in <c>Failed</c>.</summary>
    public async Task<LeagueClubImportResultDto> ImportPgnAsync(int? userId, string pgn,
        IReadOnlyList<LeagueClubImportGameDecision>? decisions, CancellationToken ct = default)
    {
        var result = new LeagueClubImportResultDto();
        var parsed = ParseAll(pgn, out var truncated);
        result.Truncated = truncated;
        var byIndex = parsed.ToDictionary(p => p.Index);
        var roster = await RosterAsync(ct);
        var chosen = (decisions ?? Array.Empty<LeagueClubImportGameDecision>()).Where(d => d != null)
            .SelectMany(d => new[] { d.White, d.Black }).Where(s => s != null).ToList();
        var mega = await MegaAsync(
            parsed.SelectMany(p => new[] { p.H("White"), p.H("Black") }).Concat(chosen.Select(s => s.Name)),
            parsed.SelectMany(p => new[] { p.H("WhiteFideId"), p.H("BlackFideId") }).Concat(chosen.Select(s => s.Fide)), ct);
        var owner = await OwnerAsync(userId, ct);
        var now = _now();
        var pending = new List<LeagueClubGame>();
        var work = decisions?.Where(d => d != null).DistinctBy(d => d.Index).Select(d => (d.Index, (LeagueClubImportGameDecision?)d)).ToList()
            ?? parsed.Select(p => (p.Index, (LeagueClubImportGameDecision?)null)).ToList();
        foreach (var (index, decision) in work)
        {
            if (!byIndex.TryGetValue(index, out var p))
            {
                result.Failed.Add(new LeagueClubFailureDto { Index = index, Reason = "notFound" });
                continue;
            }
            void Fail(string reason) => result.Failed.Add(new LeagueClubFailureDto
            {
                Index = index, White = p.H("White") is { } w0 ? Clip(w0, 120) : null, Black = p.H("Black") is { } b0 ? Clip(b0, 120) : null,
                Reason = reason,
            });
            if (p.Error != null || p.Sans == null) { Fail(p.Error ?? "illegal"); continue; }
            Side w, b;
            if (decision == null) (w, b, _) = Defaults(p, roster, owner, mega);
            else
            {
                w = Decided(decision.White, p.H("White"), p.H("WhiteFideId"), p.Elo("WhiteElo"), roster, mega);
                b = Decided(decision.Black, p.H("Black"), p.H("BlackFideId"), p.Elo("BlackElo"), roster, mega);
            }
            var (game, reason) = Build(w, b, p.Sans, YearOf(p.H("Date"), now), p.H("Result"), p.H("Event"));
            if (game == null) { Fail(reason!); continue; }
            if (await IsDuplicateAsync(game, pending, ct)) { result.Duplicates++; continue; }
            Stamp(game, userId, now);
            pending.Add(game);
        }
        await SaveAsync(pending, ct);
        result.Added = pending.Count;
        result.Anonymized = pending.Count(g => g.Anonymized);
        result.Ids = pending.Select(g => g.Id).ToList();
        _log.LogInformation("Vereins-Datenbank: {Added} Partien hochgeladen ({Anon} mit „Schwaz“), {Dup} doppelt, {Failed} abgelehnt{Via}",
            result.Added, result.Anonymized, result.Duplicates, result.Failed.Count, userId == null ? " (Teilen-Link)" : "");
        return result;
    }

    /// <summary>Eine Partie aus der Korrektur eines Partieformulars. <c>Reason</c> ≠ null = abgelehnt
    /// (<c>illegal</c> samt Meldung, sonst wie beim Import, dazu <c>duplicate</c>).</summary>
    public async Task<(LeagueClubGame? Game, string? Reason, string? Message)> AddGameAsync(int? userId, LeagueClubGameRequest req,
        CancellationToken ct = default)
    {
        var moves = (req.Moves ?? new()).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToList();
        if (moves.Count == 0) return (null, "noMoves", null);
        if (moves.Count > MaxPlies) return (null, "tooLong", null);
        List<string> sans;
        try { sans = SavedGameService.LegalSans(moves); }
        catch (ArgumentException ex) { return (null, "illegal", ex.Message); }
        var now = _now();
        var year = req.Year is { } y && y >= 1900 && y <= now.Year + 1 ? y : (int?)null;
        var roster = await RosterAsync(ct);
        var mega = await MegaAsync(new[] { req.White, req.Black }, new[] { req.WhiteFide, req.BlackFide }, ct);
        var w = Decided(new LeagueClubSideDecision { Name = req.White, Fide = req.WhiteFide, Replace = req.WhiteReplace }, req.White, null, req.WhiteElo, roster, mega);
        var b = Decided(new LeagueClubSideDecision { Name = req.Black, Fide = req.BlackFide, Replace = req.BlackReplace }, req.Black, null, req.BlackElo, roster, mega);
        var (game, reason) = Build(w, b, sans, year, req.Result, req.Event);
        if (game == null) return (null, reason, null);
        if (await IsDuplicateAsync(game, new(), ct)) return (null, "duplicate", null);
        Stamp(game, userId, now);
        await SaveAsync(new List<LeagueClubGame> { game }, ct);
        _log.LogInformation("Vereins-Datenbank: eine Partie aus einem Partieformular ({Anon}{Via})",
            game.Anonymized ? "mit „Schwaz“" : "mit Namen", userId == null ? ", Teilen-Link" : "");
        return (game, null, null);
    }

    /// <summary>Hochladender und Zeitpunkt — NUR bei Partien ohne „Schwaz" und mit Konto (siehe Klassenkommentar).</summary>
    private static void Stamp(LeagueClubGame g, int? userId, DateTime now)
    {
        if (g.Anonymized || userId is null) return;
        g.UploadedByUserId = userId;
        g.CreatedAt = now;
    }

    private async Task SaveAsync(List<LeagueClubGame> games, CancellationToken ct)
    {
        if (games.Count == 0) return;
        _db.LeagueClubGames.AddRange(games);
        await _db.SaveChangesAsync(ct);
        await RefreshCardsAsync(games.SelectMany(g => new[] { g.WhiteFide, g.BlackFide }), ct);
    }

    /// <summary>Spielerkarten der betroffenen Ligaspieler neu rechnen und die Partienzahl in den Ansichten nachziehen.</summary>
    private async Task RefreshCardsAsync(IEnumerable<string?> fides, CancellationToken ct)
    {
        var ids = fides.Where(f => !string.IsNullOrEmpty(f)).Select(f => f!).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return;
        var store = new LeagueProfileStore(_db);
        var now = _now();
        foreach (var f in ids) await store.RebuildAsync(f, ct, now: now);
        await _db.SaveChangesAsync(ct);
        await store.PatchViewCountsAsync(ids, ct);
    }

    // ── Lesen ───────────────────────────────────────────────────────

    private IQueryable<LeagueClubGame> Filter(string? fide, string? q)
    {
        var query = _db.LeagueClubGames.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(fide)) query = query.Where(g => g.WhiteFide == fide || g.BlackFide == fide);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            query = query.Where(g => g.White.Contains(t) || g.Black.Contains(t) || (g.Event != null && g.Event.Contains(t)));
        }
        return query;
    }

    public async Task<LeagueClubListDto> ListAsync(int userId, bool canManage, string? fide, string? q, int page, CancellationToken ct)
    {
        var query = Filter(fide, q);
        var total = await query.CountAsync(ct);
        page = Math.Max(1, page);
        var rows = await query.OrderByDescending(g => g.Year ?? 0).ThenByDescending(g => g.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        return new LeagueClubListDto
        {
            Total = total, Page = page, PageSize = PageSize,
            Items = rows.Select(g => new LeagueClubGameDto
            {
                Id = g.Id, Year = g.Year, White = g.White, Black = g.Black, WhiteFide = g.WhiteFide, BlackFide = g.BlackFide,
                WhiteElo = g.WhiteElo, BlackElo = g.BlackElo, Result = g.Result, Event = g.Event, Plies = g.Plies,
                Opening = OpeningOf(g.Pgn), Anonymized = g.Anonymized, CanDelete = CanDelete(g, userId, canManage),
            }).ToList(),
        };
    }

    private static bool CanDelete(LeagueClubGame g, int userId, bool canManage) =>
        canManage || (!g.Anonymized && g.UploadedByUserId == userId);

    internal static string OpeningOf(string pgn)
    {
        var moveText = PgnParser.SplitGames(pgn).Select(x => x.MoveText).FirstOrDefault() ?? string.Empty;
        return OpeningOfSans(PgnParser.ExtractMainlineSans(moveText));
    }

    internal static string OpeningOfSans(IEnumerable<string> all)
    {
        var sans = all.Take(6).ToList();
        var sb = new StringBuilder();
        for (var i = 0; i < sans.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            if (i % 2 == 0) sb.Append(i / 2 + 1).Append('.');
            sb.Append(sans[i]);
        }
        return sb.ToString();
    }

    public async Task<string> ExportAsync(string? fide, string? q, CancellationToken ct)
    {
        var pgns = await Filter(fide, q).OrderByDescending(g => g.Year ?? 0).ThenByDescending(g => g.Id)
            .Select(g => g.Pgn).ToListAsync(ct);
        return string.Join("\n", pgns.Select(p => p.TrimEnd() + "\n"));
    }

    /// <summary>Spieler zum Korrigieren eines Namens: die Ligaspieler, mit <paramref name="all"/> dazu das
    /// Spielerverzeichnis der ganzen Megabase (Treffer mit der FIDE-ID eines Ligaspielers gelten als dieser).</summary>
    public async Task<List<LeagueRosterPersonDto>> SuggestAsync(string q, bool all, CancellationToken ct)
    {
        var roster = await RosterAsync(ct);
        var list = roster.Suggest(q, 15).Select(PersonDto).ToList();
        if (!all) return list;
        var seen = list.Where(p => p.Fide != null).Select(p => p.Fide!).ToHashSet(StringComparer.Ordinal);
        foreach (var m in await new LeagueMegaPlayers(_db).SearchAsync(q, 25, ct))
        {
            if (m.FideId != null && !seen.Add(m.FideId)) continue;
            var league = roster.ByFide(m.FideId);
            list.Add(new LeagueRosterPersonDto
            {
                Name = league?.Name ?? m.Name, Fide = m.FideId, Teams = league?.Teams.Take(3).ToList() ?? new(),
                Club = league?.OwnClub ?? false, League = league != null, Source = "mega",
                Games = m.Games, LastYear = m.LastYear, MaxElo = m.MaxElo,
            });
        }
        return list;
    }

    public async Task<LeagueClubMatchDto> MatchAsync(string? white, string? black, CancellationToken ct)
    {
        var roster = await RosterAsync(ct);
        var mega = await MegaAsync(new[] { white, black }, Array.Empty<string?>(), ct);
        LeagueClubSideMatchDto One(string? name)
        {
            var hit = roster.Match(name, null);
            return MatchDto(hit, MegaOf(hit, name, null, mega));
        }
        return new LeagueClubMatchDto { White = One(white), Black = One(black) };
    }

    // ── Löschen ─────────────────────────────────────────────────────

    public enum DeleteResult { Deleted, NotFound, Forbidden }

    public async Task<DeleteResult> DeleteAsync(int userId, bool canManage, int id, CancellationToken ct = default)
    {
        var g = await _db.LeagueClubGames.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g == null) return DeleteResult.NotFound;
        if (!CanDelete(g, userId, canManage)) return DeleteResult.Forbidden;
        _db.LeagueClubGames.Remove(g);
        await _db.SaveChangesAsync(ct);
        await RefreshCardsAsync(new[] { g.WhiteFide, g.BlackFide }, ct);
        return DeleteResult.Deleted;
    }
}
