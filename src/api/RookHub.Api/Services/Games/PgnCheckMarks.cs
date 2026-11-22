using System.Text;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Schach- und Mattzeichen in einem gespeicherten PGN-Text nachtragen (1.002.4) — für den Start-Abgleich
/// <see cref="ClubCheckMarkRepair"/>. Bis 1.002.1 schrieb die Namen-/Ergebniskorrektur einer Vereinspartie die Zugliste aus
/// <c>PgnParser.ExtractMainlineSans</c> neu, das <c>+</c>/<c>#</c> abschneidet; die Analysen übernahmen dieses PGN.
///
/// <para><b>Streng</b>: nur die Züge der Hauptvariante werden angefasst, und an ihnen nur das Zeichen dahinter. Kopfzeilen,
/// Kommentare, Varianten, NAGs, Zugnummern und die Schreibweise der Züge bleiben Zeichen für Zeichen. Zum Schluss wird
/// geprüft, dass sich der Text ohne <c>+</c> und <c>#</c> nicht geändert hat und die Hauptvariante dieselbe ist — sonst
/// bleibt er, wie er ist, und <see cref="Repair"/> meldet den Grund.</para>
/// </summary>
internal static class PgnCheckMarks
{
    private static readonly Regex HeaderEnd = new(@"\r?\n[ \t]*\r?\n", RegexOptions.Compiled);
    private static readonly Regex MoveNumber = new(@"^\d+\.+", RegexOptions.Compiled);

    /// <summary>→ (neuer Text oder <c>null</c> = nichts zu tun, Grund, warum der Text nicht angefasst wird, oder <c>null</c>).</summary>
    internal static (string? Pgn, string? Problem) Repair(string pgn)
    {
        if (string.IsNullOrWhiteSpace(pgn)) return (null, null);
        var games = PgnParser.SplitGames(pgn).ToList();
        if (games.Count != 1) return (null, games.Count == 0 ? null : "multipleGames");
        var plain = PgnParser.ExtractMainlineSans(games[0].MoveText ?? string.Empty);
        if (plain.Count == 0) return (null, null);
        var fen = games[0].Headers is { } h && h.TryGetValue("FEN", out var f) && !string.IsNullOrWhiteSpace(f) ? f.Trim() : null;
        List<string> marked;
        try { marked = SavedGamePgn.LegalSans(plain, fen); }
        catch (Exception) { return (null, "illegal"); }

        var (head, body) = Split(pgn);
        var tokens = MainlineTokens(body);
        if (tokens.Count != plain.Count) return (null, "tokens");
        var sb = new StringBuilder(body.Length + 32);
        var at = 0;
        for (var k = 0; k < tokens.Count; k++)
        {
            var (start, end) = tokens[k];
            var t = body[start..end];
            var number = MoveNumber.Match(t).Value;
            var rest = t[number.Length..];
            var cut = rest.Length;
            while (cut > 0 && rest[cut - 1] is '+' or '#' or '!' or '?') cut--;
            var core = rest[..cut];
            var annotation = new string(rest[cut..].Where(c => c is '!' or '?').ToArray());
            var mark = marked[k] is { Length: > 0 } m && m[^1] is '+' or '#' ? m[^1].ToString() : string.Empty;
            sb.Append(body, at, start - at).Append(number).Append(core).Append(mark).Append(annotation);
            at = end;
        }
        sb.Append(body, at, body.Length - at);
        var fixedPgn = head + sb;
        if (fixedPgn == pgn) return (null, null);

        static string Bare(string s) => s.Replace("+", string.Empty).Replace("#", string.Empty);
        if (Bare(fixedPgn) != Bare(pgn)) return (null, "otherChange");
        var again = PgnParser.SplitGames(fixedPgn).ToList();
        if (again.Count != 1 || !PgnParser.ExtractMainlineSans(again[0].MoveText ?? string.Empty).SequenceEqual(plain))
            return (null, "otherChange");
        return (fixedPgn, null);
    }

    private static (string Head, string Body) Split(string pgn)
    {
        if (!pgn.TrimStart().StartsWith('[')) return (string.Empty, pgn);
        var m = HeaderEnd.Match(pgn);
        return m.Success ? (pgn[..m.Index], pgn[m.Index..]) : (pgn, string.Empty);
    }

    /// <summary>Die Stellen der Hauptvariantenzüge im Zugtext (Zugnummer und Zeichen gehören zum Token): außerhalb von
    /// Kommentaren und Varianten, ohne NAGs, Ergebnis und reine Zugnummern.</summary>
    private static List<(int Start, int End)> MainlineTokens(string body)
    {
        var result = new List<(int, int)>();
        var depth = 0;
        var i = 0;
        while (i < body.Length)
        {
            var c = body[i];
            if (c == '{') { var j = body.IndexOf('}', i); i = j < 0 ? body.Length : j + 1; continue; }
            if (c == ';') { var j = body.IndexOf('\n', i); i = j < 0 ? body.Length : j + 1; continue; }
            if (c == '(') { depth++; i++; continue; }
            if (c == ')') { depth--; i++; continue; }
            if (char.IsWhiteSpace(c) || c == '}') { i++; continue; }
            var start = i;
            while (i < body.Length && !char.IsWhiteSpace(body[i]) && body[i] is not ('{' or '}' or '(' or ')' or ';')) i++;
            if (depth != 0) continue;
            var t = body[start..i];
            if (PgnTokens.IsResultToken(t.AsSpan()) || t.StartsWith('$')) continue;
            var rest = t[MoveNumber.Match(t).Length..];
            if (rest.TrimEnd('+', '#', '!', '?').Length == 0) continue;
            result.Add((start, i));
        }
        return result;
    }
}
