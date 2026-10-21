using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Figurensalat in Scan-PDFs reparieren (0.818.0): Bücher, deren Textebene eine Zeichenerkennung über einem Scan ist, haben
/// statt der Figurinen Zeichensalat vor den Zugfeldern („lt:Jxc4“, „�h3“, „'§'b8“ statt ♘xc4, ♕h3, ♕b8). Je solchem Wort
/// wird die Seite gerendert, das Zeichen vor dem Feld ausgeschnitten und von einem kleinen Netz (<c>Assets/ebook-figurine-cnn.onnx</c>,
/// trainiert auf ~40 Büchern, Skripte <c>~/claude/ebook-board-training/figfix</c>) als K/Q/R/B/N (oder „keine Figur“) gelesen; nur sichere
/// Lesungen (≥ <see cref="MinConfidence"/>) ersetzen den Salat durch die Figurine. Bücher mit wenig Salat bleiben unberührt.
/// </summary>
public static class EbookFigurineRepair
{
    /// <summary>Ab so vielen Salat-Zugwörtern im Buch wird repariert (darunter: echter Text, Zufallstreffer).</summary>
    public const int MinCandidates = 40;
    public const float MinConfidence = 0.9f;
    public const int Dpi = 150;
    private const string Classes = "KQRBN-"; // Klasse 5: kein Figurenzeichen (Strich, Klammer …)

    private static readonly Regex Tok = new(@"^(\S{1,4}?)(x?[a-h][1-8SlIB](?:[^a-h0-9](?!.*[a-h][1-8]).*)?)$", RegexOptions.Compiled);
    private static readonly Regex NumPrefix = new(@"^\d{1,3}\.{1,3}(?=\S)", RegexOptions.Compiled);

    /// <summary>Länge des Salat-Präfixes eines Zugworts (ohne führende Zugnummer), sonst 0. Gleiche Regeln wie das Training.</summary>
    internal static int SaladPrefix(string word)
    {
        var m = Tok.Match(word);
        if (!m.Success) return 0;
        var pre = m.Groups[1].Value; var rest = m.Groups[2].Value;
        if (pre.Any(c => c is >= '♔' and <= '♟') || pre is "x" or "(x" or "-x") return 0;
        var p2 = pre.TrimStart('(', '[');
        if (p2.Length == 0 || (p2.Length == 1 && "KQRBNDTLSO".Contains(p2[0])) || (p2.Length == 1 && p2[0] is >= 'a' and <= 'h')) return 0;
        if (Regex.IsMatch(p2, @"^(\d+\.*|[-–—.…]+|\d+\.?[-–—]+)$")) return 0;
        var digit = rest.TrimStart('x')[1];
        if (!char.IsDigit(digit) && Regex.IsMatch(pre, "^[A-Za-z]+$")) return 0;
        return pre.Length;
    }

    private sealed record Candidate(int Page, int Start, int PrefixGlyphs, ReflowGlyph First);

    /// <summary>Repariert die Seiten; <paramref name="render"/> liefert eine Seite als Graubild in <see cref="Dpi"/>.</summary>
    public static EbookReflowPdfReader.Document Repair(EbookReflowPdfReader.Document doc, Func<int, float[,]?> render, out int repaired)
    {
        repaired = 0;
        var model = Model.Value;
        if (model == null) return doc;
        var cands = new List<Candidate>();
        var lists = doc.Pages.Select(p => p.Glyphs.OrderBy(g => Math.Round(g.Baseline, 1)).ThenBy(g => g.X0).ToList()).ToList();
        for (var pi = 0; pi < doc.Pages.Count; pi++)
        {
            var gs = lists[pi];
            for (var i = 0; i < gs.Count;)
            {
                var j = i + 1;
                while (j < gs.Count && Math.Abs(gs[j].Baseline - gs[i].Baseline) < gs[i].Size * 0.3 && gs[j].X0 - gs[j - 1].X1 <= gs[j].Size * 0.2
                       && !string.IsNullOrWhiteSpace(gs[j].Text)) j++;
                if (!string.IsNullOrWhiteSpace(gs[i].Text))
                {
                    var word = string.Concat(gs.Skip(i).Take(j - i).Select(g => g.Text));
                    var num = NumPrefix.Match(word); var rest = num.Success ? word[num.Length..] : word;
                    var pre = SaladPrefix(rest);
                    if (pre > 0)
                    {
                        // Zeichen → Glyphen: Zugnummer und Präfix überspringen
                        int k = i, chars = 0, skip = num.Success ? num.Length : 0;
                        while (k < j && chars + gs[k].Text.Length <= skip) { chars += gs[k].Text.Length; k++; }
                        int start = k, taken = 0;
                        while (k < j && taken < pre) { taken += gs[k].Text.Length; k++; }
                        if (taken == pre && k > start) cands.Add(new Candidate(pi, start, k - start, gs[start]));
                    }
                }
                i = j;
            }
        }
        if (cands.Count < MinCandidates) return doc;
        // Rendern ist der teure Teil (pdftoppm je Seite) → Seiten parallel; InferenceSession.Run ist threadsicher.
        var replace = new ConcurrentDictionary<(int Page, int Start), (int Count, char Piece)>();
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 6) };
        Parallel.ForEach(cands.GroupBy(c => c.Page), parallel, group =>
        {
            var page = doc.Pages[group.Key];
            var img = render(page.Number);
            if (img == null) return;
            var s = img.GetLength(1) / page.Width;
            var list = group.ToList();
            var t = new DenseTensor<float>([list.Count, 1, 32, 32]);
            for (var n = 0; n < list.Count; n++)
            {
                var g = list[n].First; var top = g.Baseline - g.Size; var h = g.Size * 1.3 * s;
                int x0 = (int)(g.X0 * s) - 1, y0 = (int)(top * s), x1 = (int)(g.X0 * s + h * 1.05), y1 = (int)((g.Baseline + g.Size * 0.3) * s);
                var c = EbookBoardCnn.AreaResize(img, x0, y0, x1, y1, 32, 32);
                for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++) t[n, 0, y, x] = c[y, x];
            }
            using var res = model.Run([NamedOnnxValue.CreateFromTensor(model.InputMetadata.Keys.First(), t)]);
            var logits = res.First().AsTensor<float>();
            for (var n = 0; n < list.Count; n++)
            {
                var max = float.MinValue; var arg = 0;
                for (var k = 0; k < Classes.Length; k++) if (logits[n, k] > max) { max = logits[n, k]; arg = k; }
                double sum = 0; for (var k = 0; k < Classes.Length; k++) sum += Math.Exp(logits[n, k] - max);
                if (Classes[arg] != '-' && 1 / sum >= MinConfidence) replace[(list[n].Page, list[n].Start)] = (list[n].PrefixGlyphs, Classes[arg]);
            }
        });
        if (replace.Count == 0) return doc;
        var pages = new List<ReflowPage>(doc.Pages.Count);
        for (var pi = 0; pi < doc.Pages.Count; pi++)
        {
            var gs = lists[pi]; var outp = new List<ReflowGlyph>(gs.Count);
            for (var i = 0; i < gs.Count; i++)
            {
                if (replace.TryGetValue((pi, i), out var r))
                {
                    var a = gs[i]; var b = gs[i + r.Count - 1];
                    outp.Add(a with { X1 = b.X1, Text = EbookReflowPdfReader.Figurine(r.Piece).ToString() });
                    i += r.Count - 1; repaired++;
                    continue;
                }
                outp.Add(gs[i]);
            }
            pages.Add(doc.Pages[pi] with { Glyphs = outp });
        }
        return doc with { Pages = pages };
    }

    private static readonly Lazy<InferenceSession?> Model = new(() =>
    {
        using var s = typeof(EbookFigurineRepair).Assembly.GetManifestResourceStream("RookHub.Api.Assets.ebook-figurine-cnn.onnx");
        if (s == null) return null;
        using var ms = new MemoryStream(); s.CopyTo(ms);
        return new InferenceSession(ms.ToArray(), new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 });
    });
}
