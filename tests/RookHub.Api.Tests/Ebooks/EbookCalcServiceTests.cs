using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Eigene Kalkulation am E-Book-Diagramm im Konto (1.019.0) — vorher nur im localStorage, beim Abmelden weg.</summary>
public class EbookCalcServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EbookCalcService _svc;

    public EbookCalcServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _svc = new EbookCalcService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<(int user, int other, int ebook, int diagram, int foreignEbook, int foreignDiagram)> SeedAsync()
    {
        var u = new AppUser { Username = "a", PasswordHash = "h" };
        var o = new AppUser { Username = "b", PasswordHash = "h" };
        _db.AppUsers.AddRange(u, o);
        await _db.SaveChangesAsync();
        var mine = new Ebook { Title = "Mein", OwnerUserId = u.Id, Source = EbookSource.Upload, DiagramState = EbookDiagramState.Done };
        var foreign = new Ebook { Title = "Fremd", OwnerUserId = o.Id, Source = EbookSource.Upload, DiagramState = EbookDiagramState.Done };
        _db.Ebooks.AddRange(mine, foreign);
        await _db.SaveChangesAsync();
        var d1 = new EbookDiagram { EbookId = mine.Id, Page = 1, Fen = "8/8/8/8/8/8/8/K6k w - - 0 1" };
        var d2 = new EbookDiagram { EbookId = foreign.Id, Page = 1, Fen = "8/8/8/8/8/8/8/K6k w - - 0 1" };
        _db.EbookDiagrams.AddRange(d1, d2);
        await _db.SaveChangesAsync();
        return (u.Id, o.Id, mine.Id, d1.Id, foreign.Id, d2.Id);
    }

    [Fact]
    public async Task SetAndGet_OwnDiagram_RoundTrips_OlderDoesNotOverwrite()
    {
        var s = await SeedAsync();
        Assert.Null(await _svc.GetAsync(s.user, false, s.ebook, s.diagram));
        await _svc.SetAsync(s.user, false, s.ebook, s.diagram, new EbookCalcDto { Tree = "T2", At = 200 });
        var r = await _svc.SetAsync(s.user, false, s.ebook, s.diagram, new EbookCalcDto { Tree = "T1", At = 100 });
        Assert.Equal("T2", r.Tree);
        Assert.Equal("T2", (await _svc.GetAsync(s.user, false, s.ebook, s.diagram))!.Tree);
        // Ein anderer Nutzer sieht den Stand nicht (und darf das fremde Buch gar nicht).
        await Assert.ThrowsAsync<NotFoundException>(() => _svc.GetAsync(s.other, false, s.ebook, s.diagram));
    }

    [Fact]
    public async Task Set_ForeignOrMismatchedDiagram_NotFound_InvalidBody_400()
    {
        var s = await SeedAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => _svc.SetAsync(s.user, false, s.foreignEbook, s.foreignDiagram, new EbookCalcDto { Tree = "x", At = 1 }));
        await Assert.ThrowsAsync<NotFoundException>(() => _svc.SetAsync(s.user, false, s.ebook, s.foreignDiagram, new EbookCalcDto { Tree = "x", At = 1 }));
        await Assert.ThrowsAsync<DomainValidationException>(() => _svc.SetAsync(s.user, false, s.ebook, s.diagram,
            new EbookCalcDto { Tree = new string('x', EbookCalcService.MaxTreeLength + 1), At = 1 }));
        await Assert.ThrowsAsync<DomainValidationException>(() => _svc.SetAsync(s.user, false, s.ebook, s.diagram, new EbookCalcDto { Tree = "x", At = 0 }));
    }

    [Fact]
    public async Task SetMany_SkipsForeignAndInvalid()
    {
        var s = await SeedAsync();
        var n = await _svc.SetManyAsync(s.user, false, [
            new EbookCalcBulkItemDto { DiagramId = s.diagram, Tree = "B", At = 5 },
            new EbookCalcBulkItemDto { DiagramId = s.foreignDiagram, Tree = "F", At = 5 },
            new EbookCalcBulkItemDto { DiagramId = 99999, Tree = "X", At = 5 },
        ]);
        Assert.Equal(1, n);
        Assert.Equal("B", (await _svc.GetAsync(s.user, false, s.ebook, s.diagram))!.Tree);
        Assert.False(await _db.EbookDiagramCalcs.AnyAsync(c => c.DiagramId == s.foreignDiagram));
        await Assert.ThrowsAsync<DomainValidationException>(() => _svc.SetManyAsync(s.user, false,
            Enumerable.Range(0, EbookCalcService.MaxBulk + 1).Select(_ => new EbookCalcBulkItemDto { DiagramId = s.diagram, Tree = "x", At = 1 }).ToList()));
    }
}
