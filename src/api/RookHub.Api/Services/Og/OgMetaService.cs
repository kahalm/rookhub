using System.Text.Json;
using Chess;
using RookHub.Api.DTOs;
using RookHub.Api.Validation;

namespace RookHub.Api.Services.Og;

/// <summary>Auflösung einer öffentlichen Route zu Open-Graph-Metadaten + der zu rendernden Brett-FEN.</summary>
public record OgPage(string Title, string Description, string ImageUrl, string CanonicalUrl, string Type = "website");

/// <summary>Die für das Brett-Bild aufgelöste Stellung (FEN + Perspektive), bei einer analysierten Partie dazu die
/// Bewertungskurve (<see cref="OgMetaService.CurveOf"/>).</summary>
public record OgBoard(string Fen, bool Flip, IReadOnlyList<double?>? Curve = null);

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
    private readonly ILogger<OgMetaService> _logger;

    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public OgMetaService(SavedGameService games, PuzzleService puzzles, BookPuzzleService bookPuzzles,
        CrawlerProxyService crawler, ILogger<OgMetaService> logger)
    {
        _games = games;
        _puzzles = puzzles;
        _bookPuzzles = bookPuzzles;
        _crawler = crawler;
        _logger = logger;
    }

    /// <summary>Zerlegt einen Original-Pfad in (kind, id), oder null wenn keine vorschaubare Route.</summary>
    public static (string Kind, string Id)? ParsePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        // Query abschneiden, führenden Slash entfernen.
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];
        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

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
                    var title = $"{white} – {black}";
                    var descParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(g.Result)) descParts.Add(g.Result!);
                    if (!string.IsNullOrWhiteSpace(g.Source)) descParts.Add(g.Source!);
                    descParts.Add("Partie auf RookHub nachspielen");
                    // „Kurz erzählt" (0.541.0) statt der dürren Kopfzeile, sobald es die Nacherzählung gibt.
                    var description = string.IsNullOrWhiteSpace(g.Recap) ? string.Join(" · ", descParts) : g.Recap!;
                    // Mit der Analyse bekommt das Bild die Kurve — unter NEUER Adresse: das Bild ist „immutable" gecacht,
                    // und Discord & Co. merken sich Bilder ohnehin nach der Adresse.
                    var version = CurveVersion(await _games.GetSharedEvalsAsync(id, null, ct));
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
