using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RookHub.Api.Services.Ebooks;

/// <summary>Ein Baustein eines EPUB-Kapitels in Lesereihenfolge: ein Textabsatz oder ein Bild (Pfad im ZIP).</summary>
public sealed record EpubBlock(int Spine, int Order, string? Text, string? Image);

/// <summary>
/// Liest ein EPUB für die Diagramm-Erkennung (0.775.0): <c>META-INF/container.xml</c> → OPF → Spine, dann je Kapitel die
/// Blöcke in Lesereihenfolge — Absätze (Block-Tags beenden einen Absatz, Inline-Tags nicht, damit „33.<span>♖</span>d1“
/// zusammenbleibt) und Bilder (<c>&lt;img src&gt;</c>, SVG <c>&lt;image href&gt;</c>) als Pfad im ZIP. Bewusst ohne
/// XML-Parser für die Kapitel: viele EPUBs sind kein wohlgeformtes XHTML.
/// </summary>
public static class EbookEpubContent
{
    private static readonly Regex Tag = new(@"<(/?)([a-zA-Z][a-zA-Z0-9:]*)([^>]*)>|<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Src = new(@"\b(?:src|xlink:href|href)\s*=\s*(""[^""]*""|'[^']*')", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "br", "li", "tr", "td", "th", "h1", "h2", "h3", "h4", "h5", "h6", "table", "blockquote", "section",
        "figure", "figcaption", "dt", "dd", "pre", "hr", "body", "ul", "ol",
    };

    /// <summary>Spine-Kapitel als ZIP-Pfade, in Lesereihenfolge.</summary>
    public static List<string> Spine(ZipArchive zip)
    {
        var container = Load(zip, "META-INF/container.xml");
        var opfPath = container?.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
        if (opfPath == null) return [];
        var opf = Load(zip, opfPath);
        if (opf == null) return [];
        var dir = DirOf(opfPath);
        var manifest = opf.Descendants().Where(e => e.Name.LocalName == "item")
            .Select(e => (Id: e.Attribute("id")?.Value, Href: e.Attribute("href")?.Value))
            .Where(i => i.Id != null && i.Href != null)
            .GroupBy(i => i.Id!).ToDictionary(g => g.Key, g => Resolve(dir, Uri.UnescapeDataString(g.First().Href!)));
        return opf.Descendants().Where(e => e.Name.LocalName == "itemref")
            .Select(e => e.Attribute("idref")?.Value).Where(id => id != null && manifest.ContainsKey(id))
            .Select(id => manifest[id!]).ToList();
    }

    /// <summary>Größtes Kapitel (entpackt), das gelesen wird; größere werden übersprungen (R3-009: ein Kapitel aus 1 GB
    /// Leerzeichen packt sich auf 1 MB). Echte Kapitel liegen weit darunter.</summary>
    public const long MaxChapterBytes = 16L * 1024 * 1024;

    /// <summary>Vorgabe für die Summe aller gelesenen Kapitel (entpackt); danach endet <see cref="Read"/> (R3-009).</summary>
    public const long MaxTotalBytes = 128L * 1024 * 1024;

    /// <summary>Alle Blöcke des Buchs; <see cref="EpubBlock.Spine"/> ist 1-basiert. Gedeckelt (R3-009): ein Kapitel über
    /// <see cref="MaxChapterBytes"/> wird übersprungen (gezählt wird beim Lesen, der Längenangabe im ZIP wird nicht
    /// vertraut), nach <paramref name="maxTotalBytes"/> entpackten Bytes endet das Lesen, und ein Spine-Pfad, der mehrfach
    /// in der Spine steht, wird nur beim ersten Mal gelesen (die Nummerierung der übrigen Kapitel bleibt).</summary>
    public static List<EpubBlock> Read(ZipArchive zip, long maxTotalBytes = MaxTotalBytes)
    {
        var result = new List<EpubBlock>();
        var spine = Spine(zip);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        for (var s = 0; s < spine.Count && total < maxTotalBytes; s++)
        {
            if (!seen.Add(spine[s])) continue;
            var entry = zip.GetEntry(spine[s]);
            if (entry == null || entry.Length > MaxChapterBytes) continue;
            var limit = Math.Min(MaxChapterBytes, maxTotalBytes - total);
            var bytes = ReadCapped(entry, limit);
            if (bytes == null) { total += limit; continue; }   // länger als angegeben bzw. über dem Rest: überspringen
            total += bytes.Length;
            result.AddRange(Parse(s + 1, DirOf(spine[s]), DecodeUtf8(bytes)));
        }
        return result;
    }

    /// <summary>Eintrag entpacken, höchstens <paramref name="limit"/> Bytes; <c>null</c> = länger.</summary>
    internal static byte[]? ReadCapped(ZipArchiveEntry entry, long limit)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream((int)Math.Min(Math.Max(entry.Length, 0), limit));
        var buf = new byte[81920];
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            if (ms.Length + n > limit) return null;
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    /// <summary>Wie zuvor <c>new StreamReader(…, Encoding.UTF8).ReadToEnd()</c>: BOM-Erkennung (UTF-8/UTF-16/UTF-32), sonst UTF-8.</summary>
    private static string DecodeUtf8(byte[] bytes)
    {
        using var r = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return r.ReadToEnd();
    }

    internal static List<EpubBlock> Parse(int spine, string dir, string html)
    {
        var blocks = new List<EpubBlock>();
        var sb = new StringBuilder();
        var order = 0; var skip = 0; var pos = 0;
        void Flush()
        {
            var t = Regex.Replace(WebUtility.HtmlDecode(sb.ToString()), @"\s+", " ").Trim();
            if (t.Length > 0) blocks.Add(new EpubBlock(spine, order++, t, null));
            sb.Clear();
        }
        foreach (Match m in Tag.Matches(html))
        {
            if (skip == 0) sb.Append(html, pos, m.Index - pos);
            pos = m.Index + m.Length;
            if (!m.Groups[2].Success) continue;   // Kommentar
            var name = m.Groups[2].Value;
            var local = name.Contains(':') ? name[(name.IndexOf(':') + 1)..] : name;
            var closing = m.Groups[1].Value == "/";
            if (local is "head" or "script" or "style" or "title")
            {
                if (m.Groups[3].Value.TrimEnd().EndsWith('/')) continue;
                skip = Math.Max(0, skip + (closing ? -1 : 1));
                continue;
            }
            if (skip > 0) continue;
            if (!closing && local.Equals("img", StringComparison.OrdinalIgnoreCase) || !closing && local == "image")
            {
                var src = Src.Match(m.Groups[3].Value);
                if (src.Success)
                {
                    Flush();
                    var v = WebUtility.HtmlDecode(src.Groups[1].Value[1..^1]);
                    if (!v.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && !v.Contains("://"))
                        blocks.Add(new EpubBlock(spine, order++, null, Resolve(dir, Uri.UnescapeDataString(v.Split('#')[0]))));
                }
                continue;
            }
            if (Blocks.Contains(local)) Flush();
        }
        if (skip == 0 && pos < html.Length) sb.Append(html, pos, html.Length - pos);
        Flush();
        return blocks;
    }

    /// <summary>Relativen Pfad gegen ein ZIP-Verzeichnis auflösen („../Images/a.jpg“).</summary>
    public static string Resolve(string dir, string href)
    {
        var parts = new List<string>();
        foreach (var p in (href.StartsWith('/') ? href[1..] : dir + href).Split('/'))
        {
            if (p is "" or ".") continue;
            if (p == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(p);
        }
        return string.Join('/', parts);
    }

    private static string DirOf(string path) { var i = path.LastIndexOf('/'); return i < 0 ? "" : path[..(i + 1)]; }

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var e = zip.GetEntry(path);
        if (e == null) return null;
        try { using var s = e.Open(); return XDocument.Load(s); }
        catch (System.Xml.XmlException) { return null; }
    }
}
