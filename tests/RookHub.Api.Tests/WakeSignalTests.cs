using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Der Weckruf des Worker-Takts: ein beendeter Lauf soll die Schleife sofort wieder anstoßen,
/// statt bis zu einen Tick (5 s) zu warten — gemessen am 2026-09-26 rund ein Fünftel Leerlauf je Engine.</summary>
public class WakeSignalTests
{
    private static readonly TimeSpan LongTick = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Wake_EndsARunningWait_Immediately()
    {
        var signal = new WakeSignal();
        var wait = signal.WaitAsync(LongTick, CancellationToken.None);
        Assert.False(wait.IsCompleted);

        signal.Wake();

        var woken = await wait.WaitAsync(TimeSpan.FromSeconds(2));   // deutlich unter dem Tick
        Assert.True(woken);
    }

    [Fact]
    public async Task Wake_BeforeTheWait_IsNotLost()
    {
        // Ein Lauf endet, während die Schleife gerade den Tick abarbeitet — der Weckruf muss das NÄCHSTE
        // Warten beenden, sonst bliebe die Engine doch einen ganzen Tick stehen.
        var signal = new WakeSignal();
        signal.Wake();

        var woken = await signal.WaitAsync(LongTick, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(woken);
    }

    [Fact]
    public async Task ManyWakes_CollapseIntoOne()
    {
        // Sechzehn Läufe enden fast gleichzeitig: ein Tick danach sieht ohnehin alle Engines durch.
        var signal = new WakeSignal();
        for (var i = 0; i < 16; i++) signal.Wake();   // darf nicht werfen (SemaphoreFullException)

        Assert.True(await signal.WaitAsync(LongTick, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)));
        // Der zweite Weckruf ist im ersten aufgegangen: jetzt läuft der normale Tick.
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Fact]
    public async Task WithoutWake_TheTickElapses()
    {
        var signal = new WakeSignal();
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(30), CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_StopsTheWait()
    {
        var signal = new WakeSignal();
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signal.WaitAsync(LongTick, cts.Token));
    }
}
