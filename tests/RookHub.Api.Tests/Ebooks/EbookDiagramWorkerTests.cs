using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookDiagramWorkerTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private EbookDiagramWorker Worker()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_kit.Db);
        services.AddSingleton(_kit.Storage);
        services.AddSingleton(new EbookDiagramExtractor(NullLogger<EbookDiagramExtractor>.Instance));
        var sp = services.BuildServiceProvider();
        return new EbookDiagramWorker(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<EbookDiagramWorker>.Instance);
    }

    [Fact]
    public async Task CuratedDiagrams_SurviveARescan()
    {
        var admin = await _kit.UserAsync("admin", admin: true);
        var book = new Ebook { Title = "Pongó", OwnerUserId = admin.Id, Source = EbookSource.Library, DiagramState = EbookDiagramState.Done,
            DiagramCount = 2, DiagramsCurated = true, SolutionCheckState = EbookDiagramState.Done };
        book.Files.Add(new EbookFile { Format = "pdf", RelativePath = "fehlt.pdf", FileName = "fehlt.pdf" });
        _kit.Db.Ebooks.Add(book);
        await _kit.Db.SaveChangesAsync();
        foreach (var label in new[] { "1", "2" })
            _kit.Db.EbookDiagrams.Add(new EbookDiagram { EbookId = book.Id, Page = 15, Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Label = label, FontFamily = "opus" });
        await _kit.Db.SaveChangesAsync();

        // Ein allgemeiner Rescan lässt das Buch aus …
        Assert.Equal(0, await _kit.Service.RescanDiagramsAsync());
        // … und wer es trotzdem einreiht (Recheck-Migration, DJVU-Umwandlung), bekommt die Diagramme unverändert zurück.
        book.DiagramState = EbookDiagramState.Pending;
        await _kit.Db.SaveChangesAsync();
        Assert.True(await Worker().RunStepAsync(CancellationToken.None));

        var reloaded = await _kit.Db.Ebooks.SingleAsync();
        Assert.Equal(EbookDiagramState.Done, reloaded.DiagramState);
        Assert.Equal(2, reloaded.DiagramCount);
        Assert.Equal(EbookDiagramState.Done, reloaded.SolutionCheckState);
        Assert.Equal(["1", "2"], await _kit.Db.EbookDiagrams.OrderBy(d => d.Label).Select(d => d.Label!).ToListAsync());
    }

    [Theory]
    [InlineData("scan", 44, 905, true)]     // Swindler: gescanntes PDF, sauberes EPUB → EPUB
    [InlineData("cnn", 10, 10, true)]
    [InlineData("scan", 0, 300, true)]      // PDF ohne erkannte Bretter
    [InlineData("scan", 50, 20, false)]     // EPUB mit weniger Bildern bleibt Verknüpfung
    [InlineData("chess-merida", 44, 905, false)] // Schach-Font liest exakt → PDF bleibt die Quelle
    [InlineData("scan", 0, 0, false)]
    public void PreferEpub_OnlyOverScannedPdf(string family, int pdfCount, int epubCount, bool expected)
    {
        var pdf = Enumerable.Range(0, pdfCount).Select(_ => new EbookDiagram { Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", FontFamily = family }).ToList();
        Assert.Equal(expected, EbookDiagramWorker.PreferEpub(pdf, epubCount));
    }
}
