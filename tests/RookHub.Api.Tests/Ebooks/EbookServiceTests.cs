using Microsoft.EntityFrameworkCore;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookServiceTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    [Fact]
    public async Task Upload_StoresFile_Hashes_AndTakesTitleFromFileName()
    {
        var user = await _kit.UserAsync("u");
        var dto = await _kit.UploadAsync(user.Id, false, EbookTestKit.TinyPdf(), "Silmans_Endgame_Course.pdf");

        Assert.Equal("Silmans Endgame Course", dto.Title);
        Assert.Equal("pdf", dto.PrimaryFormat);
        Assert.True(dto.IsOwner);
        Assert.True(dto.CanManage);
        Assert.Equal("upload", dto.Source);
        var file = await _kit.Db.EbookFiles.SingleAsync();
        Assert.Equal(64, file.Sha256!.Length);
        Assert.True(File.Exists(_kit.Storage.ResolvePath(EbookSource.Upload, file.RelativePath)));
        Assert.StartsWith("files/", file.RelativePath);
        Assert.Equal(EbookCoverState.Pending, (await _kit.Db.Ebooks.SingleAsync()).CoverState);
    }

    [Fact]
    public async Task Upload_RejectsUnknownFormat_AndEmptyFile_WithoutLeavingFiles()
    {
        var user = await _kit.UserAsync("u");
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _kit.UploadAsync(user.Id, false, [1, 2, 3], "virus.exe"));
        Assert.Equal(ApiErrorCodes.EbookFormatUnsupported, ex.Code);
        var ex2 = await Assert.ThrowsAsync<DomainValidationException>(() => _kit.UploadAsync(user.Id, false, [], "leer.pdf"));
        Assert.Equal(ApiErrorCodes.EbookFileEmpty, ex2.Code);
        Assert.Empty(Directory.GetFiles(Path.Combine(_kit.Root, "files"), "*", SearchOption.AllDirectories));
        Assert.Empty(_kit.Db.Ebooks);
    }

    [Fact]
    public async Task Upload_SameContentTwice_Is409_AndSecondFileIsRemoved()
    {
        var user = await _kit.UserAsync("u");
        await _kit.UploadAsync(user.Id, false, EbookTestKit.TinyPdf(), "a.pdf");
        var ex = await Assert.ThrowsAsync<ConflictException>(() => _kit.UploadAsync(user.Id, false, EbookTestKit.TinyPdf(), "b.pdf"));
        Assert.Equal(ApiErrorCodes.EbookDuplicateFile, ex.Code);
        Assert.Single(Directory.GetFiles(Path.Combine(_kit.Root, "files"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Upload_EnforcesQuota_ForUsers_NotForManagers()
    {
        using var kit = new EbookTestKit(quota: 100);
        var user = await kit.UserAsync("u");
        var admin = await kit.UserAsync("a", admin: true);
        var big = new byte[120];
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => kit.UploadAsync(user.Id, false, big, "big.epub"));
        Assert.Equal(ApiErrorCodes.EbookQuotaExceeded, ex.Code);
        var ok = await kit.UploadAsync(admin.Id, true, big, "big.epub");
        Assert.Equal("big", ok.Title);
    }

    [Fact]
    public async Task Upload_LargerThanMaxFile_IsRejected_EvenWhenLengthIsUnderstated()
    {
        using var kit = new EbookTestKit(maxFile: 50);
        var user = await kit.UserAsync("u");
        var bytes = new byte[80];
        // declaredLength lügt (0) — die Sicherung beim Schreiben greift trotzdem.
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            kit.Service.UploadAsync(user.Id, true, new MemoryStream(bytes), "x.pdf", 0, new EbookMetaDto()));
        Assert.Equal(ApiErrorCodes.EbookFileTooLarge, ex.Code);
        Assert.Empty(Directory.GetFiles(Path.Combine(kit.Root, "files"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Upload_WithoutConfiguredRoot_FailsWithCode()
    {
        using var kit = new EbookTestKit();
        var storage = new EbookStorage(Microsoft.Extensions.Options.Options.Create(new EbookOptions()), Microsoft.Extensions.Logging.Abstractions.NullLogger<EbookStorage>.Instance);
        var service = new EbookService(kit.Db, storage, Microsoft.Extensions.Logging.Abstractions.NullLogger<EbookService>.Instance);
        var user = await kit.UserAsync("u");
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.UploadAsync(user.Id, false, new MemoryStream(EbookTestKit.TinyPdf()), "x.pdf", 10, new EbookMetaDto()));
        Assert.Equal(ApiErrorCodes.EbooksNotConfigured, ex.Code);
    }

    [Fact]
    public async Task Groups_UserMayShareOnlyWithOwnGroupsAndEveryone_ManagerWithAny()
    {
        var owner = await _kit.UserAsync("owner");
        var mine = await _kit.GroupAsync("Meine", false, owner.Id);
        var foreign = await _kit.GroupAsync("Fremd", false);
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);

        var options = await _kit.Service.GroupOptionsAsync(owner.Id, false);
        Assert.Equal(["Everyone", "Meine"], options.Select(o => o.Name));
        Assert.True(options[0].IsEveryone);
        Assert.Equal(3, (await _kit.Service.GroupOptionsAsync(owner.Id, true)).Count);

        var dto = await _kit.UploadAsync(owner.Id, false, EbookTestKit.TinyPdf(), "a.pdf",
            new EbookMetaDto { GroupIds = [mine.Id, foreign.Id, everyone.Id, 4242] });
        Assert.Equal(new[] { everyone.Id, mine.Id }.Order(), dto.GroupIds!.Order());

        var updated = await _kit.Service.UpdateAsync(owner.Id, false, dto.Id, new EbookMetaDto { GroupIds = [foreign.Id] });
        Assert.Empty(updated.GroupIds!);   // fremde Gruppe still verworfen → keine Freigabe mehr

        var byManager = await _kit.Service.UpdateAsync(owner.Id, true, dto.Id, new EbookMetaDto { GroupIds = [foreign.Id] });
        Assert.Equal([foreign.Id], byManager.GroupIds!);
    }

    [Fact]
    public async Task Update_ChangesOnlyGivenFields_AndNormalizesTags()
    {
        var owner = await _kit.UserAsync("owner");
        var dto = await _kit.UploadAsync(owner.Id, false, EbookTestKit.TinyPdf(), "a.pdf",
            new EbookMetaDto { Title = "Alt", Author = "Jemand", Year = 1999, Tags = ["Endspiel"] });

        var updated = await _kit.Service.UpdateAsync(owner.Id, false, dto.Id,
            new EbookMetaDto { Tags = [" Taktik ", "taktik", "Endspiel", "", new string('x', 60)], Year = 0 });
        Assert.Equal("Alt", updated.Title);
        Assert.Equal("Jemand", updated.Author);
        Assert.Null(updated.Year);
        Assert.Equal(["Taktik", "Endspiel", new string('x', 40)], updated.Tags);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            _kit.Service.UpdateAsync(owner.Id, false, dto.Id, new EbookMetaDto { Title = "   " }));
    }

    [Fact]
    public async Task Update_And_Delete_ByForeignUser_Are404Or403()
    {
        var owner = await _kit.UserAsync("owner");
        var other = await _kit.UserAsync("other");
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);
        var secret = await _kit.UploadAsync(owner.Id, false, EbookTestKit.TinyPdf(), "a.pdf");
        var open = await _kit.UploadAsync(owner.Id, false, [1, 2, 3, 4], "b.epub", new EbookMetaDto { GroupIds = [everyone.Id] });

        await Assert.ThrowsAsync<NotFoundException>(() => _kit.Service.UpdateAsync(other.Id, false, secret.Id, new EbookMetaDto { Title = "x" }));
        await Assert.ThrowsAsync<ForbiddenException>(() => _kit.Service.UpdateAsync(other.Id, false, open.Id, new EbookMetaDto { Title = "x" }));
        await Assert.ThrowsAsync<ForbiddenException>(() => _kit.Service.DeleteAsync(other.Id, false, open.Id));
        Assert.Equal(2, await _kit.Db.Ebooks.CountAsync());
    }

    [Fact]
    public async Task Delete_RemovesRows_FileAndProgress()
    {
        var owner = await _kit.UserAsync("owner");
        var dto = await _kit.UploadAsync(owner.Id, false, EbookTestKit.TinyPdf(), "a.pdf");
        await _kit.Service.SetProgressAsync(owner.Id, false, dto.Id, 5, 100);
        var path = _kit.Storage.ResolvePath(EbookSource.Upload, (await _kit.Db.EbookFiles.SingleAsync()).RelativePath);
        Assert.True(File.Exists(path));

        await _kit.Service.DeleteAsync(owner.Id, false, dto.Id);

        Assert.False(File.Exists(path));
        Assert.Empty(_kit.Db.Ebooks);
        Assert.Empty(_kit.Db.EbookFiles);
        Assert.Empty(_kit.Db.EbookProgresses);
    }

    [Fact]
    public async Task Delete_NeverTouchesLibraryFiles()
    {
        var admin = await _kit.UserAsync("admin", admin: true);
        var libFile = Path.Combine(_kit.LibraryRoot, "Autor", "Buch.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(libFile)!);
        await File.WriteAllBytesAsync(libFile, EbookTestKit.TinyPdf());
        var book = new Ebook { Title = "Buch", OwnerUserId = admin.Id, Source = EbookSource.Library };
        book.Files.Add(new EbookFile { Format = "pdf", RelativePath = "Autor/Buch.pdf", FileName = "Buch.pdf", SizeBytes = 1 });
        _kit.Db.Ebooks.Add(book);
        await _kit.Db.SaveChangesAsync();

        await _kit.Service.DeleteAsync(admin.Id, true, book.Id);

        Assert.True(File.Exists(libFile));
        Assert.Empty(_kit.Db.Ebooks);
    }

    [Fact]
    public async Task List_Searches_FiltersByTag_Pages_AndBuildsFacetsOverAllVisible()
    {
        var owner = await _kit.UserAsync("owner");
        var reader = await _kit.UserAsync("reader");
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);
        await _kit.BookAsync("Endgame Course", owner.Id, "pdf", "Endspiel,Lehrbuch", everyone.Id);
        await _kit.BookAsync("Amateur's Mind", owner.Id, "pdf", "Strategie", everyone.Id);
        await _kit.BookAsync("Endspielstudien", owner.Id, "djvu", "Endspielstudien,Lehrbuch", everyone.Id);
        await _kit.BookAsync("Geheim", owner.Id, "pdf", "Endspiel");   // nicht freigegeben

        var all = await _kit.Service.ListAsync(reader.Id, false, new EbookQuery { Sort = "title" });
        Assert.Equal(3, all.Total);
        Assert.Equal(["Amateur's Mind", "Endgame Course", "Endspielstudien"], all.Items.Select(i => i.Title));
        Assert.Equal(["Lehrbuch", "Endspiel", "Endspielstudien", "Strategie"], all.Tags.Select(t => t.Tag));
        Assert.Equal(2, all.Tags[0].Count);
        Assert.All(all.Items, i => Assert.False(i.CanManage));
        Assert.All(all.Items, i => Assert.Null(i.GroupIds));

        var search = await _kit.Service.ListAsync(reader.Id, false, new EbookQuery { Search = "endgame" });
        Assert.Equal(["Endgame Course"], search.Items.Select(i => i.Title));
        Assert.Equal(4, search.Tags.Count);   // Facette bleibt vollständig

        var tag = await _kit.Service.ListAsync(reader.Id, false, new EbookQuery { Tag = "Endspiel" });
        Assert.Equal(["Endgame Course"], tag.Items.Select(i => i.Title));   // kein Teilwort-Treffer auf „Endspielstudien"

        var page2 = await _kit.Service.ListAsync(reader.Id, false, new EbookQuery { Sort = "title", Page = 2, PageSize = 2 });
        Assert.Equal(["Endspielstudien"], page2.Items.Select(i => i.Title));
        Assert.Equal(2, page2.Page);

        var mine = await _kit.Service.ListAsync(owner.Id, false, new EbookQuery { Mine = true });
        Assert.Equal(4, mine.Total);
        Assert.All(mine.Items, i => Assert.NotNull(i.GroupIds));
    }

    [Fact]
    public async Task List_SortRecent_PutsLastReadFirst_AndCarriesProgress()
    {
        var owner = await _kit.UserAsync("owner");
        var a = await _kit.BookAsync("A", owner.Id);
        var b = await _kit.BookAsync("B", owner.Id);
        var c = await _kit.BookAsync("C", owner.Id);
        await _kit.Service.SetProgressAsync(owner.Id, false, c.Id, 7, 50);
        await Task.Delay(5);
        await _kit.Service.SetProgressAsync(owner.Id, false, a.Id, 3, 20);

        var recent = await _kit.Service.ListAsync(owner.Id, false, new EbookQuery { Sort = "recent" });
        Assert.Equal(["A", "C", "B"], recent.Items.Select(i => i.Title));
        Assert.Equal(3, recent.Items[0].Progress!.Page);
        Assert.Equal(20, recent.Items[0].Progress!.PageCount);
        Assert.Null(recent.Items[2].Progress);
        _ = b;
    }

    [Fact]
    public async Task Sort_Newest_ByPublicationYear_WithoutYearLast()
    {
        var owner = await _kit.UserAsync("owner");
        var old = await _kit.BookAsync("Alt 1902", owner.Id);
        old.Year = 1902;
        var none = await _kit.BookAsync("Ohne Jahr", owner.Id);
        var recent = await _kit.BookAsync("Neu 2021", owner.Id);
        recent.Year = 2021;
        var mid = await _kit.BookAsync("Mitte 1990", owner.Id);
        mid.Year = 1990;
        old.CreatedAt = DateTime.UtcNow.AddMinutes(5);   // zuletzt aufgenommen — zählt erst nach dem Jahr
        await _kit.Db.SaveChangesAsync();
        var list = await _kit.Service.ListAsync(owner.Id, false, new EbookQuery { Sort = "newest" });
        Assert.Equal(["Neu 2021", "Mitte 1990", "Alt 1902", "Ohne Jahr"], list.Items.Select(i => i.Title));
        // Ohne Angabe ist das auch der Standard (0.778.1).
        var plain = await _kit.Service.ListAsync(owner.Id, false, new EbookQuery());
        Assert.Equal(["Neu 2021", "Mitte 1990", "Alt 1902", "Ohne Jahr"], plain.Items.Select(i => i.Title));
        _ = none;
    }

    [Fact]
    public async Task Progress_RequiresVisibility_AndUpserts()
    {
        var owner = await _kit.UserAsync("owner");
        var other = await _kit.UserAsync("other");
        var book = await _kit.BookAsync("A", owner.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => _kit.Service.SetProgressAsync(other.Id, false, book.Id, 2, null));
        Assert.Null(await _kit.Service.GetProgressAsync(owner.Id, false, book.Id));
        await _kit.Service.SetProgressAsync(owner.Id, false, book.Id, 2, 90);
        var p = await _kit.Service.SetProgressAsync(owner.Id, false, book.Id, 9, null);
        Assert.Equal(9, p.Page);
        Assert.Equal(90, p.PageCount);   // Seitenzahl bleibt, wenn der Leser keine schickt
        Assert.Single(_kit.Db.EbookProgresses);
        await Assert.ThrowsAsync<DomainValidationException>(() => _kit.Service.SetProgressAsync(owner.Id, false, book.Id, 0, null));
    }

    [Fact]
    public async Task SetGroups_Bulk_SkipsForeignBooks_AndReportsCount()
    {
        var owner = await _kit.UserAsync("owner");
        var other = await _kit.UserAsync("other");
        var group = await _kit.GroupAsync("G", false, owner.Id, other.Id);
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);
        var mine1 = await _kit.BookAsync("M1", owner.Id);
        var mine2 = await _kit.BookAsync("M2", owner.Id, "pdf", null, everyone.Id);
        var foreign = await _kit.BookAsync("F", other.Id, "pdf", null, group.Id);

        var updated = await _kit.Service.SetGroupsAsync(owner.Id, false, [mine1.Id, mine2.Id, foreign.Id, 999], [group.Id]);

        Assert.Equal(2, updated);
        var access = await _kit.Db.EbookGroupAccesses.ToListAsync();
        Assert.Equal([group.Id], access.Where(a => a.EbookId == mine1.Id).Select(a => a.GroupId));
        Assert.Equal([group.Id], access.Where(a => a.EbookId == mine2.Id).Select(a => a.GroupId));   // Everyone ersetzt
        Assert.Equal([group.Id], access.Where(a => a.EbookId == foreign.Id).Select(a => a.GroupId)); // unberührt
    }

    [Fact]
    public async Task ResolveFile_PrefersPdf_AndReportsMissingFileWithCode()
    {
        var owner = await _kit.UserAsync("owner");
        var dto = await _kit.UploadAsync(owner.Id, false, EbookTestKit.TinyPdf(), "a.pdf");
        var (_, file, full) = await _kit.Service.ResolveFileAsync(owner.Id, false, dto.Id, null);
        Assert.Equal("pdf", file.Format);
        Assert.True(File.Exists(full));

        File.Delete(full);
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _kit.Service.ResolveFileAsync(owner.Id, false, dto.Id, null));
        Assert.Equal(ApiErrorCodes.EbookFileMissing, ex.Code);
        await Assert.ThrowsAsync<NotFoundException>(() => _kit.Service.ResolveFileAsync(owner.Id, false, dto.Id, 424242));
    }

    [Fact]
    public void Storage_RefusesPathsThatEscapeTheRoot()
    {
        Assert.Throws<InvalidOperationException>(() => _kit.Storage.ResolvePath(EbookSource.Upload, "../../etc/passwd"));
        Assert.Throws<InvalidOperationException>(() => _kit.Storage.ResolvePath(EbookSource.Library, "/etc/passwd"));
        Assert.EndsWith(Path.Combine("files", "a.pdf"), _kit.Storage.ResolvePath(EbookSource.Upload, "files/a.pdf"));
    }

    [Fact]
    public void Formats_NormalizeTags_And_TitleFromFileName()
    {
        Assert.Equal(["A", "b"], EbookFormats.ParseTagCsv(" A ; a, b ,, "));
        Assert.Equal(EbookFormats.MaxTags, EbookFormats.NormalizeTags(Enumerable.Range(0, 50).Select(i => "t" + i)).Count);
        Assert.Equal("My Book", EbookFormats.TitleFromFileName("My__Book.PDF"));
        Assert.Equal("pdf", EbookFormats.FormatOf("x.PDF"));
        Assert.Null(EbookFormats.FormatOf("x.exe"));
        Assert.Null(EbookFormats.FormatOf("noext"));
    }

    [Fact]
    public async Task Diagrams_OnlyWhenDone_InReadingOrder_AndHiddenForStrangers()
    {
        var owner = await _kit.UserAsync("owner");
        var other = await _kit.UserAsync("other");
        var book = await _kit.BookAsync("Buch", owner.Id);
        _kit.Db.EbookDiagrams.AddRange(
            new EbookDiagram { EbookId = book.Id, Page = 5, Y = 0.6, Fen = "8/8/8/8/8/8/8/K6k w - - 0 1" },
            new EbookDiagram { EbookId = book.Id, Page = 5, Y = 0.1, Fen = "8/8/8/8/8/8/8/K5k1 b - - 0 1", Label = "7" },
            new EbookDiagram { EbookId = book.Id, Page = 2, Y = 0.5, Fen = "8/8/8/8/8/8/8/K4k2 w - - 0 1" });
        await _kit.Db.SaveChangesAsync();

        var pending = await _kit.Service.GetDiagramsAsync(owner.Id, false, book.Id);
        Assert.Equal("pending", pending.State);
        Assert.Empty(pending.Items);

        book.DiagramState = EbookDiagramState.Done;
        book.DiagramCount = 3;
        await _kit.Db.SaveChangesAsync();
        var done = await _kit.Service.GetDiagramsAsync(owner.Id, false, book.Id);
        Assert.Equal("done", done.State);
        Assert.Equal([2, 5, 5], done.Items.Select(d => d.Page));
        Assert.Equal("7", done.Items[1].Label);
        Assert.Equal(3, (await _kit.Service.GetAsync(owner.Id, false, book.Id)).DiagramCount);

        await Assert.ThrowsAsync<NotFoundException>(() => _kit.Service.GetDiagramsAsync(other.Id, false, book.Id));

        Assert.Equal(1, await _kit.Service.RescanDiagramsAsync());
        Assert.Equal(EbookDiagramState.Pending, (await _kit.Db.Ebooks.SingleAsync()).DiagramState);

        await _kit.Service.DeleteAsync(owner.Id, false, book.Id);
        Assert.Empty(_kit.Db.EbookDiagrams);
    }

    [Fact]
    public async Task ReportDiagram_ToggleCountsForManagers_AndListSurvivesRescan()
    {
        var owner = await _kit.UserAsync("owner");
        var reader = await _kit.UserAsync("reader");
        var stranger = await _kit.UserAsync("stranger");
        var g = await _kit.GroupAsync("G", false, reader.Id);
        var book = await _kit.BookAsync("Buch", owner.Id, "pdf", null, g.Id);
        book.DiagramState = EbookDiagramState.Done; book.DiagramCount = 1;
        var d = new EbookDiagram { EbookId = book.Id, Page = 3, Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", FontFamily = "Hastings" };
        _kit.Db.EbookDiagrams.Add(d);
        await _kit.Db.SaveChangesAsync();

        Assert.True(await _kit.Service.ReportDiagramAsync(reader.Id, false, book.Id, d.Id, true, " Dame fehlt "));
        Assert.True(await _kit.Service.ReportDiagramAsync(reader.Id, false, book.Id, d.Id, true, null));   // idempotent
        Assert.Single(_kit.Db.EbookDiagramReports);
        await Assert.ThrowsAsync<NotFoundException>(() => _kit.Service.ReportDiagramAsync(stranger.Id, false, book.Id, d.Id, true, null));
        await Assert.ThrowsAsync<NotFoundException>(() => _kit.Service.ReportDiagramAsync(reader.Id, false, book.Id, 999, true, null));

        var asReader = (await _kit.Service.GetDiagramsAsync(reader.Id, false, book.Id)).Items.Single();
        Assert.True(asReader.ReportedByMe);
        Assert.Equal(0, asReader.ReportCount);   // Zahl nur für Verwalter
        Assert.Equal(1, (await _kit.Service.GetDiagramsAsync(owner.Id, true, book.Id)).Items.Single().ReportCount);

        Assert.False(await _kit.Service.ReportDiagramAsync(reader.Id, false, book.Id, d.Id, false, null));
        Assert.Empty(_kit.Db.EbookDiagramReports);
        await _kit.Service.ReportDiagramAsync(reader.Id, false, book.Id, d.Id, true, " Dame fehlt ");

        var list = await _kit.Service.ListDiagramReportsAsync();
        Assert.Equal("Dame fehlt", list.Single().Note);
        Assert.Equal("Hastings", list.Single().FontFamily);
        _kit.Db.EbookDiagrams.Remove(d); await _kit.Db.SaveChangesAsync();   // Neuerkennung ersetzt die Zeilen
        Assert.False((await _kit.Service.ListDiagramReportsAsync()).Single().DiagramExists);

    }

    [Fact]
    public async Task List_WithDiagrams_OnlyBooksWithRecognisedDiagrams()
    {
        var owner = await _kit.UserAsync("owner");
        var a = await _kit.BookAsync("A", owner.Id);
        var b = await _kit.BookAsync("B", owner.Id);
        a.DiagramState = EbookDiagramState.Done; a.DiagramCount = 12;
        b.DiagramState = EbookDiagramState.Done; b.DiagramCount = 0;
        await _kit.Db.SaveChangesAsync();
        var r = await _kit.Service.ListAsync(owner.Id, false, new EbookQuery { WithDiagrams = true });
        Assert.Equal(["A"], r.Items.Select(i => i.Title));
    }
}
