using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers.Ebooks;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Eröffnungsbaum eines Buchs (0.881.0): setzen, lesen (Zugriff wie Diagramme), löschen, prüfen, Brett-Modus „tree“.</summary>
public class EbookTreeTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private EbookTreeService Svc => new(_kit.Db);

    private static EbookTreeDto Body(string json) =>
        JsonSerializer.Deserialize<EbookTreeDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private const string Danish = """{"nodes":[[0,"e4"],[1,"e5"],[2,"d4"],[3,"exd4"],[4,"c3"],[5,"dxc3"],[4,"Nf3"]]}""";

    [Fact]
    public async Task Set_Get_Replace_Delete_RoundTrip()
    {
        var owner = await _kit.UserAsync("owner");
        var book = await _kit.BookAsync("Danish Dynamite", owner.Id);

        Assert.Equal(7, await Svc.SetAsync(book.Id, Body(Danish)));
        Assert.Equal("""[[0,"e4"],[1,"e5"],[2,"d4"],[3,"exd4"],[4,"c3"],[5,"dxc3"],[4,"Nf3"]]""",
            await Svc.GetNodesJsonAsync(owner.Id, false, book.Id));
        Assert.True((await _kit.Service.GetAsync(owner.Id, false, book.Id)).HasTree);

        Assert.Equal(1, await Svc.SetAsync(book.Id, Body("""{"nodes":[[0," d4 "]]}""")));
        Assert.Equal("""[[0,"d4"]]""", await Svc.GetNodesJsonAsync(owner.Id, false, book.Id));
        Assert.Single(await _kit.Db.EbookTrees.ToListAsync());

        await Svc.DeleteAsync(book.Id);
        await Svc.DeleteAsync(book.Id);   // idempotent
        await Assert.ThrowsAsync<NotFoundException>(() => Svc.GetNodesJsonAsync(owner.Id, false, book.Id));
        Assert.False((await _kit.Service.GetAsync(owner.Id, false, book.Id)).HasTree);
    }

    [Theory]
    [InlineData("""{"nodes":[]}""")]
    [InlineData("""{}""")]
    [InlineData("""{"nodes":[[1,"e4"]]}""")]                   // Eltern = eigener Knoten
    [InlineData("""{"nodes":[[0,"e4"],[3,"e5"]]}""")]          // Eltern liegt dahinter
    [InlineData("""{"nodes":[[-1,"e4"]]}""")]
    [InlineData("""{"nodes":[[0,""]]}""")]
    [InlineData("""{"nodes":[[0,"Nbd7xe5+abc"]]}""")]          // > 10 Zeichen
    [InlineData("""{"nodes":[[0]]}""")]
    [InlineData("""{"nodes":[["0","e4"]]}""")]
    [InlineData("""{"nodes":[[0,5]]}""")]
    public async Task Set_InvalidBody_Throws400WithCode(string json)
    {
        var book = await _kit.BookAsync("B", null);
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => Svc.SetAsync(book.Id, Body(json)));
        Assert.Equal(ApiErrorCodes.EbookTreeInvalid, ex.Code);
        Assert.Empty(await _kit.Db.EbookTrees.ToListAsync());
    }

    [Fact]
    public async Task Set_TooManyNodes_Throws()
    {
        var book = await _kit.BookAsync("B", null);
        var e4 = JsonDocument.Parse("""[0,"e4"]""").RootElement.EnumerateArray().ToList();
        var dto = new EbookTreeDto { Nodes = Enumerable.Repeat(e4, EbookTreeService.MaxNodes + 1).ToList() };
        await Assert.ThrowsAsync<DomainValidationException>(() => Svc.SetAsync(book.Id, dto));
    }

    [Fact]
    public async Task Set_UnknownBook_404()
        => await Assert.ThrowsAsync<NotFoundException>(() => Svc.SetAsync(4711, Body(Danish)));

    [Fact]
    public async Task Get_InvisibleBook_404_EvenWithTree_AnonymousSeesPublicBook()
    {
        var owner = await _kit.UserAsync("owner");
        var stranger = await _kit.UserAsync("stranger");
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);
        var priv = await _kit.BookAsync("Private", owner.Id);
        var pub = await _kit.BookAsync("Public", owner.Id, groupIds: everyone.Id);
        await Svc.SetAsync(priv.Id, Body(Danish));
        await Svc.SetAsync(pub.Id, Body(Danish));

        await Assert.ThrowsAsync<NotFoundException>(() => Svc.GetNodesJsonAsync(stranger.Id, false, priv.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc.GetNodesJsonAsync(EbookCaller.Anonymous("0123456789abcdef0123456789abcdef"), false, priv.Id));
        Assert.StartsWith("[[0,", await Svc.GetNodesJsonAsync(EbookCaller.Anonymous("0123456789abcdef0123456789abcdef"), false, pub.Id));
        Assert.StartsWith("[[0,", await Svc.GetNodesJsonAsync(stranger.Id, false, pub.Id));
    }

    [Fact]
    public async Task Controller_GetTree_ReturnsNodesObject()
    {
        var everyone = await _kit.GroupAsync("Everyone", everyone: true);
        var book = await _kit.BookAsync("Public", null, groupIds: everyone.Id);
        await Svc.SetAsync(book.Id, Body(Danish));
        var c = new EbooksController(_kit.Service, new EbookAccessTokens(new EphemeralDataProtectionProvider()), _kit.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) } },
        };

        var result = Assert.IsType<ContentResult>(await c.GetTree(book.Id, Svc, CancellationToken.None));

        Assert.Equal("application/json", result.ContentType);
        var parsed = Body(result.Content!);
        Assert.Equal(7, parsed.Nodes!.Count);
        Assert.Equal("dxc3", parsed.Nodes[5][1].GetString());
    }

    [Fact]
    public async Task DeleteBook_RemovesTree()
    {
        var owner = await _kit.UserAsync("owner");
        var book = await _kit.BookAsync("B", owner.Id);
        await Svc.SetAsync(book.Id, Body(Danish));
        await _kit.Service.DeleteAsync(owner.Id, false, book.Id);
        Assert.Empty(await _kit.Db.EbookTrees.ToListAsync());
    }

    [Fact]
    public async Task DefaultBoardMode_TreeIsAccepted()
    {
        var owner = await _kit.UserAsync("owner");
        var book = await _kit.BookAsync("B", owner.Id);
        Assert.Equal("tree", (await _kit.Service.UpdateAsync(owner.Id, false, book.Id, new EbookMetaDto { DefaultBoardMode = "tree" })).DefaultBoardMode);
    }
}
