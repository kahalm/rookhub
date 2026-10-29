using Microsoft.EntityFrameworkCore;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
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
    public async Task List_BookAndFileCounts_CountEveryBookInACollection_AndFollowTheFilter()
    {
        var admin = await _kit.UserAsync("admin", admin: true);
        var a = await _kit.BookAsync("British Chess Magazine 03.2022", admin.Id, "pdf", "Zeitschrift");
        var b = await _kit.BookAsync("British Chess Magazine 05.2022", admin.Id, "pdf", "Zeitschrift");
        var c = await _kit.BookAsync("Silman Endgame Course", admin.Id, "djvu", "Endspiel");
        // DJVU mit abgeleiteter PDF (zählt nicht) und einem echten EPUB daneben.
        c.Files.Add(new EbookFile { Format = "pdf", RelativePath = "converted/c.pdf", FileName = "c.pdf", Derived = true });
        c.Files.Add(new EbookFile { Format = "EPUB", RelativePath = "files/c.epub", FileName = "c.epub" });
        await _kit.Db.SaveChangesAsync();
        await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [a.Id, b.Id], NewName = "British Chess Magazine" });

        var main = await _kit.Service.ListAsync(admin.Id, true, new EbookQuery { Sort = "title" });
        Assert.Equal(2, main.Total);                                     // Ordner + Einzelbuch
        Assert.Equal(3, main.BookCount);                                 // jedes Heft zählt
        Assert.Equal([("pdf", 2), ("djvu", 1), ("epub", 1)], main.FileCounts.Select(f => (f.Format, f.Count)));

        var tagged = await _kit.Service.ListAsync(admin.Id, true, new EbookQuery { Tag = "Zeitschrift" });
        Assert.Equal(1, tagged.Total);
        Assert.Equal(2, tagged.BookCount);
        Assert.Equal([("pdf", 2)], tagged.FileCounts.Select(f => (f.Format, f.Count)));

        var search = await _kit.Service.ListAsync(admin.Id, true, new EbookQuery { Search = "Silman" });
        Assert.Equal(1, search.BookCount);
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

    [Fact]
    public async Task Additional_KeepsTheMainCollection_BookShowsInBoth_RemoveTakesItOutOfOneOnly()
    {
        var admin = await _kit.UserAsync("admin", admin: true);
        var kongress = await _kit.BookAsync("Kongress Nürnberg 1906", admin.Id);
        var turnier = await _kit.BookAsync("Nürnberg 1896", admin.Id);
        var k = await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [kongress.Id], NewName = "Kongressbücher" });
        var t = await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [turnier.Id], NewName = "Turnierbücher" });

        var both = await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [kongress.Id], CollectionId = t!.Id, Additional = true });
        Assert.Equal(2, both!.BookCount);
        Assert.Equal(k!.Id, (await _kit.Db.Ebooks.SingleAsync(b => b.Id == kongress.Id)).CollectionId);   // Hauptsammlung bleibt
        Assert.Equal(2, (await Collections().ListAsync(true)).Single(c => c.Id == t.Id).BookCount);

        var openT = await _kit.Service.ListAsync(admin.Id, true, new EbookQuery { Collection = t.Id, Sort = "title" });
        Assert.Equal([kongress.Id, turnier.Id], openT.Items.Select(i => i.Id));
        Assert.Equal(2, openT.Collection!.BookCount);
        var main = await _kit.Service.ListAsync(admin.Id, true, new EbookQuery { Sort = "title" });
        Assert.Equal(2, main.Total);                                        // zwei Ordner, das Buch nicht doppelt

        // Zweimal zusätzlich oder in die eigene Hauptsammlung → keine doppelte Zuordnung.
        await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [kongress.Id], CollectionId = t.Id, Additional = true });
        await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [kongress.Id], CollectionId = k.Id, Additional = true });
        Assert.Equal(1, await _kit.Db.EbookCollectionLinks.CountAsync());

        // Aus der geöffneten weiteren Sammlung nehmen → Hauptsammlung bleibt.
        await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [kongress.Id], FromCollectionId = t.Id });
        Assert.Equal(k.Id, (await _kit.Db.Ebooks.SingleAsync(b => b.Id == kongress.Id)).CollectionId);
        Assert.Empty(_kit.Db.EbookCollectionLinks);
    }

    [Fact]
    public async Task RemovingTheMainCollection_PromotesTheExtraOne_AndDissolveKeepsTheOther()
    {
        var admin = await _kit.UserAsync("admin", admin: true);
        var a = await _kit.BookAsync("A", admin.Id);
        var b = await _kit.BookAsync("B", admin.Id);
        var x = await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [a.Id, b.Id], NewName = "X" });
        var y = await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [a.Id], NewName = "Y", Additional = true });
        Assert.Equal(1, y!.BookCount);
        Assert.Equal(x!.Id, (await _kit.Db.Ebooks.SingleAsync(e => e.Id == a.Id)).CollectionId);

        await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [a.Id], FromCollectionId = x.Id });
        Assert.Equal(y.Id, (await _kit.Db.Ebooks.SingleAsync(e => e.Id == a.Id)).CollectionId);       // Y rückt nach
        Assert.Empty(_kit.Db.EbookCollectionLinks);

        await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [a.Id], CollectionId = x.Id, Additional = true });
        await Collections().DeleteAsync(true, x.Id);
        Assert.Equal(y.Id, (await _kit.Db.Ebooks.SingleAsync(e => e.Id == a.Id)).CollectionId);
        Assert.Null((await _kit.Db.Ebooks.SingleAsync(e => e.Id == b.Id)).CollectionId);
        Assert.Empty(_kit.Db.EbookCollectionLinks);

        // Ohne Angabe: aus allen Sammlungen nehmen.
        await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [a.Id, b.Id], NewName = "Z", Additional = true });
        Assert.Equal(1, await _kit.Db.EbookCollectionLinks.CountAsync());
        await Collections().AssignAsync(true, new EbookCollectionAssignDto { EbookIds = [a.Id, b.Id] });
        Assert.All(await _kit.Db.Ebooks.ToListAsync(), e => Assert.Null(e.CollectionId));
        Assert.Empty(_kit.Db.EbookCollectionLinks);
        Assert.Empty(_kit.Db.EbookCollections);
    }
}
