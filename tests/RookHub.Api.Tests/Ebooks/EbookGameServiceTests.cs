using Microsoft.EntityFrameworkCore;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Partiensammlungen (0.831.0): Partien zu Diagrammen setzen und im Modus „Partie“ lesen.</summary>
public class EbookGameServiceTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private const string Moves = "e4 e5 Nf3 Nc6 Bb5 a6";

    private async Task<(AppUser Owner, Ebook Book, EbookDiagram A, EbookDiagram B)> SetupAsync()
    {
        var owner = await _kit.UserAsync("owner");
        var book = await _kit.BookAsync("Spassky's Best Games", owner.Id);
        book.DiagramState = EbookDiagramState.Done;
        var a = new EbookDiagram { EbookId = book.Id, Page = 3, FontFamily = "cnn", Fen = "r1bqkbnr/pppp1ppp/2n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3" };
        var b = new EbookDiagram { EbookId = book.Id, Page = 4, FontFamily = "cnn", Fen = "r1bqkbnr/1ppp1ppp/p1n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 4" };
        _kit.Db.EbookDiagrams.AddRange(a, b);
        await _kit.Db.SaveChangesAsync();
        return (owner, book, a, b);
    }

    private static EbookGamesImportDto Import(params EbookGameLinkItem[] links) => new()
    {
        Games = [new EbookGameImportItem { Key = "g1", White = "Spassky, Boris", Black = "Tal, Mikhail", Event = "Riga", Date = "1958", Result = "1/2-1/2", Moves = Moves, Source = "prep:42" }],
        Links = links.ToList(),
    };

    [Fact]
    public async Task Import_LinksDiagramsToOneSharedGame_ReaderGetsGameAtPly_AndDiagramListFlagsIt()
    {
        var (owner, book, a, b) = await SetupAsync();
        var svc = new EbookGameService(_kit.Db);

        var r = await svc.ImportAsync(book.Id, Import(new() { DiagramId = a.Id, GameKey = "g1", Ply = 5 }, new() { DiagramId = b.Id, GameKey = "g1", Ply = 6 }));

        Assert.Equal(1, r.Games);
        Assert.Equal(2, r.Linked);
        Assert.Equal(0, r.Skipped);
        Assert.Single(await _kit.Db.EbookGames.ToListAsync());
        var game = await svc.GetForDiagramAsync(owner.Id, false, book.Id, b.Id);
        Assert.Equal(["e4", "e5", "Nf3", "Nc6", "Bb5", "a6"], game.Moves);
        Assert.Equal(6, game.Ply);
        Assert.Equal("Spassky, Boris", game.White);
        Assert.Equal("1958", game.Date);
        var list = await _kit.Service.GetDiagramsAsync(owner.Id, false, book.Id);
        Assert.All(list.Items, d => Assert.True(d.HasGame));
    }

    [Fact]
    public async Task Import_SkipsForeignDiagram_UnknownGame_AndPlyOutsideGame_UnusedGameNotStored()
    {
        var (_, book, a, _) = await SetupAsync();
        var other = await _kit.BookAsync("Other", null);
        var foreign = new EbookDiagram { EbookId = other.Id, Page = 1, Fen = "8/8/8/8/8/8/8/K6k w - - 0 1" };
        _kit.Db.EbookDiagrams.Add(foreign);
        await _kit.Db.SaveChangesAsync();
        var svc = new EbookGameService(_kit.Db);

        var r = await svc.ImportAsync(book.Id, Import(
            new() { DiagramId = foreign.Id, GameKey = "g1", Ply = 2 },
            new() { DiagramId = a.Id, GameKey = "nope", Ply = 2 },
            new() { DiagramId = a.Id, GameKey = "g1", Ply = 7 }));

        Assert.Equal(3, r.Skipped);
        Assert.Equal(0, r.Linked);
        Assert.Empty(await _kit.Db.EbookGames.ToListAsync());
        Assert.Null((await _kit.Db.EbookDiagrams.FindAsync(foreign.Id))!.GameId);
    }

    [Fact]
    public async Task Import_Replace_DropsOldGamesAndLinks()
    {
        var (owner, book, a, b) = await SetupAsync();
        var svc = new EbookGameService(_kit.Db);
        await svc.ImportAsync(book.Id, Import(new() { DiagramId = a.Id, GameKey = "g1", Ply = 5 }, new() { DiagramId = b.Id, GameKey = "g1", Ply = 6 }));

        var dto = Import(new EbookGameLinkItem { DiagramId = b.Id, GameKey = "g1", Ply = 6 });
        dto.Replace = true;
        await svc.ImportAsync(book.Id, dto);

        Assert.Single(await _kit.Db.EbookGames.ToListAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetForDiagramAsync(owner.Id, false, book.Id, a.Id));
        Assert.Equal(6, (await svc.GetForDiagramAsync(owner.Id, false, book.Id, b.Id)).Ply);
    }

    [Fact]
    public async Task GetForDiagram_InvisibleBook_IsNotFound()
    {
        var (_, book, a, _) = await SetupAsync();
        var stranger = await _kit.UserAsync("stranger");
        var svc = new EbookGameService(_kit.Db);
        await svc.ImportAsync(book.Id, Import(new EbookGameLinkItem { DiagramId = a.Id, GameKey = "g1", Ply = 5 }));

        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetForDiagramAsync(stranger.Id, false, book.Id, a.Id));
    }

    [Fact]
    public async Task DeleteBook_RemovesItsGames()
    {
        var (owner, book, a, _) = await SetupAsync();
        await new EbookGameService(_kit.Db).ImportAsync(book.Id, Import(new EbookGameLinkItem { DiagramId = a.Id, GameKey = "g1", Ply = 5 }));

        await _kit.Service.DeleteAsync(owner.Id, false, book.Id);

        Assert.Empty(await _kit.Db.EbookGames.ToListAsync());
    }

    [Fact]
    public void BoardModes_IncludeGame()
    {
        Assert.Equal("game", EbookFormats.NormalizeBoardMode("Game"));
    }
}
