using RookHub.Api.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Liest die Diagramme eines PDF (Stufe 1: digital gesetzte Bücher mit Schach-Font). PdfPig liefert je Buchstabe
/// Schriftname, Unicode-Wert und Glyph-Rechteck; Zeichen eines bekannten Diagramm-Fonts werden zu Feldern
/// (<see cref="ChessDiagramFonts"/>), der <see cref="DiagramGridBuilder"/> macht daraus Bretter. Normaler Text liefert
/// Nummer, Überschrift und Zugrecht. Koordinaten: PdfPig rechnet vom linken UNTEREN Rand; hier wird auf links OBEN
/// gespiegelt und am Ende als Anteil der Seite abgelegt.
/// </summary>
public class EbookDiagramExtractor
{
    private readonly ILogger<EbookDiagramExtractor> _logger;

    public EbookDiagramExtractor(ILogger<EbookDiagramExtractor> logger) => _logger = logger;

    public sealed record Result(List<EbookDiagram> Diagrams, bool FontFound, int Pages);

    public Result Extract(string pdfPath, int ebookId, CancellationToken ct)
    {
        var diagrams = new List<EbookDiagram>();
        var fontFound = false;
        using var doc = PdfDocument.Open(pdfPath, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        var pages = 0;
        foreach (var page in doc.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            pages++;
            var width = page.Width;
            var height = page.Height;
            if (width <= 0 || height <= 0) continue;
            var glyphs = new List<DiagramGlyph>();
            foreach (var letter in page.Letters)
            {
                var family = ChessDiagramFonts.Detect(letter.FontName);
                if (family == null) continue;
                fontFound = true;
                if (string.IsNullOrEmpty(letter.Value)) continue;
                // Leerzeichen gar nicht erst nehmen: sie stehen auch AUSSERHALB der Bretter (Zeilenende) und ketten sonst
                // Nachbarbretter zusammen; ein fehlendes Feld gilt ohnehin als leer (DiagramGridBuilder).
                var folded = ChessDiagramFonts.Fold(letter.Value[0]);
                if (folded == ' ' || folded == '\u00A0') continue;
                var sq = family.Map(letter.Value[0]);
                if (sq == null) continue;
                // Lage über die DICKTE (Grundlinie, Vorschub) statt über das Tintenrechteck: das ist je Figur verschieden
                // groß (Bauer schmal, leeres Feld flach), die Dickte dagegen ist bei Diagramm-Fonts genau ein Feld.
                var start = letter.StartBaseLine;
                var adv = letter.EndBaseLine.X - start.X;
                if (adv <= 0.5) adv = letter.PointSize;
                var w = adv;
                var h = adv;
                var cx = start.X + adv / 2;
                // Felder stehen etwa 0,2 Feld unter bis 0,8 Feld über der Grundlinie.
                var cy = height - (start.Y + adv * 0.3);
                glyphs.Add(new DiagramGlyph(cx, cy, w, h, sq.Value, family.Name));
            }
            if (glyphs.Count < 64) continue;

            var words = new List<PageWord>();
            foreach (var word in page.GetWords())
            {
                if (ChessDiagramFonts.Detect(word.FontName) != null) continue;
                var b = word.BoundingBox;
                words.Add(new PageWord(b.Left, height - b.Top, b.Right, height - b.Bottom, word.Text, word.FontName ?? string.Empty));
            }

            foreach (var d in DiagramGridBuilder.Build(glyphs, words))
            {
                diagrams.Add(new EbookDiagram
                {
                    EbookId = ebookId,
                    Page = page.Number,
                    X = Clamp(d.X0 / width), Y = Clamp(d.Y0 / height),
                    W = Clamp((d.X1 - d.X0) / width), H = Clamp((d.Y1 - d.Y0) / height),
                    Fen = $"{d.Board} {d.Side} {DiagramGridBuilder.GuessCastling(d.Board)} - 0 1",
                    Label = d.Label,
                    Caption = d.Caption,
                    SideKnown = d.SideKnown,
                    FontFamily = d.Family,
                });
            }
        }
        _logger.LogInformation("E-Book {EbookId}: {Count} Diagramme auf {Pages} Seiten (Schach-Font {Found})", ebookId, diagrams.Count, pages, fontFound);
        return new Result(diagrams, fontFound, pages);
    }

    private static double Clamp(double v) => Math.Max(0, Math.Min(1, Math.Round(v, 5)));
}
