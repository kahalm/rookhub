using Microsoft.EntityFrameworkCore;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Massenkorrektur von Diagrammstellungen (1.041.0): gleiche Logik wie „Stellung korrigieren“, aber Zugrecht aus
/// dem Auftrag, Buch nicht kuratiert, Buch einmal eingereiht, Protokoll mit Reason, Ergebnis je Eintrag, Probelauf.</summary>
public class EbookDiagramBulkPositionTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private const string Rotated = "R2KQBNR/PPP3PP/2N2P2/3pP3/3p1b2/5p2/pp3ppp/rn1kqb1r w - - 0 1";
    private const string Fixed = "r1bqk1nr/ppp3pp/2p5/2b1p3/3Pp3/2P2N2/PP3PPP/RNBQK2R w - - 0 1";

    private async Task<(AppUser Admin, Ebook Book, EbookDiagram A, EbookDiagram B)> SetupAsync()
    {
        var admin = await _kit.UserAsync("admin", admin: true);
        var book = await _kit.BookAsync("Silman", admin.Id);
        book.DiagramState = EbookDiagramState.Done; book.SolutionCheckState = EbookDiagramState.Done;
        var a = new EbookDiagram
        {
            EbookId = book.Id, Fen = Rotated, Page = 4, FontFamily = "cnn", Confidence = 0.9f, SideKnown = true,
            SolutionUci = "a1a2", SolutionSan = "Ra2", SolutionSource = "engine", SolutionCheck = EbookSolutionCheck.Engine, EngineEval = 120,
        };
        var b = new EbookDiagram { EbookId = book.Id, Fen = Rotated, Page = 5, SideKnown = true };
        _kit.Db.EbookDiagrams.AddRange(a, b);
        await _kit.Db.SaveChangesAsync();
        return (admin, book, a, b);
    }

    [Fact]
    public async Task Bulk_Corrects_SideFromRequest_NotCurated_BookQueuedOnce_ReasonLogged()
    {
        var (admin, book, a, b) = await SetupAsync();
        var r = await _kit.Service.BulkCorrectDiagramPositionsAsync(admin.Id, new EbookBulkPositionsDto
        {
            Reason = "bulk-rotation-180",
            Items =
            {
                new() { DiagramId = a.Id, Fen = Fixed, SideKnown = false },
                new() { DiagramId = b.Id, Fen = Fixed.Replace(" w ", " b "), SideKnown = true },
            },
        });

        Assert.Equal((2, 0, 0, 0), (r.Ok, r.Invalid, r.NotFound, r.Unchanged));
        Assert.Equal(new[] { book.Id }, r.RequeuedBooks);
        var sa = await _kit.Db.EbookDiagrams.SingleAsync(d => d.Id == a.Id);
        Assert.Equal(Fixed, sa.Fen);
        Assert.False(sa.SideKnown);
        Assert.Null(sa.SolutionUci);
        Assert.Null(sa.SolutionSource);
        Assert.Null(sa.EngineEval);
        Assert.Equal(EbookSolutionCheck.Unchecked, sa.SolutionCheck);
        Assert.True((await _kit.Db.EbookDiagrams.SingleAsync(d => d.Id == b.Id)).SideKnown);
        var corr = await _kit.Db.EbookDiagramCorrections.OrderBy(c => c.DiagramId).ToListAsync();
        Assert.Equal(2, corr.Count);
        Assert.All(corr, c => Assert.Equal(("bulk-rotation-180", Rotated, admin.Id), (c.Reason, c.OldFen, c.UserId)));
        Assert.Equal(("cnn", 0.9f), (corr[0].FontFamily, corr[0].Confidence));
        var bk = await _kit.Db.Ebooks.SingleAsync();
        Assert.False(bk.DiagramsCurated);
        Assert.Equal(EbookDiagramState.Pending, bk.SolutionCheckState);
        Assert.NotNull(bk.SolutionCheckQueuedAt);
    }

    [Fact]
    public async Task PerItemStatus_InvalidNotFoundUnchangedDuplicate()
    {
        var (admin, book, a, b) = await SetupAsync();
        var r = await _kit.Service.BulkCorrectDiagramPositionsAsync(admin.Id, new EbookBulkPositionsDto
        {
            Items =
            {
                new() { DiagramId = a.Id, Fen = "8/8/8/8/8/8/8/K7 w - - 0 1" },     // kein schwarzer König
                new() { DiagramId = 999_999, Fen = Fixed },
                new() { DiagramId = b.Id, Fen = Rotated, SideKnown = true },        // gleich
                new() { DiagramId = b.Id, Fen = Fixed, SideKnown = true },          // doppelt
            },
        });

        Assert.Equal(new[] { "invalid", "notfound", "unchanged", "invalid" }, r.Items.Select(i => i.Status));
        Assert.NotNull(r.Items[0].Error);
        Assert.Equal((0, 2, 1, 1), (r.Ok, r.Invalid, r.NotFound, r.Unchanged));
        Assert.Empty(r.RequeuedBooks);
        Assert.Empty(_kit.Db.EbookDiagramCorrections);
        Assert.All(await _kit.Db.EbookDiagrams.ToListAsync(), d => Assert.Equal(Rotated, d.Fen));
        Assert.Equal(EbookDiagramState.Done, (await _kit.Db.Ebooks.SingleAsync()).SolutionCheckState);
    }

    [Fact]
    public async Task DryRun_ReportsButSavesNothing()
    {
        var (admin, book, a, _) = await SetupAsync();
        var r = await _kit.Service.BulkCorrectDiagramPositionsAsync(admin.Id, new EbookBulkPositionsDto
        {
            DryRun = true, Reason = "bulk-rotation-180",
            Items = { new() { DiagramId = a.Id, Fen = Fixed, SideKnown = false } },
        });

        Assert.True(r.DryRun);
        Assert.Equal(1, r.Ok);
        Assert.Equal((Rotated, Fixed, book.Id), (r.Items[0].OldFen, r.Items[0].NewFen, r.Items[0].EbookId));
        Assert.Equal(new[] { book.Id }, r.RequeuedBooks);
        _kit.Db.ChangeTracker.Clear();
        var saved = await _kit.Db.EbookDiagrams.SingleAsync(d => d.Id == a.Id);
        Assert.Equal((Rotated, true, "a1a2"), (saved.Fen, saved.SideKnown, saved.SolutionUci));
        Assert.Empty(_kit.Db.EbookDiagramCorrections);
        Assert.Equal(EbookDiagramState.Done, (await _kit.Db.Ebooks.SingleAsync()).SolutionCheckState);
    }

    [Fact]
    public async Task TooManyItems_Rejected()
    {
        var dto = new EbookBulkPositionsDto();
        for (var i = 0; i <= EbookBulkPositionsDto.MaxItems; i++) dto.Items.Add(new() { DiagramId = i, Fen = Fixed });
        await Assert.ThrowsAsync<DomainValidationException>(() => _kit.Service.BulkCorrectDiagramPositionsAsync(1, dto));
    }
}
