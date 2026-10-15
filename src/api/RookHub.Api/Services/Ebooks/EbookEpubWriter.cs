using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Schreibt ein <see cref="ReflowBook"/> als EPUB 3 (0.802.0): <c>mimetype</c> als erster, unkomprimierter Eintrag,
/// <c>META-INF/container.xml</c>, <c>OEBPS/content.opf</c> (Titel, Autor, Sprache, Kennung, <c>dcterms:modified</c>),
/// <c>OEBPS/nav.xhtml</c> (Inhaltsverzeichnis, mit Unterpunkten zu Seitenankern <c>#p&lt;Seite&gt;</c>), ein Titelblatt
/// und je Kapitel(-teil) eine XHTML-Datei; Diagramme als SVG unter <c>OEBPS/images/p&lt;Seite&gt;-&lt;n&gt;.svg</c>
/// (<see cref="EbookBoardSvg"/>). Die Bildpfade sind stabil, solange die Diagramme dieselben bleiben.
/// </summary>
public static class EbookEpubWriter
{
    private const string Css = """
        body { font-family: Georgia, "Times New Roman", serif; line-height: 1.5; margin: 0 4%; }
        h1 { font-size: 1.6em; margin: 1.2em 0 .6em; line-height: 1.25; }
        h2 { font-size: 1.3em; margin: 1.1em 0 .5em; line-height: 1.3; }
        h3 { font-size: 1.1em; margin: 1em 0 .4em; }
        p { margin: 0 0 .65em; text-align: left; }
        .fg { font-family: "DejaVu Sans", "Segoe UI Symbol", "Noto Sans Symbols 2", "Arial Unicode MS", sans-serif; }
        figure.diagram { display: block; margin: 1em auto; text-align: center; page-break-inside: avoid; break-inside: avoid; }
        figure.diagram img { width: 15em; max-width: 92%; height: auto; }
        figure.diagram figcaption { font-size: .85em; margin-top: .25em; }
        .titlepage { text-align: center; margin-top: 20%; }
        .titlepage .note { font-size: .8em; opacity: .7; margin-top: 3em; }
        """;

    /// <summary>Schreibt das EPUB nach <paramref name="output"/> und liefert je Diagramm (Key) Bildpfad + Spine-Position.</summary>
    public static Dictionary<int, (string ImageRef, int Spine)> Write(ReflowBook book, Stream output, DateTime modifiedUtc)
    {
        var images = new Dictionary<int, (string, int)>();
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        AddText(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        AddText(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
            </container>
            """);
        AddText(zip, "OEBPS/style.css", Css);

        // Dateinamen + Seitenanker vorab, damit das Inhaltsverzeichnis auf Seiten zeigen kann.
        var files = book.Chapters.Select((c, i) => $"text/c{i + 1:D3}.xhtml").ToList();
        var anchorFile = new SortedDictionary<int, string>();
        for (var i = 0; i < book.Chapters.Count; i++)
        {
            var prev = -1;
            foreach (var b in book.Chapters[i].Blocks)
            {
                if (b.Page != prev && !anchorFile.ContainsKey(b.Page)) anchorFile[b.Page] = files[i];
                prev = b.Page;
            }
        }

        var manifest = new StringBuilder();
        var spine = new StringBuilder();
        AddText(zip, "OEBPS/text/title.xhtml", Page(book.Language, book.Title,
            $"<div class=\"titlepage\"><h1>{E(book.Title)}</h1>{(book.Author.Length > 0 ? $"<p>{E(book.Author)}</p>" : "")}" +
            "<p class=\"note\">Reflowable edition generated from the PDF by RookHub. Diagrams are drawn from the recognised positions.</p></div>"));
        manifest.Append("<item id=\"title\" href=\"text/title.xhtml\" media-type=\"application/xhtml+xml\"/>\n");
        spine.Append("<itemref idref=\"title\"/>\n");

        var usedImages = new HashSet<string>();
        for (var i = 0; i < book.Chapters.Count; i++)
        {
            var chapter = book.Chapters[i];
            var spineIndex = i + 2;   // 1 = Titelblatt
            var body = new StringBuilder();
            var prevPage = -1;
            var perPage = new Dictionary<int, int>();
            foreach (var b in chapter.Blocks)
            {
                var anchor = b.Page != prevPage && anchorFile.TryGetValue(b.Page, out var f) && f == files[i] ? $" id=\"p{b.Page}\"" : "";
                prevPage = b.Page;
                switch (b)
                {
                    case ReflowParagraph p:
                        var tag = p.Level is >= 1 and <= 3 ? $"h{p.Level}" : "p";
                        body.Append('<').Append(tag).Append(anchor).Append('>').Append(Runs(p.Runs)).Append("</").Append(tag).Append(">\n");
                        break;
                    case ReflowFigure fig:
                        var svg = EbookBoardSvg.Render(fig.Diagram.Fen);
                        if (svg == null) continue;
                        var n = perPage[fig.Diagram.Page] = perPage.GetValueOrDefault(fig.Diagram.Page) + 1;
                        var name = $"images/p{fig.Diagram.Page:D4}-{n}.svg";
                        while (!usedImages.Add(name)) name = $"images/p{fig.Diagram.Page:D4}-{++n}.svg";
                        AddText(zip, "OEBPS/" + name, svg);
                        manifest.Append(CultureInfo.InvariantCulture, $"<item id=\"img{usedImages.Count}\" href=\"{name}\" media-type=\"image/svg+xml\"/>\n");
                        images[fig.Diagram.Key] = ("OEBPS/" + name, spineIndex);
                        var side = fig.Diagram.SideKnown ? (fig.Diagram.Fen.Split(' ').ElementAtOrDefault(1) == "b" ? "▲" : "△") : null;
                        var caption = string.Join(' ', new[] { fig.Diagram.Label != null ? $"<b>{E(fig.Diagram.Label)}</b>" : null, side }.Where(x => x != null));
                        var alt = fig.Diagram.Label != null ? $"Diagram {fig.Diagram.Label}" : "Diagram";
                        body.Append(CultureInfo.InvariantCulture, $"<figure class=\"diagram\"{anchor}><img src=\"../{name}\" alt=\"{E(alt)}\"/>");
                        if (caption.Length > 0) body.Append("<figcaption>").Append(caption).Append("</figcaption>");
                        body.Append("</figure>\n");
                        break;
                }
            }
            var id = $"c{i + 1:D3}";
            AddText(zip, "OEBPS/" + files[i], Page(book.Language, chapter.Title, body.ToString()));
            manifest.Append(CultureInfo.InvariantCulture, $"<item id=\"{id}\" href=\"{files[i]}\" media-type=\"application/xhtml+xml\"/>\n");
            spine.Append(CultureInfo.InvariantCulture, $"<itemref idref=\"{id}\"/>\n");
        }

        AddText(zip, "OEBPS/nav.xhtml", Nav(book, files, anchorFile));
        var modified = modifiedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        AddText(zip, "OEBPS/content.opf", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid" xml:lang="{E(book.Language)}">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">{E(book.Identifier)}</dc:identifier>
                <dc:title>{E(book.Title)}</dc:title>
                {(book.Author.Length > 0 ? $"<dc:creator>{E(book.Author)}</dc:creator>" : "")}
                <dc:language>{E(book.Language)}</dc:language>
                <meta property="dcterms:modified">{modified}</meta>
              </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                <item id="css" href="style.css" media-type="text/css"/>
            {manifest}  </manifest>
              <spine>
            {spine}  </spine>
            </package>
            """);
        return images;
    }

    private static string Nav(ReflowBook book, List<string> files, SortedDictionary<int, string> anchorFile)
    {
        var sb = new StringBuilder();
        sb.Append("<nav epub:type=\"toc\" id=\"toc\"><h1>Contents</h1>\n<ol>\n");
        for (var i = 0; i < book.Chapters.Count; i++)
        {
            var c = book.Chapters[i];
            if (c.Part > 1) continue;   // Fortsetzungsteile nicht eigens aufführen
            sb.Append(CultureInfo.InvariantCulture, $"<li><a href=\"{files[i]}\">{E(Short(c.Title))}</a>");
            var subs = c.Sections.Where(s => s.Page > 0).ToList();
            if (subs.Count > 0)
            {
                sb.Append("<ol>");
                foreach (var (title, page) in subs)
                {
                    // Anker der Seite oder der nächsten früheren Seite mit Anker.
                    var target = anchorFile.Keys.LastOrDefault(p => p <= page);
                    if (target == 0) target = anchorFile.Keys.FirstOrDefault();
                    if (!anchorFile.TryGetValue(target, out var file)) continue;
                    sb.Append(CultureInfo.InvariantCulture, $"<li><a href=\"{file}#p{target}\">{E(Short(title))}</a></li>");
                }
                sb.Append("</ol>");
            }
            sb.Append("</li>\n");
        }
        sb.Append("</ol></nav>\n");
        return Page(book.Language, "Contents", sb.ToString(), "style.css");
    }

    private static string Short(string s) => s.Length > 140 ? s[..140] + "…" : s;

    private static string Runs(IEnumerable<ReflowRun> runs)
    {
        var sb = new StringBuilder();
        foreach (var r in runs)
        {
            var text = Figurines(E(r.Text));
            if (text.Length == 0) continue;
            if (r.Bold) sb.Append("<b>");
            if (r.Italic) sb.Append("<i>");
            sb.Append(text);
            if (r.Italic) sb.Append("</i>");
            if (r.Bold) sb.Append("</b>");
        }
        return sb.ToString();
    }

    /// <summary>Figurinen in eine Spanne mit Symbol-Schriften (Lesegeräte-Schriften haben sie oft nicht).</summary>
    private static string Figurines(string html)
    {
        if (!html.Any(c => c is >= '♔' and <= '♘')) return html;
        var sb = new StringBuilder(html.Length + 32);
        foreach (var c in html)
        {
            if (c is >= '♔' and <= '♘') sb.Append("<span class=\"fg\">").Append(c).Append("</span>");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Page(string lang, string title, string body, string css = "../style.css") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE html>
        <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="{E(lang)}" lang="{E(lang)}">
        <head><meta charset="UTF-8"/><title>{E(title)}</title><link rel="stylesheet" type="text/css" href="{css}"/></head>
        <body>
        {body}</body>
        </html>
        """;

    /// <summary>XML-sicherer Text: escapt &amp; &lt; &gt; " und entfernt in XML verbotene Zeichen (Steuerzeichen, einzelne Surrogate).</summary>
    internal static string E(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length + 16);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[i + 1]); i++; }
                continue;
            }
            if (char.IsLowSurrogate(c)) continue;
            if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') continue;
            if (c is '\uFFFE' or '\uFFFF') continue;
            sb.Append(c switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", _ => c.ToString() });
        }
        return sb.ToString();
    }

    private static void AddText(ZipArchive zip, string path, string content, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(path, level);
        entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var s = entry.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        s.Write(bytes);
    }
}
