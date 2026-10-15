using Microsoft.EntityFrameworkCore;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>„Stellung korrigieren“ im ⋮ des Bretts: neue FEN, Protokoll in EbookDiagramCorrections, Buch kuratiert.</summary>
public class EbookDiagramCorrectionTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private const string OldFen = "8/8/8/8/8/8/8/K6k w - - 0 1";

    private async Task<(AppUser Owner, Ebook Book, EbookDiagram Diagram)> SetupAsync(EbookDiagram? diagram = null)
    {
        var owner = await _kit.UserAsync("owner");
        var book = await _kit.BookAsync("Silman", owner.Id);
        book.DiagramState = EbookDiagramState.Done; book.DiagramCount = 1; book.SolutionCheckState = EbookDiagramState.Done;
        var d = diagram ?? new EbookDiagram { Fen = OldFen, Page = 7, FontFamily = "cnn", Confidence = 0.62f };
        d.EbookId = book.Id;
        _kit.Db.EbookDiagrams.Add(d);
        await _kit.Db.SaveChangesAsync();
        return (owner, book, d);
    }

    [Fact]
    public async Task Owner_CorrectsPosition_RecordedWithFontAndConfidence_BookCurated_CheckPending()
    {
        var (owner, book, d) = await SetupAsync();

        var dto = await _kit.Service.CorrectDiagramPositionAsync(owner.Id, false, book.Id, d.Id, "4k3/8/8/8/8/8/4P3/4K3 b");

        Assert.Equal("4k3/8/8/8/8/8/4P3/4K3 b - - 0 1", dto.Fen);
        var saved = await _kit.Db.EbookDiagrams.SingleAsync();
        Assert.Equal(dto.Fen, saved.Fen);
        Assert.True(saved.SideKnown);
        var c = await _kit.Db.EbookDiagramCorrections.SingleAsync();
        Assert.Equal((book.Id, d.Id, owner.Id, 7, OldFen, dto.Fen, "cnn", 0.62f),
            (c.EbookId, c.DiagramId, c.UserId, c.Page, c.OldFen, c.NewFen, c.FontFamily, c.Confidence));
        var b = await _kit.Db.Ebooks.SingleAsync();
        Assert.True(b.DiagramsCurated);
        Assert.Equal(EbookDiagramState.Pending, b.SolutionCheckState);
    }

    [Fact]
    public async Task EngineSolution_IsDropped_OnNewPosition()
    {
        var (owner, book, d) = await SetupAsync(new EbookDiagram
        {
            Fen = OldFen, Page = 3, SolutionUci = "a1a2", SolutionSan = "Ka2", SolutionSource = "engine",
            SolutionCheck = EbookSolutionCheck.Engine, EngineEval = 0,
        });
        await _kit.Service.CorrectDiagramPositionAsync(owner.Id, false, book.Id, d.Id, "8/8/8/8/8/8/8/K5k1 w - - 0 1");
        var saved = await _kit.Db.EbookDiagrams.SingleAsync();
        Assert.Null(saved.SolutionUci);
        Assert.Null(saved.SolutionSource);
        Assert.Equal(EbookSolutionCheck.Unchecked, saved.SolutionCheck);
        Assert.Null(saved.EngineEval);
    }

    [Fact]
    public async Task OtherReader_MayNotCorrect_EvenIfVisible()
    {
        var (owner, book, d) = await SetupAsync();
        var reader = await _kit.UserAsync("reader");
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);
        _kit.Db.EbookGroupAccesses.Add(new EbookGroupAccess { EbookId = book.Id, GroupId = everyone.Id });
        await _kit.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _kit.Service.CorrectDiagramPositionAsync(reader.Id, false, book.Id, d.Id, "4k3/8/8/8/8/8/8/4K3 w - - 0 1"));
        Assert.Empty(_kit.Db.EbookDiagramCorrections);
        Assert.Equal(OldFen, (await _kit.Db.EbookDiagrams.SingleAsync()).Fen);

        // ebooks.manage darf alles.
        await _kit.Service.CorrectDiagramPositionAsync(reader.Id, true, book.Id, d.Id, "4k3/8/8/8/8/8/8/4K3 w - - 0 1");
        Assert.Single(_kit.Db.EbookDiagramCorrections);
    }

    [Theory]
    [InlineData("8/8/8/8/8/8/8/K7 w - - 0 1")]            // schwarzer König fehlt
    [InlineData("8/8/8/8/8/8/K6k w - - 0 1")]             // 7 Reihen
    [InlineData("8/8/8/8/8/8/8/K6kk w - - 0 1")]          // 9 Felder
    [InlineData("8/8/8/8/8/8/8/K6k x - - 0 1")]           // Zugrecht
    [InlineData("8/8/8/8/8/8/8/KK5k w - - 0 1")]          // zwei weiße Könige
    public async Task InvalidFen_IsRejected_NothingRecorded(string fen)
    {
        var (owner, book, d) = await SetupAsync();
        await Assert.ThrowsAsync<DomainValidationException>(() => _kit.Service.CorrectDiagramPositionAsync(owner.Id, false, book.Id, d.Id, fen));
        Assert.Empty(_kit.Db.EbookDiagramCorrections);
        Assert.False((await _kit.Db.Ebooks.SingleAsync()).DiagramsCurated);
    }

    [Fact]
    public async Task UnknownDiagram_404_AndCorrectionsGoWithTheBook()
    {
        var (owner, book, d) = await SetupAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => _kit.Service.CorrectDiagramPositionAsync(owner.Id, false, book.Id, d.Id + 99, OldFen));
        await _kit.Service.CorrectDiagramPositionAsync(owner.Id, false, book.Id, d.Id, "8/8/8/8/8/8/8/K5k1 w - - 0 1");
        await _kit.Service.DeleteAsync(owner.Id, false, book.Id);
        Assert.Empty(_kit.Db.EbookDiagramCorrections);
    }
}
