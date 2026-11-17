using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.DTOs.EngineBroker;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.EngineBroker;

/// <summary>
/// Sitzungs-Epoche je Engine (Provider-Versatz 16./17.11.2026): der Provider schickt <c>ucinewgame</c> + <c>isready</c>
/// nur bei neuer <c>sessionId</c> und verwirft dabei die liegengebliebene Ausgabe. Die Kennung wechselt deshalb je
/// API-Prozess und nach jeder erkannten fremden Ausgabe — sonst nie (die Hashtabelle bleibt warm), und flache Aufträge
/// behalten ihre eigene Sitzung (0.974.0).
/// </summary>
public class EngineSessionEpochsTests
{
    private const string EngineA = "rhe_aaaaaaaaaaaa";
    private const string EngineB = "rhe_bbbbbbbbbbbb";
    private static readonly DateTime T0 = new(2026, 11, 17, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Kennung_bleibtGleich_solangeNichtsErkanntWird()
    {
        var epochs = new EngineSessionEpochs("abcde");
        Assert.Equal("rh-bg-7-eabcde0", epochs.SessionFor(EngineA, "rh-bg-7"));
        Assert.Equal("rh-bg-7-eabcde0", epochs.SessionFor(EngineA, "rh-bg-7"));
    }

    [Fact]
    public void Kennung_wechseltNachErkennung_nurAufDieserEngine()
    {
        var epochs = new EngineSessionEpochs("abcde");
        var before = epochs.SessionFor(EngineA, "rh-bg-7");
        var other = epochs.SessionFor(EngineB, "rh-bg-7");

        epochs.ReportForeign(EngineA, T0);

        Assert.NotEqual(before, epochs.SessionFor(EngineA, "rh-bg-7"));
        Assert.Equal(other, epochs.SessionFor(EngineB, "rh-bg-7"));
    }

    [Fact]
    public void Kennung_wechseltJeProzess()
    {
        // Nach dem Neustart (dem Hauptauslöser) beginnt der erste Auftrag jeder Engine in einer neuen Sitzung.
        var first = new EngineSessionEpochs();
        var second = new EngineSessionEpochs();
        Assert.Matches("^[a-z0-9]{5}$", first.ProcessPart);
        Assert.NotEqual(first.SessionFor(EngineA, "rh-bg-7"), second.SessionFor(EngineA, "rh-bg-7"));
    }

    [Fact]
    public void FlacheAuftraege_behaltenIhreEigeneSitzung_undDerNaechsteGewoehnlicheKehrtZurueck()
    {
        var epochs = new EngineSessionEpochs("abcde");
        var deep = new AnalysisJob { Id = 11, UserId = 7, TargetDepth = 20 };
        var flat = new AnalysisJob { Id = 12, UserId = 7, TargetDepth = AnalysisJobEngineResolver.FreshSessionMaxDepth };
        var deepAgain = new AnalysisJob { Id = 13, UserId = 7, TargetDepth = 22 };

        var s1 = epochs.SessionFor(EngineA, AnalysisJobEngineResolver.SessionOf(deep));
        var s2 = epochs.SessionFor(EngineA, AnalysisJobEngineResolver.SessionOf(flat));
        var s3 = epochs.SessionFor(EngineA, AnalysisJobEngineResolver.SessionOf(deepAgain));

        Assert.Equal("rh-bg-7-fresh-12-eabcde0", s2);
        Assert.NotEqual(s1, s2);
        Assert.Equal(s1, s3);
    }

    [Fact]
    public void LangeKennung_wirdGekuerzt_EpocheBleibtGanz()
    {
        var epochs = new EngineSessionEpochs("abcde");
        var raw = new string('x', 64);
        var session = epochs.SessionFor(EngineA, raw);
        Assert.Equal(EngineSessionEpochs.MaxSessionIdLength, session.Length);
        Assert.EndsWith("-eabcde0", session);
        // Die Live-Regel (EngineStreamProxy.ValidateWork) hält auch die erweiterte Kennung ein.
        Assert.Matches("^[A-Za-z0-9-]+$", epochs.SessionFor(EngineA, "rh-bg-7"));
    }

    [Fact]
    public void Wiederholungstaeter_abDreiInFolge_FehlerHoechstensStuendlich_undAusgesetzt()
    {
        var epochs = new EngineSessionEpochs("abcde");
        var first = epochs.ReportForeign(EngineA, T0);
        var second = epochs.ReportForeign(EngineA, T0.AddSeconds(10));
        Assert.False(first.RepeatOffender);
        Assert.False(second.RepeatOffender);
        Assert.False(epochs.IsBenched(EngineA, T0.AddSeconds(10)));

        var third = epochs.ReportForeign(EngineA, T0.AddSeconds(20));
        Assert.Equal(EngineSessionEpochs.RepeatOffenderStrikes, third.InARow);
        Assert.True(third.RepeatOffender);
        Assert.True(third.LogError);
        Assert.True(epochs.IsBenched(EngineA, T0.AddMinutes(1)));
        Assert.False(epochs.IsBenched(EngineB, T0.AddMinutes(1)));
        Assert.False(epochs.IsBenched(EngineA, T0.AddSeconds(20) + EngineSessionEpochs.BenchDuration));

        var fourth = epochs.ReportForeign(EngineA, T0.AddMinutes(10));
        Assert.True(fourth.RepeatOffender);
        Assert.False(fourth.LogError);                                   // höchstens einmal je Stunde
        Assert.True(epochs.ReportForeign(EngineA, T0.AddMinutes(81)).LogError);
    }

    [Fact]
    public void SaubererLauf_unterbrichtDieFolge()
    {
        var epochs = new EngineSessionEpochs("abcde");
        epochs.ReportForeign(EngineA, T0);
        epochs.ReportForeign(EngineA, T0);
        epochs.ReportClean(EngineA);
        var next = epochs.ReportForeign(EngineA, T0);
        Assert.Equal(1, next.InARow);
        Assert.False(next.RepeatOffender);
    }

    // ── Verdrahtung: der Router hängt die Epoche an, beim Provider kommt sie an ─────────────────────────────────────

    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static EngineRef Engine() => new(
        EngineA, "Heim-PC", 8, 1024, EngineSource.Local,
        Local: new LocalEngineTarget("sel", new ExternalEngineRegistrationDto
        {
            Id = EngineA, Name = "Heim-PC", ClientSecret = "cs", UserId = "kahalm",
            MaxThreads = 8, MaxHash = 1024, Variants = ["chess"],
        }));

    [Fact]
    public async Task Router_schicktDieEpocheZumProvider_undNachErkennungEineNeue()
    {
        var options = new LocalBrokerOptions { ProviderTimeout = TimeSpan.FromSeconds(10) };
        var hub = new EngineHub(options, () => DateTime.UtcNow, startSweeper: false);
        var epochs = new EngineSessionEpochs("abcde");
        var router = new EngineBrokerRouter(new LocalEngineBroker(hub, options, NullLogger<LocalEngineBroker>.Instance),
            new LichessEngineBroker(null!, NullLogger<LichessEngineBroker>.Instance), epochs);

        async Task<string> SessionAtProviderAsync()
        {
            var analyse = router.AnalyseAsync(Engine(), new EngineWork("rh-bg-7", 1, 16, 1, Start, [], Depth: 10), CancellationToken.None);
            var polled = await hub.AcquireAsync("sel", TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.NotNull(polled);
            var job = hub.TakeOngoing(polled!.Id!)!;
            job.Accepted.TrySetResult();
            await using var session = await analyse;
            return job.Work.SessionId;
        }

        Assert.Equal("rh-bg-7-eabcde0", await SessionAtProviderAsync());
        Assert.Equal("rh-bg-7-eabcde0", await SessionAtProviderAsync());
        epochs.ReportForeign(EngineA, DateTime.UtcNow);
        Assert.Equal("rh-bg-7-eabcde1", await SessionAtProviderAsync());
    }
}
