using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Outline;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Liest ein Text-PDF für das Fließtext-EPUB (0.802.0) mit PdfPig: je Seite die Glyphen des normalen Texts (Diagramm-Fonts
/// fallen weg — die Bretter kommen als Bild aus der FEN), dazu die Lesezeichen. Hier passiert die Zeichen-Übersetzung:
/// Figurinen der Figurinen-Schriften (<see cref="EbookSolutionFinder.FigurinePiece"/>) → ♔♕♖♗♘, „†“/„‡“ → „+“/„#“,
/// Ligaturen → Buchstaben, das Wingdings-Häkchen → ✓; die Zugrecht-Dreiecke (Wingdings3) und sonstige Symbolzeichen
/// fallen weg.
/// </summary>
public static class EbookReflowPdfReader
{
    private static readonly Regex BoldName = new(@"bold|black|heavy|demi|semibold|-bd\b|bd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ItalicName = new(@"italic|oblique|-it\b|it$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record Document(IReadOnlyList<ReflowPage> Pages, IReadOnlyList<ReflowOutline> Outline, string? Title, string? Author);

    public static Document Read(string pdfPath, CancellationToken ct)
    {
        using var doc = PdfDocument.Open(pdfPath, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        var pages = new List<ReflowPage>();
        foreach (var page in doc.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            pages.Add(ReadPage(page));
        }
        var outline = new List<ReflowOutline>();
        try
        {
            if (doc.TryGetBookmarks(out var bookmarks))
                foreach (var root in bookmarks.Roots) Walk(root, outline);
        }
        catch (Exception) { /* kaputte Lesezeichen: ohne Gliederung weiter */ }
        string? title = null, author = null;
        try { title = doc.Information?.Title; author = doc.Information?.Author; } catch (Exception) { /* egal */ }
        return new Document(pages, outline, title, author);
    }

    private static void Walk(BookmarkNode node, List<ReflowOutline> into)
    {
        if (node is DocumentBookmarkNode d && d.PageNumber > 0 && !string.IsNullOrWhiteSpace(node.Title))
            into.Add(new ReflowOutline(Clean(node.Title), d.PageNumber, node.Level));
        foreach (var c in node.Children) Walk(c, into);
    }

    private static string Clean(string s) => Regex.Replace(s.Replace('\0', ' '), @"\s+", " ").Trim();

    private static ReflowPage ReadPage(Page page)
    {
        var height = page.Height;
        var glyphs = new List<ReflowGlyph>(page.Letters.Count);
        foreach (var l in page.Letters)
        {
            if (string.IsNullOrEmpty(l.Value)) continue;
            if (l.TextOrientation != TextOrientation.Horizontal) continue;   // Buchrücken, gedrehte Randnotizen
            var font = l.FontName ?? string.Empty;
            if (ChessDiagramFonts.Detect(font) != null) continue;          // Diagramm = Bild aus der FEN
            var text = Translate(l.Value, font);
            if (text.Length == 0 || string.IsNullOrWhiteSpace(text)) continue;
            var size = l.PointSize > 0.5 ? l.PointSize : l.BoundingBox.Height;
            if (size <= 0.5) size = 10;
            var x0 = l.StartBaseLine.X;
            var x1 = l.EndBaseLine.X;
            if (x1 - x0 < 0.05) x1 = x0 + Math.Max(l.BoundingBox.Width, size * 0.3);
            var name = font.Contains('+') && font.IndexOf('+') < 8 ? font[(font.IndexOf('+') + 1)..] : font;
            var bold = BoldName.IsMatch(name) || (l.FontDetails?.IsBold ?? false);
            var italic = ItalicName.IsMatch(name) || (l.FontDetails?.IsItalic ?? false);
            glyphs.Add(new ReflowGlyph(x0, x1, height - l.StartBaseLine.Y, Math.Round(size, 1), text, name, bold, italic));
        }
        return new ReflowPage(page.Number, page.Width, height, glyphs);
    }

    /// <summary>Zeichen eines PDF-Buchstabens → Text fürs EPUB (leer = weglassen).</summary>
    internal static string Translate(string value, string font)
    {
        var figurineFont = EbookSolutionFinder.IsFigurineFont(font);
        var symbolFont = font.Contains("Wingdings", StringComparison.OrdinalIgnoreCase) || font.Contains("Symbol", StringComparison.OrdinalIgnoreCase)
            || font.Contains("Dingbat", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder(value.Length);
        foreach (var raw in value)
        {
            var folded = raw >= '' && raw <= '' ? (char)(raw - 0xF000) : raw;
            if (symbolFont)
            {
                // Wingdings: ü (0xFC) = Häkchen (Woodpecker „Moves with ✓“); Wingdings3-Dreiecke = Zugrecht am Diagramm → weg.
                if (font.Contains("Wingdings", StringComparison.OrdinalIgnoreCase) && !font.Contains('3') && folded == 'ü') sb.Append('✓');
                continue;
            }
            var piece = EbookSolutionFinder.FigurinePiece(raw);
            if (piece != null && (figurineFont || IsUnambiguousFigurine(raw)))
            {
                sb.Append(Figurine(piece.Value));
                continue;
            }
            if (figurineFont) { if (folded >= ' ' && folded != '\u007F') sb.Append(FigurineFontSymbol(folded)); continue; }
            if (raw >= '' && raw <= '') continue;               // Private Use ohne Zuordnung
            if (char.IsControl(raw)) continue;
            sb.Append(Map(raw));
        }
        return sb.ToString();
    }

    /// <summary>Bewertungszeichen der SP-Figurinen-Schriften (Informator-Symbole auf Latin-1-Plätzen) → Unicode.</summary>
    private static string FigurineFontSymbol(char c) => c switch
    {
        '²' => "⩲", '³' => "⩱", 'µ' => "∓", '÷' => "∞", '„' => "⇆", '©' => "=/∞",
        _ => Map(c),
    };

    private static bool IsUnambiguousFigurine(char c) => c is >= '♔' and <= '♟' || c is >= '' and <= '';

    /// <summary>Figurbuchstabe → weiße Unicode-Figurine (die Bücher setzen Figurinen farbneutral).</summary>
    public static string Figurine(char piece) => piece switch
    {
        'K' => "♔", 'Q' => "♕", 'R' => "♖", 'B' => "♗", 'N' => "♘",
        _ => piece.ToString(),
    };

    private static string Map(char c) => c switch
    {
        '†' => "+",
        '‡' => "#",
        ' ' => " ",
        'ﬀ' => "ff", 'ﬁ' => "fi", 'ﬂ' => "fl", 'ﬃ' => "ffi", 'ﬄ' => "ffl", 'ﬅ' or 'ﬆ' => "st",
        _ => c.ToString(),
    };
}
