using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.DTOs.Games;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.TrainingVideos;

namespace RookHub.Api.Services.Og;

/// <summary>
/// Link-Vorschau der öffentlichen Teilen-Links, die bis 2026-11-22 keine hatten (Inventur in <c>docs/api/link-vorschau.md</c>):
/// <list type="bullet">
/// <item><c>/tl/{token}</c> (+ <c>/drill</c>) — Trainerstunde (<c>lesson</c>/<c>ldrill</c>), nur bei GÜLTIGEM Link, gelesen wie die
/// Gast-Seite (<see cref="TrainingLessonShares.ResolveAsync"/> + <see cref="TrainingVideoService.GetForGuestAsync"/>), ohne Namen.</item>
/// <item><c>/s/{token}/club-games/{id}</c> (+ <c>/summary</c>) — Vereinspartie über den Liga-Link (<c>scgame</c>, Kennung
/// <c>{token}~{id}</c>), gelesen über <see cref="League.LeagueShareClubGames"/>: anonymisiert nur der Anonymisierungs-Name.</item>
/// <item><c>/ga/{token}/summary</c> — anonyme Zusammenfassung (<c>ganon</c>): allgemeine Karte OHNE Spieler, Elo, Ereignis, Datum.</item>
/// <item><c>/explorer/game/masters/{id}</c> — Meisterpartie (<c>master</c>), veröffentlichte Turnierpartie mit Namen.</item>
/// <item><c>/{slug}</c>, <c>/{slug}/{kapitel}</c> — öffentlicher Kurs (<c>course</c>, Kennung <c>{slug}</c> bzw.
/// <c>{slug}~{kapitel base64url}</c>). Nur wenn der nginx die Anfrage als Kurs-Kandidat markiert (<see cref="ParseCoursePath"/>).</item>
/// </list>
/// Bilder widerrufbarer bzw. ablaufender Links kurz gecacht; ungültig/abgelaufen/widerrufen → keine Tags, Bild 404.
/// </summary>
public partial class OgMetaService
{
    internal const string LessonKind = "lesson";
    internal const string LessonDrillKind = "ldrill";
    internal const string LeagueClubGameKind = "scgame";
    internal const string AnonSummaryKind = "ganon";
    internal const string MasterGameKind = "master";
    internal const string CourseKind = "course";

    /// <summary>Kennungs-Trenner für zusammengesetzte Ids (Token + Partie, Slug + Kapitel). Base64url und Slugs kennen kein „~".</summary>
    private const char IdSeparator = '~';

    private static readonly Regex SlugShape = new("^[a-z0-9-]{1,40}$", RegexOptions.Compiled);

    /// <summary>Die neuen RookHub-Teilen-Routen; <c>null</c> = keine davon (der Aufrufer prüft dann seine übrigen Fälle).</summary>
    private static (string Kind, string Id)? ParseShareRoute(string[] segments) => segments switch
    {
        ["tl", var t] when IsToken(t) => (LessonKind, t),
        ["tl", var t, "drill"] when IsToken(t) => (LessonDrillKind, t),
        ["ga", var t, "summary"] when IsToken(t) => (AnonSummaryKind, t),
        ["s", var t, "club-games", var id] when LeagueClubGameId(t, id) is { } scg => (LeagueClubGameKind, scg),
        ["s", var t, "club-games", var id, "summary"] when LeagueClubGameId(t, id) is { } scg => (LeagueClubGameKind, scg),
        ["explorer", "game", "masters", var id] when LocalExplorerClient.IsGameId(id) => (MasterGameKind, id),
        _ => null,
    };

    private static string? LeagueClubGameId(string token, string id) =>
        IsToken(token) && int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? $"{token}{IdSeparator}{n}" : null;

    private static bool IsToken(string t) => t.Length is > 0 and <= 128 && !t.Contains(IdSeparator);

    /// <summary>
    /// Kurz-URL eines öffentlichen Kurses: <c>/{slug}</c> oder <c>/{slug}/{kapitel}</c>. Die Kurs-Routen der SPA haben keinen festen
    /// Präfix (jedes unbekannte Einzelsegment fällt dorthin), deshalb schickt der nginx sie NUR für Vorschau-Bots und mit
    /// <c>X-Og-Route: course</c> hierher; ob es den Kurs gibt, entscheidet erst die Datenbank (sonst die unveränderte Shell).
    /// </summary>
    internal static (string Kind, string Id)? ParseCoursePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var segments = path.Split('?')[0].Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is < 1 or > 2) return null;
        var slug = Uri.UnescapeDataString(segments[0]).Trim().ToLowerInvariant();
        if (!SlugShape.IsMatch(slug)) return null;
        if (segments.Length == 1) return (CourseKind, slug);
        var chapter = Uri.UnescapeDataString(segments[1]).Trim();
        if (chapter.Length is 0 or > 120) return null;
        return (CourseKind, $"{slug}{IdSeparator}{B64Url(chapter)}");
    }

    private static string B64Url(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? FromB64Url(string s)
    {
        try
        {
            var b = s.Replace('-', '+').Replace('_', '/');
            return Encoding.UTF8.GetString(Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=')));
        }
        catch (FormatException) { return null; }
    }

    private static (string Token, int Id)? SplitTokenId(string id)
    {
        var i = id.LastIndexOf(IdSeparator);
        return i > 0 && int.TryParse(id[(i + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? (id[..i], n) : null;
    }

    private static string Day(DateOnly d) => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    private static string Day(string iso) => DateOnly.TryParse(iso, CultureInfo.InvariantCulture, out var d) ? Day(d) : iso;

    /// <summary>Eine brauchbare Stellung oder die Grundstellung (Aufbereitungs-FENs sind nicht immer vollständig).</summary>
    private static string FenOrStart(string? fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) return StartFen;
        try { return ChessBoard.LoadFromFen(fen.Trim()).ToFen(); }
        catch { return StartFen; }
    }

    // ---- /tl/{token} (+ /drill) ------------------------------------------------------------------------------------------

    /// <summary>Die Stunde hinter einem GÜLTIGEN Link, wie sie der Gast sieht; sonst <c>null</c>.</summary>
    private async Task<DTOs.TrainingVideos.TrainingLessonDto?> GuestLessonAsync(string token, CancellationToken ct)
    {
        if (_lessonShares is null || _lessons is null) return null;
        var (state, lessonId) = await _lessonShares.ResolveAsync(token, ct);
        return state == TrainingShareState.Valid ? await _lessons.GetForGuestAsync(lessonId, ct) : null;
    }

    private static int DrillCount(DTOs.TrainingVideos.TrainingLessonDto l) => l.Segments.Count(s => s.TaskFen != null || s.HasQuestion);

    /// <summary>Karte einer Trainerstunde: erste besprochene Stellung, Datum, Umfang — ohne Namen (keine Partie-, keine Konto-Namen).</summary>
    internal static OgCard LessonCard(DTOs.TrainingVideos.TrainingLessonDto l, bool drill)
    {
        var first = l.Segments.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.TaskFen ?? s.Fen));
        var lines = new List<string>();
        var games = l.Games.Count;
        var positions = drill ? DrillCount(l) : l.Segments.Count(s => !string.IsNullOrWhiteSpace(s.Fen));
        var size = new List<string>();
        if (games > 0) size.Add($"{games} {(games == 1 ? "game" : "games")}");
        if (positions > 0) size.Add(drill ? $"{positions} {(positions == 1 ? "position" : "positions")} to solve" : $"{positions} {(positions == 1 ? "position" : "positions")} discussed");
        if (size.Count > 0) lines.Add(string.Join(" · ", size));
        if (!drill && l.DurationSec is > 0 and var sec) lines.Add($"{(sec + 59) / 60} min{(l.HasVideo ? " video" : "")}");
        return new OgCard
        {
            Boards = [new OgDiagram(FenOrStart(first?.TaskFen ?? first?.Fen))],
            Kicker = drill ? "Training drill" : "Training session",
            Title = Day(l.Date),
            Lines = lines,
        };
    }

    // ---- gemeinsame Auflösung ---------------------------------------------------------------------------------------------

    private async Task<OgPage?> ResolveSharePageAsync(string kind, string id, string? path, string img, string canonical, CancellationToken ct)
    {
        switch (kind)
        {
            case LessonKind:
            case LessonDrillKind:
            {
                if (await GuestLessonAsync(id, ct) is not { } l) return null;
                var drill = kind == LessonDrillKind;
                var title = $"{(drill ? "Training drill" : "Training session")} · {Day(l.Date)}";
                var desc = drill
                    ? $"{DrillCount(l)} positions from a training session — find the moves on RookHub"
                    : !string.IsNullOrWhiteSpace(l.Summary) ? Shorten(l.Summary.Trim(), 200)
                    : string.Join(" · ", LessonCard(l, false).Lines.Append("watch the training session on RookHub"));
                return new OgPage(title, desc, $"{img}?c={CardVersion}", canonical, "article");
            }
            case LeagueClubGameKind:
            {
                if (SplitTokenId(id) is not var (token, gameId) || _leagueClubGames is null
                    || await _leagueClubGames.ReadAsync(token, gameId, ct) is not { } g) return null;
                var version = CurveVersion(await _leagueClubGames.EvalsAsync(token, gameId, ct));
                var desc = string.Join(" · ", new[] { g.Result is { Length: > 0 } r && r != "*" ? r : null, g.Event, "Partie auf RookHub nachspielen" }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
                return new OgPage(GameTitle(Player(g.White), g.WhiteElo, Player(g.Black), g.BlackElo), desc,
                    $"{img}?c={CardVersion}{(version == null ? "" : $"&v={version}")}", canonical, "article");
            }
            case AnonSummaryKind:
            {
                if (_anonGames is null || await _anonGames.ReadAsync(id, ct) is not { } a) return null;
                var card = AnonSummaryCard(a);
                return new OgPage("Game summary", string.Join(" · ", card.Lines.Prepend(card.Result).Where(x => !string.IsNullOrWhiteSpace(x))
                    .Append("an anonymous game summary on RookHub")), $"{img}?c={CardVersion}", canonical, "article");
            }
            case MasterGameKind:
            {
                if (await MasterGameAsync(id, ct) is not { } m) return null;
                var desc = string.Join(" · ", new[] { m.Result is { Length: > 0 } r && r != "*" ? r : null, EventOf(m.Pgn),
                    m.PlayedAt?.Year.ToString(CultureInfo.InvariantCulture), "master game — replay it on RookHub" }.Where(x => !string.IsNullOrWhiteSpace(x)));
                return new OgPage(GameTitle(Player(m.White), m.WhiteElo, Player(m.Black), m.BlackElo), desc, $"{img}?c={CardVersion}", canonical, "article");
            }
            case CourseKind:
            {
                if (await CourseAsync(id, ct) is not { } c) return null;
                var title = c.Chapter is null ? c.Name : $"{c.Chapter} · {c.Name}";
                var desc = !string.IsNullOrWhiteSpace(c.Description) && c.Chapter is null ? Shorten(c.Description.Trim(), 200)
                    : $"{c.Count} {(c.Count == 1 ? "position" : "positions")} · free chess course on RookHub — no account needed";
                return new OgPage(title, desc, $"{img}?c={CardVersion}", canonical);
            }
        }
        return null;
    }

    private async Task<OgBoard?> ResolveShareBoardAsync(string kind, string id, CancellationToken ct)
    {
        switch (kind)
        {
            case LessonKind:
            case LessonDrillKind:
            {
                if (await GuestLessonAsync(id, ct) is not { } l) return null;
                var card = LessonCard(l, kind == LessonDrillKind);
                // Läuft ab und ist widerrufbar: kurz, damit ein Zwischenspeicher das Bild nach dem Widerruf nicht lange zeigt.
                return new OgBoard(card.Boards[0].Fen, false, Card: card, MaxAge: EditableMaxAge);
            }
            case LeagueClubGameKind:
            {
                if (SplitTokenId(id) is not var (token, gameId) || _leagueClubGames is null
                    || await _leagueClubGames.ReadAsync(token, gameId, ct) is not { } g) return null;
                var curve = CurveOf(await _leagueClubGames.EvalsAsync(token, gameId, ct));
                var card = GameCard(ClubGameAsShared(g), null, curve);
                // Liga-Link: widerrufbar, läuft ab, Partien werden umgehängt — kurz wie die Begegnung selbst.
                return new OgBoard(card.Boards[0].Fen, card.Boards[0].Flip, curve, Card: card, MaxAge: EditableMaxAge);
            }
            case AnonSummaryKind:
            {
                if (_anonGames is null || await _anonGames.ReadAsync(id, ct) is not { } a) return null;
                var card = AnonSummaryCard(a);
                return new OgBoard(card.Boards[0].Fen, card.Boards[0].Flip, Card: card, MaxAge: ExpiringMaxAge);
            }
            case MasterGameKind:
            {
                if (await MasterGameAsync(id, ct) is not { } m) return null;
                var card = GameCard(m, null, null) with { Kicker = "Master game" };
                return new OgBoard(card.Boards[0].Fen, false, Card: card);   // veröffentlichte Partie: fest je Adresse
            }
            case CourseKind:
            {
                if (await CourseAsync(id, ct) is not { } c) return null;
                var card = CourseCard(c);
                // Kurse werden bearbeitet und können ihren öffentlichen Kurznamen verlieren.
                return new OgBoard(card.Boards[0].Fen, card.Boards[0].Flip, Card: card, MaxAge: ExpiringMaxAge);
            }
        }
        return null;
    }

    // ---- /ga/{token}/summary ----------------------------------------------------------------------------------------------

    /// <summary>Allgemeine Karte der anonymen Zusammenfassung: Endstellung, Ergebnis, Zuglänge — KEIN Spieler, keine Elo, kein Ereignis,
    /// kein Datum (der anonyme Link liefert sie gar nicht erst).</summary>
    internal static OgCard AnonSummaryCard(AnonSharedGameDto a)
    {
        var plies = GamePlies.Parse(a.Pgn, MaxPlies)?.Plies ?? [];
        var moves = (plies.Count + 1) / 2;
        return new OgCard
        {
            Boards = [new OgDiagram(EndFenFromPgn(a.Pgn), a.OwnerSide == "black", plies.Count > 0 ? plies[^1].Uci : null)],
            Kicker = "Game summary",
            Title = "How the game was decided",
            Result = string.IsNullOrWhiteSpace(a.Result) || a.Result == "*" ? null : a.Result,
            Lines = plies.Count > 0 ? [$"{moves} {(moves == 1 ? "move" : "moves")}"] : [],
        };
    }

    // ---- /explorer/game/masters/{id} --------------------------------------------------------------------------------------

    private async Task<SharedGameDto?> MasterGameAsync(string id, CancellationToken ct)
    {
        if (_explorer is null || _memory is null || !LocalExplorerClient.IsGameId(id)) return null;
        var result = await _explorer.CachedMasterPgnAsync(_memory, id, ct);
        return result.Pgn is { } pgn ? MasterAsShared(pgn) : null;
    }

    /// <summary>Kopf einer Meisterpartie in der Form der geteilten Partie (für <see cref="GameCard"/>).</summary>
    internal static SharedGameDto MasterAsShared(string pgn)
    {
        var h = Services.PgnParser.SplitGames(pgn).Select(x => x.Headers).FirstOrDefault() ?? new Dictionary<string, string>();
        string? H(string k) => h.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) && v != "?" ? v.Trim() : null;
        int? Elo(string k) => int.TryParse(H(k), NumberStyles.None, CultureInfo.InvariantCulture, out var e) && e > 0 ? e : null;
        DateTime? played = H("Date") is { } d && DateTime.TryParseExact(d.Replace("??", "01"), "yyyy.MM.dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var dt) ? dt : null;
        return new SharedGameDto
        {
            White = H("White"), Black = H("Black"), WhiteElo = Elo("WhiteElo"), BlackElo = Elo("BlackElo"),
            Result = H("Result"), Pgn = pgn, PlayedAt = played, Source = string.Empty,
        };
    }

    // ---- /{slug}, /{slug}/{kapitel} ---------------------------------------------------------------------------------------

    internal sealed record CourseInfo(string Name, string? Description, string? Chapter, int Count, string Fen, string? Moves, int StartPly, bool Calculation);

    /// <summary>Öffentlicher Kurs (dieselbe Bedingung wie <see cref="CourseService.ResolvePublicSlugAsync"/>: <c>IsPublic</c> + Kurzname)
    /// samt erster Stellung in Lesereihenfolge — des Kapitels, wenn eins genannt ist (Name getrimmt, ohne Groß/klein, wie
    /// <see cref="CourseService.ResolvePublicSlugChapterAsync"/>).</summary>
    private async Task<CourseInfo?> CourseAsync(string id, CancellationToken ct)
    {
        var sep = id.IndexOf(IdSeparator);
        var slug = sep < 0 ? id : id[..sep];
        string? wanted = null;
        if (sep >= 0 && (wanted = FromB64Url(id[(sep + 1)..])?.Trim()) is not { Length: > 0 }) return null;
        if (!SlugShape.IsMatch(slug)) return null;
        var book = await _db.Books.AsNoTracking().Where(b => b.IsPublic && b.PublicSlug == slug)
            .Select(b => new { b.Id, b.DisplayName, b.Description, b.IsCalculation }).FirstOrDefaultAsync(ct);
        if (book is null) return null;
        var lines = await _db.BookPuzzles.AsNoTracking().Where(bp => bp.BookId == book.Id).InReadingOrder()
            .Select(bp => new { bp.Chapter, bp.Fen, bp.Moves, bp.StartPly, bp.IsInfoOnly }).ToListAsync(ct);
        string? chapter = null;
        if (wanted is not null)
        {
            chapter = lines.Select(l => ChapterOrder.NormalizeChapter(l.Chapter))
                .FirstOrDefault(n => n != null && string.Equals(n.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
            if (chapter is null) return null;
            lines = lines.Where(l => ChapterOrder.NormalizeChapter(l.Chapter) == chapter).ToList();
        }
        var first = lines.FirstOrDefault(l => !l.IsInfoOnly) ?? lines.FirstOrDefault();
        return new CourseInfo(string.IsNullOrWhiteSpace(book.DisplayName) ? "Chess course" : book.DisplayName, book.Description, chapter,
            lines.Count, first?.Fen ?? StartFen, first?.Moves, first?.StartPly ?? -1, book.IsCalculation);
    }

    /// <summary>Karte eines öffentlichen Kurses: erste Aufgabe in Löser-Sicht (Kalkulationsbuch: die Stellung selbst), Name, Kapitel, Umfang.</summary>
    internal static OgCard CourseCard(CourseInfo c)
    {
        var (pos, last) = c.Calculation ? (FenOrStart(c.Fen), null) : SolverPosition(FenOrStart(c.Fen), c.Moves, c.StartPly);
        var white = FenFields.WhiteToMove(pos);
        var lines = new List<string>();
        if (c.Chapter is not null) lines.Add(c.Name);
        lines.Add($"{c.Count} {(c.Count == 1 ? "position" : "positions")}");
        lines.Add("Free on RookHub — no account needed");
        return new OgCard
        {
            Boards = [new OgDiagram(pos, !white, last)],
            Kicker = c.Chapter is null ? "Chess course" : "Course chapter",
            Title = c.Chapter ?? c.Name,
            ToMoveWhite = white,
            Lines = lines,
        };
    }
}
