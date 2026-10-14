using Microsoft.EntityFrameworkCore;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookCourseBuilderTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private static EbookDiagram D(int page, string fen, string? label = null, string? san = null, string? caption = null, int ebookId = 0)
        => new() { EbookId = ebookId, Page = page, Fen = fen, Label = label, SolutionSan = san, Caption = caption, FontFamily = "opus" };

    [Fact]
    public void MoveText_NumbersMovesFromTheSideToMove()
    {
        Assert.Equal("1. Ra8+ Kd7", EbookCourseBuilder.MoveText("4k3/8/8/8/8/8/8/R3K3 w - - 0 1", "Ra8+ Kd7"));
        Assert.Equal("5... Kd7 6. Ra7+", EbookCourseBuilder.MoveText("4k3/8/8/8/8/8/8/R3K3 b - - 0 5", "Kd7 Ra7+"));
        Assert.Null(EbookCourseBuilder.MoveText("4k3/8/8/8/8/8/8/R3K3 w - - 0 1", "Qh5"));   // nicht spielbar
    }

    [Fact]
    public void BuildPgn_SolvedAreTasks_UnsolvedAreInfo_UnplayableDropped()
    {
        var r = EbookCourseBuilder.BuildPgn("Awerbach", [
            D(10, "4k3/8/8/8/8/8/8/R3K3 w - - 0 1", "1", "Ra8+", "Weiß am Zuge"),
            D(11, "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1", "2", null, "Remis"),
            D(12, "8/8/8/2P5/8/8/8/8 w - - 0 1", "3"),                       // Lehrbild ohne Könige
        ]);
        Assert.Equal(1, r.Puzzles);
        Assert.Equal(1, r.InfoLines);
        Assert.Contains("[White \"Diagramm 1\"]", r.Text);
        Assert.Contains("{Weiß am Zuge} 1. Ra8+", r.Text);
        Assert.Contains("{Remis} {[%info]}", r.Text);
        Assert.DoesNotContain("Diagramm 3", r.Text);
    }

    [Fact]
    public async Task CreateCourse_ImportsAPersonalCourseForTheCaller()
    {
        var owner = await _kit.UserAsync("owner");
        var book = new Ebook { Title = "Erfolg im Endspiel", OwnerUserId = owner.Id, Source = EbookSource.Upload, DiagramState = EbookDiagramState.Done, DiagramCount = 2 };
        _kit.Db.Ebooks.Add(book);
        await _kit.Db.SaveChangesAsync();
        _kit.Db.EbookDiagrams.AddRange(
            D(10, "4k3/8/8/8/8/8/8/R3K3 w - - 0 1", "1", "Ra8+", ebookId: book.Id),
            D(11, "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1", "2", null, "Remis", book.Id));
        await _kit.Db.SaveChangesAsync();

        var builder = new EbookCourseBuilder(_kit.Db, TestServices.Course(_kit.Db));
        var course = await builder.CreateCourseAsync((EbookCaller)owner.Id, false, book.Id);

        Assert.Equal("Erfolg im Endspiel", course.DisplayName);
        var lines = await _kit.Db.BookPuzzles.Where(p => p.BookId == course.BookId).OrderBy(p => p.Round).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.False(lines[0].IsInfoOnly);
        Assert.True(lines[1].IsInfoOnly);
    }

    [Fact]
    public async Task CreateCourse_WithoutAnySolution_IsRejected()
    {
        var owner = await _kit.UserAsync("owner");
        var book = new Ebook { Title = "Ohne", OwnerUserId = owner.Id, Source = EbookSource.Upload, DiagramState = EbookDiagramState.Done, DiagramCount = 1 };
        _kit.Db.Ebooks.Add(book);
        await _kit.Db.SaveChangesAsync();
        _kit.Db.EbookDiagrams.Add(D(1, "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1", "1", ebookId: book.Id));
        await _kit.Db.SaveChangesAsync();

        var builder = new EbookCourseBuilder(_kit.Db, TestServices.Course(_kit.Db));
        await Assert.ThrowsAsync<DomainValidationException>(() => builder.CreateCourseAsync((EbookCaller)owner.Id, false, book.Id));
    }
}
