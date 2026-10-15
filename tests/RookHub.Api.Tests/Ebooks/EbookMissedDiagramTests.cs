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
}
