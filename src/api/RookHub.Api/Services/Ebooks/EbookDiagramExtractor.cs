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

    public sealed record Result(List<EbookDiagram> Diagrams, bool FontFound, int Pages, int Solutions = 0);

    public Result Extract(string pdfPath, int ebookId, CancellationToken ct)
    {
        var diagrams = new List<EbookDiagram>();
        var fontFound = false;
        var lines = new List<PageLine>();
        // Für die Lösungssuche: je Diagramm Seite, Spalte, Unterkante (Punkte), FEN, Nummer.
        var meta = new List<(int Index, int Page, int Column, double Bottom, string Fen, bool SideKnown, string? Label)>();
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
                var cx = start.X + adv / 2;
                // Felder stehen etwa 0,2 Feld unter bis 0,8 Feld über der Grundlinie.
                var cy = height - (start.Y + adv * 0.3);
                glyphs.Add(new DiagramGlyph(cx, cy, adv, adv, sq.Value, family.Name));
            }

            var words = new List<PageWord>();
            foreach (var word in page.GetWords())
            {
                if (ChessDiagramFonts.Detect(word.FontName) != null) continue;
                var b = word.BoundingBox;
                var baseline = word.Letters.Count > 0 ? height - word.Letters[0].StartBaseLine.Y : height - b.Bottom;
                words.Add(new PageWord(b.Left, height - b.Top, b.Right, height - b.Bottom, word.Text, word.FontName ?? string.Empty, baseline));
            }
            lines.AddRange(ToLines(page.Number, width, words));
            if (glyphs.Count < 32) continue;

            foreach (var d in DiagramGridBuilder.Build(glyphs, words))
            {
                var fen = $"{d.Board} {d.Side} {DiagramGridBuilder.GuessCastling(d.Board)} - 0 1";
                meta.Add((diagrams.Count, page.Number, d.X0 >= width * 0.48 ? 1 : 0, d.Y1, fen, d.SideKnown, d.Label));
                diagrams.Add(new EbookDiagram
                {
                    EbookId = ebookId,
                    Page = page.Number,
                    X = Clamp(d.X0 / width), Y = Clamp(d.Y0 / height),
                    W = Clamp((d.X1 - d.X0) / width), H = Clamp((d.Y1 - d.Y0) / height),
                    Fen = fen,
                    Label = d.Label,
                    Caption = d.Caption,
                    SideKnown = d.SideKnown,
                    FontFamily = d.Family,
                });
            }
        }

        var solutions = diagrams.Count == 0 ? new Dictionary<int, FoundSolution>() : EbookSolutionFinder.Match(meta, lines);
        foreach (var (index, sol) in solutions)
        {
            var d = diagrams[index];
            var parts = d.Fen.Split(' ');
            // Zugrecht + Zugnummer aus der Lösung („34...“ = Schwarz am Zug, Zug 34) — die Buchnotation passt dann.
            d.Fen = $"{parts[0]} {sol.Side} {parts[2]} - 0 {sol.FirstMoveNumber}";
            d.SideKnown = true;
            d.SolutionUci = Clip(string.Join(' ', sol.Uci), 1200);
            d.SolutionSan = Clip(string.Join(' ', sol.San), 1500);
            d.SolutionText = sol.Text;
            d.SolutionPage = sol.Page;
        }
        _logger.LogInformation("E-Book {EbookId}: {Count} Diagramme auf {Pages} Seiten, {Solutions} mit Lösung (Schach-Font {Found})",
            ebookId, diagrams.Count, pages, solutions.Count, fontFound);
        return new Result(diagrams, fontFound, pages, solutions.Count);
    }

    /// <summary>Wörter einer Seite → Zeilenabschnitte: gleiche Höhe (± halbe Schrifthöhe), getrennt an großen Lücken;
    /// Spalte = linke/rechte Seitenhälfte. Lesereihenfolge: Spalte, dann von oben nach unten.</summary>
    internal static List<PageLine> ToLines(int page, double width, List<PageWord> words)
    {
        var result = new List<PageLine>();
        if (words.Count == 0) return result;
        // Zeilen über die GRUNDLINIE, nicht über das Wortrechteck: Figurinen und fette Züge sind höher als der Fließtext
        // und rutschten sonst in die Nachbarzeile („… due to: 22...bxa6 … 22.Lxa6!“ in falscher Reihenfolge).
        static double Base(PageWord w) => double.IsNaN(w.Baseline) ? w.Y1 : w.Baseline;
        var sorted = words.OrderBy(Base).ThenBy(w => w.X0).ToList();
        var rows = new List<List<PageWord>>();
        foreach (var w in sorted)
        {
            var row = rows.LastOrDefault();
            var tol = Math.Max(1.5, (w.Y1 - w.Y0) * 0.35);
            if (row != null && Math.Abs(Base(row[0]) - Base(w)) < tol) row.Add(w);
            else rows.Add([w]);
        }
        foreach (var row in rows)
        {
            var seg = new List<PageWord>();
            foreach (var w in row.OrderBy(w => w.X0))
            {
                if (seg.Count > 0 && w.X0 - seg[^1].X1 > 24) { result.Add(Seg(page, width, seg)); seg = []; }
                seg.Add(w);
            }
            if (seg.Count > 0) result.Add(Seg(page, width, seg));
        }
        return result.OrderBy(l => l.Column).ThenBy(l => l.Y0).ToList();
    }

    private static PageLine Seg(int page, double width, List<PageWord> seg) =>
        new(page, seg[0].X0 >= width * 0.48 ? 1 : 0, seg.Min(w => w.Y0), seg.Max(w => w.Y1), seg[0].X0, seg[^1].X1,
            string.Join(' ', seg.Select(w => w.Text)));

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private static double Clamp(double v) => Math.Max(0, Math.Min(1, Math.Round(v, 5)));
}
