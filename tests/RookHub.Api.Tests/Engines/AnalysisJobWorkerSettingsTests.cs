using Microsoft.Extensions.Configuration;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.Engines;

/// <summary>Die Einstellungen des Hintergrund-Analyse-Workers: Vorgaben und Klemmen wie vorher im Konstruktor.</summary>
public class AnalysisJobWorkerSettingsTests
{
    private static AnalysisJobWorkerSettings From(Dictionary<string, string?> values) =>
        AnalysisJobWorkerSettings.From(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Fact]
    public void From_Empty_UsesDefaults()
    {
        var s = From([]);
        Assert.Equal(TimeSpan.FromSeconds(5), s.Tick);
        Assert.Equal(TimeSpan.FromSeconds(20), s.IdleGrace);
        Assert.Equal(TimeSpan.FromSeconds(5), s.PersistInterval);
        Assert.Equal(TimeSpan.FromSeconds(300), s.FirstLineTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1800), s.StallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(15), s.EngineSwitchBackoff);
        Assert.Equal(TimeSpan.FromSeconds(60), s.FruitlessMinRuntime);
        Assert.Equal(10_000, s.SnapshotStep);
        Assert.Empty(s.ExplicitOnly);
    }

    [Fact]
    public void From_OutOfRange_IsClamped()
    {
        var s = From(new()
        {
            ["AnalysisJobs:TickSeconds"] = "0",
            ["AnalysisJobs:IdleGraceSeconds"] = "9999",
            ["AnalysisJobs:PersistIntervalSeconds"] = "100",
            ["AnalysisJobs:FirstLineTimeoutSeconds"] = "1",
            ["AnalysisJobs:StallTimeoutSeconds"] = "999999",
            ["AnalysisJobs:EngineSwitchBackoffSeconds"] = "1",
            ["AnalysisJobs:FruitlessMinRuntimeSeconds"] = "99999",
            ["AnalysisJobs:SnapshotStepNodes"] = "5",
        });
        Assert.Equal(TimeSpan.FromSeconds(1), s.Tick);
        Assert.Equal(TimeSpan.FromSeconds(600), s.IdleGrace);
        Assert.Equal(TimeSpan.FromSeconds(60), s.PersistInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), s.FirstLineTimeout);
        Assert.Equal(TimeSpan.FromSeconds(86400), s.StallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), s.EngineSwitchBackoff);
        Assert.Equal(TimeSpan.FromSeconds(3600), s.FruitlessMinRuntime);
        Assert.Equal(1_000, s.SnapshotStep);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-3", 0)]
    [InlineData("5000000", 1_000_000)]
    public void From_SnapshotStep_ZeroOrNegativeDisables_LargeIsCapped(string value, long expected) =>
        Assert.Equal(expected, From(new() { ["AnalysisJobs:SnapshotStepNodes"] = value }).SnapshotStep);
}
