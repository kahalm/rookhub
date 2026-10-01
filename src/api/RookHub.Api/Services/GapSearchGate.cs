namespace RookHub.Api.Services;

/// <summary>
/// Wie viele Lückensuchen (<see cref="GapSolver"/>) im ganzen Prozess GLEICHZEITIG rechnen dürfen — über alle Konten.
///
/// <para>Jede Suche ist reine CPU im Request-Thread bis zum Budget des Lösers. Der Rate-Limiter zählt nur Anfragen
/// je Minute, nicht, wie viele davon gleichzeitig laufen: ~100 fast gleichzeitige Aufrufe hielten ebenso viele
/// Thread-Pool-Threads fest und hungerten die geteilte API für alle aus (Codereview 2026-09-29, N5-001). Ist kein
/// Platz frei, wird NICHT gewartet — die Anfrage bekommt sofort 429 (<see cref="GapSearchBusyException"/>), damit
/// sich keine Schlange aufbaut, die denselben Thread-Pool bindet.</para>
/// </summary>
public sealed class GapSearchGate
{
    /// <summary>Plätze der Prozess-Schranke: drei Kerne höchstens, der Rest der API bleibt frei.</summary>
    public const int DefaultSlots = 3;

    /// <summary>Die eine Schranke des Prozesses (der Dienst ist scoped, die Schranke muss es nicht sein).</summary>
    public static GapSearchGate Shared { get; } = new(DefaultSlots);

    private readonly SemaphoreSlim _slots;

    public GapSearchGate(int slots) => _slots = new SemaphoreSlim(slots, slots);

    /// <summary>Läuft, sobald eine Suche ihren Platz hat — nur die Tests setzen es: so bricht eine Anfrage GENAU zwischen
    /// Laden (EF prüft den Token) und Rechnen ab, ohne Timer, der auf einem vollen CI-Runner zu spät käme.</summary>
    internal Action? Entered { get; init; }

    /// <summary>Einen Platz nehmen, ohne zu warten; <c>false</c> = alle belegt.</summary>
    public bool TryEnter()
    {
        if (!_slots.Wait(0)) return false;
        Entered?.Invoke();
        return true;
    }

    public void Exit() => _slots.Release();
}

/// <summary>Alle Plätze der <see cref="GapSearchGate"/> sind belegt → der Controller antwortet 429.</summary>
public sealed class GapSearchBusyException : Exception
{
    public GapSearchBusyException() : base("busy") { }
}
