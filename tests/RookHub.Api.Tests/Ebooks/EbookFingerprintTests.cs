using System.IO.Compression;
using System.Text;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>ISBN-Erkennung, Text-MinHash und Titel-/Autorenvergleich der Upload-Dublettenprüfung (0.863.0).</summary>
public class EbookFingerprintTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out = output;

    // ── Testtexte ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Ein „Buch“ aus Zufallswörtern (fester Seed): gleiche Seeds = gleicher Text.</summary>
    internal static List<string> SyntheticWords(int seed, int count)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, count).Select(_ => Vocab[rng.Next(Vocab.Length)]).ToList();
    }

    /// <summary>Wortschatz für alle Bücher gleich (gleiche Sprache).</summary>
    private static readonly string[] Vocab = BuildVocab();

    private static string[] BuildVocab()
    {
        string[] syl = ["ka", "lo", "mi", "ter", "ban", "sko", "ri", "ven", "dra", "pul", "ost", "en", "gar", "fi", "zu", "mel"];
        var r = new Random(13);
        return Enumerable.Range(0, 4000).Select(_ => string.Concat(Enumerable.Range(0, 1 + r.Next(3)).Select(_ => syl[r.Next(syl.Length)]))).ToArray();
    }

    /// <summary>Wie ein PDF-Export: Zeilen à 11 Wörter, ab und zu ein Wort am Zeilenende getrennt („ka-“ / „lomi“),
    /// alle 350 Wörter Seitenzahl + Kolumnentitel, davor eine eigene Titelei.</summary>
    internal static string AsPdfLayout(IReadOnlyList<string> words, int frontMatterWords, int seed = 1)
    {
        var sb = new StringBuilder();
        foreach (var w in SyntheticWords(seed + 5000, frontMatterWords)) sb.Append(w).Append(' ');
        var page = 1;
        for (var i = 0; i < words.Count; i++)
        {
            if (i % 350 == 0) sb.Append('\n').Append(page++).Append(" Chapter Three: the Middlegame\n");
            var w = words[i];
            if (i % 11 == 10 && w.Length > 4) sb.Append(w[..2]).Append("-\n").Append(w[2..]).Append(' ');
            else sb.Append(w).Append(i % 11 == 10 ? '\n' : ' ');
        }
        return sb.ToString();
    }

    /// <summary>Wie ein EPUB: Absätze à 60 Wörter, Großschreibung am Satzanfang, Satzzeichen, kaum Titelei.</summary>
    internal static string AsEpubLayout(IReadOnlyList<string> words, int frontMatterWords = 120)
    {
        var sb = new StringBuilder();
        foreach (var w in SyntheticWords(9001, frontMatterWords)) sb.Append(w).Append(' ');
        for (var i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (i % 12 == 0) w = char.ToUpperInvariant(w[0]) + w[1..];
            sb.Append(w).Append(i % 12 == 11 ? ". " : i % 5 == 4 ? ", " : " ");
            if (i % 60 == 59) sb.Append("\n\n");
        }
        return sb.ToString();
    }

    internal static byte[] Epub(string text, string? opfIdentifier = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string path, string content) { using var s = zip.CreateEntry(path).Open(); s.Write(Encoding.UTF8.GetBytes(content)); }
            Add("mimetype", "application/epub+zip");
            Add("META-INF/container.xml",
                "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
                "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");
            var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
            var chapters = paragraphs.Chunk(40).ToList();
            var manifest = string.Concat(chapters.Select((_, i) => $"<item id=\"c{i}\" href=\"Text/c{i}.xhtml\" media-type=\"application/xhtml+xml\"/>"));
            var spine = string.Concat(chapters.Select((_, i) => $"<itemref idref=\"c{i}\"/>"));
            var id = opfIdentifier == null ? "" : $"<dc:identifier opf:scheme=\"ISBN\">{opfIdentifier}</dc:identifier>";
            Add("OEBPS/content.opf",
                "<?xml version=\"1.0\"?><package xmlns=\"http://www.idpf.org/2007/opf\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:opf=\"http://www.idpf.org/2007/opf\">" +
                $"<metadata>{id}<dc:title>Test</dc:title></metadata><manifest>{manifest}</manifest><spine>{spine}</spine></package>");
            for (var i = 0; i < chapters.Count; i++)
                Add($"OEBPS/Text/c{i}.xhtml", "<html><body>" + string.Concat(chapters[i].Select(p => $"<p>{p}</p>")) + "</body></html>");
        }
        return ms.ToArray();
    }

    // ── ISBN ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0-306-40615-2", "9780306406157")]          // ISBN-10 → 13
    [InlineData("978-0-306-40615-7", "9780306406157")]
    [InlineData("978 1 906552 07 7", "9781906552077")]
    [InlineData("0-8044-2957-X", "9780804429573")]          // Prüfziffer X
    [InlineData("0-8044-2957-x", "9780804429573")]
    public void NormalizeIsbn_ValidForms_GiveIsbn13(string raw, string expected)
        => Assert.Equal(expected, EbookFingerprint.NormalizeIsbn(raw));

    [Theory]
    [InlineData("0-306-40615-3")]      // falsche Prüfziffer
    [InlineData("978-0-306-40615-8")]
    [InlineData("123-0-306-40615-7")]  // kein 978/979
    [InlineData("12345")]
    public void NormalizeIsbn_Invalid_IsNull(string raw) => Assert.Null(EbookFingerprint.NormalizeIsbn(raw));

    [Fact]
    public void ExtractIsbns_FindsPrefixedAndBare13_IgnoresBareTenDigits()
    {
        var text = "Copyright © 2010 Quality Chess. ISBN-13: 978-1-906552-07-7\nISBN 0-306-40615-2 (hardback)\n" +
                   "Tel. 0306406152 · Printed by 9780804429573 Ltd · Order no. 9780306406158";
        Assert.Equal(["9781906552077", "9780306406157", "9780804429573"], EbookFingerprint.ExtractIsbns(text));
    }

    [Fact]
    public void ExtractIsbns_ReadsOpfIdentifierAndUrnForm()
    {
        Assert.Equal(["9780306406157"], EbookFingerprint.ExtractIsbns("<dc:identifier opf:scheme=\"ISBN\">0306406152</dc:identifier>"));
        Assert.Equal(["9781906552077"], EbookFingerprint.ExtractIsbns("<dc:identifier>urn:isbn:9781906552077</dc:identifier>"));
    }

    [Fact]
    public void CombineIsbns_PublisherCatalogueInText_IsIgnored_MetadataStays()
    {
        var catalogue = string.Join("\n", new[] { "0-306-40615-2", "0-8044-2957-X", "978-1-906552-07-7", "978-0-14-044913-6", "978-0-19-953556-9" }
            .Select(i => "ISBN " + i));
        var isbns = EbookFingerprint.CombineIsbns("urn:isbn:9780306406157", catalogue);
        Assert.Equal(["9780306406157"], isbns);
        Assert.Equal(2, EbookFingerprint.CombineIsbns(null, "ISBN 0-306-40615-2 and ISBN 0-8044-2957-X").Count);
    }

    // ── Text ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Words_Normalizes_CaseAccentsPunctuationHyphenationAndNumbers()
    {
        Assert.Equal(["the", "position", "after", "e4", "is", "equal", "reti", "s", "idea"],
            EbookFingerprint.Words("The posi-\ntion after 12. e4 is equal (Réti's idea), p. 117").Where(w => w != "p"));
    }

    [Fact]
    public void MinHash_SameBook_DifferentLayout_IsCertain()
    {
        var book = SyntheticWords(1, 80_000);
        var pdf = EbookFingerprint.FromText(null, AsPdfLayout(book, frontMatterWords: 2500)).MinHash!;
        var epub = EbookFingerprint.FromText(null, AsEpubLayout(book)).MinHash!;
        var sim = EbookFingerprint.Similarity(pdf, epub);
        Assert.True(sim >= EbookDuplicateFinder.CertainSimilarity, $"Ähnlichkeit {sim:0.00}");
        _out.WriteLine($"PDF gegen EPUB: {sim:0.00}");

        var pdf2 = EbookFingerprint.FromText(null, AsPdfLayout(book, frontMatterWords: 600, seed: 2)).MinHash!;
        var sim2 = EbookFingerprint.Similarity(pdf, pdf2);
        _out.WriteLine($"PDF gegen PDF: {sim2:0.00}");
        Assert.True(sim2 >= 0.9, "zwei PDF-Exporte");
    }

    [Fact]
    public void MinHash_RevisedEdition_IsProbable()
    {
        // Zweite Auflage: jeder vierte Absatz (à 60 Wörter) neu geschrieben.
        var book = SyntheticWords(1, 80_000);
        var fresh = SyntheticWords(77, book.Count);
        var revised = book.Select((w, i) => i / 60 % 4 == 0 ? fresh[i] : w).ToList();
        var a = EbookFingerprint.FromText(null, AsEpubLayout(book)).MinHash!;
        var b = EbookFingerprint.FromText(null, AsEpubLayout(revised)).MinHash!;
        var sim = EbookFingerprint.Similarity(a, b);
        Assert.InRange(sim, EbookDuplicateFinder.ProbableSimilarity, EbookDuplicateFinder.CertainSimilarity - 0.0001);
        _out.WriteLine($"Neuauflage: {sim:0.00}");
    }

    [Fact]
    public void MinHash_DifferentBooks_AreFarApart()
    {
        var a = EbookFingerprint.FromText(null, AsEpubLayout(SyntheticWords(1, 80_000))).MinHash!;
        var b = EbookFingerprint.FromText(null, AsEpubLayout(SyntheticWords(2, 80_000))).MinHash!;
        var sim = EbookFingerprint.Similarity(a, b);
        _out.WriteLine($"fremd: {sim:0.00}");
        Assert.True(sim < 0.1);
    }

    [Fact]
    public void MinHash_TooLittleText_IsNull()
    {
        Assert.Null(EbookFingerprint.FromText(null, string.Join(' ', SyntheticWords(1, 500))).MinHash);
        Assert.NotNull(EbookFingerprint.FromText(null, string.Join(' ', SyntheticWords(1, 3000))).MinHash);
    }

    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        var h = EbookFingerprint.FromText(null, string.Join(' ', SyntheticWords(3, 5000))).MinHash!;
        var s = EbookFingerprint.Encode(h);
        Assert.True(s.Length <= 400);
        Assert.Equal(h, EbookFingerprint.Decode(s));
        Assert.Null(EbookFingerprint.Decode("kein base64!"));
    }

    [Fact]
    public void Compute_Epub_ReadsOpfIsbnAndText()
    {
        var path = Path.Combine(Path.GetTempPath(), "rh-fp-" + Guid.NewGuid().ToString("N") + ".epub");
        try
        {
            File.WriteAllBytes(path, Epub(AsEpubLayout(SyntheticWords(1, 8000)), "0-306-40615-2"));
            var fp = EbookFingerprint.Compute(path, "epub");
            Assert.Equal(["9780306406157"], fp.Isbns);
            Assert.NotNull(fp.MinHash);
            Assert.Equal(EbookFileFingerprint.Empty, EbookFingerprint.Compute(path, "djvu"));
            File.WriteAllBytes(path, [1, 2, 3]);
            Assert.Null(EbookFingerprint.Compute(path, "epub").MinHash);   // kaputt → leer statt Ausnahme
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Compute_ScannedPdfWithoutText_HasNoFingerprint()
    {
        var path = Path.Combine(Path.GetTempPath(), "rh-fp-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            File.WriteAllBytes(path, EbookTestKit.TinyPdf());
            var fp = EbookFingerprint.Compute(path, "pdf");
            Assert.Null(fp.MinHash);
            Assert.Empty(fp.Isbns);
        }
        finally { File.Delete(path); }
    }

    // ── Titel / Autor ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("The Woodpecker Method", "woodpecker method")]
    [InlineData("Woodpecker Method: 1128 puzzles to sharpen your tactics", "woodpecker method")]
    [InlineData("The_Woodpecker_Method - Quality Chess 2018", "woodpecker method")]
    [InlineData("Die Schachstrategie", "schachstrategie")]
    [InlineData("Réti's Ideas!", "reti s ideas")]
    [InlineData("Go", null)]
    public void NormalizeTitle_MainTitleWithoutArticle(string title, string? expected)
        => Assert.Equal(expected, EbookFingerprint.NormalizeTitle(title));

    [Theory]
    [InlineData("Jeremy Silman", "Silman, Jeremy", true)]
    [InlineData("Axel Smith; Hans Tikkanen", "Tikkanen, Hans", true)]
    [InlineData("John Nunn", "John Emms", false)]
    [InlineData("Kotov, Yudovich", "Alexander Kotov", true)]
    [InlineData("", "John Nunn", false)]
    public void AuthorsOverlap_ComparesSurnames(string a, string b, bool expected)
        => Assert.Equal(expected, EbookFingerprint.AuthorsOverlap(a, b));
}
