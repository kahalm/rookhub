using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.Engines;
using RookHub.Api.DTOs.Repertoires;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.Engines;

/// <summary>Meisterpartie des lokalen Explorers per Kennung (0.980.0): <c>GET /api/explorer/master-games/{id}</c>.</summary>
public class ExplorerMasterGameTests
{
    private const string Pgn = "[Event \"7. Sinquefield Cup 2019\"]\n[White \"Carlsen, Magnus\"]\n[Black \"Caruana, Fabiano\"]\n[Result \"1/2-1/2\"]\n\n1. d4 Nf6 2. c4 e6 1/2-1/2";

    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<string> Paths = [];
        public HttpStatusCode Status = HttpStatusCode.OK;
        public bool Throw;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (Throw) throw new HttpRequestException("weg");
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Status == HttpStatusCode.OK ? Pgn + "\n" : "") });
        }
    }

    private static LocalExplorerClient Client(Handler handler, bool configured = true) =>
        new(new HttpClient(handler) { BaseAddress = configured ? new Uri("http://local-explorer.test/") : null }, NullLogger<LocalExplorerClient>.Instance);

    private static ExplorerMasterGameController Controller(Handler handler, bool configured = true) =>
        new(Client(handler, configured), new MemoryCache(new MemoryCacheOptions()));

    [Theory]
    [InlineData("LZBtv84I", true)]
    [InlineData("00000000", true)]
    [InlineData("LZBtv84", false)]       // zu kurz
    [InlineData("LZBtv84Ia", false)]     // zu lang
    [InlineData("LZBt/84I", false)]      // Pfadtrenner
    [InlineData("LZBt.84I", false)]
    [InlineData("LZBt%2FI", false)]
    [InlineData("LZBtv84é", false)]      // kein ASCII
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsGameId_nurAchtZiffernOderBuchstaben(string? id, bool expected) =>
        Assert.Equal(expected, LocalExplorerClient.IsGameId(id));

    [Fact]
    public async Task Get_gefunden_liefertPgn_undFragtDenExplorerNurEinmal()
    {
        var handler = new Handler();
        var controller = Controller(handler);

        var first = Assert.IsType<OkObjectResult>((await controller.Get("LZBtv84I", default)).Result);
        var dto = Assert.IsType<ExplorerMasterGameDto>(first.Value);
        Assert.Equal(("LZBtv84I", Pgn), (dto.Id, dto.Pgn));
        Assert.IsType<OkObjectResult>((await controller.Get("LZBtv84I", default)).Result);

        Assert.Equal(["/masters/pgn/LZBtv84I"], handler.Paths);   // zweiter Aufruf aus dem Arbeitsspeicher
        Assert.Equal("/explorer/game/masters/LZBtv84I", LocalExplorerClient.MasterGamePath(dto.Id));
    }

    [Fact]
    public async Task Get_unbekannt_ist404_undWirdNichtGemerkt()
    {
        var handler = new Handler { Status = HttpStatusCode.NotFound };
        var controller = Controller(handler);

        Assert.IsType<NotFoundResult>((await controller.Get("XXXXXXXX", default)).Result);
        handler.Status = HttpStatusCode.OK;
        Assert.IsType<OkObjectResult>((await controller.Get("XXXXXXXX", default)).Result);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("../monitor")]
    [InlineData("LZBtv84I?x=1")]
    [InlineData("LZBtv84I/..")]
    public async Task Get_ungueltigeKennung_ist400_ohneAnfrageAnDenExplorer(string id)
    {
        var handler = new Handler();

        var result = (await Controller(handler).Get(id, default)).Result;

        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(result).StatusCode);
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task Get_ExplorerNichtErreichbarOderNichtEingerichtet_ist503()
    {
        var down = (await Controller(new Handler { Throw = true }).Get("LZBtv84I", default)).Result;
        Assert.Equal(503, Assert.IsType<ObjectResult>(down).StatusCode);
        var broken = (await Controller(new Handler { Status = HttpStatusCode.InternalServerError }).Get("LZBtv84I", default)).Result;
        Assert.Equal(503, Assert.IsType<ObjectResult>(broken).StatusCode);

        var handler = new Handler();
        var none = (await Controller(handler, configured: false).Get("LZBtv84I", default)).Result;
        Assert.Equal(503, Assert.IsType<ObjectResult>(none).StatusCode);
        Assert.Empty(handler.Paths);
    }
}
