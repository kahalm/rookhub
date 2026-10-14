namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Belegungen der Schach-Diagramm-Fonts: welcher Buchstabe welches Feld zeichnet. Ein Diagramm in einem digital
/// gesetzten Buch ist Text — 8 Reihen zu je 8 Zeichen aus dieser Schrift (plus Rand- und Koordinatenzeichen).
///
/// <para><b>Marroquin-Familie</b> (Chess Merida, Alpha, Berlin, Cases, Condal, Harlequin, Kingdom, Leipzig, Line, Lucena,
/// Magnetic, Marroquin, Maya, Mediaeval, Millennia, Motif, Smart, Usual, Utrecht — alle gleich belegt): Kleinbuchstabe =
/// Figur auf HELLEM Feld, Großbuchstabe = auf DUNKLEM Feld; <c>pnbrqk</c> weiß, <c>omvtwl</c> schwarz; Leerzeichen =
/// leeres helles, <c>+</c> = leeres dunkles Feld. Die PDF-ToUnicode-Tabelle legt die Zeichen oft nach U+F000+ASCII
/// (Symbol-Bereich), deshalb wird der Private-Use-Block auf ASCII zurückgefaltet. Geprüft an „The Woodpecker Method"
/// gegen den Chessable-Kurs (1 118 von 1 128 Aufgaben identisch, Rest Kursabweichungen; 2026-10-14).</para>
///
/// <para>Weitere Familien (ChessBase DiagramTT, Hastings, Linares …) kommen hinzu, sobald ihre Belegung an einem Buch
/// mit bekannten Stellungen verifiziert ist — eine Belegung wird nie geraten (eine falsche FEN ist schlimmer als keine).</para>
/// </summary>
public static class ChessDiagramFonts
{
    /// <summary>Ergebnis für EIN Zeichen: <c>Empty</c> = leeres Feld, <c>Piece</c> = FEN-Buchstabe, <c>null</c> = kein Feld (Rand, Koordinate).</summary>
    public readonly record struct Square(bool Empty, char Piece)
    {
        public static readonly Square EmptySquare = new(true, '\0');
    }

    public sealed record Family(string Name, Func<string, bool> Matches, Func<char, Square?> Map);

    private static readonly string[] MarroquinNames =
    [
        "merida", "chess alpha", "chessalpha", "chess-alpha", "berlin", "chess cases", "chesscases", "condal", "harlequin",
        "kingdom", "leipzig", "lucena", "magnetic", "marroquin", "chess maya", "mediaeval", "millennia", "chess motif",
        "chess smart", "chess usual", "utrecht", "chess line",
        // „CHESSBOARD“ (Problemsammlungen, Taylor/White): Marroquin-Belegung, am Seitenbild geprüft 14.10.
        "chessboard",
    ];

    public static readonly IReadOnlyList<Family> Families =
    [
        new("Marroquin", name => MarroquinNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase)), MapMarroquin),
        new("ChessBase", name => name.Contains("DiagramTT", StringComparison.OrdinalIgnoreCase)
            || name.Contains("UDiagramm", StringComparison.OrdinalIgnoreCase), MapChessBase),
        new("Hastings", name => name.Contains("HastingsDiagram", StringComparison.OrdinalIgnoreCase)
            || name.Contains("LinaresDiagram", StringComparison.OrdinalIgnoreCase), MapHastings),
    ];

    /// <summary>Die Familie zu einem PDF-Fontnamen (mit oder ohne Subset-Präfix „ABCDEF+") — <c>null</c> = kein bekannter Diagramm-Font.</summary>
    public static Family? Detect(string? fontName)
    {
        if (string.IsNullOrEmpty(fontName)) return null;
        var plus = fontName.IndexOf('+');
        var name = plus >= 0 && plus < 8 ? fontName[(plus + 1)..] : fontName;
        return Families.FirstOrDefault(f => f.Matches(name));
    }

    /// <summary>U+F000–U+F0FF (Symbol-Font-Bereich) → ASCII; alles andere unverändert.</summary>
    public static char Fold(char c) => c >= '' && c <= '' ? (char)(c - 0xF000) : c;

    private static Square? MapMarroquin(char raw)
    {
        var c = Fold(raw);
        switch (c)
        {
            case ' ': case '+': case ' ': return Square.EmptySquare;
            case 'p': case 'P': return new Square(false, 'P');
            case 'n': case 'N': return new Square(false, 'N');
            case 'b': case 'B': return new Square(false, 'B');
            case 'r': case 'R': return new Square(false, 'R');
            case 'q': case 'Q': return new Square(false, 'Q');
            case 'k': case 'K': return new Square(false, 'K');
            case 'o': case 'O': return new Square(false, 'p');
            case 'm': case 'M': return new Square(false, 'n');
            case 'v': case 'V': return new Square(false, 'b');
            case 't': case 'T': return new Square(false, 'r');
            case 'w': case 'W': return new Square(false, 'q');
            case 'l': case 'L': return new Square(false, 'k');
            default: return null;
        }
    }

    private static Square? MapChessBase(char raw)
    {
        var c = Fold(raw);
        return c switch
        {
            '+' or '-' => Square.EmptySquare,
            'K' or 'Q' or 'R' or 'N' or 'P' => new Square(false, c),
            'L' => new Square(false, 'B'),
            'k' or 'q' or 'r' or 'n' or 'p' => new Square(false, c),
            'l' => new Square(false, 'b'),
            _ => null,
        };
    }

    private static Square? MapHastings(char raw)
    {
        var c = Fold(raw);
        return c switch
        {
            'W' or 'w' or 'D' or 'd' => Square.EmptySquare,
            'P' or 'N' or 'B' or 'R' or 'Q' or 'K' or 'p' or 'n' or 'b' or 'r' or 'q' or 'k' => new Square(false, c),
            ')' => new Square(false, 'P'), 'H' => new Square(false, 'N'), 'G' => new Square(false, 'B'),
            '$' => new Square(false, 'R'), '!' => new Square(false, 'Q'), 'I' => new Square(false, 'K'),
            '0' => new Square(false, 'p'), 'h' => new Square(false, 'n'), 'g' => new Square(false, 'b'),
            '4' => new Square(false, 'r'), '1' => new Square(false, 'q'), 'i' => new Square(false, 'k'),
            _ => null,
        };
    }
}
