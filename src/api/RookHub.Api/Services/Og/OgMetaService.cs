using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Validation;

namespace RookHub.Api.Services.Og;

/// <summary>Auflösung einer öffentlichen Route zu Open-Graph-Metadaten + der zu rendernden Brett-FEN.</summary>
public record OgPage(string Title, string Description, string ImageUrl, string CanonicalUrl, string Type = "website");

/// <summary>Die für das Brett-Bild aufgelöste Stellung (FEN + Perspektive), bei einer analysierten Partie dazu die
/// Bewertungskurve (<see cref="OgMetaService.CurveOf"/>).</summary>
public record OgBoard(string Fen, bool Flip, IReadOnlyList<double?>? Curve = null, OgImageService.TrainCard? Train = null);

/// <summary>
/// Liest aus einer öffentlichen SPA-Route (<c>/g/{token}</c>, <c>/puzzles/*</c>, <c>/t/{id}</c>) die Daten
/// für die Link-Vorschau: Titel/Beschreibung/Bild-URL (für <see cref="OgController"/>) sowie die FEN, aus
/// der <see cref="OgImageService"/> das Brett rendert. Alles best-effort — bei jedem Fehler <c>null</c>,
/// der Controller liefert dann die unveränderte SPA aus bzw. kein Bild.
/// </summary>
public class OgMetaService
{
    private readonly SavedGameService _games;
    private readonly PuzzleService _puzzles;
    private readonly BookPuzzleService _bookPuzzles;
    private readonly CrawlerProxyService _crawler;
    private readonly AppDbContext _db;
    private readonly ILogger<OgMetaService> _logger;

    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public OgMetaService(SavedGameService games, PuzzleService puzzles, BookPuzzleService bookPuzzles,
        CrawlerProxyService crawler, AppDbContext db, ILogger<OgMetaService> logger)
    {
        _games = games;
        _puzzles = puzzles;
        _bookPuzzles = bookPuzzles;
        _crawler = crawler;
        _db = db;
        _logger = logger;
    }

    /// <summary>Die öffentliche Kennung eines Kalendereintrags: chess-results-Nummer oder <c>f&lt;FIDE-Nr.&gt;</c> usw.</summary>
    private static readonly Regex CalendarId = new("^[A-Za-z0-9_-]{1,24}$", RegexOptions.Compiled);

    /// <summary>Zerlegt einen Original-Pfad in (kind, id), oder null wenn keine vorschaubare Route.</summary>
    public static (string Kind, string Id)? ParsePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        // Query abschneiden, führenden Slash entfernen.
        var q = path.IndexOf('?');
        var query = q >= 0 ? path[(q + 1)..] : string.Empty;
        if (q >= 0) path = path[..q];
        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        // Kalendereintrag der Turnierseite: /tournaments/calendar/{id} — oder die ältere Form ?t={id}, wie sie in den
        // Benachrichtigungen steht (TournamentDirectoryService.DetailLink).
        if (segments is ["tournaments", "calendar", ..])
        {
            if (segments.Length >= 3) return ("calendar", segments[2]);
            var t = query.Split('&').Select(p => p.Split('=', 2))
                .FirstOrDefault(p => p.Length == 2 && p[0] == "t")?[1];
            return string.IsNullOrWhiteSpace(t) ? null : ("calendar", Uri.UnescapeDataString(t));
        }

        switch (segments[0])
        {
            case "g" when segments.Length >= 2:
                return ("game", segments[1]);
            case "t" when segments.Length >= 2:
                return ("tournament", segments[1]);
            case "puzzles" when segments.Length >= 3 && segments[1] == "book":
                return ("book", segments[2]);
            case "puzzles" when segments.Length >= 3 && segments[1] == "daily":
                return ("daily", segments[2]);
            case "puzzles" when segments.Length >= 2 && int.TryParse(segments[1], out _):
                return ("puzzle", segments[1]);
            default:
                return null;
        }
    }

    /// <summary>Ein Wert aus dem Query-Teil des Pfads (oder null).</summary>
    internal static string? QueryParam(string? path, string name)
    {
        var q = path?.IndexOf('?') ?? -1;
        if (q < 0) return null;
        var hit = path![(q + 1)..].Split('&').Select(p => p.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == name);
        return hit == null ? null : Uri.UnescapeDataString(hit[1]);
    }

    public async Task<OgPage?> ResolvePageAsync(string? path, string baseUrl, CancellationToken ct = default)
    {
        var parsed = ParsePath(path);
        if (parsed is null) return null;
        var (kind, id) = parsed.Value;
        var canonical = $"{baseUrl}{path}";
        var img = $"{baseUrl}/api/og/img/{kind}/{Uri.EscapeDataString(id)}.png";

        try
        {
            switch (kind)
            {
                case "game":
                {
                    var g = await _games.GetSharedAsync(id);
                    if (g is null) return null;
                    var white = string.IsNullOrWhiteSpace(g.White) ? "?" : g.White!;
                    var black = string.IsNullOrWhiteSpace(g.Black) ? "?" : g.Black!;
                    // Elo im Namen (0.737.0): „Mitteregger, Gottfried (Elo 1826) – Erlacher, Herbert (Elo 1712)"
                    var title = $"{white}{(g.WhiteElo is int we ? $" (Elo {we})" : "")} – {black}{(g.BlackElo is int be ? $" (Elo {be})" : "")}";
                    var descParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(g.Result)) descParts.Add(g.Result!);
                    if (!string.IsNullOrWhiteSpace(g.Source)) descParts.Add(g.Source!);
                    descParts.Add("Partie auf RookHub nachspielen");
                    // „Kurz erzählt" (0.541.0) statt der dürren Kopfzeile, sobald es die Nacherzählung gibt.
                    var description = string.IsNullOrWhiteSpace(g.Recap) ? string.Join(" · ", descParts) : g.Recap!;
                    // Mit Roast geteilt (0.742.0, /g/{token}?roast={id}): der freigegebene Roast-Text ist die Beschreibung.
                    if (int.TryParse(QueryParam(path, "roast"), out var roastId))
                    {
                        var roast = await _db.GameRoasts.AsNoTracking()
                            .Where(r => r.Id == roastId && r.SharedAt != null
                                && _db.SavedGames.Any(s => s.Id == r.SavedGameId && s.ShareToken == id))
                            .Select(r => new { r.Text, r.Language })
                            .FirstOrDefaultAsync(ct);
                        if (roast != null && !string.IsNullOrWhiteSpace(roast.Text))
                            description = PieceLetters.Convert(roast.Text, "en", roast.Language);
                    }
                    // Mit der Analyse bekommt das Bild die Kurve — unter NEUER Adresse: das Bild ist „immutable" gecacht,
                    // und Discord & Co. merken sich Bilder ohnehin nach der Adresse.
                    var evals = await _games.GetSharedEvalsAsync(id, null, ct);
                    var version = CurveVersion(evals);
                    // Trainingslink (0.745.0, /g/{token}?train=white|black, Fehler-Training seit 0.741.0): eigene Karte mit
                    // Überschrift „Verbessere dich" und den Fehlern dieser Seite in der Kurve.
                    if (QueryParam(path, "train") is "white" or "black" && QueryParam(path, "train") is { } side)
                    {
                        var who = side == "white" ? white : black;
                        var n = TrainMarks(evals, side == "white").Count;
                        var trainDesc = n > 0
                            ? $"{n} Fehler von {who} in {white} – {black} — finde jetzt die besseren Züge."
                            : $"{white} – {black}: spiele die Fehler von {who} neu und finde die besseren Züge.";
                        var trainImg = $"{baseUrl}/api/og/img/train/{side}-{Uri.EscapeDataString(id)}.png";
                        return new OgPage(TrainTitle, trainDesc, version == null ? trainImg : $"{trainImg}?v={version}", canonical, "article");
                    }
                    return new OgPage(title, description, version == null ? img : $"{img}?v={version}", canonical, "article");
                }
                case "puzzle":
                {
                    if (!int.TryParse(id, out var pid)) return null;
                    var p = await _puzzles.GetByIdAsync(pid);
                    if (p is null) return null;
                    return new OgPage($"Schachpuzzle #{pid}",
                        "Finde den besten Zug — auf RookHub", img, canonical);
                }
                case "book":
                {
                    if (!int.TryParse(id, out var bid)) return null;
                    var b = await _bookPuzzles.GetByIdAsync(bid);
                    if (b is null) return null;
                    var title = !string.IsNullOrWhiteSpace(b.BookTitle) ? b.BookTitle! : "Schachpuzzle";
                    return new OgPage(title, "Finde den besten Zug — auf RookHub", img, canonical);
                }
                case "daily":
                {
                    if (!TryParseDaily(id, out var date)) return null;
                    var d = await _bookPuzzles.GetOrAssignDailyAsync(date);
                    if (d is null) return null;
                    return new OgPage($"Tagespuzzle {date:yyyy-MM-dd}",
                        "Löse das heutige Puzzle auf RookHub", img, canonical);
                }
                case "tournament":
                {
                    if (!TournamentIdValidator.IsValid(id)) return null;
                    var name = await TryTournamentNameAsync(id, ct);
                    var title = name ?? "Schachturnier";
                    return new OgPage(title, "Turnierdaten live auf RookHub", img, canonical);
                }
                case "calendar":
                {
                    // Ein geteilter Kalender-Link soll im Vorschaufenster die ECKDATEN zeigen (Termin, Ort, Bedenkzeit,
                    // Runden …) — vorher bekam er nur die allgemeine Seitenvorschau der Turnierseite.
                    if (!CalendarId.IsMatch(id)) return null;
                    var entry = await _db.TournamentDirectoryEntries.AsNoTracking()
                        .FirstOrDefaultAsync(e => e.PublicId == id, ct);
                    if (entry is null) return null;
                    return new OgPage(entry.Name, CalendarDescription(entry), img, canonical, "article");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OG: ResolvePage für {Path} fehlgeschlagen.", path);
        }
        return null;
    }

    /// <summary>Löst die zu rendernde Brett-Stellung für ein (kind,id) auf.</summary>
    public async Task<OgBoard?> ResolveBoardAsync(string kind, string id, CancellationToken ct = default)
    {
        try
        {
            switch (kind)
            {
                case "game":
                {
                    var g = await _games.GetSharedAsync(id);
                    if (g is null) return null;
                    // Aus der Sicht des Teilenden: spielte er Schwarz, wird auch die Vorschau gedreht. Die Kurve dreht nicht
                    // mit — wie auf der Seite steht Weiß unten.
                    return new OgBoard(EndFenFromPgn(g.Pgn), Flip: g.OwnerSide == "black",
                        Curve: CurveOf(await _games.GetSharedEvalsAsync(id, null, ct)));
                }
                case "train":
                {
                    // „white-<token>" bzw. „black-<token>" (das Token selbst darf Bindestriche tragen)
                    var dash = id.IndexOf('-');
                    if (dash <= 0) return null;
                    var side = id[..dash];
                    if (side is not ("white" or "black")) return null;
                    var token = id[(dash + 1)..];
                    var g = await _games.GetSharedAsync(token);
                    if (g is null) return null;
                    var evals = await _games.GetSharedEvalsAsync(token, null, ct);
                    var who = side == "white" ? (string.IsNullOrWhiteSpace(g.White) ? "Weiß" : g.White!) : (string.IsNullOrWhiteSpace(g.Black) ? "Schwarz" : g.Black!);
                    return new OgBoard(EndFenFromPgn(g.Pgn), Flip: side == "black", Curve: CurveOf(evals),
                        Train: new OgImageService.TrainCard("Verbessere dich", "Spiele deine Fehler neu", TrainMarks(evals, side == "white"),
                            $"Die Fehler von {who}"));
                }
                case "puzzle":
                {
                    if (!int.TryParse(id, out var pid)) return null;
                    var p = await _puzzles.GetByIdAsync(pid);
                    return p is null ? null : new OgBoard(p.Fen, FlipFromFen(p.Fen));
                }
                case "book":
                {
                    if (!int.TryParse(id, out var bid)) return null;
                    var b = await _bookPuzzles.GetByIdAsync(bid);
                    return b is null ? null : new OgBoard(b.Fen, FlipFromFen(b.Fen));
                }
                case "daily":
                {
                    if (!TryParseDaily(id, out var date)) return null;
                    var d = await _bookPuzzles.GetOrAssignDailyAsync(date);
                    return d is null ? null : new OgBoard(d.Fen, FlipFromFen(d.Fen));
                }
                case "tournament":
                    // Turniere haben keine einzelne Stellung → generisches Schach-Motiv (Grundstellung).
                    return TournamentIdValidator.IsValid(id) ? new OgBoard(StartFen, Flip: false) : null;
                case "calendar":
                    return CalendarId.IsMatch(id) ? new OgBoard(StartFen, Flip: false) : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OG: ResolveBoard für {Kind}/{Id} fehlgeschlagen.", kind, id);
        }
        return null;
    }

    /// <summary>Kurvenhöhe wie <c>graphHeight</c> im Client (<c>game-review.util.ts</c>): 0..100, 50 = ausgeglichen, Weiß-Sicht;
    /// linear bis ±<see cref="GraphCapPawns"/> Bauern, Matt am Rand.</summary>
    public const double GraphCapPawns = 10;

    internal static double? GraphHeight(int? cp, int? mate)
    {
        if (mate is int m) return m > 0 ? 100 : m < 0 ? 0 : null;
        if (cp is not int c) return null;
        var pawns = Math.Clamp(c / 100.0, -GraphCapPawns, GraphCapPawns);
        return 50 + 50 * pawns / GraphCapPawns;
    }

    /// <summary>
    /// Die Bewertungskurve für das Vorschaubild: ein Punkt je Stellung vor jedem Halbzug plus der nach dem letzten
    /// (<see cref="GameEvalsDto.Final"/>), <c>null</c> = Lücke. Nur mit FERTIGER Analyse — eine halbe Kurve sähe im geteilten
    /// Bild wie das Ende der Partie aus. Weniger als zwei Punkte: keine Kurve.
    /// </summary>
    /// <summary>Überschrift der Trainingslink-Karte (Wunsch 2026-10-10).</summary>
    internal const string TrainTitle = "Verbessere dich — spiele deine Fehler neu";

    /// <summary>
    /// Die Fehler einer Seite als Kurven-Index der Stellung NACH dem Zug: ein Zug, der aus Sicht des Ziehenden mindestens
    /// 10 Prozentpunkte Gewinnchance kostet (Fehler und grober Fehler wie im Rückblick, <c>CLASS_LIMITS</c>). Weiß zieht die
    /// geraden Halbzüge (Partie ab der Grundstellung); fehlt eine Bewertung, zählt der Zug nicht.
    /// </summary>
    internal static List<int> TrainMarks(GameEvalsDto? evals, bool white)
    {
        var marks = new List<int>();
        if (evals is null || evals.Status != "done" || evals.Total <= 0) return marks;
        var byPly = evals.Plies.GroupBy(p => p.Ply).ToDictionary(g => g.Key, g => g.First());
        double? Win(int i)
        {
            if (i == evals.Total) return evals.Final is { } f ? WinPercent(f.Cp, f.Mate) : null;
            return byPly.TryGetValue(i, out var p) ? WinPercent(p.Cp, p.Mate) : null;
        }
        for (var k = white ? 0 : 1; k < evals.Total; k += 2)
        {
            if (Win(k) is not double before || Win(k + 1) is not double after) continue;
            var drop = white ? before - after : after - before;
            if (drop >= 10) marks.Add(k + 1);
        }
        return marks;
    }

    /// <summary>Gewinnchance von Weiß in Prozent (Lichess-Formel wie <c>winPercent</c> im Client), Matt = 100/0.</summary>
    internal static double? WinPercent(int? cp, int? mate)
    {
        if (mate is int m) return m > 0 ? 100 : m < 0 ? 0 : null;
        if (cp is not int c) return null;
        return 50 + 50 * (2 / (1 + Math.Exp(-0.00368208 * c)) - 1);
    }

    internal static IReadOnlyList<double?>? CurveOf(GameEvalsDto? evals)
    {
        if (evals is null || evals.Status != "done" || evals.Total <= 0) return null;
        var byPly = evals.Plies.GroupBy(p => p.Ply).ToDictionary(g => g.Key, g => g.First());
        var curve = new List<double?>(evals.Total + 1);
        for (var i = 0; i < evals.Total; i++)
            curve.Add(byPly.TryGetValue(i, out var p) ? GraphHeight(p.Cp, p.Mate) : null);
        curve.Add(evals.Final is { } f ? GraphHeight(f.Cp, f.Mate) : null);
        return curve.Count(v => v is not null) >= 2 ? curve : null;
    }

    /// <summary>Kennung der Kurve für die Bild-Adresse: Analyse + Stand der Vertiefung (die Kurve wird dabei genauer).</summary>
    internal static string? CurveVersion(GameEvalsDto? evals)
        => CurveOf(evals) is not null && evals!.AnalysisId is int id ? $"{id}-{evals.Refined}" : null;

    /// <summary>Endstellung einer Partie aus dem PGN (Fallback: Grundstellung).</summary>
    private static string EndFenFromPgn(string pgn)
    {
        if (!string.IsNullOrWhiteSpace(pgn) && ChessBoard.TryLoadFromPgn(pgn, out var board) && board is not null)
        {
            try { return board.ToFen(); } catch { /* fällt auf Startstellung zurück */ }
        }
        return StartFen;
    }

    /// <summary>Brett aus Sicht der am Zug befindlichen Seite (Schwarz → gedreht).</summary>
    private static bool FlipFromFen(string fen)
    {
        var parts = fen.Split(' ');
        return parts.Length >= 2 && parts[1] == "b";
    }

    private static bool TryParseDaily(string s, out DateOnly date)
    {
        if (string.Equals(s, "today", StringComparison.OrdinalIgnoreCase))
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow);
            return true;
        }
        return DateOnly.TryParseExact(s, "yyyyMMdd", null,
            System.Globalization.DateTimeStyles.None, out date);
    }

    /// <summary>
    /// Die Eckdaten eines Kalendereintrags als EINE Zeile fürs Vorschaufenster — nur was bekannt ist, in fester
    /// Reihenfolge: Termin · Ort · Tempo (Bedenkzeit) · Runden · Teilnehmer · Mannschaft · Veranstalter. Ein aus dem
    /// Kalender genommenes Turnier sagt das vorn.
    /// </summary>
    internal static string CalendarDescription(TournamentDirectoryEntry e)
    {
        var parts = new List<string>();
        if (DateRange(e.StartDate, e.EndDate) is { } dates) parts.Add(dates);

        var place = !string.IsNullOrWhiteSpace(e.GeoPlaceName) ? e.GeoPlaceName : e.LocationText;
        if (!string.IsNullOrWhiteSpace(place)) parts.Add(Shorten(place.Trim(), 60));

        var speed = e.Speed switch
        {
            TournamentSpeed.Standard => "Turnierschach",
            TournamentSpeed.Rapid => "Schnellschach",
            TournamentSpeed.Blitz => "Blitz",
            _ => null,
        };
        var tc = string.IsNullOrWhiteSpace(e.TimeControlText) ? null : Shorten(e.TimeControlText.Trim(), 40);
        if (speed is not null || tc is not null)
            parts.Add(speed is not null && tc is not null ? $"{speed} ({tc})" : speed ?? tc!);

        if (e.Rounds is > 0) parts.Add(e.Rounds == 1 ? "1 Runde" : $"{e.Rounds} Runden");
        if (e.PlayerCount is > 0) parts.Add($"{e.PlayerCount} Teilnehmer");
        if (e.Kind == TournamentKind.Team) parts.Add("Mannschaftsturnier");
        if (!string.IsNullOrWhiteSpace(e.Organizer)) parts.Add($"Veranstalter: {Shorten(e.Organizer.Trim(), 50)}");

        var line = parts.Count == 0 ? "Turnier im Kalender der Turnierseite" : string.Join(" · ", parts);
        return e.RemovedAt is null ? line : $"Nicht mehr ausgeschrieben · {line}";
    }

    /// <summary>„27.09.2026", „27.–29.09.2026", „30.09.–02.10.2026", „30.12.2026–02.01.2027".</summary>
    internal static string? DateRange(DateOnly? start, DateOnly? end)
    {
        if (start is null && end is null) return null;
        var inv = CultureInfo.InvariantCulture;
        var a = start ?? end!.Value;
        var b = end ?? a;
        if (b <= a) return a.ToString("dd.MM.yyyy", inv);
        if (a.Year != b.Year) return $"{a.ToString("dd.MM.yyyy", inv)}–{b.ToString("dd.MM.yyyy", inv)}";
        if (a.Month != b.Month) return $"{a.ToString("dd.MM.", inv)}–{b.ToString("dd.MM.yyyy", inv)}";
        return $"{a.ToString("dd.", inv)}–{b.ToString("dd.MM.yyyy", inv)}";
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    private async Task<string?> TryTournamentNameAsync(string id, CancellationToken ct)
    {
        try
        {
            var json = await _crawler.GetAsync($"/api/tournaments/{id}", ct);
            foreach (var key in new[] { "name", "tournamentName", "title", "eventName" })
            {
                if (json.ValueKind == JsonValueKind.Object &&
                    json.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "OG: Turniername für {Id} nicht auflösbar.", id);
        }
        return null;
    }
}
