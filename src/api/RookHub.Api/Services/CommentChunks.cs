using System.Text;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Kommentare einer Bibliothekspartie → Textstücke für die semantische Suche (<see cref="CommentSearchService"/>).
/// Jedes Stück beginnt mit der Kopfzeile (Spieler, Turnier, Jahr — „die Partie, in der Carlsen …" soll auch über
/// Namen finden) und reiht dann kommentierte Halbzüge aneinander („17. Bxh7+: …"), bis etwa <see cref="TargetChars"/>
/// Zeichen. Figurenzeichen der ChessBase-Schrift werden aufgelöst (<see cref="Figurines"/>), sonst stünde im Text
/// „…h7+" statt „Bxh7+".
/// </summary>
public static class CommentChunks
{
    public const int TargetChars = 900;
    public const int MaxChars = 1500;

    public sealed record Chunk(int FromPly, int ToPly, string Text);

    public static List<Chunk> Build(LibraryGame game)
    {
        var chunks = new List<Chunk>();
        if (string.IsNullOrEmpty(game.Pgn) || !game.Pgn.Contains('{')) return chunks;
        var parsed = PgnParser.SplitGames(game.Pgn).FirstOrDefault();
        if (parsed.MoveText is null) return chunks;
        var comments = PgnParser.ExtractMoveComments(parsed.MoveText);
        if (comments is null || comments.Count == 0) return chunks;
        var plies = GamePlies.Parse(game.Pgn, 600)?.Plies ?? new List<GamePlies.Ply>();
        var lang = (game.Languages ?? "en").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l != "und") ?? "en";
        var (startMove, blackFirst) = StartOf(game.StartFen);
        var header = Header(game);

        var sb = new StringBuilder(header);
        int? from = null;
        var to = -1;
        void Flush()
        {
            if (from is int f) chunks.Add(new Chunk(f, to, sb.ToString().Trim()));
            sb.Clear().Append(header);
            from = null;
        }
        foreach (var (ply, raw) in comments.OrderBy(c => c.Key))
        {
            var text = Figurines.Apply(raw, lang).Trim();
            if (text.Length == 0) continue;
            string label;
            if (ply < 0) label = "Intro";
            else
            {
                var san = ply < plies.Count ? plies[ply].San : "?";
                var half = ply + (blackFirst ? 1 : 0);
                var number = startMove + half / 2;
                label = half % 2 == 0 ? $"{number}. {san}" : $"{number}... {san}";
            }
            var entry = $"{label}: {text}";
            if (entry.Length > MaxChars - header.Length) entry = entry[..(MaxChars - header.Length - 1)] + "…";
            if (from != null && sb.Length + entry.Length + 1 > TargetChars) Flush();
            sb.Append('\n').Append(entry);
            from ??= ply;
            to = ply;
        }
        Flush();
        return chunks;
    }

    private static string Header(LibraryGame g)
    {
        var year = g.PlayedOn is DateOnly d ? $" {d.Year}" : "";
        var evt = string.IsNullOrWhiteSpace(g.Event) || g.Event == "?" ? "" : $", {g.Event}";
        return $"{g.White ?? "?"} – {g.Black ?? "?"}{evt}{year}" + (string.IsNullOrWhiteSpace(g.Annotator) ? "" : $" (annotated by {g.Annotator})");
    }

    private static (int StartMove, bool BlackFirst) StartOf(string? fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) return (1, false);
        var parts = fen.Split(' ');
        var black = parts.Length > 1 && parts[1] == "b";
        var move = parts.Length > 5 && int.TryParse(parts[5], out var n) && n > 0 ? n : 1;
        return (move, black);
    }
}
