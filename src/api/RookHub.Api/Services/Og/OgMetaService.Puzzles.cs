using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Services.Og;

/// <summary>
/// Karten für Puzzles (Lichess-Puzzle, Buch-Puzzle, Tagespuzzle), den Wochenpost und eine geteilte Stellung
/// (<c>/analysis?fen=…</c>, ⋮ → „Stellung teilen"). Das Diagramm zeigt die Stellung, die der LÖSER vor sich hat — nach dem
/// Setup-Zug des Gegners, aus seiner Sicht, mit Zugmarke — und nie die Lösung.
/// </summary>
public partial class OgMetaService
{
    /// <summary>Platzierung + Zugrecht einer FEN, wie die Analyse-Seite sie annimmt (Rochade/e.p. optional).</summary>
    private static readonly Regex FenShape = new(@"^([pnbrqkPNBRQK1-8]{1,8}/){7}[pnbrqkPNBRQK1-8]{1,8} [wb]( [KQkqA-Ha-h-]{1,4}( ([a-h][36]|-)( \d{1,4}( \d{1,4})?)?)?)?$", RegexOptions.Compiled);

    /// <summary>Lichess-Themen, die nichts über die Stellung sagen (Länge, Herkunft, Bewertungsstufe).</summary>
    private static readonly HashSet<string> QuietThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "short", "long", "veryLong", "oneMove", "master", "masterVsMaster", "superGM", "crushing", "advantage", "equality",
        "opening", "middlegame", "endgame",
    };

    /// <summary>
    /// Die Stellung, vor der der Löser sitzt: <paramref name="startPly"/> −1 = die FEN selbst; 0 = nach dem Setup-Zug
    /// <c>moves[0]</c>; k = nach Vorspiel <c>moves[0..k-1]</c> und Setup-Zug <c>moves[k]</c> (wie <c>setupSolver</c> im Client).
    /// Lässt sich ein Zug nicht spielen (Buch-FENs sind nicht immer legal), bleibt es bei der Stellung davor.
    /// </summary>
    internal static (string Fen, string? LastMove) SolverPosition(string fen, string? moves, int startPly)
    {
        var list = (moves ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var count = startPly < 0 ? 0 : Math.Min(startPly + 1, list.Length);
        string? last = null;
        for (var i = 0; i < count; i++)
        {
            if (MoveComparisonService.PlayUci(fen, list[i]) is not { } next) break;
            fen = next;
            last = list[i];
        }
        return (fen, last);
    }

    /// <summary>„mateIn2 fork" → „Mate in 2 · Fork" — höchstens drei, ohne die Längen-/Herkunfts-Themen.</summary>
    internal static string? Themes(string? themes)
    {
        if (string.IsNullOrWhiteSpace(themes)) return null;
        var shown = themes.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !QuietThemes.Contains(t)).Take(3).Select(Humanize).ToList();
        return shown.Count == 0 ? null : string.Join(" · ", shown);
    }

    private static string Humanize(string theme)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < theme.Length; i++)
        {
            var c = theme[i];
            if (i > 0 && (char.IsUpper(c) || char.IsDigit(c) && !char.IsDigit(theme[i - 1]))) sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static string SideToMove(string fen) => FenFields.WhiteToMove(fen) ? "White to move" : "Black to move";

    /// <summary>Karte eines Puzzles: Diagramm aus Sicht des Lösers mit dem Setup-Zug als Zugmarke, „X to move", Angaben.</summary>
    internal static OgCard PuzzleCard(string fen, string? moves, int startPly, string kicker, string? title, IEnumerable<string?> lines)
    {
        var (pos, last) = SolverPosition(fen, moves, startPly);
        var white = FenFields.WhiteToMove(pos);
        return new OgCard
        {
            Boards = [new OgDiagram(pos, !white, last)],
            Kicker = kicker,
            Title = title,
            ToMoveWhite = white,
            Lines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!).Append("Find the best move on RookHub").ToList(),
        };
    }

    /// <summary><c>/analysis?fen=…&amp;orientation=black</c> → Bild-Kennung „w-…" bzw. „b-…" (FEN base64url); <c>null</c> = keine Stellung.</summary>
    internal static string? PositionId(string? fen, string? orientation)
    {
        fen = fen?.Trim();
        if (string.IsNullOrEmpty(fen) || fen.Length > 100 || !FenShape.IsMatch(fen)) return null;
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(fen)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (orientation == "black" ? "b-" : "w-") + b64;
    }

    /// <summary>Gegenstück zu <see cref="PositionId"/>; <c>null</c> bei allem, was keine Stellung ist.</summary>
    internal static (string Fen, bool Flip)? ParsePositionId(string id)
    {
        if (id.Length < 3 || id[1] != '-' || id[0] is not ('w' or 'b')) return null;
        try
        {
            var s = id[2..].Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            var fen = Encoding.UTF8.GetString(Convert.FromBase64String(s));
            return fen.Length <= 100 && FenShape.IsMatch(fen) ? (fen, id[0] == 'b') : null;
        }
        catch (FormatException) { return null; }
    }

    internal static OgCard PositionCard(string fen, bool flip) => new()
    {
        Boards = [new OgDiagram(fen, flip)],
        Kicker = "Chess position",
        ToMoveWhite = FenFields.WhiteToMove(fen),
        Lines = ["Analyse it on RookHub — no account needed"],
    };

    private async Task<Models.WeeklyPost?> PublishedWeeklyAsync(string id, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var wid)) return null;
        var now = DateTime.UtcNow;
        // Nur veröffentlichte Posts — wie WeeklyPostController ohne weeklyposts.manage.
        return await _db.WeeklyPosts.AsNoTracking().FirstOrDefaultAsync(w => w.Id == wid && w.ScheduledAt <= now, ct);
    }

    private async Task<OgCard?> WeeklyCardAsync(Models.WeeklyPost w)
    {
        List<DTOs.Courses.BookPuzzleDto> puzzles = [];
        if (_weekly != null)
        {
            try { puzzles = await _weekly.GetPlayPuzzlesAsync(w); }
            catch (Exception ex) { _logger.LogDebug(ex, "OG: Wochenpost {Id} ohne Puzzles.", w.Id); }
        }
        var lines = new List<string?> { puzzles.Count == 1 ? "1 puzzle" : $"{puzzles.Count} puzzles",
            w.ScheduledAt.ToString("d MMM yyyy", CultureInfo.InvariantCulture) };
        if (puzzles.Count == 0)
            return new OgCard { Kicker = "Weekly post", Title = w.Title, Lines = lines.Where(l => l != null).Select(l => l!).ToList() };
        var first = puzzles[0];
        return PuzzleCard(first.Fen, first.Moves, first.StartPly, "Weekly post", w.Title, lines);
    }

    private async Task<OgPage?> ResolvePuzzlePageAsync(string kind, string id, string? path, string baseUrl, string img, string canonical, CancellationToken ct)
    {
        switch (kind)
        {
            case "puzzle":
            {
                if (!int.TryParse(id, out var pid)) return null;
                var p = await _puzzles.GetByIdAsync(pid);
                if (p is null) return null;
                var (pos, _) = SolverPosition(p.Fen, p.Moves, 0);
                var desc = string.Join(" · ", new[] { $"Rating {p.Rating}", Themes(p.Themes), "find the best move on RookHub" }.Where(x => x != null));
                return new OgPage($"Chess puzzle #{pid} · {SideToMove(pos)}", desc, $"{img}?c={CardVersion}", canonical);
            }
            case "book":
            {
                if (!int.TryParse(id, out var bid)) return null;
                var b = await _bookPuzzles.GetByIdAsync(bid);
                if (b is null) return null;
                var (pos, _) = SolverPosition(b.Fen, b.Moves, b.StartPly);
                var title = !string.IsNullOrWhiteSpace(b.BookTitle) ? b.BookTitle! : "Chess puzzle";
                return new OgPage(title, $"{SideToMove(pos)} — find the best move on RookHub", $"{img}?c={CardVersion}", canonical);
            }
            case "daily":
            {
                if (!TryParseDaily(id, out var date)) return null;
                var d = await _bookPuzzles.GetOrAssignDailyAsync(date);
                if (d is null) return null;
                var (pos, _) = SolverPosition(d.Fen, d.Moves, d.StartPly);
                // „today" bekäme sonst eine Woche lang dasselbe Bild (immutable) — die Adresse trägt das Datum.
                var dated = $"{baseUrl}/api/og/img/daily/{date:yyyyMMdd}.png?c={CardVersion}";
                return new OgPage($"Daily puzzle {date:yyyy-MM-dd}", $"{SideToMove(pos)} — solve today's puzzle on RookHub", dated, canonical);
            }
            case "weekly":
            {
                var w = await PublishedWeeklyAsync(id, ct);
                if (w is null) return null;
                var card = await WeeklyCardAsync(w);
                return new OgPage(w.Title, string.Join(" · ", card!.Lines.Take(1).Append("weekly puzzles on RookHub")), $"{img}?c={CardVersion}", canonical);
            }
            case "position":
            {
                if (ParsePositionId(id) is not { } p) return null;
                return new OgPage($"Chess position · {SideToMove(p.Fen)}", "Analyse this position on RookHub — no account needed",
                    $"{img}?c={CardVersion}", canonical);
            }
        }
        return null;
    }

    private async Task<OgBoard?> ResolvePuzzleBoardAsync(string kind, string id, CancellationToken ct)
    {
        switch (kind)
        {
            case "puzzle":
            {
                if (!int.TryParse(id, out var pid)) return null;
                var p = await _puzzles.GetByIdAsync(pid);
                if (p is null) return null;
                var card = PuzzleCard(p.Fen, p.Moves, 0, $"Puzzle #{pid} · Rating {p.Rating}", null, [Themes(p.Themes)]);
                return new OgBoard(card.Boards[0].Fen, card.Boards[0].Flip, Card: card);
            }
            case "book":
            {
                if (!int.TryParse(id, out var bid)) return null;
                var b = await _bookPuzzles.GetByIdAsync(bid);
                if (b is null) return null;
                var card = PuzzleCard(b.Fen, b.Moves, b.StartPly, string.IsNullOrWhiteSpace(b.BookTitle) ? "Chess puzzle" : b.BookTitle!, null,
                    [b.BookRating is int r ? $"Rating {r}" : null]);
                return new OgBoard(card.Boards[0].Fen, card.Boards[0].Flip, Card: card);
            }
            case "daily":
            {
                if (!TryParseDaily(id, out var date)) return null;
                var d = await _bookPuzzles.GetOrAssignDailyAsync(date);
                if (d is null) return null;
                var card = PuzzleCard(d.Fen, d.Moves, d.StartPly, $"Daily puzzle · {date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}", null, []);
                // „today" ändert sich täglich: kurz cachen; ein festes Datum bleibt unveränderlich.
                return new OgBoard(card.Boards[0].Fen, card.Boards[0].Flip, Card: card,
                    MaxAge: string.Equals(id, "today", StringComparison.OrdinalIgnoreCase) ? ExpiringMaxAge : null);
            }
            case "weekly":
            {
                var w = await PublishedWeeklyAsync(id, ct);
                if (w is null) return null;
                var card = (await WeeklyCardAsync(w))!;
                return new OgBoard(card.Boards.Count > 0 ? card.Boards[0].Fen : StartFen, false, Card: card, MaxAge: ExpiringMaxAge);
            }
            case "position":
            {
                if (ParsePositionId(id) is not { } p) return null;
                return new OgBoard(p.Fen, p.Flip, Card: PositionCard(p.Fen, p.Flip));
            }
        }
        return null;
    }
}
