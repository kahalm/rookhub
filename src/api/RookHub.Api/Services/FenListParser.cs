using System.Text;

namespace RookHub.Api.Services;

/// <summary>
/// Parst eine hineinkopierte Liste von Stellungen (Memo-Feld der Kurs-Detailseite) zu FENs.
/// Erwartet eine Stellung je Zeile, mit optionaler Nummerierung und optionalem Kommentar:
/// <code>
/// 1: r2q1r1k/1pp1bppb/p1np4/4p1Pp/2B1P2N/2PPB2P/PP1Q1P2/R3R1K1 w - - 0 18
/// 2. r2qk2r/p3bppp/Q4n2/4p1N1/3n4/8/PPPP1PPP/RNB2RK1 b kq - 0 12 | Wie bewertest du das?
/// 8/8/8/4k3/8/8/4K3/8 w {Turmendspiel, Schlüsselstellung}
/// </code>
/// Leerzeilen werden übersprungen; jede fehlerhafte Zeile wird mit Zeilennummer und Grund
/// zurückgemeldet (statt den ganzen Einfüge-Vorgang scheitern zu lassen).
///
/// <para><b>Bewusst KEINE Legalitätsprüfung</b>: nur die Struktur der FEN wird geprüft (8 Reihen à 8
/// Felder, Seite am Zug). Stellungen ohne König o. Ä. sind in Buch-/Kurs-Diagrammen zulässig
/// (Chessable-Muster) — der Kalkulations-Modus fängt das ab.</para>
/// </summary>
public static class FenListParser
{
    /// <summary>Obergrenze je Einfüge-Vorgang (schützt vor versehentlichem Riesen-Paste).</summary>
    public const int MaxLines = 500;

    /// <summary>Eine erkannte Stellung. <paramref name="LineNumber"/> ist 1-basiert (Memo-Zeile).</summary>
    public record ParsedFen(int LineNumber, string Fen, string? Comment);

    /// <summary>Eine nicht verwertbare Zeile mit Grund-Schlüssel (i18n im Frontend).</summary>
    public record FenError(int LineNumber, string Text, string Reason);

    public record Result(List<ParsedFen> Positions, List<FenError> Errors);

    private const string PieceChars = "pnbrqkPNBRQK";

    public static Result Parse(string? text)
    {
        var positions = new List<ParsedFen>();
        var errors = new List<FenError>();
        if (string.IsNullOrWhiteSpace(text)) return new Result(positions, errors);

        var rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < rawLines.Length; i++)
        {
            var lineNumber = i + 1;
            var raw = rawLines[i].Trim();
            if (raw.Length == 0) continue;

            if (positions.Count + errors.Count >= MaxLines)
            {
                errors.Add(new FenError(lineNumber, Truncate(raw), "too_many"));
                break;
            }

            var (body, comment) = SplitComment(StripIndex(raw));
            var fen = NormalizeFen(body);
            if (fen == null)
            {
                errors.Add(new FenError(lineNumber, Truncate(raw), "invalid_fen"));
                continue;
            }
            positions.Add(new ParsedFen(lineNumber, fen, comment));
        }
        return new Result(positions, errors);
    }

    /// <summary>Entfernt eine führende Nummerierung („1:", „2.", „3)", „4 -").</summary>
    private static string StripIndex(string line)
    {
        var i = 0;
        while (i < line.Length && char.IsDigit(line[i])) i++;
        if (i == 0) return line;                                  // keine Nummer davor
        var j = i;
        while (j < line.Length && line[j] == ' ') j++;
        if (j >= line.Length || (line[j] != ':' && line[j] != '.' && line[j] != ')' && line[j] != '-'))
            return line;                                          // Zahl gehört zur FEN? → unverändert
        return line[(j + 1)..].TrimStart();
    }

    /// <summary>Trennt einen Kommentar ab: alles nach dem ersten „|", oder ein „{…}" am Zeilenende.</summary>
    private static (string Body, string? Comment) SplitComment(string line)
    {
        var pipe = line.IndexOf('|');
        if (pipe >= 0)
        {
            var comment = line[(pipe + 1)..].Trim();
            return (line[..pipe].Trim(), comment.Length == 0 ? null : comment);
        }
        if (line.EndsWith('}'))
        {
            var open = line.LastIndexOf('{');
            if (open >= 0)
            {
                var comment = line[(open + 1)..^1].Trim();
                return (line[..open].Trim(), comment.Length == 0 ? null : comment);
            }
        }
        return (line, null);
    }

    /// <summary>
    /// Prüft die STRUKTUR einer FEN und füllt fehlende hintere Felder mit Standardwerten auf
    /// (Rochade „-", en passant „-", Halbzug 0, Zug 1). <c>null</c> = unbrauchbar.
    /// </summary>
    public static string? NormalizeFen(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;                         // Brett + Seite am Zug sind Pflicht
        if (!IsValidBoard(parts[0])) return null;
        var side = parts[1].ToLowerInvariant();
        if (side != "w" && side != "b") return null;

        var castling = parts.Length > 2 ? parts[2] : "-";
        if (!IsValidCastling(castling)) return null;
        var ep = parts.Length > 3 ? parts[3] : "-";
        if (!IsValidEnPassant(ep)) return null;
        var halfMove = parts.Length > 4 && int.TryParse(parts[4], out var hm) && hm >= 0 ? hm : 0;
        var fullMove = parts.Length > 5 && int.TryParse(parts[5], out var fm) && fm >= 1 ? fm : 1;

        return $"{parts[0]} {side} {castling} {ep} {halfMove} {fullMove}";
    }

    /// <summary>8 Reihen, je genau 8 Felder aus Ziffern (Leerfelder) und Figurenbuchstaben.</summary>
    private static bool IsValidBoard(string board)
    {
        var ranks = board.Split('/');
        if (ranks.Length != 8) return false;
        foreach (var rank in ranks)
        {
            if (rank.Length == 0) return false;
            var files = 0;
            foreach (var c in rank)
            {
                if (c is >= '1' and <= '8') files += c - '0';
                else if (PieceChars.Contains(c)) files++;
                else return false;
            }
            if (files != 8) return false;
        }
        return true;
    }

    private static bool IsValidCastling(string castling)
    {
        if (castling == "-") return true;
        if (castling.Length > 4) return false;
        // Shredder-/X-FEN-Schreibweise (Dateibuchstaben) wird mitgenommen, ohne sie auszuwerten.
        foreach (var c in castling)
            if (!"KQkq".Contains(c) && !(char.ToLowerInvariant(c) is >= 'a' and <= 'h')) return false;
        return true;
    }

    private static bool IsValidEnPassant(string ep)
    {
        if (ep == "-") return true;
        return ep.Length == 2 && ep[0] is >= 'a' and <= 'h' && ep[1] is >= '1' and <= '8';
    }

    private static string Truncate(string s) => s.Length <= 120 ? s : s[..120];

    /// <summary>Nur für Tests/Diagnose: die erkannten FENs als Text.</summary>
    public static string Describe(Result result)
    {
        var sb = new StringBuilder();
        foreach (var p in result.Positions) sb.AppendLine($"{p.LineNumber}: {p.Fen}");
        foreach (var e in result.Errors) sb.AppendLine($"!{e.LineNumber}: {e.Reason}");
        return sb.ToString();
    }
}
