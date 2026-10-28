using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Upload-Dublettenprüfung jenseits der Byte-Gleichheit (0.863.0): ISBN, Text-Fingerabdruck, Titel + Autor,
/// „trotzdem neu“ und Sichtbarkeit; dazu der Nachhol-Worker.</summary>
public class EbookDuplicateUploadTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private static readonly string BookText = EbookFingerprintTests.AsEpubLayout(EbookFingerprintTests.SyntheticWords(1, 8000));

    /// <summary>Vorhandenes Buch mit Fingerabdruck (wie nach Upload/Worker), optional für „Everyone“ freigegeben.</summary>
    private async Task<Ebook> ExistingAsync(string title, int? ownerId, string? isbns = null, string? text = null, string author = "", params int[] groups)
    {
        var b = await _kit.BookAsync(title, ownerId, "pdf", null, groups);
        b.Author = author;
        b.Year = 2018;
        var f = b.Files.Single();
        f.Isbns = isbns;
        f.TextFingerprint = text == null ? null : EbookFingerprint.Encode(EbookFingerprint.FromText(null, text).MinHash!);
        f.FingerprintedAt = DateTime.UtcNow;
        await _kit.Db.SaveChangesAsync();
        return b;
    }

    private static List<EbookDuplicateDto> BooksOf(ConflictException ex)
    {
        Assert.NotNull(ex.Details);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ex.Details));
        return doc.RootElement.GetProperty("books").Deserialize<List<EbookDuplicateDto>>()!;
    }

    private int UploadedFiles() => Directory.Exists(Path.Combine(_kit.Root, "files"))
        ? Directory.GetFiles(Path.Combine(_kit.Root, "files"), "*", SearchOption.AllDirectories).Length : 0;

    [Fact]
    public async Task Upload_SameTextAsVisibleBook_Is409WithExistingBook_AndNothingStays()
    {
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);
        var admin = await _kit.UserAsync("admin", admin: true);
        var user = await _kit.UserAsync("u");
        var existing = await ExistingAsync("Mein Endspielbuch", admin.Id, text: BookText, author: "Jeremy Silman", groups: everyone.Id);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _kit.UploadAsync(user.Id, false, EbookFingerprintTests.Epub(BookText), "ganz_anderer_name.epub"));

        Assert.Equal(ApiErrorCodes.EbookDuplicateBook, ex.Code);
        var book = Assert.Single(BooksOf(ex));
        Assert.Equal(existing.Id, book.Id);
        Assert.Equal("text", book.Reason);
        Assert.Equal("Jeremy Silman", book.Author);
        Assert.Equal(2018, book.Year);
        Assert.Equal(["pdf"], book.Formats);
        Assert.True(book.Similarity >= EbookDuplicateFinder.CertainSimilarity);
        Assert.Equal(1, await _kit.Db.Ebooks.CountAsync());
        Assert.Equal(0, UploadedFiles());
    }

    [Fact]
    public async Task Upload_SameIsbn_Is409_ReasonIsbn()
    {
        var user = await _kit.UserAsync("u");
        var mine = await ExistingAsync("Altes Buch", user.Id, isbns: "9781906552077,9780306406157");

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _kit.UploadAsync(user.Id, false, EbookFingerprintTests.Epub("kurz", "0-306-40615-2"), "neu.epub"));

        Assert.Equal(ApiErrorCodes.EbookDuplicateBook, ex.Code);
        var book = Assert.Single(BooksOf(ex));
        Assert.Equal((mine.Id, "isbn"), (book.Id, book.Reason));
    }

    [Fact]
    public async Task Upload_ForceNew_SkipsTheCheck_AndStoresFingerprint()
    {
        var user = await _kit.UserAsync("u");
        await ExistingAsync("Altes Buch", user.Id, isbns: "9780306406157", text: BookText);
        var bytes = EbookFingerprintTests.Epub(BookText, "0-306-40615-2");

        var dto = await _kit.Service.UploadAsync(user.Id, false, new MemoryStream(bytes), "neu.epub", bytes.Length, new EbookMetaDto(),
            CancellationToken.None, forceNew: true);

        Assert.Null(dto.PossibleDuplicates);
        var file = await _kit.Db.EbookFiles.SingleAsync(f => f.EbookId == dto.Id);
        Assert.Equal("9780306406157", file.Isbns);
        Assert.NotNull(EbookFingerprint.Decode(file.TextFingerprint));
        Assert.NotNull(file.FingerprintedAt);
    }

    [Fact]
    public async Task Upload_SameTitleAndAuthor_IsCreated_WithPossibleDuplicate()
    {
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);
        var admin = await _kit.UserAsync("admin", admin: true);
        var user = await _kit.UserAsync("u");
        var existing = await ExistingAsync("The Woodpecker Method", admin.Id, author: "Axel Smith; Hans Tikkanen", groups: everyone.Id);
        await ExistingAsync("Woodpecker Method", admin.Id, author: "John Nunn", groups: everyone.Id);   // anderer Autor → nicht

        var dto = await _kit.UploadAsync(user.Id, false, EbookTestKit.TinyPdf(), "scan.pdf",
            new EbookMetaDto { Title = "Woodpecker Method: 1128 puzzles", Author = "Smith, Axel" });

        Assert.Equal(3, await _kit.Db.Ebooks.CountAsync());
        var d = Assert.Single(dto.PossibleDuplicates!);
        Assert.Equal((existing.Id, "title"), (d.Id, d.Reason));
    }

    [Fact]
    public async Task Upload_DoesNotRevealInvisibleBooks()
    {
        var other = await _kit.UserAsync("other");
        var user = await _kit.UserAsync("u");
        await ExistingAsync("Geheimes Buch", other.Id, isbns: "9780306406157", text: BookText);   // privat, nicht freigegeben

        var dto = await _kit.UploadAsync(user.Id, false, EbookFingerprintTests.Epub(BookText, "0-306-40615-2"), "Geheimes Buch.epub");

        Assert.Null(dto.PossibleDuplicates);
        Assert.Equal(2, await _kit.Db.Ebooks.CountAsync());
    }

    [Fact]
    public async Task Upload_SameBytes_NamesVisibleBook_ButNotInvisibleOne()
    {
        var user = await _kit.UserAsync("u");
        var other = await _kit.UserAsync("other");
        var first = await _kit.UploadAsync(user.Id, false, EbookTestKit.TinyPdf(), "a.pdf");

        var mine = await Assert.ThrowsAsync<ConflictException>(() => _kit.UploadAsync(user.Id, false, EbookTestKit.TinyPdf(), "b.pdf"));
        Assert.Equal(ApiErrorCodes.EbookDuplicateFile, mine.Code);
        var b = Assert.Single(BooksOf(mine));
        Assert.Equal((first.Id, "file"), (b.Id, b.Reason));

        var foreign = await Assert.ThrowsAsync<ConflictException>(() => _kit.UploadAsync(other.Id, false, EbookTestKit.TinyPdf(), "c.pdf"));
        Assert.Equal(ApiErrorCodes.EbookDuplicateFile, foreign.Code);
        Assert.Null(foreign.Details);
        // Ohne Zusatzdaten bleibt die Antwort { message, code } wie bisher.
        Assert.Equal(JsonSerializer.Serialize(new { message = "This file is already in the library.", code = "ebook_duplicate_file" }),
            JsonSerializer.Serialize(ApiErrorResponses.Body(foreign)));
    }

    [Fact]
    public void ErrorBody_WithDetails_CarriesThem()
    {
        var ex = new ConflictException("x") { Code = ApiErrorCodes.EbookDuplicateBook, Details = new { books = new[] { 1 } } };
        Assert.Equal("{\"message\":\"x\",\"code\":\"ebook_duplicate_book\",\"details\":{\"books\":[1]}}", JsonSerializer.Serialize(ApiErrorResponses.Body(ex)));
    }

    [Fact]
    public async Task Worker_FingerprintsOpenFiles_AndMarksTextlessFormatsDone()
    {
        var user = await _kit.UserAsync("u");
        var epubBook = await _kit.BookAsync("EPUB", user.Id, "epub");
        var djvuBook = await _kit.BookAsync("DJVU", user.Id, "djvu");
        var epubFile = epubBook.Files.Single();
        var full = _kit.Storage.ResolvePath(EbookSource.Upload, epubFile.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, EbookFingerprintTests.Epub(BookText, "978-1-906552-07-7"));

        var done = await EbookFingerprintWorker.RunBatchAsync(_kit.Db, _kit.Storage, NullLogger.Instance, CancellationToken.None, batchSize: 10);

        Assert.Equal(2, done);
        var e = await _kit.Db.EbookFiles.SingleAsync(f => f.EbookId == epubBook.Id);
        Assert.Equal("9781906552077", e.Isbns);
        Assert.NotNull(e.TextFingerprint);
        var d = await _kit.Db.EbookFiles.SingleAsync(f => f.EbookId == djvuBook.Id);
        Assert.NotNull(d.FingerprintedAt);
        Assert.Null(d.TextFingerprint);
        Assert.Equal(0, await EbookFingerprintWorker.RunBatchAsync(_kit.Db, _kit.Storage, NullLogger.Instance, CancellationToken.None));
    }
}
