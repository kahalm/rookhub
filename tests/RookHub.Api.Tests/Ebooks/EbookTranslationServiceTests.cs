using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Tests.Games;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>
/// Ein Übersetzer für die Buch-Tests — NIE ein echtes Modell. Ersetzt jeden Buchstaben außerhalb der Platzhalter durch „x“
/// (gleiche Länge, keine Funktionswörter: Längen- und Sprachprüfung des Kerns schlagen nicht an) und merkt sich, was gefragt war.
/// </summary>
public sealed class FakeBookTranslator : IClaudeJsonClient
{
    private readonly Lock _gate = new();
    public List<List<string>> Calls { get; } = new();
    public bool IsConfigured { get; set; } = true;
    public bool IsLocal { get; set; } = true;
    public bool IsUnreachable { get; set; }
    public string TranslationModel => "test-spark";
    /// <summary>Beim n-ten Aufruf (1-basiert) diesen Abbruch auslösen — die Antwort kommt trotzdem.</summary>
    public (int Call, CancellationTokenSource Cts)? CancelAt { get; set; }
    /// <summary>Texte, die dieses Wort enthalten, kommen OHNE Platzhalter zurück (Modell vergisst sie).</summary>
    public string? DropPlaceholdersFor { get; set; }

    public IEnumerable<string> AllTexts { get { lock (_gate) return Calls.SelectMany(c => c).ToList(); } }

    public Task<string?> GenerateHintsJsonAsync(string system, string prompt, CancellationToken ct = default) => Task.FromResult<string?>(null);

    public Task<string?> TranslateCommentsJsonAsync(string system, string prompt, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(prompt);
        var items = doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => (Ply: i.GetProperty("ply").GetInt32(), Text: i.GetProperty("text").GetString() ?? "")).ToList();
        int n;
        lock (_gate) { Calls.Add(items.Select(i => i.Text).ToList()); n = Calls.Count; }
        if (CancelAt is { } c && c.Call == n) c.Cts.Cancel();
        var answer = items.Select(i => new { ply = i.Ply, text = Translate(i.Text) }).ToList();
        return Task.FromResult<string?>(JsonSerializer.Serialize(new { items = answer }));
    }

    private string Translate(string text)
    {
        if (DropPlaceholdersFor != null && text.Contains(DropPlaceholdersFor)) return Regex.Replace(text, "<[^>]*>", "").Replace(DropPlaceholdersFor, "yyy");
        return Regex.Replace(text, @"(<[^>]*>)|\p{L}", m => m.Groups[1].Success ? m.Value : "x");
    }
}

public class EbookTranslationServiceTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    private readonly FakeBookTranslator _llm = new();
    private readonly EbookTranslationSignal _signal = new();
    public void Dispose() => _kit.Dispose();

    private EbookTranslationService Service(int parallel = 1, int chunkItems = 40, QuietHours? quiet = null) => new(
        _kit.Db, _kit.Storage, _llm,
        Options.Create(new EbookTranslationOptions { Languages = "en", Parallel = parallel, ChunkItems = chunkItems }),
        _signal, NullLogger<EbookTranslationService>.Instance, reflow: null, quiet: quiet);

    private async Task<Ebook> BookAsync(string title, string? language, string format = "epub", string source = EbookLanguageDetector.SourceText)
    {
        var b = await _kit.BookAsync(title, null, format);
        b.Language = language;
        b.LanguageSource = source;
        await _kit.Db.SaveChangesAsync();
        return b;
    }

    /// <summary>Kapitel 1 (deutsch, mit Notation, Markup, Bild), Kapitel 2 mit einer Wiederholung, Navigation, NCX, Bild, CSS.</summary>
    internal static byte[] GermanEpub(int paragraphs = 3)
    {
        var ch1 = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"de\" xml:lang=\"de\">" +
            "<head><title>Kapitel 1</title><link rel=\"stylesheet\" href=\"../Styles/s.css\"/></head><body>\n" +
            "<h1 id=\"k1\">Die offene Linie</h1>\n<p class=\"t\">Weiß am Zug gewinnt.</p>\n" +
            "<p>Nach 12.Sf3 Sc6 13.<span class=\"f\">♗</span>b5 steht Weiß <b>deutlich</b> besser ±.</p>\n" +
            "<div class=\"d\"><img src=\"../Images/d1.png\" alt=\"\"/></div>\n");
        for (var i = 0; i < paragraphs; i++) ch1.Append($"<p>Absatz Nummer {i} erklärt den Plan mit dem Springer.</p>\n");
        ch1.Append("<p>1.e4 e5 2.Sf3 Sc6</p>\n</body></html>");
        const string ch2 = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>2</title></head><body>" +
            "<p class=\"t\">Weiß am Zug gewinnt.</p><p>Karpow – Kasparow, Moskau 1985</p><p>Ein ruhiger Zug.</p></body></html>";
        const string nav = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><head><title>Inhalt</title></head>" +
            "<body><nav epub:type=\"toc\"><ol><li><a href=\"Text/ch1.xhtml#k1\">Die offene Linie</a></li></ol></nav></body></html>";
        const string ncx = "<?xml version=\"1.0\"?><ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\"><head></head><docTitle><text>Mein Buch</text></docTitle>" +
            "<navMap><navPoint id=\"n1\"><navLabel><text>Die offene Linie</text></navLabel><content src=\"Text/ch1.xhtml\"/></navPoint></navMap></ncx>";
        const string opf = "<?xml version=\"1.0\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
            "<dc:title>Mein Buch</dc:title><dc:language>de</dc:language></metadata><manifest>" +
            "<item id=\"c1\" href=\"Text/ch1.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"c2\" href=\"Text/ch2.xhtml\" media-type=\"application/xhtml+xml\"/>" +
            "<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/><item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>" +
            "<item id=\"i1\" href=\"Images/d1.png\" media-type=\"image/png\"/><item id=\"s\" href=\"Styles/s.css\" media-type=\"text/css\"/></manifest>" +
            "<spine toc=\"ncx\"><itemref idref=\"c1\"/><itemref idref=\"c2\"/></spine></package>";
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string path, byte[] bytes) { using var s = zip.CreateEntry(path).Open(); s.Write(bytes); }
            Add("mimetype", Encoding.ASCII.GetBytes("application/epub+zip"));
            Add("META-INF/container.xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
                "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>"));
            Add("OEBPS/content.opf", Encoding.UTF8.GetBytes(opf));
            Add("OEBPS/Text/ch1.xhtml", Encoding.UTF8.GetBytes(ch1.ToString()));
            Add("OEBPS/Text/ch2.xhtml", Encoding.UTF8.GetBytes(ch2));
            Add("OEBPS/nav.xhtml", Encoding.UTF8.GetBytes(nav));
            Add("OEBPS/toc.ncx", Encoding.UTF8.GetBytes(ncx));
            Add("OEBPS/Images/d1.png", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4]);
            Add("OEBPS/Styles/s.css", Encoding.UTF8.GetBytes("p { margin: 0 }"));
        }
        return ms.ToArray();
    }

    private void WriteFile(Ebook b, byte[] bytes)
    {
        var path = _kit.Storage.ResolveFile(b, b.Files[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static string Entry(string epub, string path)
    {
        using var zip = ZipFile.OpenRead(epub);
        using var r = new StreamReader(zip.GetEntry(path)!.Open());
        return r.ReadToEnd();
    }

    // ── Reihenfolge ──────────────────────────────────────────────────────────────────────────────

    private async Task<List<int>> ClaimAllAsync(EbookTranslationService svc)
    {
        var order = new List<int>();
        while (await svc.ClaimNextAsync(CancellationToken.None) is { } id)
        {
            var row = await _kit.Db.EbookTranslations.SingleAsync(t => t.Id == id);
            order.Add(row.EbookId);
            row.State = EbookTranslationState.Done;   // so tun, als wäre er fertig
            await _kit.Db.SaveChangesAsync();
        }
        return order;
    }

    [Fact]
    public async Task Claim_OtherLanguagesFirst_ThenGerman_EnglishNever()
    {
        var de1 = await BookAsync("De1", "de");
        var es = await BookAsync("Es", "es");
        var en = await BookAsync("En", "en");
        var de2 = await BookAsync("De2", "de");
        var ru = await BookAsync("Ru", "ru");
        await BookAsync("Unklar", null, source: EbookLanguageDetector.SourceUnclear);
        await BookAsync("Scan", null, "pdf", EbookLanguageDetector.SourceNoText);

        var order = await ClaimAllAsync(Service());

        Assert.Equal([es.Id, ru.Id, de1.Id, de2.Id], order);
        Assert.DoesNotContain(en.Id, order);
    }

    [Fact]
    public async Task Claim_GermanWaitsWhileBooksAreUndetected()
    {
        var de = await BookAsync("De", "de");
        var unknown = await BookAsync("Neu", null, source: null!);
        unknown.LanguageSource = null;
        await _kit.Db.SaveChangesAsync();

        Assert.Null(await Service().ClaimNextAsync(CancellationToken.None));

        unknown.Language = "fr";
        unknown.LanguageSource = EbookLanguageDetector.SourceText;
        await _kit.Db.SaveChangesAsync();
        Assert.Equal([unknown.Id, de.Id], await ClaimAllAsync(Service()));
    }

    [Fact]
    public async Task Claim_PrioritisedBookFirst_EvenGerman()
    {
        await BookAsync("Es", "es");
        var de = await BookAsync("De", "de");
        await Service().EnqueueAsync(de.Id, "en", CancellationToken.None);

        var first = await Service().ClaimNextAsync(CancellationToken.None);
        Assert.Equal(de.Id, (await _kit.Db.EbookTranslations.SingleAsync(t => t.Id == first)).EbookId);
    }

    [Fact]
    public async Task Claim_WaitingBookIsSkippedUntilNotBefore()
    {
        var es = await BookAsync("Es", "es");
        var fr = await BookAsync("Fr", "fr");
        _kit.Db.EbookTranslations.Add(new EbookTranslation { EbookId = es.Id, Language = "en", NotBefore = DateTime.UtcNow.AddHours(1) });
        await _kit.Db.SaveChangesAsync();

        var id = await Service().ClaimNextAsync(CancellationToken.None);
        Assert.Equal(fr.Id, (await _kit.Db.EbookTranslations.SingleAsync(t => t.Id == id)).EbookId);
    }

    [Fact]
    public async Task Enqueue_BookAlreadyInTargetLanguage_Is400()
    {
        var en = await BookAsync("En", "en");
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.DomainValidationException>(() => Service().EnqueueAsync(en.Id, "en", CancellationToken.None));
    }

    // ── Ein Lauf ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_TranslatesProseOnly_KeepsStructureNotationAndImages()
    {
        var book = await BookAsync("Buch", "de");
        WriteFile(book, GermanEpub());
        var svc = Service();
        var id = (await svc.ClaimNextAsync(CancellationToken.None))!.Value;

        Assert.Equal(EbookTranslationOutcome.Done, await svc.RunAsync(id, CancellationToken.None));

        var row = await _kit.Db.EbookTranslations.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal(EbookTranslationState.Done, row.State);
        Assert.Equal(0, row.SegmentsFailed);
        Assert.Equal($"converted/{book.Id}.en.epub", row.RelativePath);
        var output = _kit.Storage.ResolvePath(EbookSource.Upload, row.RelativePath!);

        // Gleiche Einträge in gleicher Schreibweise, Bild und CSS Byte für Byte.
        using (var src = new ZipArchive(new MemoryStream(GermanEpub())))
        using (var dst = ZipFile.OpenRead(output))
        {
            Assert.Equal(src.Entries.Select(e => e.FullName), dst.Entries.Select(e => e.FullName));
            Assert.Equal("mimetype", dst.Entries[0].FullName);
            using var img = new MemoryStream();
            dst.GetEntry("OEBPS/Images/d1.png")!.Open().CopyTo(img);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 }, img.ToArray());
        }
        var ch1 = Entry(output, "OEBPS/Text/ch1.xhtml");
        Assert.Contains("<h1 id=\"k1\">xxx xxxxxx xxxxx</h1>", ch1);
        Assert.Contains("<p>xxxx 12.Sf3 Sc6 13.<span class=\"f\">♗</span>b5 xxxxx xxxx <b>xxxxxxxx</b> xxxxxx ±.</p>", ch1);
        Assert.Contains("<img src=\"../Images/d1.png\" alt=\"\"/>", ch1);
        Assert.Contains("<p>1.e4 e5 2.Sf3 Sc6</p>", ch1);
        Assert.Contains("lang=\"en\" xml:lang=\"en\"", ch1);
        Assert.Contains("<title>Kapitel 1</title>", ch1);
        var ch2 = Entry(output, "OEBPS/Text/ch2.xhtml");
        Assert.Contains("<p>Karpow – Kasparow, Moskau 1985</p>", ch2);
        Assert.Contains("<a href=\"Text/ch1.xhtml#k1\">xxx xxxxxx xxxxx</a>", Entry(output, "OEBPS/nav.xhtml"));
        Assert.Contains("<navLabel><text>xxx xxxxxx xxxxx</text></navLabel>", Entry(output, "OEBPS/toc.ncx"));
        Assert.Contains("<dc:language>en</dc:language>", Entry(output, "OEBPS/content.opf"));

        // Jeder verschiedene Text ging genau EINMAL ans Modell („Weiß am Zug gewinnt.“ steht zweimal, die Überschrift dreimal).
        var asked = _llm.AllTexts.ToList();
        Assert.Equal(asked.Distinct().Count(), asked.Count);
        Assert.Single(asked, t => t == "Weiß am Zug gewinnt.");
        Assert.DoesNotContain(asked, t => t.Contains("Sf3"));
        Assert.DoesNotContain(asked, t => t.Contains("Kasparow"));
        // Der Auftrag kennt die Buch-Regeln.
        Assert.Equal(row.SegmentsTotal, asked.Count);
    }

    [Fact]
    public async Task Run_InterruptedRun_ResumesWhereItStopped()
    {
        var book = await BookAsync("Buch", "de");
        WriteFile(book, GermanEpub(paragraphs: 6));
        var svc = Service(parallel: 1, chunkItems: 2);
        var id = (await svc.ClaimNextAsync(CancellationToken.None))!.Value;

        using var cts = new CancellationTokenSource();
        _llm.CancelAt = (2, cts);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.RunAsync(id, cts.Token));
        var firstRun = _llm.AllTexts.ToList();
        Assert.True(firstRun.Count >= 2);
        var cache = _kit.Storage.ResolvePath(EbookSource.Upload, EbookStorage.TranslationCachePath(book.Id, "en"));
        Assert.True(File.Exists(cache));
        var row = await _kit.Db.EbookTranslations.SingleAsync(t => t.Id == id);
        Assert.True(row.SegmentsDone > 0 && row.SegmentsDone < row.SegmentsTotal);
        Assert.False(File.Exists(_kit.Storage.ResolvePath(EbookSource.Upload, EbookStorage.TranslatedEpubPath(book.Id, "en"))));

        // Nächster Abend: nur der Rest geht ans Modell.
        _llm.CancelAt = null;
        _llm.Calls.Clear();
        Assert.Equal(EbookTranslationOutcome.Done, await svc.RunAsync(id, CancellationToken.None));
        var secondRun = _llm.AllTexts.ToList();
        Assert.NotEmpty(secondRun);
        Assert.Empty(secondRun.Intersect(firstRun));
        row = await _kit.Db.EbookTranslations.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal(row.SegmentsTotal, row.SegmentsDone);
        Assert.Equal(2, row.Runs);
    }

    [Fact]
    public async Task Run_PlaceholderLost_RetriedAlone_ThenKeptInTheOriginal()
    {
        var book = await BookAsync("Buch", "de");
        WriteFile(book, GermanEpub());
        _llm.DropPlaceholdersFor = "deutlich";
        var svc = Service();
        var id = (await svc.ClaimNextAsync(CancellationToken.None))!.Value;

        Assert.Equal(EbookTranslationOutcome.Done, await svc.RunAsync(id, CancellationToken.None));

        var row = await _kit.Db.EbookTranslations.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal(1, row.SegmentsFailed);
        // Einmal in der Fuhre, einmal allein.
        Assert.Equal(2, _llm.AllTexts.Count(t => t.Contains("deutlich")));
        var ch1 = Entry(_kit.Storage.ResolvePath(EbookSource.Upload, row.RelativePath!), "OEBPS/Text/ch1.xhtml");
        Assert.Contains("steht Weiß <b>deutlich</b> besser ±.", ch1);
    }

    [Fact]
    public async Task Run_SparkUnreachable_StopsWithoutFailing()
    {
        var book = await BookAsync("Buch", "de");
        WriteFile(book, GermanEpub());
        _llm.IsUnreachable = true;
        _llm.DropPlaceholdersFor = null;
        var failing = new FailingTranslator();
        var svc = new EbookTranslationService(_kit.Db, _kit.Storage, failing,
            Options.Create(new EbookTranslationOptions { Languages = "en" }), _signal, NullLogger<EbookTranslationService>.Instance);
        var id = (await svc.ClaimNextAsync(CancellationToken.None))!.Value;

        Assert.Equal(EbookTranslationOutcome.Unreachable, await svc.RunAsync(id, CancellationToken.None));
        Assert.NotEqual(EbookTranslationState.Failed, (await _kit.Db.EbookTranslations.AsNoTracking().SingleAsync(t => t.Id == id)).State);
    }

    private sealed class FailingTranslator : IClaudeJsonClient
    {
        public bool IsConfigured => true;
        public bool IsLocal => true;
        public bool IsUnreachable => true;
        public string TranslationModel => "down";
        public Task<string?> GenerateHintsJsonAsync(string s, string p, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> TranslateCommentsJsonAsync(string s, string p, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task Run_PdfOnlyBookWithoutReflow_IsNoText()
    {
        var book = await BookAsync("Scan", "es", "pdf");
        var svc = Service();
        var id = (await svc.ClaimNextAsync(CancellationToken.None))!.Value;
        Assert.Equal(EbookTranslationOutcome.NoText, await svc.RunAsync(id, CancellationToken.None));
        Assert.Equal(EbookTranslationState.NoText, (await _kit.Db.EbookTranslations.AsNoTracking().SingleAsync(t => t.Id == id)).State);
        Assert.Null(await svc.ClaimNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Verify_ChangedSource_MakesTheTranslationPendingAgain()
    {
        var book = await BookAsync("Buch", "de");
        WriteFile(book, GermanEpub());
        var svc = Service();
        var id = (await svc.ClaimNextAsync(CancellationToken.None))!.Value;
        await svc.RunAsync(id, CancellationToken.None);

        Assert.Equal(0, await svc.VerifyAsync(5, CancellationToken.None));
        WriteFile(book, GermanEpub(paragraphs: 4));
        Assert.Equal(1, await svc.VerifyAsync(5, CancellationToken.None));
        Assert.Equal(EbookTranslationState.Pending, (await _kit.Db.EbookTranslations.AsNoTracking().SingleAsync(t => t.Id == id)).State);

        // Der neue Lauf schickt nur den neuen Absatz.
        _llm.Calls.Clear();
        id = (await svc.ClaimNextAsync(CancellationToken.None))!.Value;
        Assert.Equal(EbookTranslationOutcome.Done, await svc.RunAsync(id, CancellationToken.None));
        Assert.Equal(["Absatz Nummer 3 erklärt den Plan mit dem Springer."], _llm.AllTexts);
    }

    [Fact]
    public async Task Delete_RemovesTranslationRowsAndFiles()
    {
        var owner = await _kit.UserAsync("owner", admin: true);
        var book = await BookAsync("Buch", "de");
        book.OwnerUserId = owner.Id;
        await _kit.Db.SaveChangesAsync();
        WriteFile(book, GermanEpub());
        var svc = Service();
        var id = (await svc.ClaimNextAsync(CancellationToken.None))!.Value;
        await svc.RunAsync(id, CancellationToken.None);
        var epub = _kit.Storage.ResolvePath(EbookSource.Upload, EbookStorage.TranslatedEpubPath(book.Id, "en"));
        var cache = _kit.Storage.ResolvePath(EbookSource.Upload, EbookStorage.TranslationCachePath(book.Id, "en"));
        Assert.True(File.Exists(epub) && File.Exists(cache));

        var dto = await _kit.Service.GetAsync(owner.Id, true, book.Id);
        Assert.Equal(["en"], dto.Translations);
        Assert.Equal("de", dto.Language);

        await _kit.Service.DeleteAsync(owner.Id, true, book.Id);
        Assert.Empty(_kit.Db.EbookTranslations);
        Assert.False(File.Exists(epub));
        Assert.False(File.Exists(cache));
    }

    [Fact]
    public async Task ResolveTranslation_OnlyDoneAndVisible()
    {
        var owner = await _kit.UserAsync("owner");
        var other = await _kit.UserAsync("other");
        var book = await BookAsync("Buch", "de");
        book.OwnerUserId = owner.Id;
        await _kit.Db.SaveChangesAsync();
        WriteFile(book, GermanEpub());
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.NotFoundException>(() => _kit.Service.ResolveTranslationAsync(owner.Id, false, book.Id, "en"));
        var svc = Service();
        await svc.RunAsync((await svc.ClaimNextAsync(CancellationToken.None))!.Value, CancellationToken.None);

        var (_, path) = await _kit.Service.ResolveTranslationAsync(owner.Id, false, book.Id, "en");
        Assert.True(File.Exists(path));
        Assert.NotNull(await EbookEpubEntries.ReadAsync(path, "OEBPS/Images/d1.png"));
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.NotFoundException>(() => _kit.Service.ResolveTranslationAsync(other.Id, false, book.Id, "en"));
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.NotFoundException>(() => _kit.Service.ResolveTranslationAsync(owner.Id, false, book.Id, "fr"));
    }

    [Fact]
    public async Task DetectLanguages_FromTheEpubText()
    {
        var book = await BookAsync("Buch", null, source: null!);
        book.LanguageSource = null;
        await _kit.Db.SaveChangesAsync();
        WriteFile(book, EpubWith(Enumerable.Repeat("<p>Las blancas tienen una posición mejor, pero el alfil no es tan fuerte como parece después de esta jugada.</p>", 40)));
        var scan = await BookAsync("Scan", null, "djvu", null!);
        scan.LanguageSource = null;
        await _kit.Db.SaveChangesAsync();

        Assert.Equal(2, await Service().DetectLanguagesAsync(10, CancellationToken.None));

        var b = await _kit.Db.Ebooks.AsNoTracking().SingleAsync(x => x.Id == book.Id);
        Assert.Equal(("es", EbookLanguageDetector.SourceText), (b.Language, b.LanguageSource));
        var s = await _kit.Db.Ebooks.AsNoTracking().SingleAsync(x => x.Id == scan.Id);
        Assert.Equal((null, EbookLanguageDetector.SourceNoText), (s.Language, s.LanguageSource));
    }

    private static byte[] EpubWith(IEnumerable<string> paragraphs)
    {
        var bytes = GermanEpub(0);
        using var ms = new MemoryStream();
        ms.Write(bytes);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, true))
        {
            zip.GetEntry("OEBPS/Text/ch1.xhtml")!.Delete();
            using var s = zip.CreateEntry("OEBPS/Text/ch1.xhtml").Open();
            s.Write(Encoding.UTF8.GetBytes("<html><body>" + string.Join("", paragraphs) + "</body></html>"));
        }
        return ms.ToArray();
    }

    // ── Hintergrunddienst: Sperrzeiten, nur Spark ───────────────────────────────────────────────

    private EbookTranslationWorker Worker(QuietHours quiet)
    {
        var services = new ServiceCollection();
        services.AddSingleton<AppDbContext>(_kit.Db);
        services.AddScoped(_ => Service(quiet: quiet));
        var sp = services.BuildServiceProvider();
        return new EbookTranslationWorker(sp.GetRequiredService<IServiceScopeFactory>(), _signal, NullLogger<EbookTranslationWorker>.Instance, quiet);
    }

    /// <summary>Do 01.10.2026 10:00 Wien (UTC+2) — Sperrzeit; 20:00 frei.</summary>
    private static readonly DateTimeOffset ThursdayMorning = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ThursdayEvening = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Worker_QuietHours_NoTranslation_ButLanguageDetectionRuns()
    {
        var book = await BookAsync("Buch", "de");
        WriteFile(book, GermanEpub());
        var undetected = await BookAsync("Neu", null, source: null!);
        undetected.LanguageSource = null;
        await _kit.Db.SaveChangesAsync();
        var clock = new QuietHoursTests.ManualTime { Now = ThursdayMorning };
        var worker = Worker(new QuietHours(QuietHours.DefaultSpec, "Europe/Vienna", clock));

        await worker.StepAsync(CancellationToken.None);
        Assert.Empty(_llm.Calls);
        Assert.Empty(_kit.Db.EbookTranslations);
        Assert.NotNull((await _kit.Db.Ebooks.AsNoTracking().SingleAsync(b => b.Id == undetected.Id)).LanguageSource);

        clock.Now = ThursdayEvening;
        Assert.Equal(TimeSpan.Zero, await worker.StepAsync(CancellationToken.None));
        Assert.NotEmpty(_llm.Calls);
        Assert.Equal(EbookTranslationState.Done, (await _kit.Db.EbookTranslations.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task Worker_OnlyOverOwnHardware()
    {
        var book = await BookAsync("Buch", "de");
        WriteFile(book, GermanEpub());
        _llm.IsLocal = false;   // Claude-Text-Schlüssel statt Spark
        var worker = Worker(new QuietHours(QuietHours.DefaultSpec, "Europe/Vienna", new QuietHoursTests.ManualTime { Now = ThursdayEvening }));

        Assert.Equal(EbookTranslationWorker.NotConfiguredPoll, await worker.StepAsync(CancellationToken.None));
        Assert.Empty(_llm.Calls);
        Assert.Empty(_kit.Db.EbookTranslations);
    }

    [Fact]
    public async Task Status_CountsLanguagesAndQueue()
    {
        await BookAsync("Es", "es");
        await BookAsync("De", "de");
        await BookAsync("En", "en");
        var status = await Service().StatusAsync(CancellationToken.None);
        Assert.True(status.Enabled);
        Assert.Equal(["en"], status.Languages);
        var en = Assert.Single(status.Targets);
        Assert.Equal(2, en.Remaining);
        Assert.Equal(1, en.RemainingFirstRound);
        Assert.Equal(["Es", "De"], status.Next.Select(n => n.Title));
        Assert.All(status.Next, n => Assert.Equal("queued", n.State));
    }
}
