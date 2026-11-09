using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers.Ebooks;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Externe Übersetzer (Claude-Agenten): beanspruchen, Texte holen/zurückgeben, zusammenbauen, freigeben.</summary>
public class EbookExternalTranslationTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    private readonly FakeBookTranslator _llm = new();
    private readonly EbookTranslationSignal _signal = new();
    public void Dispose() => _kit.Dispose();

    /// <summary>Genug deutsche Figurenbuchstaben, damit das Schreiben sie umstellt (≥ 5, keine englischen).</summary>
    private const string MoreMoves = "<p>Danach folgt 14.Te1 Ld7 15.Dd2 und Weiß steht gut.</p>";

    private EbookTranslationService Spark() => new(
        _kit.Db, _kit.Storage, _llm, Options.Create(new EbookTranslationOptions { Languages = "en", Parallel = 1 }),
        _signal, NullLogger<EbookTranslationService>.Instance);

    private EbookExternalTranslationService External() => new(
        _kit.Db, _kit.Storage, Spark(), _signal, NullLogger<EbookExternalTranslationService>.Instance);

    private async Task<Ebook> GermanBookAsync(string? extra = MoreMoves)
    {
        var b = await _kit.BookAsync("Mein Buch", null, "epub");
        b.Language = "de";
        b.LanguageSource = EbookLanguageDetector.SourceText;
        await _kit.Db.SaveChangesAsync();
        var path = _kit.Storage.ResolveFile(b, b.Files[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, EbookTranslationServiceTests.GermanEpub(extraHtml: extra));
        return b;
    }

    /// <summary>„Übersetzung“ wie im Spark-Ersatz: jeder Buchstabe außerhalb der Platzhalter wird „x“.</summary>
    private static string Xs(string text) => Regex.Replace(text, @"(<[^>]*>)|\p{L}", m => m.Groups[1].Success ? m.Value : "x");

    private Task<ExternalTranslationInfoDto> ClaimAsync(EbookExternalTranslationService ext, int ebookId, string translator = "test-agent")
        => ext.ClaimAsync(new ExternalTranslationClaimDto { EbookId = ebookId, Language = "en", Translator = translator }, CancellationToken.None);

    private async Task<List<ExternalTranslationSegmentDto>> AllAsync(EbookExternalTranslationService ext, int id, bool onlyOpen = false)
        => (await ext.SegmentsAsync(id, 0, 500, onlyOpen, CancellationToken.None)).Items;

    private async Task TranslateAllAsync(EbookExternalTranslationService ext, int id)
    {
        var items = (await AllAsync(ext, id, onlyOpen: true)).Select(s => new ExternalTranslationTargetDto { Index = s.Index, Target = Xs(s.Source) }).ToList();
        var r = await ext.PutAsync(id, items, CancellationToken.None);
        Assert.Empty(r.Rejected);
    }

    private static string Entry(string epub, string path)
    {
        using var zip = ZipFile.OpenRead(epub);
        using var r = new StreamReader(zip.GetEntry(path)!.Open());
        return r.ReadToEnd();
    }

    [Fact]
    public async Task Claim_StoresSegments_AndTheSparkLeavesTheBookAlone()
    {
        var book = await GermanBookAsync();
        var ext = External();
        var info = await ClaimAsync(ext, book.Id);

        var row = await _kit.Db.EbookTranslations.SingleAsync();
        Assert.Equal(info.TranslationId, row.Id);
        Assert.Equal(EbookTranslationState.Running, row.State);
        Assert.Equal("external:test-agent", row.Model);
        Assert.Equal("test-agent", info.Translator);
        Assert.Equal("de", info.SourceLanguage);
        Assert.True(row.SegmentsTotal > 0);
        Assert.Equal(row.SegmentsTotal, await _kit.Db.EbookTranslationSegments.CountAsync(s => s.EbookTranslationId == row.Id));
        Assert.Equal(row.SegmentsTotal, info.SegmentsOpen);
        Assert.Contains(info.Notes, n => n.Contains("piece letters"));

        var spark = Spark();
        Assert.Null(await spark.ClaimNextAsync(CancellationToken.None));
        Assert.Equal(0, await spark.RequeueInterruptedAsync(CancellationToken.None));   // Neustart lässt den externen Anspruch stehen
        Assert.Equal(EbookTranslationState.Running, (await _kit.Db.EbookTranslations.SingleAsync()).State);
        Assert.Null(await spark.ClaimNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Claim_SameTranslatorAgain_IsIdempotent_OthersAndDoneAndSparkRunAre409()
    {
        var book = await GermanBookAsync();
        var ext = External();
        var first = await ClaimAsync(ext, book.Id);
        var again = await ClaimAsync(ext, book.Id);
        Assert.Equal(first.TranslationId, again.TranslationId);
        Assert.Equal(first.SegmentsTotal, await _kit.Db.EbookTranslationSegments.CountAsync());
        await Assert.ThrowsAsync<ConflictException>(() => ClaimAsync(ext, book.Id, "someone-else"));

        var row = await _kit.Db.EbookTranslations.SingleAsync();
        row.Model = "qwen-spark";   // die Spark läuft gerade
        await _kit.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => ClaimAsync(ext, book.Id));
        row.State = EbookTranslationState.Done;
        await _kit.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => ClaimAsync(ext, book.Id));
    }

    [Fact]
    public async Task Claim_BookInTargetLanguageOrScan_Is400()
    {
        var book = await GermanBookAsync();
        book.Language = "en";
        await _kit.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<DomainValidationException>(() => ClaimAsync(External(), book.Id));
        book.Language = null;
        book.LanguageSource = EbookLanguageDetector.SourceNoText;
        await _kit.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<DomainValidationException>(() => ClaimAsync(External(), book.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => ClaimAsync(External(), 999_999));
    }

    [Fact]
    public async Task Segments_AreExactlyTheModelTextsOfTheSpark_WithPlaceholders()
    {
        var book = await GermanBookAsync();
        var ext = External();
        var info = await ClaimAsync(ext, book.Id);

        using var zip = ZipFile.OpenRead(_kit.Storage.ResolveFile(book, book.Files[0]));
        var expected = EbookEpubTranslation.Plan(zip).UniqueTexts();
        var all = await AllAsync(ext, info.TranslationId);
        Assert.Equal(expected, all.Select(s => s.Source).ToList());
        Assert.Equal(Enumerable.Range(0, expected.Count), all.Select(s => s.Index));
        Assert.Contains(all, s => Regex.IsMatch(s.Source, @"<t\d+/>") && s.Source.Contains("deutlich"));   // Notation + <b> als Platzhalter
        Assert.DoesNotContain(all, s => s.Source.Contains("Sf3"));
        Assert.Contains(all, s => s.Document == "OEBPS/Text/ch1.xhtml");

        var page = await ext.SegmentsAsync(info.TranslationId, 2, 3, true, CancellationToken.None);
        Assert.Equal(expected.Count, page.Total);
        Assert.Equal([2, 3, 4], page.Items.Select(i => i.Index));

        var detail = await ext.GetAsync(info.TranslationId, CancellationToken.None);
        Assert.Contains("chess book from German to English", detail.SystemPrompt!.Replace("\n", " ").Replace("  ", " "));
        Assert.Contains("PUT", detail.Instructions);
    }

    [Fact]
    public async Task Put_ChecksLikeASparkAnswer_CountsAndOverwrites()
    {
        var book = await GermanBookAsync();
        var ext = External();
        var info = await ClaimAsync(ext, book.Id);
        var all = await AllAsync(ext, info.TranslationId);
        var marked = all.First(s => Regex.IsMatch(s.Source, @"<t\d+>"));
        var plain = all.First(s => !s.Source.Contains('<'));

        var r = await ext.PutAsync(info.TranslationId,
        [
            new() { Index = plain.Index, Target = "Translated text." },
            new() { Index = marked.Index, Target = Regex.Replace(marked.Source, "<[^>]*>", "") },   // Platzhalter verloren
            new() { Index = 9999, Target = "x" },
            new() { Index = all[^1].Index, Target = "  " },
            new() { Index = plain.Index, Target = "again" },
        ], CancellationToken.None);
        Assert.Equal(1, r.Accepted);
        Assert.Equal(["placeholders", "unknown_index", "empty", "duplicate"], r.Rejected.Select(x => x.Reason));
        Assert.Equal(1, r.SegmentsDone);
        Assert.Equal(1, (await _kit.Db.EbookTranslations.SingleAsync()).SegmentsDone);

        r = await ext.PutAsync(info.TranslationId, [new() { Index = plain.Index, Target = "Overwritten." }], CancellationToken.None);
        Assert.Equal(1, r.SegmentsDone);
        Assert.Equal("Overwritten.", (await _kit.Db.EbookTranslationSegments.SingleAsync(s => s.Index == plain.Index)).Target);
        Assert.Equal(all.Count - 1, (await ext.SegmentsAsync(info.TranslationId, 0, 500, true, CancellationToken.None)).Total);
    }

    [Fact]
    public void Check_NestingQuoteLengthLanguage()
    {
        Assert.Equal("open_quote", EbookExternalTranslationService.Check("Er sagt »gut«.", "He says “good.", "en", null)?.Reason);
        var longDe = string.Join(" ", Enumerable.Repeat("Der Springer steht auf dem Feld und die Dame ist nicht im Spiel.", 4));
        Assert.Equal("too_short", EbookExternalTranslationService.Check(longDe, "Short.", "en", null)?.Reason);
        Assert.Equal("wrong_language", EbookExternalTranslationService.Check(longDe, longDe, "en", null)?.Reason);
        var en = string.Join(" ", Enumerable.Repeat("The knight stands on the square and the queen is not in the game.", 4));
        Assert.Null(EbookExternalTranslationService.Check(longDe, en, "en", null));
        Assert.Equal("placeholders", EbookExternalTranslationService.Check("a <t1>b</t1>", "</t1>b<t1> a", "en", null)?.Reason);
    }

    [Fact]
    public async Task Assemble_WritesTheEpubLikeTheWorker_WithPieceLetters_AndIsDone()
    {
        var book = await GermanBookAsync();
        var ext = External();
        var info = await ClaimAsync(ext, book.Id);
        await TranslateAllAsync(ext, info.TranslationId);

        var result = await ext.AssembleAsync(info.TranslationId, new ExternalTranslationAssembleDto { Title = "My Book" }, CancellationToken.None);
        Assert.Equal("done", result.State);
        Assert.Equal(0, result.SegmentsFailed);
        Assert.Equal(info.SegmentsTotal, result.SegmentsDone);
        Assert.Equal($"converted/{book.Id}.en.epub", result.RelativePath);

        var row = await _kit.Db.EbookTranslations.SingleAsync();
        Assert.Equal(EbookTranslationState.Done, row.State);
        Assert.NotNull(row.CompletedAt);
        Assert.Equal("My Book", row.Title);
        Assert.Equal(0, await _kit.Db.EbookTranslationSegments.CountAsync());   // Texte weg, der Zwischenspeicher hält sie
        Assert.Equal(info.SegmentsTotal, Spark().OpenCache(book.Id, "en").Count);

        var (_, epub) = await _kit.Service.ResolveTranslationAsync(0, true, book.Id, "en");   // der Leser zeigt sie wie jede andere
        var ch1 = Entry(epub, "OEBPS/Text/ch1.xhtml");
        Assert.Contains("lang=\"en\"", ch1);
        Assert.Contains("<p>1.e4 e5 2.Nf3 Nc6</p>", ch1);                                    // Zugliste: nie übersetzt, umgestellt
        Assert.Contains("12.Nf3 Nc6 13.<span class=\"f\">♗</span>b5", ch1);                  // Notation im übersetzten Absatz
        Assert.Contains("<b>xxxxxxxx</b>", ch1);
        Assert.Contains("14.Re1 Bd7 15.Qd2", ch1);
        Assert.Contains("<dc:language>en</dc:language>", Entry(epub, "OEBPS/content.opf"));

        await Assert.ThrowsAsync<ConflictException>(() => ext.AssembleAsync(info.TranslationId, new(), CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() => ext.ReleaseAsync(info.TranslationId, CancellationToken.None));
    }

    [Fact]
    public async Task Assemble_WithOpenTexts_Is409_AllowMissingKeepsThemInTheOriginal()
    {
        var book = await GermanBookAsync();
        var ext = External();
        var info = await ClaimAsync(ext, book.Id);
        var all = await AllAsync(ext, info.TranslationId);
        var open = all.First(s => s.Source.Contains("ruhiger"));
        await ext.PutAsync(info.TranslationId, all.Where(s => s.Index != open.Index)
            .Select(s => new ExternalTranslationTargetDto { Index = s.Index, Target = Xs(s.Source) }).ToList(), CancellationToken.None);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => ext.AssembleAsync(info.TranslationId, new(), CancellationToken.None));
        Assert.Contains("1 texts", conflict.Message);
        Assert.Equal(EbookTranslationState.Running, (await _kit.Db.EbookTranslations.SingleAsync()).State);

        var result = await ext.AssembleAsync(info.TranslationId, new() { AllowMissing = true }, CancellationToken.None);
        Assert.Equal("done", result.State);
        Assert.Equal(1, result.SegmentsFailed);
        Assert.Equal(all.Count - 1, result.SegmentsDone);
        var (_, epub) = await _kit.Service.ResolveTranslationAsync(0, true, book.Id, "en");
        Assert.Contains("<p>Ein ruhiger Zug.</p>", Entry(epub, "OEBPS/Text/ch2.xhtml"));
    }

    [Fact]
    public async Task Release_GivesTheBookBackToTheSpark_WithTheFinishedTextsInTheCache()
    {
        var book = await GermanBookAsync();
        var ext = External();
        var info = await ClaimAsync(ext, book.Id);
        var first = (await AllAsync(ext, info.TranslationId))[0];
        await ext.PutAsync(info.TranslationId, [new() { Index = first.Index, Target = Xs(first.Source) }], CancellationToken.None);

        await ext.ReleaseAsync(info.TranslationId, CancellationToken.None);
        var row = await _kit.Db.EbookTranslations.SingleAsync();
        Assert.Equal(EbookTranslationState.Pending, row.State);
        Assert.Null(row.Model);
        Assert.Equal(0, await _kit.Db.EbookTranslationSegments.CountAsync());

        var spark = Spark();
        Assert.Equal(row.Id, await spark.ClaimNextAsync(CancellationToken.None));
        Assert.Equal(EbookTranslationOutcome.Done, await spark.RunAsync(row.Id, CancellationToken.None));
        Assert.DoesNotContain(_llm.AllTexts, t => t == first.Source);   // das extern Fertige ging nicht noch einmal an die Spark
    }

    [Fact]
    public async Task Claim_TakesOverWhatTheSparkAlreadyTranslated()
    {
        var book = await GermanBookAsync();
        using (var zip = ZipFile.OpenRead(_kit.Storage.ResolveFile(book, book.Files[0])))
        {
            var t = EbookEpubTranslation.Plan(zip).UniqueTexts()[0];
            Spark().OpenCache(book.Id, "en").Add([new(t, Xs(t))]);
        }
        _kit.Db.EbookTranslations.Add(new EbookTranslation { EbookId = book.Id, Language = "en", State = EbookTranslationState.Failed, Model = "qwen" });
        await _kit.Db.SaveChangesAsync();

        var info = await ClaimAsync(External(), book.Id);
        Assert.Equal(1, info.SegmentsDone);
        Assert.Equal(info.SegmentsTotal - 1, info.SegmentsOpen);
    }

    [Fact]
    public async Task Spark_RetryFailed_LeavesAFailedExternalClaimAlone()
    {
        var book = await GermanBookAsync();
        _kit.Db.EbookTranslations.Add(new EbookTranslation { EbookId = book.Id, Language = "en", State = EbookTranslationState.Failed, Model = "external:x" });
        await _kit.Db.SaveChangesAsync();
        Assert.Equal(0, await Spark().RetryFailedAsync(CancellationToken.None));
    }

    [Fact]
    public void Controller_IsAdminOnly_UnderTheExternalRoute()
    {
        var type = typeof(EbookExternalTranslationController);
        Assert.Equal("api/admin/ebooks/translations/external", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(PermissionPolicyProvider.Prefix + Permissions.EbooksManage, type.GetCustomAttribute<HasPermissionAttribute>()!.Policy);
        var verbs = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods.Select(v => v + " " + a.Template)))
            .Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["DELETE {id:int}", "GET ", "GET {id:int}", "GET {id:int}/segments", "POST claim", "POST {id:int}/assemble", "PUT {id:int}/segments"], verbs);
        Assert.DoesNotContain(EndpointAuthInventoryTests.Inventory(), e => e.Template.StartsWith("/api/admin/ebooks/translations/external") && e.Anonymous);
    }
}
