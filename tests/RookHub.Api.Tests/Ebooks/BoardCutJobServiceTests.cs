using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Brett-Ausschneide-Aufträge über RookHub (0.874.0): anlegen, leihen, Datei, Ergebnis ablegen, Fehler.</summary>
public class BoardCutJobServiceTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    private readonly BoardCutJobService _service;

    public BoardCutJobServiceTests() => _service = new BoardCutJobService(_kit.Db, _kit.Storage, NullLogger<BoardCutJobService>.Instance);

    public void Dispose() => _kit.Dispose();

    private static JsonElement Payload(string json = """{"diagrams":2,"pages":{"3":[[11,0.1,0.1,0.3,0.3,"8/8/8/8/8/8/8/K6k w - - 0 1",0.9]]}}""")
        => JsonDocument.Parse(json).RootElement.Clone();

    private async Task<Ebook> BookWithPdfAsync()
    {
        var book = await _kit.BookAsync("Brett", null);
        var path = _kit.Storage.ResolveFile(book, book.Files.Single());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, EbookTestKit.TinyPdf());
        return book;
    }

    private static MemoryStream Zip(params (string Name, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
                using (var w = new StreamWriter(zip.CreateEntry(name).Open()))
                    w.Write(content);
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream Text(string s) => new(Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task Claim_File_Submit_WritesTheSameFilesAsTheWebDavWorker()
    {
        var book = await BookWithPdfAsync();
        var created = await _service.CreateAsync(new BoardCutJobCreateDto { EbookId = book.Id, Payload = Payload() }, CancellationToken.None);

        var job = await _service.ClaimAsync("pat:7", "spark", CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(created.JobId, job!.JobId);
        Assert.Equal(2, job.Payload.GetProperty("diagrams").GetInt32());
        Assert.Equal($"api/board-worker/jobs/{job.JobId}/file", job.File.Url);
        Assert.True(File.Exists(await _service.FileAsync(job.JobId, "pat:7", CancellationToken.None)));
        Assert.Null(await _service.ClaimAsync("pat:8", "laptop", CancellationToken.None));   // geliehen → niemand sonst

        var accepted = await _service.SubmitAsync(job.JobId, "pat:7", Text("NPZ"), Text("""{"diagrams":2,"cut":2}"""),
            Zip(("11.png", "a"), ("12.png", "b")), CancellationToken.None);

        Assert.Equal(2, accepted.Crops);
        var dir = Path.Combine(_kit.Root, "board-cells", book.Id.ToString());
        Assert.Equal("NPZ", await File.ReadAllTextAsync(Path.Combine(dir, "cells.npz")));
        Assert.Contains("\"cut\":2", await File.ReadAllTextAsync(Path.Combine(dir, "prep.json")));
        Assert.True(File.Exists(Path.Combine(dir, "crops", "11.png")));
        Assert.Empty(Directory.GetFiles(dir, "*.part", SearchOption.AllDirectories));
        Assert.Equal("Done", (await _service.GetAsync(job.JobId, CancellationToken.None)).State);
    }

    [Fact]
    public async Task ExpiredLease_IsFreeAgain_AndTheOldOwnerIsRejected()
    {
        var book = await BookWithPdfAsync();
        await _service.CreateAsync(new BoardCutJobCreateDto { EbookId = book.Id, Payload = Payload() }, CancellationToken.None);
        var first = await _service.ClaimAsync("pat:1", null, CancellationToken.None);
        var row = await _kit.Db.BoardCutJobs.SingleAsync();
        row.LeaseUntil = DateTime.UtcNow.AddMinutes(-1);
        await _kit.Db.SaveChangesAsync();

        var second = await _service.ClaimAsync("pat:2", null, CancellationToken.None);

        Assert.Equal(first!.JobId, second!.JobId);
        await Assert.ThrowsAsync<ConflictException>(() => _service.HeartbeatAsync(first.JobId, "pat:1", CancellationToken.None));
    }

    [Fact]
    public async Task CropNameWithAPath_IsRejected_AndTheJobStaysOpen()
    {
        var book = await BookWithPdfAsync();
        await _service.CreateAsync(new BoardCutJobCreateDto { EbookId = book.Id, Payload = Payload() }, CancellationToken.None);
        var job = await _service.ClaimAsync("pat:1", null, CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() => _service.SubmitAsync(job!.JobId, "pat:1", Text("NPZ"), Text("{}"),
            Zip(("../../evil.png", "x")), CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(_kit.Root, "evil.png")));
        Assert.False(File.Exists(Path.Combine(_kit.Root, "board-cells", book.Id.ToString(), "prep.json")));
        Assert.Equal("Pending", (await _service.GetAsync(job!.JobId, CancellationToken.None)).State);
    }

    [Fact]
    public async Task Release_WithoutError_FreesTheJob_WithError_FailsIt()
    {
        var book = await BookWithPdfAsync();
        await _service.CreateAsync(new BoardCutJobCreateDto { EbookId = book.Id, Payload = Payload() }, CancellationToken.None);
        var job = await _service.ClaimAsync("pat:1", null, CancellationToken.None);

        await _service.ReleaseAsync(job!.JobId, "pat:1", null, CancellationToken.None);
        var again = await _service.ClaimAsync("pat:2", null, CancellationToken.None);
        Assert.Equal(job.JobId, again!.JobId);

        await _service.ReleaseAsync(job.JobId, "pat:2", "pdftoppm kaputt", CancellationToken.None);
        var status = await _service.GetAsync(job.JobId, CancellationToken.None);
        Assert.Equal("Failed", status.State);
        Assert.Equal("pdftoppm kaputt", status.Error);
        Assert.Null(await _service.ClaimAsync("pat:3", null, CancellationToken.None));
    }

    [Fact]
    public async Task NewJob_ReplacesTheWaitingOne_ForTheSameBook()
    {
        var book = await BookWithPdfAsync();
        var old = await _service.CreateAsync(new BoardCutJobCreateDto { EbookId = book.Id, Payload = Payload() }, CancellationToken.None);
        var fresh = await _service.CreateAsync(new BoardCutJobCreateDto { EbookId = book.Id, Payload = Payload() }, CancellationToken.None);

        Assert.Equal("Cancelled", (await _service.GetAsync(old.JobId, CancellationToken.None)).State);
        Assert.Equal(fresh.JobId, (await _service.ClaimAsync("pat:1", null, CancellationToken.None))!.JobId);
    }

    [Fact]
    public async Task Payload_WithoutPages_OrBookWithoutPdf_IsRejected()
    {
        var book = await BookWithPdfAsync();
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            _service.CreateAsync(new BoardCutJobCreateDto { EbookId = book.Id, Payload = Payload("""{"diagrams":1}""") }, CancellationToken.None));
        var epub = await _kit.BookAsync("Nur EPUB", null, format: "epub");
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            _service.CreateAsync(new BoardCutJobCreateDto { EbookId = epub.Id, Payload = Payload() }, CancellationToken.None));
    }

    [Fact]
    public void PdfFile_PrefersTheOriginalOverTheDerivedPdf()
    {
        var book = new Ebook();
        book.Files.Add(new EbookFile { Id = 1, Format = "pdf", Derived = true });
        book.Files.Add(new EbookFile { Id = 2, Format = "pdf" });
        book.Files.Add(new EbookFile { Id = 3, Format = "epub" });
        Assert.Equal(2, BoardCutJobService.PdfFile(book, null)!.Id);
        Assert.Equal(1, BoardCutJobService.PdfFile(book, 1)!.Id);
        Assert.Null(BoardCutJobService.PdfFile(book, 3));
    }
}
