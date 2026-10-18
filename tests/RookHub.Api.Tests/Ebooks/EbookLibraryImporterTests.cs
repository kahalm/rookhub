using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookLibraryImporterTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private EbookLibraryImporter Importer => new(_kit.Db, _kit.Storage, NullLogger<EbookLibraryImporter>.Instance);

    private void Put(string relative, string content = "x")
    {
        var full = Path.Combine(_kit.LibraryRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public async Task Import_GroupsFormatsOfOneTitle_ReadsSidecar_AndIsIdempotent()
    {
        var admin = await _kit.UserAsync("admin", admin: true);
        Put("Silman, Jeremy/Complete Endgame Course.pdf", "pdf");
        Put("Silman, Jeremy/Complete Endgame Course.djvu", "djvu");
        Put("Silman, Jeremy/Complete Endgame Course.json",
            """{"title":"Silman's Complete Endgame Course","author":["Jeremy Silman"],"year":2008,"tags":["Endspiel"],"targetMinElo":800,"format":"epub"}""");
        Put("Kotov, Alexander/Think Like a Grandmaster.PDF", "pdf");
        Put("Kotov, Alexander/notes.txt", "kein Buch");
        Put(".stfolder/syncthing-folder.txt");
        Put("AAAnew/Blindfold Chess.epub", "epub");

        var first = await Importer.ImportAsync(admin.Id);

        Assert.True(first.Configured);
        Assert.Equal(4, first.FilesSeen);
        Assert.Equal(3, first.Added);
        Assert.Equal(4, first.FilesAdded);
        Assert.Equal(0, first.Skipped);

        var books = await _kit.Db.Ebooks.Include(b => b.Files).OrderBy(b => b.Title).ToListAsync();
        var blindfold = books.Single(b => b.Title == "Blindfold Chess");
        Assert.Equal(string.Empty, blindfold.Author);   // „AAAnew" ist kein Autor
        var kotov = books.Single(b => b.Title == "Think Like a Grandmaster");
        Assert.Equal("Alexander Kotov", kotov.Author);
        Assert.Equal("pdf", kotov.Files.Single().Format);
        var silman = books.Single(b => b.Title == "Silman's Complete Endgame Course");
        Assert.Equal("Jeremy Silman", silman.Author);
        Assert.Equal(2008, silman.Year);
        Assert.Equal("Endspiel", silman.Tags);
        Assert.Equal(800, silman.TargetMinElo);
        Assert.Equal(["djvu", "pdf"], silman.Files.Select(f => f.Format).Order());
        Assert.Equal("Silman, Jeremy/Complete Endgame Course.pdf", silman.Files.Single(f => f.Format == "pdf").RelativePath);
        Assert.All(books, b => Assert.Equal(EbookSource.Library, b.Source));
        Assert.All(books, b => Assert.Equal(admin.Id, b.OwnerUserId));
        Assert.All(books, b => Assert.Equal(EbookCoverState.Pending, b.CoverState));
        Assert.Empty(_kit.Db.EbookGroupAccesses);   // zunächst nur für Verwalter sichtbar

        // Zweiter Lauf: nichts Neues; ein neues Format neben einem bekannten Buch hängt sich an.
        Put("Kotov, Alexander/Think Like a Grandmaster.epub", "epub");
        var second = await Importer.ImportAsync(admin.Id);
        Assert.Equal(0, second.Added);
        Assert.Equal(1, second.FilesAdded);
        Assert.Equal(2, second.Skipped);
        Assert.Equal(3, await _kit.Db.Ebooks.CountAsync());
        Assert.Equal(["epub", "pdf"], (await _kit.Db.EbookFiles.Where(f => f.EbookId == kotov.Id).Select(f => f.Format).ToListAsync()).Order());
    }

    // d3-rk-reader-titel-ohne-ellipse: Punkte im Namen kosteten Titelende, Autor und Jahr; „Gemeinfrei“ war der Autor.
    [Fact]
    public async Task Import_CollectionFolderWithDotsInName_KeepsTitle_TakesAuthorFromSubfolder_AndYear()
    {
        var admin = await _kit.UserAsync("admin", admin: true);
        Put("Gemeinfrei/V. M. N. Portilla/Chess Problems [V. M. N. Portilla, 1890].pdf", "pdf");
        Put("Gemeinfrei/Georg Marco, Carl Schlechter/Karlsbad 1907 [Georg Marco, Carl Schlechter, 1907].pdf", "pdf");
        Put("Gemeinfrei/(Turnierbuch)/St. Petersburg 1914 [(Turnierbuch), 1914].pdf", "pdf");
        Put("Nimzowitsch, Aron/My System 2. Ed.pdf", "pdf");

        await Importer.ImportAsync(admin.Id);

        var books = await _kit.Db.Ebooks.ToListAsync();
        var portilla = books.Single(b => b.Title == "Chess Problems");
        Assert.Equal("V. M. N. Portilla", portilla.Author);
        Assert.Equal(1890, portilla.Year);
        var karlsbad = books.Single(b => b.Title == "Karlsbad 1907");
        Assert.Equal("Georg Marco, Carl Schlechter", karlsbad.Author);   // im Sammelordner wörtlich, nicht umgestellt
        Assert.Equal(1907, karlsbad.Year);
        var spb = books.Single(b => b.Title == "St. Petersburg 1914");
        Assert.Equal(string.Empty, spb.Author);
        var nimzo = books.Single(b => b.Author == "Aron Nimzowitsch");
        Assert.Equal("My System 2. Ed", nimzo.Title);   // Punkt im Namen bleibt
        Assert.Null(nimzo.Year);
        Assert.DoesNotContain(books, b => b.Author == "Gemeinfrei");
    }

    [Fact]
    public async Task Import_WithoutLibraryRoot_ReportsNotConfigured()
    {
        using var kit = new EbookTestKit(withLibrary: false);
        var importer = new EbookLibraryImporter(kit.Db, kit.Storage, NullLogger<EbookLibraryImporter>.Instance);
        var r = await importer.ImportAsync(1);
        Assert.False(r.Configured);
        Assert.Equal(0, r.Added);
    }

    [Fact]
    public void Sidecar_TakesAuthorAsStringOrList_AndIgnoresGarbage()
    {
        var s = EbookLibraryImporter.ParseSidecar("""{"title":" T ","author":"A, B","year":"1999","tags":["x",1]}""");
        Assert.Equal("T", s!.Title);
        Assert.Equal("A, B", s.Author);
        Assert.Null(s.Year);
        Assert.Equal(["x"], s.Tags);
        Assert.Null(EbookLibraryImporter.ParseSidecar("not json"));
        Assert.Null(EbookLibraryImporter.ParseSidecar("[1,2]"));
        Assert.Equal("Vorname Nachname", EbookLibraryImporter.AuthorFromFolder("Nachname, Vorname"));
        Assert.Equal("Pafu", EbookLibraryImporter.AuthorFromFolder("Pafu"));
        Assert.Equal(string.Empty, EbookLibraryImporter.AuthorFromFolder("AAAwant to read"));
        Assert.Equal(string.Empty, EbookLibraryImporter.AuthorFromFolder("Unknown"));
    }
}
