using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Ein Diagrammsatz für mehrere Formate (0.793.0): Liegt ein Buch als PDF UND als EPUB vor, liest die Erkennung die
/// Stellungen aus dem PDF (Schachfont = exakt) und hängt jedem Diagramm die Stelle im EPUB an (<see cref="EbookDiagram.ImageRef"/>
/// + <see cref="EbookDiagram.EpubChapter"/>). Die EPUB-Bilder werden dafür nur „gesehen“ (fehleranfällig) — zugeordnet wird
/// über die Reihenfolge im Buch (Needleman-Wunsch) mit der Ähnlichkeit der Stellungen als Bewertung; so verkraftet der
/// Abgleich Lesefehler, fehlende und zusätzliche Bilder.
/// </summary>
public static class EbookFormatLinker
{
    /// <summary>Ab dieser Ähnlichkeit gelten zwei Stellungen als dasselbe Diagramm (bei drei Steinen darf einer falsch sein).</summary>
    public const double MinSimilarity = 0.6;

    /// <summary>Ähnlichkeit über die BESETZTEN Felder: gleiche Figur auf gleichem Feld / Steine der volleren Stellung. Leere
    /// Felder zählen nicht mit — sonst wirkte ein leeres Brett jedem Endspiel mit vier Steinen zu 94 % gleich.</summary>
    public static double Similarity(string fenA, string fenB)
    {
        var a = Squares(fenA); var b = Squares(fenB);
        if (a == null || b == null) return 0;
        int same = 0, na = 0, nb = 0;
        for (var i = 0; i < 64; i++)
        {
            if (a[i] != '.') na++;
            if (b[i] != '.') nb++;
            if (a[i] != '.' && a[i] == b[i]) same++;
        }
        var n = Math.Max(na, nb);
        return n == 0 ? 0 : (double)same / n;
    }

    /// <summary>Ordnet <paramref name="pdf"/> (Lesereihenfolge) die passenden <paramref name="epub"/>-Diagramme zu und
    /// setzt ImageRef/EpubChapter. Rückgabe: Zahl der verknüpften Diagramme.</summary>
    public static int Link(IReadOnlyList<EbookDiagram> pdf, IReadOnlyList<EbookDiagram> epub)
    {
        int n = pdf.Count, m = epub.Count;
        if (n == 0 || m == 0) return 0;
        var sim = new double[n, m];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < m; j++) sim[i, j] = Similarity(pdf[i].Fen, epub[j].Fen);
        // Punkte: gutes Paar +1 (je ähnlicher, desto mehr), schlechtes Paar stark negativ, Lücke 0.
        var score = new double[n + 1, m + 1];
        var move = new byte[n + 1, m + 1];   // 1 = Paar, 2 = PDF-Diagramm ohne Bild, 3 = Bild ohne PDF-Diagramm
        for (var i = 1; i <= n; i++) { score[i, 0] = 0; move[i, 0] = 2; }
        for (var j = 1; j <= m; j++) { score[0, j] = 0; move[0, j] = 3; }
        for (var i = 1; i <= n; i++)
            for (var j = 1; j <= m; j++)
            {
                var s = sim[i - 1, j - 1];
                var pair = score[i - 1, j - 1] + (s >= MinSimilarity ? s : -1);
                var up = score[i - 1, j];
                var left = score[i, j - 1];
                if (pair >= up && pair >= left) { score[i, j] = pair; move[i, j] = 1; }
                else if (up >= left) { score[i, j] = up; move[i, j] = 2; }
                else { score[i, j] = left; move[i, j] = 3; }
            }
        var linked = 0;
        for (int i = n, j = m; i > 0 && j > 0;)
        {
            switch (move[i, j])
            {
                case 1:
                    if (sim[i - 1, j - 1] >= MinSimilarity)
                    {
                        pdf[i - 1].ImageRef = epub[j - 1].ImageRef;
                        pdf[i - 1].EpubChapter = epub[j - 1].Page;   // EPUB-Erkennung: Page = Kapitel
                        linked++;
                    }
                    i--; j--; break;
                case 2: i--; break;
                default: j--; break;
            }
        }
        return linked;
    }

    private static char[]? Squares(string fen)
    {
        var board = fen.Split(' ')[0];
        var res = new char[64]; var k = 0;
        foreach (var c in board)
        {
            if (c == '/') continue;
            if (char.IsAsciiDigit(c)) { for (var t = 0; t < c - '0'; t++) { if (k >= 64) return null; res[k++] = '.'; } }
            else { if (k >= 64) return null; res[k++] = c; }
        }
        return k == 64 ? res : null;
    }
}
