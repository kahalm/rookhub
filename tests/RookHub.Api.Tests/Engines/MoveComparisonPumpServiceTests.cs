using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.Engines;

/// <summary>R1-013: die Zugvergleichs-Pumpe läuft auf dem gemeinsamen <see cref="PeriodicWorker"/>-Rumpf (Pause, Fehler,
/// Shutdown wie alle anderen) — sofort beim Start, dann im konfigurierten Takt.</summary>
public class MoveComparisonPumpServiceTests
{
    private static MoveComparisonPumpService Pump(string? seconds) => new(null!,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MoveComparison:PumpIntervalSeconds"] = seconds,
        }).Build(), NullLogger<MoveComparisonPumpService>.Instance);

    [Fact]
    public void Pumpe_istPeriodicWorker_pausierbar_startetSofort()
    {
        var pump = Pump(null);
        Assert.IsAssignableFrom<PeriodicWorker>(pump);
        Assert.True(pump.PausableByAdmin);
        Assert.Equal(TimeSpan.Zero, pump.InitialDelay(DateTime.UtcNow));
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData("1", 2)]
    [InlineData("500", 120)]
    public void Takt_ausKonfiguration_geklemmt(string? configured, int expectedSeconds)
    {
        // Der Abstand nach einem Schritt ist der Takt; der Startlauf kommt sofort.
        var pump = Pump(configured);
        var prop = typeof(MoveComparisonPumpService).GetProperty("Schedule",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var schedule = (WorkerSchedule)prop.GetValue(pump)!;
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), schedule.NextDelay(DateTime.UtcNow));
    }
}
