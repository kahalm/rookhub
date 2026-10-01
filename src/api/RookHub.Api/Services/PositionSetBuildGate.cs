namespace RookHub.Api.Services;

/// <summary>
/// Wie viele Neuaufbauten von Positions-Sets (<see cref="RepertoireAnalyzeService"/>) im ganzen Prozess GLEICHZEITIG
/// laufen dürfen — über alle Konten.
///
/// <para>Ein Neuaufbau liest die markierten PGNs eines Kontos und spielt jeden Halbzug nach (bis zu einige Sekunden
/// CPU). Die Sperre je Konto lässt nur einen Neuaufbau je Konto zu, N Konten banden aber N Kerne — und der
/// Partie-Rückblick (<see cref="RepertoireAnalyzeService.BookPliesAsync"/>) läuft an der Policy der Erweiterung vorbei
/// (Codereview 2026-09-29, N8-005 Nacharbeit; Vorbild <see cref="GapSearchGate"/>). Gewartet wird nur kurz
/// (<see cref="DefaultWait"/>, ohne einen Thread zu halten); danach <see cref="PositionSetBusyException"/>.</para>
/// </summary>
public sealed class PositionSetBuildGate
{
    /// <summary>Plätze der Prozess-Schranke: die Hälfte der Kerne, höchstens zwei.</summary>
    public static readonly int DefaultSlots = Math.Clamp(Environment.ProcessorCount / 2, 1, 2);

    /// <summary>So lange wartet ein Neuaufbau auf einen Platz — ein übliches Set ist in unter einer Sekunde gebaut.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(5);

    /// <summary>Die eine Schranke des Prozesses (der Dienst ist scoped, die Schranke muss es nicht sein).</summary>
    public static PositionSetBuildGate Shared { get; } = new(DefaultSlots, DefaultWait);

    private readonly SemaphoreSlim _slots;
    private readonly TimeSpan _wait;

    public PositionSetBuildGate(int slots, TimeSpan wait)
    {
        _slots = new SemaphoreSlim(slots, slots);
        _wait = wait;
    }

    /// <summary>Einen Platz nehmen, höchstens <c>wait</c> lang warten; <c>false</c> = alle belegt.</summary>
    public Task<bool> TryEnterAsync() => _slots.WaitAsync(_wait);

    public void Exit() => _slots.Release();
}

/// <summary>Alle Plätze der <see cref="PositionSetBuildGate"/> sind belegt → <c>analyze-game</c> antwortet 429.</summary>
public sealed class PositionSetBusyException : Exception
{
    public PositionSetBusyException() : base("busy") { }
}
