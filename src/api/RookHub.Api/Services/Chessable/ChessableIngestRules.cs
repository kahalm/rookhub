using RookHub.Api.DTOs.Chessable;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Reine Formregeln der Ingest-Anfragen der Erweiterung (<c>ingest</c>, <c>ingest/chunk</c>, <c>ingest/live</c>).
/// Aus dem <c>ExtensionController</c> gelöst (Codereview A3-012, Zerlegung Z3), damit der Chunk-Import sie im
/// Dienst prüfen kann.
/// </summary>
public static class ChessableIngestRules
{
    /// <summary>Form der <c>LineOids</c> prüfen — dieselben Regeln wie piratechess, aber hier mit klarer Meldung
    /// statt eines durchgereichten 400. <c>null</c> = in Ordnung.</summary>
    public static string? ValidateLineOids(IEnumerable<ChessableIngestChapter> chapters)
    {
        foreach (var ch in chapters)
        {
            var lines = ch.Lines ?? new List<string>();
            if (ch.LineOids is null)
            {
                if (lines.Any(string.IsNullOrEmpty))
                    return "A line without content needs lineOids (it is filled from the shared cache).";
                continue;
            }
            if (ch.LineOids.Count != lines.Count)
                return "lineOids must have exactly one entry per line.";
            if (!ch.LineOids.All(oid => ChessableIds.TryParseOid(oid, out _)))
                return "Invalid oid in lineOids.";
        }
        return null;
    }

    /// <summary>Soll piratechess Linien aus dem geteilten Cache einsetzen? Das sind genau die Linien ohne Inhalt
    /// (nach <see cref="ValidateLineOids"/> tragen sie immer eine oid) — dieselbe Regel wie <c>OidsToFill</c> dort.</summary>
    public static bool WantsCacheFill(IEnumerable<ChessableIngestChapter> chapters)
        => chapters.Any(ch => (ch.Lines ?? new List<string>()).Any(string.IsNullOrEmpty));
}
