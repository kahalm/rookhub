using System.Text;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Ebooks;

/// <summary>Ein gesetztes Feld-Zeichen auf der Seite (Mitte und Größe in PDF-Punkten, Ursprung links OBEN).</summary>
public readonly record struct DiagramGlyph(double X, double Y, double W, double H, ChessDiagramFonts.Square Square, string Family);

/// <summary>Ein Wort des normalen Texts (Rechteck in PDF-Punkten, Ursprung links oben) — für Beschriftung und Zugrecht.</summary>
public readonly record struct PageWord(double X0, double Y0, double X1, double Y1, string Text, string FontName);

/// <summary>Ein erkanntes Diagramm: Rechteck in PDF-Punkten (Ursprung links oben), FEN, Beschriftung.</summary>
public sealed record ExtractedDiagram(double X0, double Y0, double X1, double Y1, string Board, char Side, bool SideKnown,
    string? Label, string? Caption, string Family);

/// <summary>
/// Baut aus den Feld-Zeichen einer Seite die Diagramme: Zeichen, die höchstens 2,1 Zellen auseinander liegen (Nachbarfelder genau 1; eine fehlende Leerstelle überbrückt), gehören
/// zusammen; eine Gruppe, die genau 8 × 8 Zellen überspannt, ist ein Brett. Fehlende Zellen gelten als leer — PDF-Bibliotheken
/// unterschlagen Leerzeichen (= leeres helles Feld) gern (PdfPig auf Woodpecker S. 88). Reihen von oben nach unten = 8. bis 1.
/// Reihe, Spalten von links = a bis h (die Bücher drucken aus Weiß-Sicht; ein gedrehtes Diagramm ist selten und
/// dann ohnehin an den Koordinaten zu erkennen — Stufe 2).
///
/// <para>Beschriftung: die nächste reine Zahl links der Oberkante oder darüber (Aufgabennummer); Überschrift: die
/// Textzeile unmittelbar darüber. Zugrecht: „White/Black to move" (auch Deutsch) in der Nähe, sonst der Dreieck-Marker
/// der Quality-Chess-Bücher (Wingdings3: U+F072 = Weiß, U+F071 = Schwarz), sonst Weiß als Vorgabe (<c>SideKnown=false</c>).</para>
/// </summary>
public static class DiagramGridBuilder
{
    private static readonly Regex NumberLabel = new(@"^\(?(\d{1,4})[.)]?$", RegexOptions.Compiled);
    private static readonly Regex WhiteToMove = new(@"\b(white|wei(ß|ss)|blanc|bianco)\b.{0,12}\b(to move|am zug|to play|joue|muove)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BlackToMove = new(@"\b(black|schwarz|noir|nero)\b.{0,12}\b(to move|am zug|to play|joue|muove)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<ExtractedDiagram> Build(IReadOnlyList<DiagramGlyph> glyphs, IReadOnlyList<PageWord> words)
    {
        var result = new List<ExtractedDiagram>();
        if (glyphs.Count < 32) return result;
        var cell = Median(glyphs.Select(g => g.W));
        if (cell <= 0) return result;

        // Zusammenhangskomponenten über Zellabstand.
        var parent = Enumerable.Range(0, glyphs.Count).ToArray();
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        for (var i = 0; i < glyphs.Count; i++)
            for (var j = i + 1; j < glyphs.Count; j++)
                if (Math.Abs(glyphs[i].X - glyphs[j].X) < 2.1 * cell && Math.Abs(glyphs[i].Y - glyphs[j].Y) < 2.1 * cell)
                    parent[Find(i)] = Find(j);
        var groups = new Dictionary<int, List<DiagramGlyph>>();
        for (var i = 0; i < glyphs.Count; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var list)) groups[root] = list = new List<DiagramGlyph>();
            list.Add(glyphs[i]);
        }

        foreach (var g in groups.Values)
        {
            if (g.Count < 32 || g.Count > 64) continue;
            var board = ToBoard(g, cell);
            if (board == null) continue;
            var x0 = g.Min(s => s.X - s.W / 2); var x1 = g.Max(s => s.X + s.W / 2);
            var y0 = g.Min(s => s.Y - s.H / 2); var y1 = g.Max(s => s.Y + s.H / 2);
            var (label, caption) = Labels(words, x0, y0, x1, cell);
            var (side, known) = SideToMove(words, x0, y0, x1, y1, cell);
            result.Add(new ExtractedDiagram(x0, y0, x1, y1, board, side, known, label, caption, g[0].Family));
        }
        return result.OrderBy(d => d.Y0).ThenBy(d => d.X0).ToList();
    }

    private static string? ToBoard(List<DiagramGlyph> g, double cell)
    {
        var minX = g.Min(s => s.X); var minY = g.Min(s => s.Y);
        var cols = (int)Math.Round((g.Max(s => s.X) - minX) / cell);
        var rows = (int)Math.Round((g.Max(s => s.Y) - minY) / cell);
        if (cols != 7 || rows != 7) return null;
        var grid = new char[8, 8];   // '\0' = leer
        var seen = new bool[8, 8];
        foreach (var sq in g)
        {
            var c = (int)Math.Round((sq.X - minX) / cell);
            var r = (int)Math.Round((sq.Y - minY) / cell);
            if (Math.Abs(sq.X - minX - c * cell) > cell * 0.3 || Math.Abs(sq.Y - minY - r * cell) > cell * 0.3) return null;   // nicht im Raster
            if (seen[r, c]) return null;   // zwei Zeichen auf einem Feld — kein sauberes Brett
            seen[r, c] = true;
            if (!sq.Square.Empty) grid[r, c] = sq.Square.Piece;
        }
        var sb = new StringBuilder();
        for (var r = 0; r < 8; r++)
        {
            var empty = 0;
            for (var c = 0; c < 8; c++)
            {
                if (grid[r, c] == '\0') { empty++; continue; }
                if (empty > 0) { sb.Append(empty); empty = 0; }
                sb.Append(grid[r, c]);
            }
            if (empty > 0) sb.Append(empty);
            if (r < 7) sb.Append('/');
        }
        return sb.ToString();
    }

    private static (string? Label, string? Caption) Labels(IReadOnlyList<PageWord> words, double x0, double y0, double x1, double cell)
    {
        // Nummer: links der Oberkante (im Kreis, Quality Chess) oder in der Zeile direkt über dem Brett.
        var left = words.Where(w => w.X1 <= x0 + cell * 0.5 && w.X1 >= x0 - 3 * cell && w.Y0 < y0 + 2 * cell && w.Y1 > y0 - cell)
            .OrderByDescending(w => w.X1);
        var above = words.Where(w => w.Y1 <= y0 + cell * 0.5 && w.Y1 >= y0 - 3.5 * cell && w.X1 > x0 - cell && w.X0 < x1 + cell)
            .OrderByDescending(w => w.Y1).ToList();
        string? label = null;
        foreach (var w in left)
        {
            var m = NumberLabel.Match(w.Text.Trim());
            if (m.Success) { label = m.Groups[1].Value; break; }
        }
        // Darüber nur eine Zahl, die ALLEIN auf ihrer Zeile steht — sonst wären Jahreszahlen („Moscow 1960") und
        // Partienummern („(2)") Aufgabennummern (Woodpecker, Einleitungsdiagramme).
        if (label == null)
            foreach (var w in above)
            {
                var m = NumberLabel.Match(w.Text.Trim());
                if (!m.Success) continue;
                var alone = !above.Any(o => !ReferenceEquals(o.Text, w.Text) && Math.Abs(o.Y1 - w.Y1) < cell / 3 && o.X0 != w.X0);
                if (alone) { label = m.Groups[1].Value; break; }
            }
        // Überschrift: die oberste Wortzeile innerhalb von 2,5 Zellen über dem Brett, ohne reine Zahlen und Marker.
        string? caption = null;
        var lines = above.Where(w => w.Y1 >= y0 - 2.5 * cell && w.Text.Trim().Length > 0 && !(NumberLabel.IsMatch(w.Text.Trim()) && w.Text.Trim() == label))
            .GroupBy(w => (int)Math.Round(w.Y1 / (cell / 3))).OrderByDescending(l => l.Key).Take(2)
            .Select(l => string.Join(' ', l.OrderBy(w => w.X0).Select(w => w.Text.Trim())).Trim()).Where(t => t.Length > 2).ToList();
        if (lines.Count > 0)
        {
            // Zwei Zeilen (Spieler + Ort/Jahr) als eine Überschrift, in Lesereihenfolge (obere zuerst).
            caption = string.Join(" · ", lines.AsEnumerable().Reverse());
            if (caption.Length > 200) caption = caption[..200];
        }
        return (label, caption);
    }

    private static (char Side, bool Known) SideToMove(IReadOnlyList<PageWord> words, double x0, double y0, double x1, double y1, double cell)
    {
        // 1. Marker rechts neben DIESEM Brett (Quality Chess: Wingdings3-Dreieck; 'r' = U+F072 → Weiß, 'q' = U+F071 → Schwarz).
        //    Eng begrenzt auf die Höhe des Bretts — sonst greift bei übereinanderstehenden Diagrammen der Marker des oberen.
        foreach (var w in words.Where(w => w.FontName.Contains("Wingdings", StringComparison.OrdinalIgnoreCase)
                     && w.X0 >= x1 - cell && w.X0 <= x1 + 2.5 * cell && w.Y0 >= y0 - cell * 0.5 && w.Y1 <= y1 + cell * 0.5))
        {
            var folded = new string(w.Text.Select(ChessDiagramFonts.Fold).ToArray());
            if (folded.Contains('r')) return ('w', true);
            if (folded.Contains('q')) return ('b', true);
        }
        // 2. „White/Black to move" direkt unter oder über dem Brett.
        var near = words.Where(w => w.X1 > x0 - cell && w.X0 < x1 + cell
            && ((w.Y0 >= y1 - cell * 0.2 && w.Y0 <= y1 + 2.5 * cell) || (w.Y1 <= y0 + cell * 0.2 && w.Y1 >= y0 - 2.5 * cell)));
        var text = string.Join(' ', near.OrderBy(w => w.Y0).ThenBy(w => w.X0).Select(w => w.Text));
        if (WhiteToMove.IsMatch(text)) return ('w', true);
        if (BlackToMove.IsMatch(text)) return ('b', true);
        return ('w', false);
    }

    /// <summary>
    /// Rochaderechte aus der Stellung geschätzt: König und Turm auf ihren Ausgangsfeldern ⇒ das Recht wird angenommen
    /// (ein Diagramm sagt nichts darüber; so verfährt auch Chessable bei Buchstellungen — sonst wäre eine Lösung mit
    /// Rochade unspielbar).
    /// </summary>
    public static string GuessCastling(string board)
    {
        var rows = board.Split('/');
        if (rows.Length != 8) return "-";
        static string Expand(string row) => string.Concat(row.Select(c => char.IsDigit(c) ? new string('.', c - '0') : c.ToString()));
        var r1 = Expand(rows[7]); var r8 = Expand(rows[0]);
        if (r1.Length != 8 || r8.Length != 8) return "-";
        var sb = new StringBuilder();
        if (r1[4] == 'K' && r1[7] == 'R') sb.Append('K');
        if (r1[4] == 'K' && r1[0] == 'R') sb.Append('Q');
        if (r8[4] == 'k' && r8[7] == 'r') sb.Append('k');
        if (r8[4] == 'k' && r8[0] == 'r') sb.Append('q');
        return sb.Length == 0 ? "-" : sb.ToString();
    }

    private static double Median(IEnumerable<double> values)
    {
        var list = values.OrderBy(v => v).ToList();
        return list.Count == 0 ? 0 : list[list.Count / 2];
    }
}
