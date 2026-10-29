using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RookHub.Api.Services.Ebooks;

/// <summary>Ein zu übersetzendes Dokument des EPUB (Kapitel, Navigation, NCX) — zerlegt.</summary>
public sealed record EpubTextDocument(string Path, EbookTextDocument Document);

/// <summary>Was in einem EPUB zu übersetzen ist: die Dokumente, das OPF und der Fingerabdruck der Quelle.</summary>
public sealed class EpubTranslationPlan
{
    public string OpfPath { get; init; } = "";
    public List<EpubTextDocument> Documents { get; } = new();
    /// <summary>SHA-256 über OPF und alle Text-Dokumente — ändert sich die Quelle, ist die Übersetzung veraltet.</summary>
    public string SourceHash { get; init; } = "";

    /// <summary>Jeder verschiedene Modelltext einmal, in Lesereihenfolge (erstes Vorkommen).</summary>
    public List<string> UniqueTexts()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var d in Documents)
            foreach (var s in d.Document.Segments)
                if (seen.Add(s.ModelText)) list.Add(s.ModelText);
        return list;
    }

    /// <summary>Blöcke insgesamt (mit Wiederholungen).</summary>
    public int SegmentCount => Documents.Sum(d => d.Document.Segments.Count());
}

/// <summary>Das EPUB passt nicht (kein OPF, kein Kapitel) — die Übersetzung ist gescheitert.</summary>
public sealed class EpubTranslationException(string message) : Exception(message);

/// <summary>
/// EPUB lesen und übersetzt wieder schreiben (0.876.0). <see cref="Plan"/> findet über <c>container.xml</c> → OPF alle
/// (X)HTML-Dokumente des Manifests (Kapitel, auch die Navigation <c>nav.xhtml</c>) und das NCX und zerlegt sie
/// (<see cref="EbookTextSegmenter"/>); <see cref="Write"/> kopiert das Archiv Eintrag für Eintrag — <c>mimetype</c> zuerst
/// und unkomprimiert, Bilder/Schriften/CSS unverändert, dieselben Pfade — und ersetzt nur die Dokumente: übersetzte
/// Blöcke, <c>lang</c>/<c>xml:lang</c> am <c>&lt;html&gt;</c> und <c>dc:language</c> im OPF auf die Zielsprache. Andere
/// Zeichensätze als UTF-8 werden gelesen und als UTF-8 geschrieben (Deklaration angepasst).
/// </summary>
public static class EbookEpubTranslation
{
    private static readonly string[] TextTypes = ["application/xhtml+xml", "text/html", "application/x-dtbncx+xml"];

    public static EpubTranslationPlan Plan(ZipArchive zip)
    {
        var container = LoadXml(zip, "META-INF/container.xml") ?? throw new EpubTranslationException("META-INF/container.xml is missing.");
        var opfPath = container.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value
            ?? throw new EpubTranslationException("No OPF in container.xml.");
        var opfEntry = zip.GetEntry(opfPath) ?? throw new EpubTranslationException("The OPF is missing.");
        var opfBytes = ReadBytes(opfEntry);
        XDocument opf;
        try { opf = XDocument.Parse(DecodeText(opfBytes)); }
        catch (System.Xml.XmlException ex) { throw new EpubTranslationException("The OPF is not readable: " + ex.Message); }
        var dir = opfPath.Contains('/') ? opfPath[..(opfPath.LastIndexOf('/') + 1)] : "";

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(opfBytes);
        var docs = new List<EpubTextDocument>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in opf.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            var type = item.Attribute("media-type")?.Value?.Trim().ToLowerInvariant();
            var href = item.Attribute("href")?.Value;
            if (href == null || type == null || !TextTypes.Contains(type)) continue;
            var path = EbookEpubContent.Resolve(dir, Uri.UnescapeDataString(href.Split('#')[0]));
            if (!seen.Add(path)) continue;
            var entry = zip.GetEntry(path);
            if (entry == null || entry.Length > EbookEpubEntries.MaxEntryBytes) continue;
            var bytes = ReadBytes(entry);
            sha.AppendData(Encoding.UTF8.GetBytes(path));
            sha.AppendData(bytes);
            docs.Add(new EpubTextDocument(path, EbookTextSegmenter.Parse(DecodeText(bytes))));
        }
        if (docs.Count == 0) throw new EpubTranslationException("The EPUB has no text documents.");
        var plan = new EpubTranslationPlan { OpfPath = opfPath, SourceHash = Convert.ToHexStringLower(sha.GetHashAndReset()) };
        plan.Documents.AddRange(docs);
        return plan;
    }

    /// <summary>Fingerabdruck der Quelle ohne Zerlegen (Prüfung „ist die Übersetzung noch aktuell?“).</summary>
    public static string SourceHash(ZipArchive zip) => Plan(zip).SourceHash;

    /// <summary>
    /// Übersetztes EPUB schreiben. <paramref name="translate"/>: Modelltext → Übersetzung (<c>null</c> = Original lassen).
    /// Antwort: Blöcke, deren Übersetzung nicht passte und die im Original blieben. <paramref name="sourceLanguage"/>:
    /// Sprache des Buchs — setzt das Buch landestypische Figurenbuchstaben, stellt <see cref="EbookNotationLetters"/> sie
    /// im ganzen Kapitel auf die Zielsprache um (auch reine Zuglisten, die nie ans Modell gehen).
    /// </summary>
    public static int Write(ZipArchive source, EpubTranslationPlan plan, Stream output, string language, Func<string, string?> translate,
        string? sourceLanguage = null)
    {
        var rejected = 0;
        var docs = plan.Documents.ToDictionary(d => d.Path, StringComparer.Ordinal);
        var letters = EbookNotationLetters.For(sourceLanguage, language,
            plan.Documents.Select(d => d.Document.Rebuild(_ => null, out _)));
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var mimetype = source.GetEntry("mimetype");
        if (mimetype != null) Copy(mimetype, zip, CompressionLevel.NoCompression);
        foreach (var entry in source.Entries)
        {
            if (entry.FullName == "mimetype") continue;
            if (entry.FullName.EndsWith('/')) { zip.CreateEntry(entry.FullName); continue; }
            if (docs.TryGetValue(entry.FullName, out var doc))
            {
                var text = doc.Document.Rebuild(translate, out var r);
                rejected += r;
                if (letters != null) text = letters(text);
                WriteText(zip, entry, SetHtmlLanguage(Utf8Declaration(text), language));
            }
            else if (entry.FullName == plan.OpfPath)
            {
                WriteText(zip, entry, SetOpfLanguage(Utf8Declaration(DecodeText(ReadBytes(entry))), language));
            }
            else
            {
                Copy(entry, zip, IsCompressed(entry.FullName) ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            }
        }
        return rejected;
    }

    private static bool IsCompressed(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".woff" or ".woff2" or ".mp3" or ".mp4";
    }

    private static void Copy(ZipArchiveEntry entry, ZipArchive zip, CompressionLevel level)
    {
        var target = zip.CreateEntry(entry.FullName, level);
        target.LastWriteTime = entry.LastWriteTime;
        using var from = entry.Open();
        using var to = target.Open();
        from.CopyTo(to);
    }

    private static void WriteText(ZipArchive zip, ZipArchiveEntry entry, string text)
    {
        var target = zip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
        target.LastWriteTime = entry.LastWriteTime;
        using var to = target.Open();
        var bytes = new UTF8Encoding(false).GetBytes(text);
        to.Write(bytes);
    }

    private static readonly Regex HtmlTag = new(@"<html\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LangAttr = new(@"(\s(?:xml:)?lang\s*=\s*)(""[^""]*""|'[^']*')", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OpfLanguage = new(@"(<(?:dc:)?language\b[^>]*>)[^<]*(</(?:dc:)?language>)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex XmlEncoding = new(@"^(\s*<\?xml[^>]*?\bencoding\s*=\s*)(""[^""]*""|'[^']*')", RegexOptions.Compiled);
    private static readonly Regex MetaCharset = new(@"(<meta\b[^>]*?\bcharset\s*=\s*[""']?)([\w\-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary><c>lang</c>/<c>xml:lang</c> am <c>&lt;html&gt;</c> auf die Zielsprache (Silbentrennung, Vorlesen).</summary>
    internal static string SetHtmlLanguage(string html, string language)
    {
        var m = HtmlTag.Match(html);
        if (!m.Success) return html;
        var tag = LangAttr.Replace(m.Value, x => x.Groups[1].Value + "\"" + language + "\"");
        return html[..m.Index] + tag + html[(m.Index + m.Length)..];
    }

    internal static string SetOpfLanguage(string opf, string language)
        => OpfLanguage.Replace(opf, m => m.Groups[1].Value + language + m.Groups[2].Value);

    /// <summary>Wir schreiben UTF-8 — eine andere Deklaration (<c>encoding="iso-8859-1"</c>, <c>charset=…</c>) wird angepasst.</summary>
    private static string Utf8Declaration(string text)
    {
        text = XmlEncoding.Replace(text, m => m.Groups[1].Value + "\"utf-8\"", 1);
        return MetaCharset.Replace(text, m => m.Groups[1].Value + "utf-8");
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static readonly Regex DeclaredEncoding = new(@"(?:encoding|charset)\s*=\s*[""']?([\w\-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Text eines Eintrags: BOM, sonst die Deklaration in den ersten Bytes, sonst UTF-8.</summary>
    internal static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
        var m = DeclaredEncoding.Match(head);
        if (m.Success && !m.Groups[1].Value.Equals("utf-8", StringComparison.OrdinalIgnoreCase)
                      && !m.Groups[1].Value.Equals("utf8", StringComparison.OrdinalIgnoreCase))
        {
            try { return Encoding.GetEncoding(m.Groups[1].Value).GetString(bytes); }
            catch (ArgumentException) { /* unbekannt → UTF-8 */ }
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private static XDocument? LoadXml(ZipArchive zip, string path)
    {
        var e = zip.GetEntry(path);
        if (e == null) return null;
        try { return XDocument.Parse(DecodeText(ReadBytes(e))); }
        catch (System.Xml.XmlException) { return null; }
    }
}

/// <summary>
/// Zwischenspeicher einer Buch-Übersetzung (0.876.0): je übersetztem Text eine JSON-Zeile <c>{"k":…,"t":…}</c>
/// (Schlüssel = SHA-256 des Modelltexts, gekürzt). Angehängt wird nach jeder Fuhre — bricht der Lauf ab (Sperrzeit,
/// Neustart), liest der nächste die Datei und schickt nur, was fehlt. Eine halb geschriebene letzte Zeile wird übergangen.
/// </summary>
public sealed class EbookTranslationCache
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _entries = new(StringComparer.Ordinal);

    private EbookTranslationCache(string path) => _path = path;

    public int Count => _entries.Count;

    public static string Key(string modelText) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(modelText)))[..32];

    public static EbookTranslationCache Open(string path)
    {
        var cache = new EbookTranslationCache(path);
        if (!File.Exists(path)) return cache;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                var k = doc.RootElement.GetProperty("k").GetString();
                var t = doc.RootElement.GetProperty("t").GetString();
                if (k != null && t != null) cache._entries[k] = t;
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException) { /* halbe Zeile */ }
        }
        return cache;
    }

    public bool Contains(string modelText) => _entries.ContainsKey(Key(modelText));

    public string? Get(string modelText) => _entries.TryGetValue(Key(modelText), out var t) ? t : null;

    /// <summary>Übersetzungen merken und sofort an die Datei hängen.</summary>
    public void Add(IReadOnlyCollection<KeyValuePair<string, string>> translations)
    {
        if (translations.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var (text, translated) in translations)
        {
            var k = Key(text);
            _entries[k] = translated;
            sb.Append(System.Text.Json.JsonSerializer.Serialize(new { k, t = translated })).Append('\n');
        }
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
