using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Gemeinsamer Aufbau der E-Book-Tests: InMemory-DB, zwei Temp-Wurzeln (Uploads + Bibliothek), Dienst.</summary>
public sealed class EbookTestKit : IDisposable
{
    public AppDbContext Db { get; }
    public string Root { get; }
    public string LibraryRoot { get; }
    public EbookOptions Options { get; }
    public EbookStorage Storage { get; }
    public EbookService Service { get; }

    public EbookTestKit(bool withLibrary = true, long? quota = null, long? maxFile = null)
    {
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Root = Path.Combine(Path.GetTempPath(), "rh-ebooks-" + Guid.NewGuid().ToString("N"));
        LibraryRoot = Path.Combine(Path.GetTempPath(), "rh-ebooks-lib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        if (withLibrary) Directory.CreateDirectory(LibraryRoot);
        Options = new EbookOptions { Root = Root, LibraryRoot = withLibrary ? LibraryRoot : null };
        if (quota != null) Options.UserQuotaBytes = quota.Value;
        if (maxFile != null) Options.MaxFileBytes = maxFile.Value;
        Storage = new EbookStorage(Microsoft.Extensions.Options.Options.Create(Options), NullLogger<EbookStorage>.Instance);
        Service = new EbookService(Db, Storage, NullLogger<EbookService>.Instance, Microsoft.Extensions.Options.Options.Create(Options));
    }

    public async Task<AppUser> UserAsync(string name, bool admin = false)
    {
        var u = new AppUser { Username = name, PasswordHash = "x", IsAdmin = admin, CreatedAt = DateTime.UtcNow };
        Db.AppUsers.Add(u);
        await Db.SaveChangesAsync();
        return u;
    }

    public async Task<Group> GroupAsync(string name, bool everyone = false, params int[] memberIds)
    {
        var g = new Group { Name = name, IsEveryone = everyone, CreatedAt = DateTime.UtcNow };
        Db.Groups.Add(g);
        await Db.SaveChangesAsync();
        foreach (var id in memberIds) Db.UserGroups.Add(new UserGroup { UserId = id, GroupId = g.Id });
        await Db.SaveChangesAsync();
        return g;
    }

    /// <summary>Ein Buch direkt in der DB (ohne Platte), optional mit Freigaben.</summary>
    public async Task<Ebook> BookAsync(string title, int? ownerId, string format = "pdf", string? tags = null, params int[] groupIds)
    {
        var b = new Ebook
        {
            Title = title, OwnerUserId = ownerId, Tags = tags ?? string.Empty, Source = EbookSource.Upload,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        b.Files.Add(new EbookFile { Format = format, RelativePath = $"files/{Guid.NewGuid():N}.{format}", FileName = title + "." + format, SizeBytes = 10 });
        Db.Ebooks.Add(b);
        await Db.SaveChangesAsync();
        foreach (var g in groupIds) Db.EbookGroupAccesses.Add(new EbookGroupAccess { EbookId = b.Id, GroupId = g });
        await Db.SaveChangesAsync();
        return b;
    }

    /// <summary>Eine kleine, gültige PDF (eine leere Seite) — reicht pdftoppm/pdfinfo und dem Format-Check.</summary>
    public static byte[] TinyPdf() => System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 300]>>endobj\nxref\n0 4\n0000000000 65535 f \n" +
        "0000000009 00000 n \n0000000052 00000 n \n0000000101 00000 n \ntrailer<</Size 4/Root 1 0 R>>\nstartxref\n166\n%%EOF\n");

    public Task<RookHub.Api.DTOs.Ebooks.EbookDto> UploadAsync(int userId, bool manageAll, byte[] bytes, string fileName,
        RookHub.Api.DTOs.Ebooks.EbookMetaDto? meta = null)
        => Service.UploadAsync(userId, manageAll, new MemoryStream(bytes), fileName, bytes.Length, meta ?? new(), CancellationToken.None);

    public void Dispose()
    {
        Db.Dispose();
        try { Directory.Delete(Root, recursive: true); } catch { /* egal */ }
        try { if (Directory.Exists(LibraryRoot)) Directory.Delete(LibraryRoot, recursive: true); } catch { /* egal */ }
    }
}
