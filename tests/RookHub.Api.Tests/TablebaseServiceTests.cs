using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Endspiel-Datenbank über Lichess (0.729.0): nur bis 7 Steine, Zwischenspeicher, Pause nach 429, Zug-Ergebnisse
/// aus Sicht der ziehenden Seite. Lichess selbst wird nie gefragt — eine Attrappe antwortet.</summary>
public class TablebaseServiceTests
{
    // Weiß: Kc2, Bauern b2 c3; Schwarz: Ke8, Bauern e3 f2 g2 — Weiß am Zug verliert (Beispiel von tablebase.lichess.ovh).
    private const string SevenMen = "4k3/8/8/8/8/2P1p3/1PK2pp1/8 w - - 0 1";
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private const string LichessAnswer = """
        {"checkmate":false,"stalemate":false,"insufficient_material":false,"dtz":-2,"dtm":null,"category":"loss",
         "moves":[{"uci":"c2b1","san":"Kb1","zeroing":false,"checkmate":false,"stalemate":false,"dtz":1,"dtm":null,"category":"win"},
                  {"uci":"c3c4","san":"c4","zeroing":true,"checkmate":false,"stalemate":false,"dtz":0,"dtm":null,"category":"draw"},
                  {"uci":"b2b4","san":"b4","zeroing":true,"checkmate":false,"stalemate":false,"dtz":-3,"dtm":null,"category":"cursed-win"}]}
        """;

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Calls;
        public string? LastUrl;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUrl = request.RequestUri!.ToString();
            return Task.FromResult(answer(request));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri(TablebaseService.DefaultBaseUrl) };
    }

    private static (TablebaseService Svc, FakeHandler Handler) Make(HttpStatusCode code = HttpStatusCode.OK, string body = LichessAnswer,
        TablebaseGate? gate = null, Func<DateTime>? now = null)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(code) { Content = new StringContent(body) });
        var svc = new TablebaseService(gate ?? new TablebaseGate(), new Factory(handler), new MemoryCache(new MemoryCacheOptions()),
            NullLogger<TablebaseService>.Instance, now);
        return (svc, handler);
    }

    [Fact]
    public void PieceCount_zaehltAlleSteine()
    {
        Assert.Equal(7, TablebaseService.PieceCount(SevenMen));
        Assert.Equal(32, TablebaseService.PieceCount(Start));
        Assert.Equal(-1, TablebaseService.PieceCount("kaputt"));
    }

    [Fact]
    public async Task Lookup_liefertErgebnis_undDrehtDieZuegeAufDieSichtDesZiehenden()
    {
        var (svc, handler) = Make();
        var r = await svc.LookupAsync(SevenMen);

        Assert.Equal("ok", r.Status);
        Assert.Equal("loss", r.Category);
        Assert.Equal(-2, r.Dtz);
        Assert.Contains("fen=4k3%2F8%2F8", handler.LastUrl);
        Assert.Equal(new[] { "Kb1", "c4", "b4" }, r.Moves.Select(m => m.San));
        // Lichess meldet die Stellung NACH dem Zug (Gegner am Zug) — für Weiß ist Kb1 also ein Verlust
        Assert.Equal(new[] { "loss", "draw", "blessed-loss" }, r.Moves.Select(m => m.Category));
        Assert.Equal(new int?[] { -1, 0, 3 }, r.Moves.Select(m => m.Dtz));
        Assert.True(r.Moves[1].Zeroing);
    }

    [Fact]
    public async Task Lookup_mehrAlsSiebenSteine_oderUnlesbar_fragtLichessGarNicht()
    {
        var (svc, handler) = Make();
        Assert.Equal("tooManyPieces", (await svc.LookupAsync(Start)).Status);
        Assert.Equal("invalid", (await svc.LookupAsync("8/8/8/8 w - - 0 1")).Status);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Lookup_zweimalDieselbeStellung_einmalGefragt_auchMitAnderemZugzaehler()
    {
        var (svc, handler) = Make();
        await svc.LookupAsync(SevenMen);
        await svc.LookupAsync(SevenMen.Replace(" 0 1", " 0 37"));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Lookup_429_pausiert_undFragtWaehrendDerPauseNicht()
    {
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var gate = new TablebaseGate();
        var (svc, handler) = Make(HttpStatusCode.TooManyRequests, "{}", gate, () => now);

        Assert.Equal("rateLimited", (await svc.LookupAsync(SevenMen)).Status);
        Assert.Equal("rateLimited", (await svc.LookupAsync("4k3/8/8/8/8/2P5/1PK2pp1/8 w - - 0 1")).Status);
        Assert.Equal(1, handler.Calls);                      // die zweite Stellung ging gar nicht erst raus
        now += TablebaseService.RateLimitPause + TimeSpan.FromSeconds(1);
        await svc.LookupAsync(SevenMen);
        Assert.Equal(2, handler.Calls);                      // nach der Pause wieder
    }

    [Fact]
    public async Task Lookup_LichessAntwortetNicht_unavailable_undNichtsGespeichert()
    {
        var (svc, handler) = Make(HttpStatusCode.BadGateway, "");
        Assert.Equal("unavailable", (await svc.LookupAsync(SevenMen)).Status);
        Assert.Equal("unavailable", (await svc.LookupAsync(SevenMen)).Status);
        Assert.Equal(2, handler.Calls);                      // ein Fehler wird nicht zwischengespeichert
    }

    [Theory]
    [InlineData("win", "loss")]
    [InlineData("cursed-win", "blessed-loss")]
    [InlineData("maybe-loss", "maybe-win")]
    [InlineData("draw", "draw")]
    [InlineData("unknown", "unknown")]
    public void Flip_drehtDasErgebnis(string from, string to) => Assert.Equal(to, TablebaseService.Flip(from));
}
