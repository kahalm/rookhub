using Microsoft.EntityFrameworkCore;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookMissedDiagramTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    [Fact]
    public async Task Answer_IsStoredWithPlaceAndUser_OnlyForVisibleBooks_AndRemovedWithTheBook()
    {
        var owner = await _kit.UserAsync("owner");
        var other = await _kit.UserAsync("other");
        var book = await _kit.BookAsync("Silman", owner.Id);

        await _kit.Service.RecordMissedDiagramAsync(owner.Id, false, book.Id, new EbookMissedDiagramDto { Page = 12, X = 0.4, Y = 1.7, IsDiagram = true });
        var m = await _kit.Db.EbookMissedDiagrams.SingleAsync();
        Assert.Equal((book.Id, 12, 0.4, 1.0, true, owner.Id), (m.EbookId, m.Page, m.X, m.Y, m.IsDiagram, m.UserId));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _kit.Service.RecordMissedDiagramAsync(other.Id, false, book.Id, new EbookMissedDiagramDto { Page = 1, IsDiagram = false }));

        await _kit.Service.DeleteAsync(owner.Id, false, book.Id);
        Assert.Empty(_kit.Db.EbookMissedDiagrams);
    }

    [Fact]
    public async Task Epub_PageZeroWithImageOrChapter_IsStored_WithoutAnyPlace_Rejected()
    {
        var owner = await _kit.UserAsync("owner");
        var book = await _kit.BookAsync("Dvoretsky", owner.Id, "epub");

        await _kit.Service.RecordMissedDiagramAsync(owner.Id, false, book.Id,
            new EbookMissedDiagramDto { Page = 0, EpubRef = " OEBPS/images/fig12.png ", EpubChapter = "OEBPS/ch03.xhtml", IsDiagram = true });
        await _kit.Service.RecordMissedDiagramAsync(owner.Id, false, book.Id,
            new EbookMissedDiagramDto { Page = 0, EpubChapter = "OEBPS/ch04.xhtml", IsDiagram = false });
        var rows = await _kit.Db.EbookMissedDiagrams.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(("OEBPS/images/fig12.png", "OEBPS/ch03.xhtml", 0), (rows[0].EpubRef, rows[0].EpubChapter, rows[0].Page));
        Assert.Equal((null, "OEBPS/ch04.xhtml"), (rows[1].EpubRef, rows[1].EpubChapter));

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            _kit.Service.RecordMissedDiagramAsync(owner.Id, false, book.Id, new EbookMissedDiagramDto { Page = 0, EpubRef = "  ", IsDiagram = true }));
        Assert.Equal(2, await _kit.Db.EbookMissedDiagrams.CountAsync());
    }

    [Fact]
    public async Task NoteAndSource_AreStored_ReaderSendsNeither()
    {
        var owner = await _kit.UserAsync("owner");
        var book = await _kit.BookAsync("Nunn", owner.Id);

        await _kit.Service.RecordMissedDiagramAsync(owner.Id, false, book.Id, new EbookMissedDiagramDto
            { Page = 7, X = 0.1, Y = 0.1, IsDiagram = true, Note = "  oben links, grauer Rahmen, Lehrdiagramm ", Source = " review-2026-11 " });
        await _kit.Service.RecordMissedDiagramAsync(owner.Id, false, book.Id, new EbookMissedDiagramDto { Page = 8, X = 0.5, Y = 0.5, IsDiagram = true });

        var rows = await _kit.Db.EbookMissedDiagrams.OrderBy(m => m.Page).ToListAsync();
        Assert.Equal(("oben links, grauer Rahmen, Lehrdiagramm", "review-2026-11"), (rows[0].Note, rows[0].Source));
        Assert.Equal(((string?)null, (string?)null), (rows[1].Note, rows[1].Source));
    }

    [Fact]
    public async Task AdminList_NewestFirst_FiltersByBook_HonoursLimit_WithTitleAndUser()
    {
        var owner = await _kit.UserAsync("owner");
        var a = await _kit.BookAsync("Silman", owner.Id);
        var b = await _kit.BookAsync("Dvoretsky", owner.Id);
        var t0 = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        _kit.Db.EbookMissedDiagrams.AddRange(
            new RookHub.Api.Models.EbookMissedDiagram { Id = 990001, EbookId = a.Id, Page = 3, X = 0.2, Y = 0.1, IsDiagram = true, UserId = owner.Id, CreatedAt = t0, Note = "oben links", Source = "review" },
            new RookHub.Api.Models.EbookMissedDiagram { Id = 990002, EbookId = b.Id, Page = 9, X = 0.8, Y = 0.9, IsDiagram = false, UserId = owner.Id, CreatedAt = t0.AddHours(1) },
            new RookHub.Api.Models.EbookMissedDiagram { Id = 990003, EbookId = a.Id, Page = 5, X = 0.5, Y = 0.5, IsDiagram = true, UserId = owner.Id, CreatedAt = t0.AddHours(2) });
        await _kit.Db.SaveChangesAsync();

        var all = await _kit.Service.ListMissedDiagramsAsync(null);
        Assert.Equal(new[] { 990003, 990002, 990001 }, all.Select(m => m.Id));
        var first = all.Single(m => m.Id == 990001);
        Assert.Equal(("Silman", "owner", "oben links", "review", 3, 0.2, 0.1), (first.EbookTitle, first.Username, first.Note, first.Source, first.Page, first.X, first.Y));
        Assert.Null(all.Single(m => m.Id == 990002).Source);

        Assert.Equal(new[] { 990003, 990001 }, (await _kit.Service.ListMissedDiagramsAsync(a.Id)).Select(m => m.Id));
        Assert.Equal(new[] { 990003 }, (await _kit.Service.ListMissedDiagramsAsync(null, 1)).Select(m => m.Id));
        Assert.Single(await _kit.Service.ListMissedDiagramsAsync(null, 0));   // limit < 1 → 1
    }
}
