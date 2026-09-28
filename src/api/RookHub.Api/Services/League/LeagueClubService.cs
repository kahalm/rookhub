using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Vereins-Datenbank in LeagueHub (Wunsch 2026-09-28): Mitglieder von SK Schwaz laden Partien hoch — viele auf einmal als
/// PGN oder einzeln aus einem eingelesenen Partieformular — und die Spielerkarten der Gegner zeigen sie mit.
///
/// <para>Regeln, alle vom Nutzer vorgegeben:</para>
/// <list type="bullet">
/// <item><b>Beide Namen gegen die Meldelisten</b> (<see cref="LeagueRosterIndex"/>). Ist keine der verbleibenden Seiten
/// ein Ligaspieler, wird die Partie abgelehnt (<c>noLeaguePlayer</c>) — sie nützte keiner Vorbereitung.</item>
/// <item><b>„Meinen Namen durch Schwaz ersetzen"</b> (Vorgabe an): die Seite des Hochladenden heißt „Schwaz", ohne Elo
/// und FIDE-ID, die Veranstaltung fällt weg, und es wird WEDER gespeichert, wer dahinter steht, NOCH wer hochgeladen hat
/// oder wann (auch nicht versteckt) — damit man nicht gegen die eigenen Spieler vorbereiten kann. Beim PGN-Import wird die
/// eigene Seite über das Profil gefunden (FIDE-ID, sonst der Name); wer darin nicht vorkommt, bekommt die Partie mit
/// <c>ownerNotFound</c> zurück, statt dass sie mit echtem Namen landet.</item>
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

    public async Task<LeagueRosterIndex> RosterAsync(CancellationToken ct) =>
        new(await _db.LeaguePlayers.AsNoTracking()
            .Select(p => new LeagueRosterIndex.Row(p.Tnr, p.Team, p.Name, p.NameKey, p.FideId)).ToListAsync(ct));

    // ── Hochladen ───────────────────────────────────────────────────

    private sealed record Owner(string? Fide, string?[] Names);

    private async Task<Owner> OwnerAsync(int userId, CancellationToken ct)
    {
        var p = await _db.UserProfiles.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.FideId, x.LastName, x.DisplayName, x.FirstName }).FirstOrDefaultAsync(ct);
        return new Owner(string.IsNullOrWhiteSpace(p?.FideId) ? null : p!.FideId!.Trim(),
            new[] { p?.LastName, p?.DisplayName, p?.FirstName });
    }

    /// <summary>Welche Seite spielt der Hochladende? FIDE-ID vor Name; bei Gleichstand keine.</summary>
    private static string? OwnerSideOf(Owner o, string? white, string? black, string? whiteFide, string? blackFide,
        LeagueRosterIndex.Hit hw, LeagueRosterIndex.Hit hb)
    {
        if (o.Fide != null)
        {
            var w = o.Fide == whiteFide || o.Fide == hw.Person?.Fide;
            var b = o.Fide == blackFide || o.Fide == hb.Person?.Fide;
            if (w != b) return w ? "white" : "black";
        }
        return ScoresheetScanService.GuessOwnerSide(white, black, o.Names);
    }

    private sealed record Input(string? White, string? Black, string? WhiteFide, string? BlackFide, int? WhiteElo, int? BlackElo,
        string? Result, string? Event, int? Year, List<string> Sans, bool Anonymize, string? OwnerSide);

    /// <summary>Aus einer geprüften Zugfolge + Kopfdaten die zu speichernde Zeile (oder den Ablehnungsgrund).</summary>
    private static (LeagueClubGame? Game, string? Reason) Build(Input i, LeagueRosterIndex roster, Owner owner)
    {
        var hw = roster.Match(i.White, i.WhiteFide);
        var hb = roster.Match(i.Black, i.BlackFide);
        if (!hw.League && !hb.League) return (null, "noLeaguePlayer");
        string? anonSide = null;
        if (i.Anonymize)
        {
            anonSide = i.OwnerSide is "white" or "black" ? i.OwnerSide
                : OwnerSideOf(owner, i.White, i.Black, i.WhiteFide, i.BlackFide, hw, hb);
            if (anonSide == null) return (null, "ownerNotFound");
        }
        (string Name, string? Fide, int? Elo, bool League) Side(string? raw, LeagueRosterIndex.Hit hit, int? elo, bool anon) =>
            anon ? (AnonymousName, null, null, false)
                : (Clip(hit.Person?.Name ?? LeagueNames.Clean(raw), 120) is { Length: > 0 } n ? n : "?", hit.Person?.Fide, Elo(elo), hit.League);
        var w = Side(i.White, hw, i.WhiteElo, anonSide == "white");
        var b = Side(i.Black, hb, i.BlackElo, anonSide == "black");
        if (!w.League && !b.League) return (null, "noLeaguePlayer");

        var game = new LeagueClubGame
        {
            Year = i.Year,
            White = w.Name, Black = b.Name, WhiteFide = w.Fide, BlackFide = b.Fide, WhiteElo = w.Elo, BlackElo = b.Elo,
            Result = i.Result is { } r && Results.Contains(r) ? r : "*",
            Event = anonSide != null ? null : Clip(LeagueNames.Clean(i.Event), 200) is { Length: > 0 } e && e != "?" ? e : null,
            Plies = i.Sans.Count,
            MovesHash = HashOf(i.Sans),
            Anonymized = anonSide != null,
        };
        game.Pgn = PgnOf(game, i.Sans);
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

    /// <summary>PGN-Massenimport. Wirft nicht bei einzelnen Partien — die stehen mit Grund in <c>Failed</c>.</summary>
    public async Task<LeagueClubImportResultDto> ImportPgnAsync(int userId, string pgn, bool anonymize, CancellationToken ct = default)
    {
        var result = new LeagueClubImportResultDto();
        var games = PgnParser.SplitGames(pgn).Where(g => !string.IsNullOrWhiteSpace(g.MoveText)).ToList();
        if (games.Count > MaxImportGames) { result.Truncated = true; games = games.Take(MaxImportGames).ToList(); }
        var roster = await RosterAsync(ct);
        var owner = await OwnerAsync(userId, ct);
        var now = _now();
        var pending = new List<LeagueClubGame>();
        var index = 0;
        foreach (var (rawHeaders, moveText) in games)
        {
            index++;
            var h = rawHeaders ?? new Dictionary<string, string>();
            string? H(string key) => h.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) && v.Trim() != "?" ? v.Trim() : null;
            void Fail(string reason) => result.Failed.Add(new LeagueClubFailureDto
            {
                Index = index, White = H("White") is { } w ? Clip(w, 120) : null, Black = H("Black") is { } b ? Clip(b, 120) : null,
                Reason = reason,
            });

            if (H("FEN") is { } fen && string.Join(' ', fen.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4)) != StartPosition)
            { Fail("fromPosition"); continue; }
            List<string> sans;
            try
            {
                var start = new Chess.ChessBoard().ToFen();
                var uci = PgnParser.TryExtractUciMainline(start, moveText);
                if (uci == null) { Fail("illegal"); continue; }
                if (uci.Count == 0) { Fail("noMoves"); continue; }
                if (uci.Count > MaxPlies) { Fail("tooLong"); continue; }
                sans = SavedGameService.SansOf(start, uci);
                if (sans.Count != uci.Count) { Fail("illegal"); continue; }
            }
            catch (Exception) { Fail("illegal"); continue; }

            var input = new Input(H("White"), H("Black"), H("WhiteFideId"), H("BlackFideId"),
                int.TryParse(H("WhiteElo"), out var we) ? we : null, int.TryParse(H("BlackElo"), out var be) ? be : null,
                H("Result"), H("Event"), YearOf(H("Date"), now), sans, anonymize, null);
            var (game, reason) = Build(input, roster, owner);
            if (game == null) { Fail(reason!); continue; }
            if (await IsDuplicateAsync(game, pending, ct)) { result.Duplicates++; continue; }
            Stamp(game, userId, now);
            pending.Add(game);
        }
        await SaveAsync(pending, ct);
        result.Added = pending.Count;
        result.Anonymized = pending.Count(g => g.Anonymized);
        result.Ids = pending.Select(g => g.Id).ToList();
        _log.LogInformation("Vereins-Datenbank: {Added} Partien hochgeladen ({Anon} anonym), {Dup} doppelt, {Failed} abgelehnt",
            result.Added, result.Anonymized, result.Duplicates, result.Failed.Count);
        return result;
    }

    /// <summary>Eine Partie aus der Korrektur eines Partieformulars. <c>Reason</c> ≠ null = abgelehnt
    /// (<c>illegal</c> samt Meldung, sonst wie beim Import, dazu <c>duplicate</c>).</summary>
    public async Task<(LeagueClubGame? Game, string? Reason, string? Message)> AddGameAsync(int userId, LeagueClubGameRequest req,
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
        var input = new Input(req.White, req.Black, null, null, req.WhiteElo, req.BlackElo, req.Result, req.Event, year, sans,
            req.Anonymize, req.OwnerSide);
        var (game, reason) = Build(input, await RosterAsync(ct), await OwnerAsync(userId, ct));
        if (game == null) return (null, reason, null);
        if (await IsDuplicateAsync(game, new(), ct)) return (null, "duplicate", null);
        Stamp(game, userId, now);
        await SaveAsync(new List<LeagueClubGame> { game }, ct);
        _log.LogInformation("Vereins-Datenbank: eine Partie aus einem Partieformular ({Anon})", game.Anonymized ? "anonym" : "mit Namen");
        return (game, null, null);
    }

    /// <summary>Hochladender und Zeitpunkt — NUR bei nicht anonymisierten Partien (siehe Klassenkommentar).</summary>
    private static void Stamp(LeagueClubGame g, int userId, DateTime now)
    {
        if (g.Anonymized) return;
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
        var sans = PgnParser.ExtractMainlineSans(moveText).Take(6).ToList();
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

    public async Task<List<LeagueRosterPersonDto>> SuggestAsync(string q, CancellationToken ct) =>
        (await RosterAsync(ct)).Suggest(q, 15)
            .Select(p => new LeagueRosterPersonDto { Name = p.Name, Fide = p.Fide, Teams = p.Teams.Take(3).ToList() }).ToList();

    public async Task<LeagueClubMatchDto> MatchAsync(string? white, string? black, CancellationToken ct)
    {
        var roster = await RosterAsync(ct);
        static LeagueClubSideMatchDto Dto(LeagueRosterIndex.Hit h) => new()
        {
            League = h.League, Ambiguous = h.Ambiguous, Name = h.Person?.Name, Fide = h.Person?.Fide,
        };
        return new LeagueClubMatchDto { White = Dto(roster.Match(white, null)), Black = Dto(roster.Match(black, null)) };
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
