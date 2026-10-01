using RookHub.Api.Services;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Grenzen der External-Engine-Anbindung stehen nur in <see cref="EngineProtocol"/>; jeder Riegel
/// (Live-Pfad, Aufträge, eigener Broker) folgt ihnen. Vorher standen 5/60/600 an neun Stellen, drei davon als
/// nackte Literale — wer nur eine anhob, bekam im Worker still das alte Maximum und im Parser einen Fehler.
/// </summary>
public class EngineProtocolTests
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Fact]
    public void AllPublicLimits_FollowTheOneSource()
    {
        Assert.Equal(EngineProtocol.MaxMultiPv, AnalysisJobService.MaxMultiPv);
        Assert.Equal(EngineProtocol.MaxMultiPv, WorkSanitizer.MaxMultiPv);
        Assert.Equal(EngineProtocol.MaxDepth, AnalysisJobService.MaxDepth);
        Assert.Equal(EngineProtocol.MaxMoves, WorkSanitizer.MaxMoves);
    }

    [Fact]
    public void Protocol_ValuesAndErrorText_Unchanged()
    {
        // lila-engine: multiPv 1..5, höchstens 600 Züge; der Fehlertext ist wortgleich mit dem des Brokers.
        Assert.Equal(5, EngineProtocol.MaxMultiPv);
        Assert.Equal(60, EngineProtocol.MaxDepth);
        Assert.Equal(600, EngineProtocol.MaxMoves);
        Assert.Equal("invalid multipv: supported range is 1 to 5", EngineProtocol.MultiPvRangeError);
    }

    [Fact]
    public void UciLineParser_AcceptsUpToTheMaximum_RejectsOneMore()
    {
        var ok = UciLineParser.Parse($"info multipv {EngineProtocol.MaxMultiPv}");
        Assert.Equal(UciLineKind.Info, ok.Kind);
        Assert.Equal(EngineProtocol.MaxMultiPv, ok.Info!.MultiPv);
        var tooMany = UciLineParser.Parse($"info multipv {EngineProtocol.MaxMultiPv + 1}");
        Assert.Equal(UciLineKind.Error, tooMany.Kind);
        Assert.Equal(EngineProtocol.MultiPvRangeError, tooMany.Error);
    }

    [Fact]
    public void WorkSanitizer_RejectsOneMoreLine_WithTheProtocolText()
    {
        var work = new EngineWork("s", 1, 16, EngineProtocol.MaxMultiPv + 1, Start, [], 10, null, null);
        var ex = Assert.Throws<InvalidWorkException>(() => WorkSanitizer.Sanitize(work, 8, 1024));
        Assert.Equal(EngineProtocol.MultiPvRangeError, ex.Message);
    }
}
