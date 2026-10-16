using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Kapitel eines Buchs aus seinem eigenen Inhaltsverzeichnis (0.805.0, „Als Kurs anlegen“ gliedert nach Buchkapiteln):
/// beim PDF die Lesezeichen (Position = Seite), beim EPUB das Inhaltsverzeichnis (nav.xhtml oder toc.ncx; Position =
/// Kapitel im Spine, 1-basiert wie <see cref="Models.EbookDiagram.Page"/> der EPUB-Diagramme). Ein Diagramm gehört zum
/// letzten Eintrag, der an oder vor seiner Position beginnt.
/// </summary>
public static class EbookChapterIndex
{
    public sealed record Entry(int Position, int Level, string Label);

    /// <summary>Kapitelname für eine Position — gegliedert nach der obersten Ebene, wenn sie mindestens zwei Einträge hat
    /// (sonst auch die zweite: Bücher mit einem einzigen Titel-Lesezeichen und den Kapiteln darunter). <c>null</c> vor dem
    /// ersten Eintrag oder ohne Verzeichnis.</summary>
    public static string? Pick(IReadOnlyList<Entry> entries, int position)
    {
        if (entries.Count == 0) return null;
        var top = entries.Min(e => e.Level);
        var maxLevel = entries.Count(e => e.Level == top) >= 2 ? top : top + 1;
        Entry? best = null;
        foreach (var e in entries)
            if (e.Level <= maxLevel && e.Position <= position && (best == null || e.Position >= best.Position)) best = e;
        return best?.Label;
    }

    public static IReadOnlyList<Entry> FromPdf(string path)
    {
        var list = new List<Entry>();
        try
        {
            using var doc = PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
            if (!doc.TryGetBookmarks(out var bookmarks)) return list;
            void Walk(BookmarkNode n)
            {
                if (n is DocumentBookmarkNode d && d.PageNumber > 0 && Clean(n.Title) is { Length: > 0 } t) list.Add(new Entry(d.PageNumber, n.Level, t));
                foreach (var c in n.Children) Walk(c);
            }
            foreach (var r in bookmarks.Roots) Walk(r);
        }
        catch (Exception e) when (e is not OperationCanceledException) { /* kaputte Lesezeichen: ohne Kapitel */ }
        return list.OrderBy(e => e.Position).ToList();
    }

    private static readonly Regex FullPath = new(@"full-path=""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex Item = new(@"<item\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Attr = new(@"\b(id|href|media-type|properties)\s*=\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ItemRef = new(@"<itemref\b[^>]*\bidref\s*=\s*""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NavToc = new(@"<nav\b[^>]*epub:type\s*=\s*""[^""]*\btoc\b[^""]*""[^>]*>(.*?)</nav>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex NavToken = new(@"<(/?)ol\b[^>]*>|<a\b[^>]*\bhref\s*=\s*""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex NcxToken = new(@"<(/?)navPoint\b[^>]*>|<text>(.*?)</text>|<content\b[^>]*\bsrc\s*=\s*""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);

    public static IReadOnlyList<Entry> FromEpub(string path)
    {
        var list = new List<Entry>();
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var container = Text(zip, "META-INF/container.xml");
            var opfPath = container == null ? null : FullPath.Match(container) is { Success: true } m ? m.Groups[1].Value : null;
            var opf = opfPath == null ? null : Text(zip, opfPath);
            if (opf == null) return list;
            var opfDir = Dir(opfPath!);
            var items = Item.Matches(opf).Select(x => Attr.Matches(x.Value).ToDictionary(a => a.Groups[1].Value.ToLowerInvariant(), a => a.Groups[2].Value))
                .Where(a => a.ContainsKey("id") && a.ContainsKey("href")).ToList();
            var byId = items.GroupBy(a => a["id"]).ToDictionary(g => g.Key, g => g.First());
            var spine = ItemRef.Matches(opf).Select(x => byId.TryGetValue(x.Groups[1].Value, out var a) ? Key(Join(opfDir, a["href"])) : null).ToList();
            int SpineIndex(string baseDir, string href)
            {
                var file = href.Split('#')[0];
                var i = spine.IndexOf(Key(Join(baseDir, file)));
                return i < 0 ? -1 : i + 1;
            }
            var nav = items.FirstOrDefault(a => a.TryGetValue("properties", out var p) && p.Split(' ').Contains("nav"));
            if (nav != null && Text(zip, Join(opfDir, nav["href"])) is { } navDoc && NavToc.Match(navDoc) is { Success: true } toc)
            {
                var navDir = Dir(Join(opfDir, nav["href"]));
                var depth = 0;
                foreach (Match t in NavToken.Matches(toc.Groups[1].Value))
                {
                    if (t.Groups[2].Success) Add(SpineIndex(navDir, t.Groups[2].Value), depth - 1, t.Groups[3].Value);
                    else depth += t.Groups[1].Value == "/" ? -1 : 1;
                }
            }
            if (list.Count == 0 && items.FirstOrDefault(a => a.TryGetValue("media-type", out var mt) && mt.Contains("ncx")) is { } ncxItem
                && Text(zip, Join(opfDir, ncxItem["href"])) is { } ncx)
            {
                var ncxDir = Dir(Join(opfDir, ncxItem["href"]));
                var depth = 0; string? label = null;
                foreach (Match t in NcxToken.Matches(ncx))
                {
                    if (t.Groups[2].Success) label = t.Groups[2].Value;
                    else if (t.Groups[3].Success) { if (label != null) Add(SpineIndex(ncxDir, t.Groups[3].Value), depth - 1, label); label = null; }
                    else depth += t.Groups[1].Value == "/" ? -1 : 1;
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException) { /* kaputtes EPUB: ohne Kapitel */ }
        return list.OrderBy(e => e.Position).ToList();

        void Add(int position, int level, string raw)
        {
            if (position < 1) return;
            var label = Clean(WebUtility.HtmlDecode(Tag.Replace(raw, " ")));
            if (label.Length > 0) list.Add(new Entry(position, Math.Max(0, level), label));
        }
    }

    private static string? Text(ZipArchive zip, string path)
    {
        var e = zip.GetEntry(path) ?? zip.Entries.FirstOrDefault(x => string.Equals(x.FullName, path, StringComparison.OrdinalIgnoreCase));
        if (e == null || e.Length > 4_000_000) return null;
        using var r = new StreamReader(e.Open());
        return r.ReadToEnd();
    }

    private static string Dir(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";

    /// <summary>Pfad im Archiv auflösen („../“), Vergleich ohne URL-Kodierung und Groß/Klein (calibre kodiert Leerzeichen und Klammern).</summary>
    private static string Join(string dir, string href)
    {
        var parts = new List<string>(dir.Length == 0 ? [] : dir.Split('/'));
        foreach (var p in href.Split('/'))
        {
            if (p == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else if (p != "." && p.Length > 0) parts.Add(p);
        }
        return string.Join('/', parts);
    }

    private static string Key(string path)
    {
        try { path = Uri.UnescapeDataString(path); } catch (UriFormatException) { /* so lassen */ }
        return path.ToLowerInvariant();
    }

    private static string Clean(string s) => Regex.Replace(s.Replace('\0', ' '), @"\s+", " ").Trim() is var t && t.Length > 200 ? t[..200] : t;
}
