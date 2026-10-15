using System.Text;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Macht aus den Glyphen der PDF-Seiten fließenden Text (0.802.0): Zeilen → Abschnitte → Absätze und Überschriften, mit den
/// Diagrammen als Bild an ihrer Stelle, verteilt auf Kapitel. Die Regeln (alle in PDF-Punkten, Schriftgröße als Maß):
/// <list type="bullet">
/// <item><b>Zeilen</b>: gleiche Grundlinie (± 0,35 Schriftgröße); Wortgrenze bei einer Lücke &gt; 0,12 Schriftgröße, neuer
/// Abschnitt bei &gt; 1,5 oder am <b>Bundsteg</b> — einer senkrechten Spur zwischen 35 und 65 % der Seitenbreite, die fast
/// keine Zeile kreuzt, mit Text auf beiden Seiten (zweispaltig: erst die linke Spalte ganz, dann die rechte).</item>
/// <item><b>Kolumnentitel und Seitenzahlen</b>: oberste/unterste Zeile im Randstreifen (9 % / 7 %), abgesetzt vom Text, deren
/// Wortlaut (Ziffern gleichgesetzt) auf mindestens 3 Seiten wiederkehrt — oder nur eine Zahl ist.</item>
/// <item><b>Diagramme</b>: Text im Brett fällt weg (Diagramm-Fonts liest der <see cref="EbookReflowPdfReader"/> gar nicht erst),
/// im Rand darum Koordinaten (a–h, 1–8), die Aufgabennummer und Symbolzeichen.</item>
/// <item><b>Absatzende</b>: großer Zeilenabstand (&gt; 1,45 × üblich), Einzug der nächsten Zeile, kurze vorige Zeile, Wechsel
/// der Überschriftenstufe, Diagramm. Über Seiten und Spalten hinweg läuft ein Absatz weiter, wenn die Zeile voll war.</item>
/// <item><b>Trennung</b>: „Kombi-“ + „nation“ (Kleinbuchstabe danach) → „Kombination“; vor einem Großbuchstaben bleibt der
/// Bindestrich („Nimzo-Indian“); weiches Trennzeichen fällt immer weg.</item>
/// <item><b>Überschriften</b>: Schrift ≥ 1,15 × Grundschrift; die Größen, die auf mindestens zwei Seiten vorkommen, ergeben
/// absteigend h1/h2/h3 (Einzelgrößen darüber, z. B. das Titelblatt, = h1). Eine einzeilige ganz fette Zeile ohne Ziffern = h3.</item>
/// <item><b>Kapitel</b>: Lesezeichen der obersten Ebene (≥ 2), sonst die h1- bzw. h2-Überschriften, sonst je 20 Seiten; lange
/// Kapitel (&gt; 24 Seiten) werden an einer Überschrift bzw. Seitengrenze in Teile zerlegt (epub.js lädt Kapitelweise).</item>
/// </list>
/// Bilder und Tabellen des PDF übernimmt der Prototyp nicht (Tabellen werden zu Textzeilen).
/// </summary>
public static class EbookReflowLayout
{
    /// <summary>Ein Zeilenabschnitt (Spalte einer Zeile) mit Maßen und Auszeichnung.</summary>
    internal sealed class Segment
    {
        public int Page;
        public int Column;
        public double X0, X1, Baseline, Size;
        public double BoldShare;
        /// <summary>Breite des ersten Worts — hätte es noch in die vorige Zeile gepasst, endete dort ein Absatz (Flattersatz).</summary>
        public double FirstWordWidth;
        public List<ReflowRun> Runs = new();
        public string Text = string.Empty;
        public bool Removed;
        public double Top => Baseline - Size * 0.8;
    }

    private const int ChapterSplitPages = 24;

    // ── 1. Seite → Abschnitte ───────────────────────────────────────────────────────────────────────────

    /// <summary>Glyphen einer Seite → Abschnitte in Lesereihenfolge (noch ohne Kopfzeilen-Filter). <paramref name="gutter"/> =
    /// erkannter Bundsteg (X) oder <c>null</c> (einspaltig).</summary>
    internal static List<Segment> Segments(ReflowPage page, IReadOnlyList<ReflowDiagram> diagrams, out double? gutter)
    {
        var glyphs = page.Glyphs.Where(g => !InsideBoard(g, page, diagrams)).ToList();
        var lines = Lines(glyphs);
        gutter = FindGutter(lines, page.Width) ?? DiagramGutter(page, diagrams);
        var result = new List<Segment>();
        foreach (var line in lines)
        {
            var seg = new List<ReflowGlyph>();
            foreach (var g in line)
            {
                if (seg.Count > 0)
                {
                    var prev = seg[^1];
                    var size = Math.Max(prev.Size, g.Size);
                    var crossesGutter = gutter is double gx && prev.X1 <= gx + 0.5 && g.X0 >= gx - 0.5 && (prev.X1 + g.X0) / 2 is var mid && Math.Abs(mid - gx) < size * 3;
                    if (g.X0 - prev.X1 > 1.5 * size || crossesGutter && g.X0 - prev.X1 > 0.3 * size)
                    {
                        result.Add(Build(page, seg, gutter));
                        seg = new List<ReflowGlyph>();
                    }
                }
                seg.Add(g);
            }
            if (seg.Count > 0) result.Add(Build(page, seg, gutter));
        }
        RemoveDiagramSurroundings(result, page, diagrams);
        return result;
    }

    /// <summary>Bundsteg aus nebeneinanderstehenden Diagrammen (Aufgabenseiten: 2 × 3 Bretter, kaum Text): Mitte zwischen
    /// dem rechten Rand des linken und dem linken Rand des rechten Bretts.</summary>
    internal static double? DiagramGutter(ReflowPage page, IReadOnlyList<ReflowDiagram> diagrams)
    {
        foreach (var a in diagrams)
            foreach (var b in diagrams)
            {
                if (a.X + a.W > b.X || Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y) < Math.Min(a.H, b.H) * 0.5) continue;
                var mid = (a.X + a.W + b.X) / 2;
                if (mid > 0.3 && mid < 0.7) return mid * page.Width;
            }
        return null;
    }

    private static bool InsideBoard(ReflowGlyph g, ReflowPage page, IReadOnlyList<ReflowDiagram> diagrams)
    {
        if (diagrams.Count == 0) return false;
        var cx = (g.X0 + g.X1) / 2;
        var cy = g.Baseline - g.Size * 0.3;
        foreach (var d in diagrams)
        {
            var x0 = d.X * page.Width; var x1 = (d.X + d.W) * page.Width;
            var y0 = d.Y * page.Height; var y1 = (d.Y + d.H) * page.Height;
            var pad = (x1 - x0) / 8 * 0.15;
            if (cx >= x0 - pad && cx <= x1 + pad && cy >= y0 - pad && cy <= y1 + pad) return true;
        }
        return false;
    }

    private static readonly Regex Coordinates = new(@"^[a-h1-8](\s?[a-h1-8]){0,7}$", RegexOptions.Compiled);
    private static readonly Regex OnlyNumber = new(@"^\(?\d{1,4}[.)]?$", RegexOptions.Compiled);

    /// <summary>Im Rand um ein Brett: Koordinaten, Aufgabennummer (kommt in die Bildunterschrift) und Restzeichen weg.</summary>
    private static void RemoveDiagramSurroundings(List<Segment> segs, ReflowPage page, IReadOnlyList<ReflowDiagram> diagrams)
    {
        foreach (var d in diagrams)
        {
            var x0 = d.X * page.Width; var x1 = (d.X + d.W) * page.Width;
            var y0 = d.Y * page.Height; var y1 = (d.Y + d.H) * page.Height;
            var cell = (x1 - x0) / 8;
            foreach (var s in segs)
            {
                if (s.Removed) continue;
                var cx = (s.X0 + s.X1) / 2; var cy = s.Baseline - s.Size * 0.3;
                if (cx < x0 - 3.2 * cell || cx > x1 + 2.6 * cell || cy < y0 - 1.6 * cell || cy > y1 + 1.4 * cell) continue;
                var t = s.Text.Trim();
                var inRing = cx >= x0 - 1.6 * cell && cx <= x1 + 1.2 * cell && cy >= y0 - 0.8 * cell && cy <= y1 + 1.1 * cell;
                if (inRing && Coordinates.IsMatch(t)) s.Removed = true;
                else if (d.Label != null && OnlyNumber.IsMatch(t) && t.Trim('(', ')', '.') == d.Label && cy <= y0 + 2 * cell) s.Removed = true;
                else if (inRing && !t.Any(char.IsLetterOrDigit)) s.Removed = true;
            }
        }
    }

    /// <summary>Glyphen → Zeilen (gleiche Grundlinie), jede von links nach rechts.</summary>
    internal static List<List<ReflowGlyph>> Lines(List<ReflowGlyph> glyphs)
    {
        var lines = new List<List<ReflowGlyph>>();
        double lineBase = double.NaN, lineSize = 0;
        foreach (var g in glyphs.OrderBy(g => g.Baseline).ThenBy(g => g.X0))
        {
            var tol = 0.35 * Math.Max(lineSize, g.Size);
            if (lines.Count > 0 && Math.Abs(g.Baseline - lineBase) <= tol) { lines[^1].Add(g); lineSize = Math.Max(lineSize, g.Size); continue; }
            lines.Add([g]);
            lineBase = g.Baseline;
            lineSize = g.Size;
        }
        foreach (var l in lines) l.Sort((a, b) => a.X0.CompareTo(b.X0));
        return lines;
    }

    /// <summary>Bundsteg: eine X-Position zwischen 35 und 65 % der Breite, die höchstens 6 % der Zeilen kreuzen, mit je
    /// mindestens 25 % der Zeilen ganz links bzw. ganz rechts davon. <c>null</c> = einspaltig.</summary>
    internal static double? FindGutter(List<List<ReflowGlyph>> lines, double width)
    {
        if (lines.Count < 8 || width <= 0) return null;
        // Je Zeile die bedeckten Strecken: Glyphen mit Lücken unter 0,8 Schriftgröße (Wortzwischenräume) verschmolzen.
        var spans = lines.Select(l =>
        {
            var list = new List<(double A, double B)>();
            foreach (var g in l)
            {
                if (list.Count > 0 && g.X0 - list[^1].B < 0.8 * g.Size) list[^1] = (list[^1].A, Math.Max(list[^1].B, g.X1));
                else list.Add((g.X0, g.X1));
            }
            return list;
        }).ToList();
        double? best = null; var bestCross = int.MaxValue;
        for (var x = width * 0.35; x <= width * 0.65; x += 1)
        {
            int cross = 0, left = 0, right = 0;
            foreach (var l in spans)
            {
                var hasLeft = false; var hasRight = false; var spansX = false;
                foreach (var (a, b) in l)
                {
                    if (a < x && b > x) { spansX = true; break; }
                    if (b <= x) hasLeft = true; else hasRight = true;
                }
                if (spansX) cross++;
                else { if (hasLeft) left++; if (hasRight) right++; }
            }
            if (cross > lines.Count * 0.06 || left < lines.Count * 0.25 || right < lines.Count * 0.25) continue;
            if (cross < bestCross) { bestCross = cross; best = x; }
        }
        if (best is not double g) return null;
        // Beide Seiten brauchen Fließtext-Zeilen (Median-Breite ≥ 22 % der Seite) — sonst ist es eine Tabelle oder ein
        // Inhaltsverzeichnis (Titel links, Seitenzahlen rechts), keine zweite Spalte.
        double MedianWidth(bool leftSide)
        {
            var widths = spans.Select(l => l.Where(sp => leftSide ? sp.B <= g : sp.A >= g).Select(sp => sp.B - sp.A).DefaultIfEmpty(0).Sum())
                .Where(w => w > 0).OrderBy(w => w).ToList();
            return widths.Count == 0 ? 0 : widths[widths.Count / 2];
        }
        return MedianWidth(true) >= width * 0.22 && MedianWidth(false) >= width * 0.22 ? g : null;
    }

    private static Segment Build(ReflowPage page, List<ReflowGlyph> glyphs, double? gutter)
    {
        var runs = new List<ReflowRun>();
        var sb = new StringBuilder();
        var cur = new StringBuilder(); bool curBold = glyphs[0].Bold, curItalic = glyphs[0].Italic;
        var boldChars = 0; var chars = 0;
        var sizes = new Dictionary<double, int>();
        for (var i = 0; i < glyphs.Count; i++)
        {
            var g = glyphs[i];
            if (i > 0)
            {
                var prev = glyphs[i - 1];
                if (g.X0 - prev.X1 > 0.12 * Math.Max(g.Size, prev.Size)) { cur.Append(' '); sb.Append(' '); }
            }
            if (g.Bold != curBold || g.Italic != curItalic)
            {
                if (cur.Length > 0) runs.Add(new ReflowRun(cur.ToString(), curBold, curItalic));
                cur.Clear(); curBold = g.Bold; curItalic = g.Italic;
            }
            cur.Append(g.Text); sb.Append(g.Text);
            chars += g.Text.Length;
            if (g.Bold) boldChars += g.Text.Length;
            var key = Math.Round(g.Size * 2) / 2;
            sizes[key] = sizes.GetValueOrDefault(key) + g.Text.Length;
        }
        if (cur.Length > 0) runs.Add(new ReflowRun(cur.ToString(), curBold, curItalic));
        var x0 = glyphs[0].X0; var x1 = glyphs[^1].X1;
        var firstWordEnd = glyphs[0].X1;
        for (var i = 1; i < glyphs.Count && glyphs[i].X0 - glyphs[i - 1].X1 <= 0.12 * Math.Max(glyphs[i].Size, glyphs[i - 1].Size); i++)
            firstWordEnd = glyphs[i].X1;
        return new Segment
        {
            FirstWordWidth = firstWordEnd - x0,
            Page = page.Number,
            Column = gutter is double gx && x0 >= gx - 0.5 ? 1 : 0,
            X0 = x0, X1 = x1,
            Baseline = glyphs.Min(g => g.Baseline),
            Size = sizes.OrderByDescending(kv => kv.Value).First().Key,
            BoldShare = chars == 0 ? 0 : (double)boldChars / chars,
            Runs = runs,
            Text = sb.ToString(),
        };
    }

    // ── 2. Kolumnentitel ──────────────────────────────────────────────────────────────────────────────

    private static readonly Regex PageNumber = new(@"^(\d{1,4}|[ivxlcdm]{1,7})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Kolumnentitel/Fußzeilen und Seitenzahlen markieren (<see cref="Segment.Removed"/>).</summary>
    internal static void RemoveRunningHeads(IReadOnlyList<(ReflowPage Page, List<Segment> Segs)> pages, IEnumerable<string>? knownHeads = null)
    {
        // Kolumnentitel sind meist Kapitel- oder Buchtitel — die kennen wir aus den Lesezeichen, auch wenn ein kurzes Kapitel
        // nur zwei Seiten hat (unter der 3-Seiten-Schwelle).
        var known = new HashSet<string>((knownHeads ?? []).Select(Key).Where(k => k.Length > 2));
        var candidates = new List<(Segment Seg, string Key)>();
        foreach (var (page, segs) in pages)
        {
            var live = segs.Where(s => !s.Removed).OrderBy(s => s.Baseline).ToList();
            if (live.Count == 0) continue;
            // Oben: alle Abschnitte der obersten Zeile im Streifen, wenn darunter Abstand ist.
            AddEdge(live, page, top: true, candidates);
            AddEdge(live, page, top: false, candidates);
        }
        var counts = candidates.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.Select(c => c.Seg.Page).Distinct().Count());
        var body = BodySize(pages.SelectMany(p => p.Segs).Where(s => !s.Removed));
        foreach (var (seg, key) in candidates)
        {
            // Ein bekannter Titel in großer Schrift ist die Kapitelüberschrift selbst, kein Kolumnentitel.
            var knownHead = seg.Size <= body * 1.1 && (known.Contains(key) || known.Contains(Key(Regex.Replace(seg.Text, @"^\s*\d+\.?\s+", ""))));
            if (PageNumber.IsMatch(seg.Text.Trim()) || counts[key] >= 3 || knownHead) seg.Removed = true;
        }
    }

    private static void AddEdge(List<Segment> live, ReflowPage page, bool top, List<(Segment, string)> into)
    {
        var edge = top ? live[0] : live[^1];
        var band = top ? page.Height * 0.09 : page.Height * 0.93;
        if (top ? edge.Baseline > band : edge.Baseline < band) return;
        var row = live.Where(s => Math.Abs(s.Baseline - edge.Baseline) < edge.Size * 0.5).ToList();
        var others = live.Except(row).ToList();
        if (others.Count > 0)
        {
            var next = top ? others.Min(s => s.Baseline) : others.Max(s => s.Baseline);
            var gap = Math.Abs(next - edge.Baseline);
            if (gap < edge.Size * 1.6) return;   // klebt am Text: gehört zum Text
        }
        foreach (var s in row) into.Add((s, Key(s.Text)));
    }

    private static string Key(string text) => Regex.Replace(text.Trim().ToLowerInvariant(), @"\d+", "#");

    // ── 3. Absätze ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Alle Seiten → Blöcke in Lesereihenfolge.</summary>
    public static List<ReflowBlock> Blocks(IReadOnlyList<ReflowPage> pages, IReadOnlyList<ReflowDiagram> diagrams, IEnumerable<string>? knownHeads = null)
    {
        var byPage = diagrams.GroupBy(d => d.Page).ToDictionary(g => g.Key, g => g.ToList());
        var perPage = new List<(ReflowPage Page, List<Segment> Segs, double? Gutter)>();
        foreach (var p in pages)
        {
            var ds = byPage.GetValueOrDefault(p.Number) ?? [];
            var segs = Segments(p, ds, out var gutter);
            perPage.Add((p, segs, gutter));
        }
        RemoveRunningHeads(perPage.Select(x => (x.Page, x.Segs)).ToList(), knownHeads);

        var all = perPage.SelectMany(x => x.Segs).Where(s => !s.Removed).ToList();
        var body = BodySize(all);
        var spacing = LineSpacing(perPage.Select(x => x.Segs.Where(s => !s.Removed).ToList()), body);
        var levels = HeadingLevels(all, body);

        var blocks = new List<ReflowBlock>();
        ReflowParagraph? cur = null; Segment? last = null;
        (double Left, double Right) lastBox = default;
        double firstX0 = 0, secondX0 = double.NaN;   // Einzug der ersten und zweiten Zeile des laufenden Absatzes
        foreach (var (page, segs, gutter) in perPage)
        {
            var ds = byPage.GetValueOrDefault(page.Number) ?? [];
            var boxes = ColumnBoxes(segs.Where(s => !s.Removed).ToList(), body, page.Width, gutter);
            var items = new List<(int Col, double Top, double X, object Item)>();
            foreach (var s in segs.Where(s => !s.Removed)) items.Add((s.Column, s.Top, s.X0, s));
            foreach (var d in ds.OrderBy(d => d.Y).ThenBy(d => d.X))
            {
                var cx = (d.X + d.W / 2) * page.Width;
                items.Add((gutter is double gx && cx > gx ? 1 : 0, d.Y * page.Height, d.X * page.Width, d));
            }
            var ordered = items.OrderBy(i => i.Col).ThenBy(i => Math.Round(i.Top, 1)).ThenBy(i => i.X).ToList();
            for (var k = 0; k < ordered.Count; k++)
            {
                var (col, _, _, item) = ordered[k];
                if (item is ReflowDiagram d)
                {
                    if (cur != null) { blocks.Add(cur); cur = null; }
                    blocks.Add(new ReflowFigure(page.Number, d));
                    last = null;
                    continue;
                }
                var s = (Segment)item;
                var level = levels.TryGetValue(Math.Round(s.Size * 2) / 2, out var lv) && s.Text.Any(char.IsLetter) && s.Text.Length <= 160 ? lv : 0;
                var box = boxes[col];
                var next = k + 1 < ordered.Count && ordered[k + 1].Item is Segment n && n.Column == s.Column ? n : null;
                if (cur != null && last != null && Continues(cur, last, lastBox, s, box, level, body, spacing, next, firstX0, secondX0))
                {
                    Append(cur.Runs, s.Runs);
                    if (double.IsNaN(secondX0)) secondX0 = s.X0;
                }
                else
                {
                    if (cur != null) blocks.Add(cur);
                    cur = new ReflowParagraph(page.Number, level, s.Runs.Select(r => r with { }).ToList());
                    firstX0 = s.X0; secondX0 = double.NaN;
                }
                last = s; lastBox = box;
            }
        }
        if (cur != null) blocks.Add(cur);
        return PromoteBoldLines(blocks, body);
    }

    private static bool Continues(ReflowParagraph cur, Segment last, (double Left, double Right) lastBox, Segment s,
        (double Left, double Right) box, int level, double body, double spacing, Segment? next, double firstX0, double secondX0)
    {
        if (level != cur.Level) return false;
        var samePlace = s.Page == last.Page && s.Column == last.Column && s.Baseline > last.Baseline;
        if (level > 0) return samePlace && s.Baseline - last.Baseline < 2.3 * s.Size;
        if (samePlace && s.Baseline - last.Baseline > 1.45 * spacing) return false;
        var hangingPara = !double.IsNaN(secondX0) && secondX0 > firstX0 + 0.7 * body;
        if (s.X0 > box.Left + 0.7 * body)
        {
            // Eingerückt = neuer Absatz (Erstzeileneinzug, zentriert) — außer beim HÄNGENDEN Einzug der Variantenbücher
            // („a) 10 ♘xe6 …“ am Rand, die Folgezeilen eingerückt): Folgezeile des Absatzes, die wie die nächste Zeile steht.
            var hanging = double.IsNaN(secondX0)
                ? s.X0 > firstX0 + 0.7 * body && (firstX0 <= box.Left + 0.7 * body || next != null && Math.Abs(next.X0 - s.X0) < 0.5 * body)
                : hangingPara && Math.Abs(s.X0 - secondX0) < 0.5 * body;
            if (!hanging) return false;
        }
        else if (hangingPara && s.X0 < secondX0 - 0.7 * body) return false;    // zurück an den Rand: nächster hängender Absatz
        if (last.X1 < lastBox.Right - 2.0 * body) return false;                   // vorige Zeile kurz = Absatzende
        if (last.X1 + s.FirstWordWidth + 0.3 * body < lastBox.Right) return false; // erstes Wort hätte noch gepasst (Flattersatz)
        if (last.X0 > lastBox.Left + 2.0 * body && last.X1 < lastBox.Right - 1.0 * body) return false;   // zentrierte Zeile
        if (!samePlace && s.Page != last.Page && s.Page != last.Page + 1) return false;
        return true;
    }

    /// <summary>Hängt Text an: Trennstrich am Zeilenende vor Kleinbuchstaben fällt weg, sonst Leerzeichen.</summary>
    internal static void Append(List<ReflowRun> runs, List<ReflowRun> next)
    {
        if (next.Count == 0) return;
        if (runs.Count == 0) { runs.AddRange(next); return; }
        var lastRun = runs[^1];
        var t = lastRun.Text.TrimEnd();
        var first = next[0].Text.TrimStart();
        var nextFirst = first.Length > 0 ? first[0] : ' ';
        string joined;
        if (t.EndsWith('­')) joined = t[..^1];
        else if (t.Length >= 2 && t[^1] == '-' && char.IsLetter(t[^2]) && char.IsLower(nextFirst)) joined = t[..^1];
        else if (t.EndsWith('-') || t.EndsWith('/') || t.EndsWith('\u2212')) joined = t;   // „Varga,Z−“ + „Pirisi“ (Minus als Bindestrich)
        else joined = t + " ";
        runs[^1] = lastRun with { Text = joined };
        var head = next[0] with { Text = first };
        if (head.Bold == runs[^1].Bold && head.Italic == runs[^1].Italic) runs[^1] = runs[^1] with { Text = runs[^1].Text + head.Text };
        else runs.Add(head);
        runs.AddRange(next.Skip(1));
    }

    private static readonly Regex HasDigit = new(@"\d", RegexOptions.Compiled);

    /// <summary>Einzeilige, ganz fette Zeile in Grundschrift ohne Ziffern (keine Züge) = Zwischenüberschrift (h3).</summary>
    private static List<ReflowBlock> PromoteBoldLines(List<ReflowBlock> blocks, double body)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not ReflowParagraph { Level: 0 } p) continue;
            var text = p.PlainText.Trim();
            if (text.Length < 3 || text.Length > 70 || HasDigit.IsMatch(text) || !char.IsUpper(text[0])) continue;
            if (!p.Runs.All(r => r.Bold || string.IsNullOrWhiteSpace(r.Text))) continue;
            if (text.Any(c => c is '♔' or '♕' or '♖' or '♗' or '♘') || text.EndsWith('.') || text.EndsWith(',')) continue;
            blocks[i] = p with { Level = 3 };
        }
        return blocks;
    }

    /// <summary>Grundschrift = die Größe mit den meisten Zeichen.</summary>
    internal static double BodySize(IEnumerable<Segment> segs)
    {
        var counts = new Dictionary<double, int>();
        foreach (var s in segs) counts[s.Size] = counts.GetValueOrDefault(s.Size) + s.Text.Length;
        return counts.Count == 0 ? 10 : counts.OrderByDescending(kv => kv.Value).First().Key;
    }

    private static double LineSpacing(IEnumerable<List<Segment>> pages, double body)
    {
        var diffs = new List<double>();
        foreach (var segs in pages)
            foreach (var col in segs.Where(s => Math.Abs(s.Size - body) < 0.6).GroupBy(s => s.Column))
            {
                var ys = col.Select(s => s.Baseline).Distinct().OrderBy(y => y).ToList();
                for (var i = 1; i < ys.Count; i++)
                {
                    var d = ys[i] - ys[i - 1];
                    if (d > body * 0.8 && d < body * 2.5) diffs.Add(d);
                }
            }
        if (diffs.Count == 0) return body * 1.2;
        diffs.Sort();
        return diffs[diffs.Count / 2];
    }

    /// <summary>Schriftgröße → Überschriftenstufe (nur Größen ≥ 1,15 × Grundschrift).</summary>
    internal static Dictionary<double, int> HeadingLevels(IReadOnlyList<Segment> segs, double body)
    {
        var big = segs.Where(s => s.Size >= body * 1.15 + 0.2 && s.Text.Any(char.IsLetter)).ToList();
        var pagesBySize = big.GroupBy(s => s.Size).ToDictionary(g => g.Key, g => g.Select(s => s.Page).Distinct().Count());
        // Größen, die fast überall vorkommen, sind keine Überschriften (z. B. ein größerer Satzspiegel in Teilen des Buchs).
        var totalPages = segs.Select(s => s.Page).Distinct().Count();
        var ranked = pagesBySize.Where(kv => kv.Value >= 2 && kv.Value <= Math.Max(4, totalPages * 0.6)).Select(kv => kv.Key).OrderByDescending(s => s).ToList();
        var result = new Dictionary<double, int>();
        foreach (var size in pagesBySize.Keys)
        {
            if (pagesBySize[size] > Math.Max(4, totalPages * 0.6)) continue;
            var idx = ranked.IndexOf(size);
            result[size] = idx < 0 ? (ranked.Count == 0 || size > ranked[0] ? 1 : 3) : Math.Min(idx + 1, 3);
        }
        return result;
    }

    /// <summary>Linker/rechter Rand je Spalte (Perzentile der Grundschrift-Zeilen) — Maß für Einzug und kurze Zeilen.</summary>
    private static (double Left, double Right)[] ColumnBoxes(List<Segment> segs, double body, double width, double? gutter)
    {
        var res = new (double, double)[2];
        for (var c = 0; c < 2; c++)
        {
            var col = segs.Where(s => s.Column == c && Math.Abs(s.Size - body) < 1.0).ToList();
            if (col.Count < 3) col = segs.Where(s => s.Column == c).ToList();
            if (col.Count == 0)
            {
                res[c] = c == 0 ? (width * 0.1, gutter ?? width * 0.9) : (gutter ?? width * 0.5, width * 0.9);
                continue;
            }
            var lefts = col.Select(s => s.X0).OrderBy(x => x).ToList();
            var rights = col.Select(s => s.X1).OrderBy(x => x).ToList();
            res[c] = (lefts[(int)(lefts.Count * 0.1)], rights[Math.Min(rights.Count - 1, (int)(rights.Count * 0.95))]);
        }
        return res;
    }

    // ── 4. Kapitel ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Blöcke → Kapitel (Lesezeichen, sonst Überschriften, sonst feste Seitenzahl), lange Kapitel in Teilen.</summary>
    public static List<ReflowChapter> Chapters(List<ReflowBlock> blocks, IReadOnlyList<ReflowOutline> outline, int pageCount)
    {
        var chapters = new List<ReflowChapter>();
        if (blocks.Count == 0) return chapters;
        var starts = ChapterStarts(blocks, outline, pageCount);
        var idx = 0;
        // Vorspann vor dem ersten Kapitel.
        var front = new ReflowChapter { Title = "—" };
        var si = 0;
        ReflowChapter current = front;
        foreach (var b in blocks)
        {
            while (si < starts.Count && StartsHere(starts[si], b, idx))
            {
                if (current.Blocks.Count > 0) chapters.Add(current);
                current = new ReflowChapter { Title = starts[si].Title };
                current.Sections.AddRange(starts[si].Sections);
                si++;
            }
            current.Blocks.Add(b);
            idx++;
        }
        if (current.Blocks.Count > 0) chapters.Add(current);
        if (chapters.Count > 0 && chapters[0] == front)
            front.Title = FirstHeading(front) ?? (chapters.Count == 1 ? "Text" : "Front matter");
        foreach (var c in chapters) if (string.IsNullOrWhiteSpace(c.Title)) c.Title = FirstHeading(c) ?? $"p. {c.Blocks[0].Page}";
        return SplitLong(chapters);
    }

    private sealed record Start(int Page, int? BlockIndex, string Title, List<(string, int)> Sections);

    private static bool StartsHere(Start s, ReflowBlock b, int idx) => s.BlockIndex is int bi ? idx >= bi : b.Page >= s.Page;

    private static List<Start> ChapterStarts(List<ReflowBlock> blocks, IReadOnlyList<ReflowOutline> outline, int pageCount)
    {
        // Kapitel = Lesezeichen der obersten ZWEI Ebenen (Quality Chess: „Title, Copyright, Contents ▸ 1. Easy Exercises …“ —
        // die eigentlichen Kapitel hängen unter „Contents“); sind das zu viele (Partiesammlung), nur die oberste.
        var maxLevel = outline.Count(o => o.Level <= 1) <= 80 ? 1 : 0;
        var top = outline.Where(o => o.Level <= maxLevel).ToList();
        if (top.Count >= 2)
        {
            var list = new List<Start>();
            foreach (var o in top.OrderBy(o => o.Page).ThenBy(o => o.Level))
            {
                if (list.Count > 0 && list[^1].Page == o.Page) { list[^1].Sections.Add((o.Title, o.Page)); continue; }
                list.Add(new Start(o.Page, null, o.Title, new()));
            }
            // Unterpunkte: Lesezeichen eine Ebene tiefer, dem Kapitel nach Seite zugeordnet.
            foreach (var o in outline.Where(o => o.Level == maxLevel + 1).OrderBy(o => o.Page))
            {
                var owner = list.LastOrDefault(s => s.Page <= o.Page);
                owner?.Sections.Add((o.Title, o.Page));
            }
            return list;
        }
        foreach (var lv in new[] { 1, 2 })
        {
            var heads = blocks.Select((b, i) => (b, i)).Where(x => x.b is ReflowParagraph p && p.Level == lv).ToList();
            if (heads.Count >= 2 && heads.Count <= 400)
            {
                var list = new List<Start>();
                foreach (var (b, i) in heads)
                {
                    // Mehrere Überschriften derselben Stufe direkt hintereinander (Kapitelnummer + Titel) = ein Start.
                    if (list.Count > 0 && list[^1].BlockIndex is int prev && i - prev <= 1) continue;
                    list.Add(new Start(b.Page, i, ((ReflowParagraph)b).PlainText.Trim(), new()));
                }
                return list;
            }
        }
        var chunks = new List<Start>();
        for (var p = 21; p <= pageCount; p += 20) chunks.Add(new Start(p, null, $"p. {p}", new()));
        return chunks;
    }

    private static string? FirstHeading(ReflowChapter c) =>
        c.Blocks.OfType<ReflowParagraph>().FirstOrDefault(p => p.Level > 0 && p.PlainText.Trim().Length > 1)?.PlainText.Trim() is string t
            ? (t.Length > 120 ? t[..120] : t) : null;

    /// <summary>Kapitel über <see cref="ChapterSplitPages"/> Seiten in Teile: an der ersten Überschrift nach 16 Seiten,
    /// spätestens an der Seitengrenze nach 24 Seiten.</summary>
    private static List<ReflowChapter> SplitLong(List<ReflowChapter> chapters)
    {
        var result = new List<ReflowChapter>();
        foreach (var c in chapters)
        {
            var part = new ReflowChapter { Title = c.Title, Part = 1 };
            part.Sections.AddRange(c.Sections);
            var partStart = c.Blocks[0].Page;
            var prevPage = partStart;
            foreach (var b in c.Blocks)
            {
                var span = b.Page - partStart;
                var cut = part.Blocks.Count > 0 && ((span >= 16 && b is ReflowParagraph { Level: > 0 }) || (span >= ChapterSplitPages && b.Page != prevPage));
                if (cut)
                {
                    result.Add(part);
                    part = new ReflowChapter { Title = c.Title, Part = part.Part + 1 };
                    partStart = b.Page;
                }
                part.Blocks.Add(b);
                prevPage = b.Page;
            }
            result.Add(part);
        }
        return result;
    }

    /// <summary>Sprache grob aus häufigen Wörtern (en/de/fr/es/nl), Vorgabe Englisch.</summary>
    public static string GuessLanguage(IEnumerable<ReflowBlock> blocks)
    {
        var text = string.Join(' ', blocks.OfType<ReflowParagraph>().Take(400).Select(p => p.PlainText)).ToLowerInvariant();
        var words = Regex.Matches(text, @"\p{L}+").Select(m => m.Value).ToList();
        int Count(params string[] ws) => words.Count(w => ws.Contains(w));
        var scores = new Dictionary<string, int>
        {
            ["en"] = Count("the", "and", "is", "of", "with"),
            ["de"] = Count("der", "die", "und", "ist", "nicht", "mit"),
            ["fr"] = Count("le", "les", "et", "est", "une", "des"),
            ["es"] = Count("el", "los", "y", "es", "una", "las"),
            ["nl"] = Count("de", "het", "en", "is", "een", "niet"),
        };
        var best = scores.OrderByDescending(kv => kv.Value).First();
        return best.Value == 0 ? "en" : best.Key;
    }
}
