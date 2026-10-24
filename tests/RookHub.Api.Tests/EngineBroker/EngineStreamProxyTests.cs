using RookHub.Api.DTOs.EngineBroker;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests.EngineBroker;

/// <summary>Die reinen Teile des Live-Proxys (Zerlegung Z12): Prüfen der Anfrage, genau EIN Limit, Klemmen auf die
/// Engine-Maxima, und die Maske des Tokens aus <see cref="EngineSettingsService"/>.</summary>
public class EngineStreamProxyTests
{
    private static EngineAnalyseRequest Valid() => new()
    {
        SessionId = "s1", InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", MultiPv = 3, Depth = 20,
    };

    private static EngineRef Engine(int threads, int hash) => new("eei_x", "X", threads, hash, EngineSource.Lichess);

    [Fact]
    public void ValidateWork_nimmtEineGueltigeAnfrage() => Assert.Null(EngineStreamProxy.ValidateWork(Valid()));

    [Fact]
    public void ValidateWork_lehntAbWasDerControllerFrueherAblehnte()
    {
        Assert.Equal("Request body is required", EngineStreamProxy.ValidateWork(null));
        var r = Valid(); r.SessionId = new string('x', 65);
        Assert.Equal("Invalid sessionId", EngineStreamProxy.ValidateWork(r));
        r = Valid(); r.Movetime = 1000;
        Assert.Equal("Exactly one of depth/movetime/nodes is required", EngineStreamProxy.ValidateWork(r));
        r = Valid(); r.Depth = 0;
        Assert.Equal("Invalid depth", EngineStreamProxy.ValidateWork(r));
        r = Valid(); r.Moves = ["e2e4", "toolong"];
        Assert.Equal("Invalid move", EngineStreamProxy.ValidateWork(r));
    }

    [Fact]
    public void WorkFor_klemmtThreadsUndHashAufDieEngine_undSchicktGenauEinLimit()
    {
        var r = Valid(); r.Threads = 64; r.Hash = 99_999;
        var w = EngineStreamProxy.WorkFor(r, Engine(threads: 8, hash: 512));
        Assert.Equal(8, w.Threads);
        Assert.Equal(512, w.Hash);
        Assert.Equal(20, w.Depth);
        Assert.Null(w.Movetime);
        Assert.Null(w.Nodes);

        var defaults = EngineStreamProxy.WorkFor(Valid(), Engine(threads: 0, hash: 0));   // kaputte Maxima → 1
        Assert.Equal(1, defaults.Threads);
        Assert.Equal(1, defaults.Hash);
    }

    [Theory]
    [InlineData("abc", "***")]
    [InlineData("lip_1234567890", "**********7890")]
    [InlineData("lip_abcdefghijklmnopqrstuvwxyz0123", "********************0123")]
    public void Mask_zeigtNurDieLetztenVierZeichen(string token, string expected)
        => Assert.Equal(expected, EngineSettingsService.Mask(token));
}
