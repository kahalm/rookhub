using System.Text;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Zugrecht eines Scan-Diagramms aus der Bildunterschrift (0.823.0): viele Bücher schreiben „White to move“ / „Black to
/// move“ (auch „Weiß/Schwarz am Zug“) als Zeile unter das Brett statt eines Dreiecks daneben (Pongó „Tactical Targets“).
/// Die OCR-Textebene zerreißt solche Wörter gern („B l ack to move“), deshalb wird je Zeile ohne Leerzeichen gesucht.
/// Unterschrift DARUNTER geht vor: die Zeile knapp über einem Brett ist meist die Unterschrift des Bretts darüber.
/// </summary>
public static class ScanCaptionSide
{
    private static readonly Regex Caption = new(@"(white|wh[il1]te|wei(ß|ss)|black|b[l1]ack|schwarz)(to(move|play)|amzug)", RegexOptions.Compiled);

    /// <summary>Zeilen „X to move“ einer Seite: Seite ('w'/'b'), Mitte waagrecht und senkrecht (Punkte, links oben).</summary>
    public static List<(char Side, double Xc, double Yc)> Captions(IReadOnlyList<PageWord> words)
    {
        var result = new List<(char, double, double)>();
        if (words.Count == 0) return result;
        var lines = new List<List<PageWord>>();
        foreach (var w in words.OrderBy(w => Math.Round(w.Y1)).ThenBy(w => w.X0))
        {
            var last = lines.Count > 0 ? lines[^1] : null;
            if (last != null && Math.Abs(last[^1].Y1 - w.Y1) < (w.Y1 - w.Y0) * 0.6) last.Add(w);
            else lines.Add([w]);
        }
        foreach (var line in lines)
        {
            line.Sort((a, b) => a.X0.CompareTo(b.X0));
            var text = new StringBuilder();
            var owner = new List<int>();
            for (var k = 0; k < line.Count; k++)
                foreach (var ch in line[k].Text.ToLowerInvariant())
                    if (ch is >= 'a' and <= 'z' or 'ä' or 'ö' or 'ü' or 'ß' or '1') { text.Append(ch); owner.Add(k); }
            foreach (Match m in Caption.Matches(text.ToString()))
            {
                var side = m.Groups[1].Value[0] is 'b' or 's' ? 'b' : 'w';
                var first = line[owner[m.Index]];
                var lastW = line[owner[m.Index + m.Length - 1]];
                var ws = line.Skip(owner[m.Index]).Take(owner[m.Index + m.Length - 1] - owner[m.Index] + 1).ToList();
                result.Add((side, (first.X0 + lastW.X1) / 2, (ws.Min(w => w.Y0) + ws.Max(w => w.Y1)) / 2));
            }
        }
        return result;
    }

    /// <summary>Zugrecht für das Brett (x0, y0)–(x1, y1) in Punkten; <c>null</c> ohne passende Unterschrift.</summary>
    public static char? Find(IReadOnlyList<(char Side, double Xc, double Yc)> captions, double x0, double y0, double x1, double y1, double pageWidth, double pageHeight)
    {
        var dx = pageWidth * 0.02;
        var inX = captions.Where(c => c.Xc >= x0 - dx && c.Xc <= x1 + dx).ToList();
        var below = inX.Where(c => c.Yc >= y1 - pageHeight * 0.01 && c.Yc <= y1 + pageHeight * 0.08).OrderBy(c => c.Yc - y1).ToList();
        if (below.Count > 0) return below[0].Side;
        var above = inX.Where(c => c.Yc >= y0 - pageHeight * 0.05 && c.Yc <= y0 + pageHeight * 0.01).OrderBy(c => y0 - c.Yc).ToList();
        return above.Count > 0 ? above[0].Side : null;
    }
}
