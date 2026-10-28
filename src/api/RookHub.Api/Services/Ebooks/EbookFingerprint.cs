using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace RookHub.Api.Services.Ebooks;

/// <summary>Fingerabdruck EINER Datei: gefundene ISBN-13 und der MinHash des Texts (beide <c>null</c>, wenn nichts da ist).</summary>
public sealed record EbookFileFingerprint(IReadOnlyList<string> Isbns, uint[]? MinHash)
{
    public static readonly EbookFileFingerprint Empty = new([], null);

    /// <summary>Spaltenwert für <c>EbookFile.Isbns</c> (kommagetrennt) oder <c>null</c>.</summary>
    public string? IsbnCsv => Isbns.Count == 0 ? null : string.Join(',', Isbns);
}

/// <summary>
/// Erkennt „dasselbe Buch in einer anderen Datei“ (0.863.0, Upload-Dublettenprüfung jenseits der Byte-Gleichheit):
///
/// <para><b>ISBN</b> — aus den Metadaten (EPUB-OPF <c>dc:identifier</c>, PDF-XMP/Info) und dem Text der ersten Seiten.
/// Nur mit gültiger Prüfziffer; ISBN-10 wird nach ISBN-13 umgerechnet. Ohne das Wort „ISBN“ davor zählt nur eine 13-stellige
/// 978/979-Zahl (eine beliebige 10-stellige Zahl besteht die Prüfziffer jedes elfte Mal). Liefert der Text mehr als
/// <see cref="MaxTextIsbns"/> verschiedene, ist es eine Verlagsliste („Also available …“) — dann zählen nur die Metadaten.</para>
///
/// <para><b>Text-MinHash</b> — normalisierter Wortstrom (klein, ohne Akzente, nur Buchstaben/Ziffern, reine Zahlen wie
/// Seitenzahlen fallen weg, Trennstriche am Zeilenende werden zusammengezogen), daraus ein FENSTER von höchstens
/// <see cref="WindowWords"/> Wörtern nach den ersten <see cref="SkipWords"/> (Titelei, Inhaltsverzeichnis unterscheiden
/// sich zwischen PDF und EPUB am stärksten). Das Fenster beginnt bei beiden Fassungen fast an derselben Stelle, darum
/// überlappen die Wort-5-Gramme (Shingles) weitgehend, egal wie Zeilen und Seiten umbrechen. 64 Hashfunktionen,
/// Jaccard-Schätzung = Anteil gleicher Minima. Weniger als <see cref="MinWords"/> Wörter (Scan ohne Textebene) → kein
/// Fingerabdruck; dann bleiben nur ISBN und Titel.</para>
/// </summary>
public static class EbookFingerprint
{
    public const int HashCount = 64;
    public const int ShingleSize = 5;
    /// <summary>So viele Wörter am Anfang überspringen (Titelei).</summary>
    public const int SkipWords = 1000;
    /// <summary>So viele Wörter bilden den Fingerabdruck (~170–200 Druckseiten). Je größer, desto weniger zählt eine
    /// verschieden lange Titelei: 2 500 Wörter Versatz kosten bei 40 000 Wörtern ~0,12 Jaccard, bei 60 000 ~0,08.</summary>
    public const int WindowWords = 60_000;
    /// <summary>Weniger Wörter = kein verwertbarer Text (Scan, Bildband).</summary>
    public const int MinWords = 2_000;
    /// <summary>Mehr verschiedene ISBN im Text = Verlagsliste, nicht die des Buchs.</summary>
    public const int MaxTextIsbns = 4;
    /// <summary>PDF: aus so vielen ersten Seiten kommen die ISBN.</summary>
    public const int IsbnPages = 10;
    /// <summary>PDF: höchstens so viele Seiten lesen.</summary>
    public const int MaxPdfPages = 400;
    /// <summary>PDF: hat der Text nach so vielen Seiten keine <see cref="ScanProbeWords"/> Wörter, ist es ein Scan → aufhören.</summary>
    public const int ScanProbePages = 20;
    public const int ScanProbeWords = 200;
    /// <summary>Zeitdeckel je Datei (Upload wartet darauf).</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(45);

    // ── ISBN ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>„ISBN“-Präfix (auch „ISBN-13:“, „eISBN“, „urn:isbn:“, OPF <c>scheme="ISBN"&gt;</c>), dann 13 oder 10 Stellen.</summary>
    private static readonly Regex PrefixedIsbn = new(
        @"ISBN(?:[\s\-]?1[03])?[^0-9A-Za-z]{0,8}((?:\d[\s\-]?){12}\d|(?:\d[\s\-]?){9}[\dXx])(?![\dXx])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>13-stellige 978/979-Zahl ohne Präfix (mit Bindestrichen/Leerzeichen).</summary>
    private static readonly Regex BareIsbn13 = new(@"(?<![\d\-])(97[89](?:[\s\-]?\d){10})(?![\d\-]?\d)", RegexOptions.Compiled);

    /// <summary>ISBN-10 oder -13 (mit Bindestrichen/Leerzeichen) → ISBN-13 ohne Trenner; ungültige Prüfziffer → <c>null</c>.</summary>
    public static string? NormalizeIsbn(string raw)
    {
        var s = new StringBuilder(13);
        foreach (var c in raw)
            if (char.IsAsciiDigit(c) || c is 'X' or 'x') s.Append(char.ToUpperInvariant(c));
        var v = s.ToString();
        if (v.Length == 13 && v.All(char.IsAsciiDigit) && (v.StartsWith("978") || v.StartsWith("979")))
            return Isbn13Check(v[..12]) == v[12] - '0' ? v : null;
        if (v.Length == 10 && v[..9].All(char.IsAsciiDigit))
        {
            var sum = 0;
            for (var i = 0; i < 9; i++) sum += (10 - i) * (v[i] - '0');
            sum += v[9] == 'X' ? 10 : v[9] - '0';
            if (sum % 11 != 0) return null;
            var body = "978" + v[..9];
            return body + Isbn13Check(body);
        }
        return null;
    }

    private static int Isbn13Check(string twelve)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (twelve[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10;
    }

    /// <summary>Alle gültigen ISBN eines Texts als ISBN-13, in Fundreihenfolge, ohne Doppelte.</summary>
    public static List<string> ExtractIsbns(string text)
    {
        var found = new List<(int Pos, string Isbn)>();
        foreach (Match m in PrefixedIsbn.Matches(text))
            if (NormalizeIsbn(m.Groups[1].Value) is { } i) found.Add((m.Index, i));
        foreach (Match m in BareIsbn13.Matches(text))
            if (NormalizeIsbn(m.Groups[1].Value) is { } i) found.Add((m.Index, i));
        return found.OrderBy(f => f.Pos).Select(f => f.Isbn).Distinct().ToList();
    }

    /// <summary>ISBN aus Metadaten (immer) plus aus dem Text (nur ohne Verlagsliste), höchstens 8.</summary>
    public static List<string> CombineIsbns(string? metadataText, string? frontText)
    {
        var meta = metadataText == null ? [] : ExtractIsbns(metadataText);
        var text = frontText == null ? [] : ExtractIsbns(frontText);
        if (text.Count > MaxTextIsbns) text = [];
        return meta.Concat(text).Distinct().Take(8).ToList();
    }

    // ── Text → Wörter → MinHash ─────────────────────────────────────────────────────────────────

    private static readonly Regex Hyphenation = new(@"(\p{L})[\-­‐]\s+(\p{Ll})", RegexOptions.Compiled);

    /// <summary>Text → normalisierte Wörter (klein, ohne Akzente, nur Buchstaben/Ziffern, ohne reine Zahlen).</summary>
    public static IEnumerable<string> Words(string text)
    {
        text = Hyphenation.Replace(text.Replace("­", ""), "$1$2");
        var sb = new StringBuilder();
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) { sb.Append(char.ToLowerInvariant(ch)); continue; }
            if (sb.Length > 0)
            {
                var w = sb.ToString();
                sb.Clear();
                if (!w.All(char.IsDigit)) yield return w;
            }
        }
        if (sb.Length > 0 && !sb.ToString().All(char.IsDigit)) yield return sb.ToString();
    }

    /// <summary>Das Fenster aus dem Wortstrom: nach <see cref="SkipWords"/> (bei kurzen Texten anteilig weniger) höchstens
    /// <see cref="WindowWords"/> Wörter. Zu wenig Text → <c>null</c>.</summary>
    public static List<string>? Window(IReadOnlyList<string> words)
    {
        if (words.Count < MinWords) return null;
        var skip = Math.Min(SkipWords, words.Count / 10);
        return words.Skip(skip).Take(WindowWords).ToList();
    }

    /// <summary>MinHash (64 × uint32) über die Wort-5-Gramme; <c>null</c> bei zu wenig Text.</summary>
    public static uint[]? MinHash(IReadOnlyList<string> words)
    {
        var window = Window(words);
        if (window == null) return null;
        var mins = new uint[HashCount];
        Array.Fill(mins, uint.MaxValue);
        for (var i = 0; i + ShingleSize <= window.Count; i++)
        {
            var h = 14695981039346656037UL;   // FNV-1a 64 über die fünf Wörter
            for (var k = 0; k < ShingleSize; k++)
            {
                foreach (var c in window[i + k]) { h ^= c; h *= 1099511628211UL; }
                h ^= 0x20; h *= 1099511628211UL;
            }
            for (var j = 0; j < HashCount; j++)
            {
                var v = (uint)Mix(h ^ Seeds[j]);
                if (v < mins[j]) mins[j] = v;
            }
        }
        return mins;
    }

    private static readonly ulong[] Seeds = Enumerable.Range(0, HashCount).Select(i => Mix(0x9E3779B97F4A7C15UL * (ulong)(i + 1))).ToArray();

    /// <summary>SplitMix64-Finalisierer.</summary>
    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Geschätzte Jaccard-Ähnlichkeit zweier Fingerabdrücke (0–1).</summary>
    public static double Similarity(uint[] a, uint[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        if (n == 0) return 0;
        var same = 0;
        for (var i = 0; i < n; i++) if (a[i] == b[i]) same++;
        return (double)same / n;
    }

    public static string Encode(uint[] hash)
    {
        var bytes = new byte[hash.Length * 4];
        for (var i = 0; i < hash.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), hash[i]);
        return Convert.ToBase64String(bytes);
    }

    public static uint[]? Decode(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return null;
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length == 0 || bytes.Length % 4 != 0) return null;
            var r = new uint[bytes.Length / 4];
            for (var i = 0; i < r.Length; i++) r[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4));
            return r;
        }
        catch (FormatException) { return null; }
    }

    /// <summary>Fingerabdruck aus fertigem Text (Tests, EPUB): Metadaten-Text + Volltext in Lesereihenfolge.</summary>
    public static EbookFileFingerprint FromText(string? metadataText, string fullText, int frontChars = 30_000)
    {
        var front = fullText.Length <= frontChars ? fullText : fullText[..frontChars];
        var words = Words(fullText).Take(SkipWords + WindowWords).ToList();
        return new EbookFileFingerprint(CombineIsbns(metadataText, front), MinHash(words));
    }

    // ── Dateien ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Fingerabdruck einer Datei nach Format (pdf, epub); andere Formate → leer. Fehler beim Lesen → leer.</summary>
    public static EbookFileFingerprint Compute(string path, string format, CancellationToken ct = default)
    {
        try
        {
            return format switch
            {
                "pdf" => FromPdf(path, ct),
                "epub" => FromEpub(path, ct),
                _ => EbookFileFingerprint.Empty,
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return EbookFileFingerprint.Empty; }   // kaputte Datei: kein Fingerabdruck, Upload läuft weiter
    }

    private static EbookFileFingerprint FromPdf(string path, CancellationToken ct)
    {
        using var doc = PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        var meta = new StringBuilder();
        try
        {
            var info = doc.Information;
            meta.Append(info?.Title).Append(' ').Append(info?.Subject).Append(' ').Append(info?.Keywords).Append(' ');
        }
        catch (Exception) { /* kaputte Info: egal */ }
        try
        {
            if (doc.TryGetXmpMetadata(out var xmp)) meta.Append(xmp.GetXDocument().ToString());
        }
        catch (Exception) { /* kaputtes XMP: egal */ }

        var front = new StringBuilder();
        var words = new List<string>();
        var clock = Stopwatch.StartNew();
        var pages = Math.Min(doc.NumberOfPages, MaxPdfPages);
        for (var p = 1; p <= pages; p++)
        {
            ct.ThrowIfCancellationRequested();
            string text;
            try { text = string.Join(' ', doc.GetPage(p).GetWords().Select(w => w.Text)); }
            catch (Exception) { continue; }   // eine kaputte Seite
            if (p <= IsbnPages) front.Append(text).Append('\n');
            words.AddRange(Words(text));
            if (words.Count >= SkipWords + WindowWords) break;
            if (p == ScanProbePages && words.Count < ScanProbeWords) break;   // Scan
            if (clock.Elapsed > Budget) break;
        }
        return new EbookFileFingerprint(CombineIsbns(meta.ToString(), front.ToString()), MinHash(words));
    }

    private static EbookFileFingerprint FromEpub(string path, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(path);
        string? opf = null;
        var opfPath = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase));
        if (opfPath != null && opfPath.Length < 2_000_000)
            using (var r = new StreamReader(opfPath.Open(), Encoding.UTF8)) opf = r.ReadToEnd();
        ct.ThrowIfCancellationRequested();
        var text = new StringBuilder();
        foreach (var block in EbookEpubContent.Read(zip))
        {
            if (block.Text == null) continue;
            text.Append(block.Text).Append('\n');
            if (text.Length > 2_000_000) break;   // ~ 300 000 Wörter — weit mehr als das Fenster braucht
        }
        ct.ThrowIfCancellationRequested();
        return FromText(opf, text.ToString());
    }

    // ── Titel / Autor ───────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> Articles = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "der", "die", "das", "ein", "eine", "le", "la", "les", "el", "il", "lo",
    };

    /// <summary>Haupttitel zum Vergleich: vor „:“ bzw. „ - “, klein, ohne Akzente/Satzzeichen, ohne führenden Artikel.
    /// Zu kurz (unter 4 Zeichen) → <c>null</c> (vergleicht nicht).</summary>
    public static string? NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var main = title;
        var cut = main.IndexOfAny([':', '|']);
        if (cut > 0) main = main[..cut];
        var dash = main.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) main = main[..dash];
        var words = Words(main.Replace('_', ' ')).ToList();
        if (words.Count > 1 && Articles.Contains(words[0])) words.RemoveAt(0);
        var n = string.Join(' ', words);
        return n.Length < 4 ? null : n;
    }

    /// <summary>Nachnamen eines Autorenfelds: Personen getrennt an <c>; &amp; / und and et y</c>; je Person mit Komma
    /// („Nachname, Vorname“) das erste Wort vor dem Komma, sonst das letzte Wort.</summary>
    public static HashSet<string> Surnames(string? author)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(author)) return result;
        foreach (var part in Regex.Split(author, @"[;&/]|\band\b|\bund\b|\bet\b|\by\b", RegexOptions.IgnoreCase))
        {
            var comma = part.IndexOf(',');
            var words = Words(comma > 0 ? part[..comma] : part).Where(w => w.Length >= 3).ToList();
            if (words.Count > 0) result.Add(comma > 0 ? words[0] : words[^1]);
        }
        return result;
    }

    /// <summary>Haben die beiden Autorenfelder einen gemeinsamen Nachnamen? („Silman, Jeremy“ ~ „Jeremy Silman“,
    /// „John Nunn“ ≁ „John Emms“.)</summary>
    public static bool AuthorsOverlap(string? a, string? b)
    {
        var sa = Surnames(a);
        var sb = Surnames(b);
        if (sa.Count == 0 || sb.Count == 0) return false;
        var ta = Words(a!).ToHashSet();
        var tb = Words(b!).ToHashSet();
        return sa.Any(tb.Contains) || sb.Any(ta.Contains);
    }
}
