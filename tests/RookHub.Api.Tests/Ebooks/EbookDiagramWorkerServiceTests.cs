using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Externe Diagramm-Worker (0.870.0): Leihe, Ablauf, Prüfung des Ergebnisses, gemeinsamer Speicherweg, Vorrang.</summary>
public class EbookDiagramWorkerServiceTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private const string Laptop = "pat:991001";
    private const string OtherLaptop = "pat:991002";
    private const string KingsFen = "8/8/8/8/8/8/8/K6k w - - 0 1";
    private const string OtherFen = "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1";

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_kit.Db);
        services.AddSingleton(_kit.Storage);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(_kit.Options));
        services.AddSingleton(new EbookDiagramExtractor(NullLogger<EbookDiagramExtractor>.Instance));
        services.AddSingleton(new EbookEpubDiagramExtractor(NullLogger<EbookEpubDiagramExtractor>.Instance));
        return services.BuildServiceProvider();
    }

    private EbookDiagramWorkerService Service() =>
        new(_kit.Db, _kit.Storage, Services(), NullLogger<EbookDiagramWorkerService>.Instance);

    private EbookDiagramWorker Worker() =>
        new(Services().GetRequiredService<IServiceScopeFactory>(), NullLogger<EbookDiagramWorker>.Instance);

    private async Task<Ebook> PendingAsync(string title, string format = "pdf", EbookSource source = EbookSource.Library)
    {
        var b = await _kit.BookAsync(title, null, format);
        b.Source = source;
        b.DiagramState = EbookDiagramState.Pending;
        await _kit.Db.SaveChangesAsync();
        return b;
    }

    private static DiagramWorkerResultDto Result(int ebookId, params DiagramWorkerDiagramDto[] diagrams) => new()
    {
        EbookId = ebookId, FontFound = true, Pages = 12, CliVersion = "0.870.0", Diagrams = diagrams.ToList(),
    };

    private static DiagramWorkerDiagramDto D(int page, double y, string fen = KingsFen, string family = "cnn") => new()
    {
        Page = page, X = 0.1, Y = y, W = 0.4, H = 0.3, Fen = fen, FontFamily = family, SideKnown = true,
    };

    private async Task<Ebook> ReloadAsync(int id)
    {
        _kit.Db.ChangeTracker.Clear();
        return await _kit.Db.Ebooks.Include(b => b.Files).SingleAsync(b => b.Id == id);
    }

    // ── Übernehmen ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_TakesQueueOrder_LeasesBook_ListsFilesWithRoles_NextClaimTakesNextBook()
    {
        var library = await PendingAsync("Bibliothek");
        var upload = await PendingAsync("Upload", source: EbookSource.Upload);
        upload.Files.Add(new EbookFile { Format = "pdf", Derived = true, RelativePath = "converted/l.pdf", FileName = "l.pdf", SizeBytes = 5 });
        upload.Files.Add(new EbookFile { Format = "epub", RelativePath = "files/u.epub", FileName = "u.epub", SizeBytes = 7 });
        await _kit.Db.SaveChangesAsync();

        var job = await Service().ClaimAsync(Laptop, "laptop", CancellationToken.None);

        Assert.NotNull(job);
        Assert.Equal(upload.Id, job.EbookId);   // Uploads vor Bibliotheksbüchern, wie beim eigenen Worker
        Assert.Equal(["primary:pdf", "reading:pdf", "epub:epub"], job.Files.Select(f => $"{f.Role}:{f.Format}").ToArray());
        Assert.All(job.Files, f => Assert.Equal($"api/diagram-worker/jobs/{job.JobId}/files/{f.FileId}", f.Url));
        var leased = await ReloadAsync(upload.Id);
        Assert.Equal((job.JobId, Laptop, EbookDiagramState.Pending), (leased.DiagramLeaseId!.Value, leased.DiagramLeaseOwner!, leased.DiagramState));
        Assert.True(leased.DiagramLeaseUntil > DateTime.UtcNow.AddMinutes(25));

        var second = await Service().ClaimAsync(OtherLaptop, null, CancellationToken.None);
        Assert.Equal(library.Id, second!.EbookId);
        Assert.Null(await Service().ClaimAsync(OtherLaptop, null, CancellationToken.None));
    }

    [Fact]
    public async Task Claim_SkipsWhatOnlyTheServerCanDo_AndActiveLeases_ExpiredLeaseIsFreeAgain()
    {
        var djvu = await PendingAsync("Nur DJVU", "djvu");
        var curated = await PendingAsync("Kuratiert");
        curated.DiagramsCurated = true;
        var failed = await PendingAsync("Schon gescheitert");
        failed.DiagramExternalFailed = true;
        var serverBusy = await PendingAsync("Server rechnet");
        serverBusy.DiagramLeaseId = Guid.NewGuid(); serverBusy.DiagramLeaseOwner = EbookDiagramLease.ServerOwner;
        serverBusy.DiagramLeaseUntil = DateTime.UtcNow.AddHours(1);
        await _kit.Db.SaveChangesAsync();

        Assert.Null(await Service().ClaimAsync(Laptop, null, CancellationToken.None));

        serverBusy.DiagramLeaseUntil = DateTime.UtcNow.AddSeconds(-1);   // Leihe abgelaufen → wieder frei
        await _kit.Db.SaveChangesAsync();
        Assert.Equal(serverBusy.Id, (await Service().ClaimAsync(Laptop, null, CancellationToken.None))!.EbookId);
        Assert.NotNull(djvu);
    }

    // ── Ergebnis ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_StoresThroughTheWorkerPath_ReplacesDiagrams_ClearsLeaseAndPriority()
    {
        var book = await PendingAsync("Buch");
        book.DiagramPriority = true;
        _kit.Db.EbookDiagrams.Add(new EbookDiagram { Id = 991101, EbookId = book.Id, Page = 1, Fen = KingsFen, FontFamily = "scan" });
        await _kit.Db.SaveChangesAsync();
        var job = (await Service().ClaimAsync(Laptop, null, CancellationToken.None))!;

        var accepted = await Service().SubmitAsync(job.JobId, Laptop,
            Result(book.Id, D(3, 0.1), D(4, 0.2, OtherFen), D(5, 0.3, "PKK5/8/8/8/8/8/8/8 w - - 0 1", EbookDiagramVisibility.Unread)),
            CancellationToken.None);

        Assert.Equal(("Done", 2), (accepted.State, accepted.DiagramCount));   // das ungelesene zählt nicht
        var b = await ReloadAsync(book.Id);
        Assert.Equal(EbookDiagramState.Done, b.DiagramState);
        Assert.Equal(EbookDiagramState.Pending, b.SolutionCheckState);
        Assert.Equal(12, b.PageCount);
        Assert.Null(b.DiagramLeaseId); Assert.Null(b.DiagramLeaseOwner); Assert.Null(b.DiagramLeaseUntil);
        Assert.False(b.DiagramPriority);
        Assert.Equal([3, 4, 5], await _kit.Db.EbookDiagrams.Where(d => d.EbookId == book.Id).OrderBy(d => d.Page).Select(d => d.Page).ToListAsync());
    }

    [Fact]
    public async Task Submit_Supplement_MergesLikeTheWorker_KeepsCuratedDiagrams()
    {
        var book = await PendingAsync("Kuratiert, ergänzen");
        book.DiagramsCurated = true;
        _kit.Db.EbookDiagrams.Add(new EbookDiagram { Id = 991201, EbookId = book.Id, Page = 3, X = 0.1, Y = 0.1, W = 0.4, H = 0.3, Fen = KingsFen, FontFamily = "opus" });
        await _kit.Db.SaveChangesAsync();
        await _kit.Service.QueueDiagramSupplementAsync(book.Id);
        var job = (await Service().ClaimAsync(Laptop, null, CancellationToken.None))!;
        Assert.True(job.Supplement);

        await Service().SubmitAsync(job.JobId, Laptop, Result(book.Id, D(3, 0.11, OtherFen), D(3, 0.6, OtherFen)), CancellationToken.None);

        var all = await _kit.Db.EbookDiagrams.Where(d => d.EbookId == book.Id).OrderBy(d => d.Y).ToListAsync();
        Assert.Equal([("opus", KingsFen), ("cnn", OtherFen)], all.Select(d => (d.FontFamily!, d.Fen)).ToArray());   // gleiche Stelle bleibt
        var b = await ReloadAsync(book.Id);
        Assert.False(b.DiagramSupplement);
        Assert.True(b.DiagramsCurated);
        Assert.Equal((EbookDiagramState.Done, 2), (b.DiagramState, b.DiagramCount));
        Assert.False(b.DiagramPriority);
    }

    [Fact]
    public async Task Submit_WrongOwner_ExpiredLease_OtherBook_ImplausibleFen_AreRejected_BookStaysPending()
    {
        var book = await PendingAsync("Buch");
        var svc = Service();
        var job = (await svc.ClaimAsync(Laptop, null, CancellationToken.None))!;

        await Assert.ThrowsAsync<ConflictException>(() => svc.SubmitAsync(job.JobId, OtherLaptop, Result(book.Id, D(1, 0.1)), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => svc.SubmitAsync(job.JobId, Laptop, Result(book.Id + 1, D(1, 0.1)), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            svc.SubmitAsync(job.JobId, Laptop, Result(book.Id, D(1, 0.1, "KK6/8/8/8/8/8/8/7k w - - 0 1")), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            svc.SubmitAsync(job.JobId, Laptop, Result(book.Id, D(1, 0.1, family: "opus")), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.SubmitAsync(Guid.NewGuid(), Laptop, Result(book.Id, D(1, 0.1)), CancellationToken.None));

        var b = await ReloadAsync(book.Id);
        b.DiagramLeaseUntil = DateTime.UtcNow.AddSeconds(-5);
        await _kit.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => Service().SubmitAsync(job.JobId, Laptop, Result(book.Id, D(1, 0.1)), CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() => Service().HeartbeatAsync(job.JobId, Laptop, CancellationToken.None));

        var after = await ReloadAsync(book.Id);
        Assert.Equal(EbookDiagramState.Pending, after.DiagramState);
        Assert.Empty(await _kit.Db.EbookDiagrams.ToListAsync());
    }

    [Fact]
    public async Task Heartbeat_ExtendsLease_Release_WithError_LeavesBookToTheServer()
    {
        var book = await PendingAsync("Buch");
        var job = (await Service().ClaimAsync(Laptop, null, CancellationToken.None))!;
        var b = await ReloadAsync(book.Id);
        b.DiagramLeaseUntil = DateTime.UtcNow.AddMinutes(1);
        await _kit.Db.SaveChangesAsync();

        var until = await Service().HeartbeatAsync(job.JobId, Laptop, CancellationToken.None);
        Assert.True(until > DateTime.UtcNow.AddMinutes(25));

        await Service().ReleaseAsync(job.JobId, Laptop, "rookhub-diagrams exit 1", CancellationToken.None);
        var released = await ReloadAsync(book.Id);
        Assert.Null(released.DiagramLeaseId);
        Assert.True(released.DiagramExternalFailed);
        Assert.Equal(EbookDiagramState.Pending, released.DiagramState);
        Assert.Null(await Service().ClaimAsync(OtherLaptop, null, CancellationToken.None));   // externe lassen es liegen …

        Assert.True(await Worker().RunStepAsync(CancellationToken.None));                    // … der Server macht es (Datei fehlt → Failed)
        var done = await ReloadAsync(book.Id);
        Assert.Equal(EbookDiagramState.Failed, done.DiagramState);
        Assert.False(done.DiagramExternalFailed);
        Assert.Null(done.DiagramLeaseOwner);
    }

    // ── Eigener Worker + Leihe ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ServerWorker_SkipsBookUnderExternalLease_TakesItAfterExpiry_ReleasesStaleServerLeases()
    {
        var book = await PendingAsync("Buch");
        var job = (await Service().ClaimAsync(Laptop, null, CancellationToken.None))!;

        Assert.True(await Worker().RunStepAsync(CancellationToken.None));
        var still = await ReloadAsync(book.Id);
        Assert.Equal((EbookDiagramState.Pending, (Guid?)job.JobId), (still.DiagramState, still.DiagramLeaseId));

        still.DiagramLeaseUntil = DateTime.UtcNow.AddSeconds(-1);
        await _kit.Db.SaveChangesAsync();
        Assert.True(await Worker().RunStepAsync(CancellationToken.None));
        var taken = await ReloadAsync(book.Id);
        Assert.Equal(EbookDiagramState.Failed, taken.DiagramState);   // Datei fehlt — Hauptsache, der Server hat es genommen
        Assert.Null(taken.DiagramLeaseId);
        await Assert.ThrowsAsync<NotFoundException>(() => Service().SubmitAsync(job.JobId, Laptop, Result(book.Id, D(1, 0.1)), CancellationToken.None));

        // Ein Neustart mitten im Buch hinterlässt eine Leihe „server“ — der neue Worker gibt sie zurück und rechnet es.
        var stuck = await PendingAsync("Nach Neustart");
        stuck.DiagramLeaseId = Guid.NewGuid(); stuck.DiagramLeaseOwner = EbookDiagramLease.ServerOwner; stuck.DiagramLeaseUntil = DateTime.UtcNow.AddHours(10);
        await _kit.Db.SaveChangesAsync();
        Assert.True(await Worker().RunStepAsync(CancellationToken.None));
        Assert.Equal(EbookDiagramState.Failed, (await ReloadAsync(stuck.Id)).DiagramState);
    }

    // ── Vorrang ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Priority_AdminSupplement_JumpsTheQueue_ClearedAfterTheRun()
    {
        var upload = await PendingAsync("Upload", source: EbookSource.Upload);
        var rescan = await _kit.BookAsync("Neu ergänzen", null);
        rescan.Source = EbookSource.Library; rescan.DiagramState = EbookDiagramState.Done; rescan.DiagramCount = 5;
        await _kit.Db.SaveChangesAsync();

        await _kit.Service.QueueDiagramSupplementAsync(rescan.Id);

        var order = await EbookDiagramWorker.QueueOrder(_kit.Db.Ebooks.Where(b => b.DiagramState == EbookDiagramState.Pending))
            .Select(b => b.Title).ToListAsync();
        Assert.Equal(["Neu ergänzen", "Upload"], order);
        var job = (await Service().ClaimAsync(Laptop, null, CancellationToken.None))!;
        Assert.Equal(rescan.Id, job.EbookId);

        await Service().SubmitAsync(job.JobId, Laptop, Result(rescan.Id, D(2, 0.5)), CancellationToken.None);
        Assert.False((await ReloadAsync(rescan.Id)).DiagramPriority);
        Assert.NotNull(upload);
    }

    // ── Prüfung ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("8/8/8/8/8/8/8/K6k w - - 0 1", true, null)]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 34", true, null)]
    [InlineData("8/8/8/3N4/8/8/8/8 w - - 0 1", true, null)]                 // Lehrbuch-Illustration ohne König
    [InlineData("8/8/8/8/8/8/8/K6k w - e3 0 1", true, null)]
    [InlineData("8/8/8/8/8/8/8/K6k", true, "FEN needs 6 fields")]
    [InlineData("8/8/8/8/8/8/K6k w - - 0 1", true, "FEN needs 8 ranks")]
    [InlineData("9/8/8/8/8/8/8/K6k w - - 0 1", true, "FEN has invalid character '9'")]
    [InlineData("7/8/8/8/8/8/8/K6k w - - 0 1", true, "FEN rank does not have 8 files")]
    [InlineData("8/8/8/8/8/8/8/K6k x - - 0 1", true, "FEN side must be w or b")]
    [InlineData("8/8/8/8/8/8/8/K6k w KK - 0 1", true, "FEN castling invalid")]
    [InlineData("8/8/8/8/8/8/8/K6k w - e4 0 1", true, "FEN en passant invalid")]
    [InlineData("8/8/8/8/8/8/8/K6k w - - 0 0", true, "FEN move counters invalid")]
    [InlineData("KK6/8/8/8/8/8/8/7k w - - 0 1", true, "implausible FEN: more than one king of a colour")]
    [InlineData("KK6/8/8/8/8/8/8/7k w - - 0 1", false, null)]               // „unread“: nur die Syntax zählt
    [InlineData("PPPPPPPP/PPPPPPPP/PPPPPPPP/8/8/8/8/7k w - - 0 1", true, "implausible FEN: more than 16 pieces of a colour")]
    public void FenError_SyntaxAlways_PlausibilityForReadable(string fen, bool strict, string? expected)
        => Assert.Equal(expected, EbookDiagramWorkerService.FenError(fen, strict));

    [Fact]
    public void Validate_PositionLengthAndCountLimits()
    {
        Assert.Null(EbookDiagramWorkerService.Validate(Result(1, D(1, 0.5))));
        Assert.Equal("diagram 0: position out of range", EbookDiagramWorkerService.Validate(Result(1, D(1, 1.5))));
        // EPUB-Bild: Y ist die Reihenfolge im Kapitel, darf über 1 liegen.
        Assert.Null(EbookDiagramWorkerService.Validate(Result(1, new DiagramWorkerDiagramDto { Page = 4, Y = 70, Fen = KingsFen, FontFamily = "image-cnn", ImageRef = "images/1.jpeg" })));
        var longCaption = D(1, 0.5); longCaption.Caption = new string('x', 201);
        Assert.Equal("diagram 0: text too long", EbookDiagramWorkerService.Validate(Result(1, longCaption)));
        var engine = D(1, 0.5); engine.SolutionSource = "engine";
        Assert.Equal("diagram 0: solution source must be 'book'", EbookDiagramWorkerService.Validate(Result(1, engine)));
        var many = Result(1); many.Diagrams = Enumerable.Range(0, EbookDiagramWorkerService.MaxDiagrams + 1).Select(_ => D(1, 0.5)).ToList();
        Assert.StartsWith("too many diagrams", EbookDiagramWorkerService.Validate(many));
    }

    /// <summary>Das Programm (tools/DiagramCli) schreibt die Erkennung über <see cref="EbookDiagramWorkerService.ToDto(int, EbookDiagramExtractor.Result, string?)"/>;
    /// zurück in eine Zeile wird daraus wieder dasselbe Diagramm.</summary>
    [Fact]
    public void ToDto_ToEntity_RoundTripsEveryRecognizedField()
    {
        var d = new EbookDiagram
        {
            EbookId = 99, Page = 7, X = 0.1, Y = 0.2, W = 0.3, H = 0.4, Fen = KingsFen, Label = "12", Caption = "Tal – Botwinnik", SideKnown = true,
            ImageRef = "OEBPS/i.png", Confidence = 0.93f, EpubChapter = 3, FontFamily = "chess-merida", SolutionUci = "a1a2",
            SolutionSan = "Ka2", SolutionText = "1 Ka2", SolutionPage = 8, SolutionSource = "book",
        };
        var dto = EbookDiagramWorkerService.ToDto(99, new EbookDiagramExtractor.Result([d], true, 100, 1), "0.870.0");
        var back = EbookDiagramWorkerService.ToEntity(dto.Diagrams.Single(), 99);

        Assert.Equal((99, true, 100, 1, "0.870.0"), (dto.EbookId, dto.FontFound, dto.Pages, dto.Solutions, dto.CliVersion));
        Assert.Equivalent(d, back, strict: true);
    }
}
