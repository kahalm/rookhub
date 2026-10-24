using System.Text;
using System.Text.RegularExpressions;
using RookHub.Api.Services.Repertoires;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Reine PGN-Block-Helfer des Chessable-Imports: ein Mehr-Partien-PGN in [Event]-Blöcke zerlegen, die
/// Chessable-oid eines Blocks lesen, die Zugtext-Signatur bilden und oids nachtragen. Aus
/// <see cref="ChessableImportService"/> herausgelöst (Zerlegung Z3); Golden-Tests in <c>ChessablePgnBlocksTests</c>.
/// </summary>
public static class ChessablePgnBlocks
{
    internal static readonly Regex ChessableOidRegex =
        new("\\[ChessableOid \"([^\"]+)\"\\]", RegexOptions.Compiled);

    /// <summary>Chessable-oid eines PGN-Blocks (<c>[ChessableOid "…"]</c>), <c>null</c> ohne.</summary>
    internal static string? OidOf(string block)
    {
        var m = ChessableOidRegex.Match(block);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// Trägt je Block (Index wie <see cref="Split"/>) den Header <c>[ChessableOid "…"]</c> hinter dessen
    /// letztem Header ein — in place, der übrige Text bleibt Zeichen für Zeichen erhalten. Blöcke ohne Header
    /// bleiben unangetastet.
    /// </summary>
    internal static string InsertChessableOids(string pgn, IReadOnlyDictionary<int, string> oidByBlock)
    {
        var first = pgn.IndexOf("[Event ", StringComparison.Ordinal);
        if (first < 0 || oidByBlock.Count == 0) return pgn;
        // Dieselben Schnittstellen wie Split: jedes „[Event " ab dem ersten beginnt einen Block.
        var starts = Regex.Matches(pgn[first..], @"\[Event ")
            .Select(m => first + m.Index).ToList();
        var sb = new StringBuilder(pgn);
        foreach (var (blockIndex, oid) in oidByBlock.OrderByDescending(kv => kv.Key))   // von hinten: Positionen bleiben gültig
        {
            if (blockIndex < 0 || blockIndex >= starts.Count) continue;
            var end = blockIndex + 1 < starts.Count ? starts[blockIndex + 1] : pgn.Length;
            var at = HeaderEnd(pgn, starts[blockIndex], end);
            if (at >= 0) sb.Insert(at, $"\n[ChessableOid \"{oid}\"]");
        }
        return sb.ToString();
    }

    /// <summary>Position direkt hinter dem letzten Header eines Blocks (vor dessen Zeilenumbruch), -1 ohne Header.</summary>
    internal static int HeaderEnd(string pgn, int start, int end)
    {
        var at = -1;
        for (var pos = start; pos < end;)
        {
            var nl = pgn.IndexOf('\n', pos, end - pos);
            var lineEnd = nl < 0 ? end : nl;
            var line = pgn.AsSpan(pos, lineEnd - pos).Trim();
            if (line.Length == 0 || line[0] != '[') break;
            at = lineEnd > pos && pgn[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
            if (nl < 0) break;
            pos = nl + 1;
        }
        return at;
    }

    /// <summary>Zerlegt ein Mehr-Partien-PGN in einzelne [Event]-Blöcke.</summary>
    internal static IEnumerable<string> Split(string pgn)
    {
        if (string.IsNullOrWhiteSpace(pgn)) yield break;
        var idx = pgn.IndexOf("[Event ", StringComparison.Ordinal);
        if (idx < 0) yield break;
        foreach (var part in Regex.Split(pgn[idx..], @"(?=\[Event )"))
            if (!string.IsNullOrWhiteSpace(part)) yield return part.Trim();
    }

    /// <summary>
    /// Zugtext eines PGN-Blocks — alles hinter den führenden Header- und Leerzeilen, whitespace-normiert — als
    /// Dedup-Signatur einer Linie (Züge sind je Kurslinie praktisch eindeutig). Früher „alles nach der ersten
    /// Leerzeile": piratechess trennt Header und Züge seit dem <c>[ChessableOid]</c>-Header aber durch eine Zeile
    /// aus LEERZEICHEN. Dann gab es kein <c>\n\n</c>, die Signatur enthielt die Header, und eine neu geholte Linie
    /// glich ihrem alten Gegenstück nie — in einem echten Kurs 0 von 175 (header-frei: 175 von 175).
    /// </summary>
    internal static string MovetextOf(string block) => RepertoirePgnCleanup.MovetextOf(block);

    internal static int Count(string pgn) => Split(pgn).Count();
}
