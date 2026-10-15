using Microsoft.EntityFrameworkCore;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookCollectionTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private EbookCollectionService Collections() => new(_kit.Db);

    [Fact]
    public async Task List_ShowsACollectionAsOneFolder_OpenShowsItsBooks_SearchShowsSingleIssues()
    {
        var admin = await _kit.UserAsync("admin", admin: true);
        var a = await _kit.BookAsync("British Chess Magazine 03.2022", admin.Id);
        var b = await _kit.BookAsync("British Chess Magazine 05.2022", admin.Id);
        var c = await _kit.BookAsync("Silman Endgame Course", admin.Id);
        var coll = await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [a.Id, b.Id], NewName = "British Chess Magazine" });
        Assert.Equal(2, coll!.BookCount);

        var main = await _kit.Service.ListAsync(admin.Id, true, new EbookQuery { Sort = "title" });
        Assert.Equal(2, main.Total);
        var folder = Assert.Single(main.Items, i => i.CollectionStub);
        Assert.Equal(a.Id, folder.Id);                                   // ältestes Heft steht für die Sammlung
        Assert.Equal("British Chess Magazine", folder.Collection!.Name);
        Assert.Equal(2, folder.Collection.BookCount);
        Assert.Contains(main.Items, i => i.Id == c.Id && i.Collection == null);

        var open = await _kit.Service.ListAsync(admin.Id, true, new EbookQuery { Collection = coll.Id, Sort = "title" });
        Assert.Equal([a.Id, b.Id], open.Items.Select(i => i.Id));
        Assert.All(open.Items, i => Assert.False(i.CollectionStub));
        Assert.Equal("British Chess Magazine", open.Collection!.Name);

        var search = await _kit.Service.ListAsync(admin.Id, true, new EbookQuery { Search = "05.2022" });
        Assert.Equal(b.Id, Assert.Single(search.Items).Id);
    }

    [Fact]
    public async Task Assign_RemovingTheLastBook_DeletesTheEmptyCollection_AndOnlyManagersMayManage()
    {
        var admin = await _kit.UserAsync("admin", admin: true);
        var a = await _kit.BookAsync("A", admin.Id);
        var coll = await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [a.Id], NewName = "X" });
        await Assert.ThrowsAsync<ForbiddenException>(() => Collections().AssignAsync(false, new EbookCollectionAssignDto { EbookIds = [a.Id] }));

        Assert.Null(await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [a.Id] }));
        Assert.Null((await _kit.Db.Ebooks.SingleAsync()).CollectionId);
        Assert.False(await _kit.Db.EbookCollections.AnyAsync(x => x.Id == coll!.Id));
    }

    [Fact]
    public async Task Rename_And_Dissolve_KeepTheBooks()
    {
        var admin = await _kit.UserAsync("admin", admin: true);
        var a = await _kit.BookAsync("A", admin.Id);
        var coll = await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [a.Id], NewName = "X" });
        Assert.Equal("Y", (await Collections().RenameAsync(true, coll!.Id, " Y ")).Name);
        await Collections().DeleteAsync(true, coll.Id);
        Assert.Null((await _kit.Db.Ebooks.SingleAsync()).CollectionId);
        Assert.Empty(_kit.Db.EbookCollections);
    }
}
