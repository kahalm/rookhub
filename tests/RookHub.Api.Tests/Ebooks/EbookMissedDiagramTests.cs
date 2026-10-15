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
}
