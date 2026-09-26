namespace RookHub.Api.Services;

/// <summary>
/// Ein Takt, der sich vorzeitig wecken lässt. Der <see cref="AnalysisJobWorker"/> lief bis 0.543.0 in einem
/// festen 5-s-Takt: eine Engine, deren Lauf endete, bekam ihren nächsten Auftrag erst beim nächsten Tick —
/// bei Läufen von zehn Sekunden (Tiefe 20, eine Linie) im Mittel 2,5 s Leerlauf je Stellung, gemessen am
/// 2026-09-26 auf Prod als rund ein Fünftel der Wanduhr. Jetzt ruft ein beendeter Lauf <see cref="Wake"/>,
/// und <see cref="WaitAsync"/> kehrt sofort zurück.
///
/// <para>Mehrere Weckrufe während EINES Wartens fallen zu einem zusammen (die Schleife sieht danach ohnehin
/// alle Engines durch), und ein Weckruf, der zwischen zwei Wartezeiten eintrifft, geht nicht verloren — er
/// beendet das nächste Warten sofort.</para>
/// </summary>
public sealed class WakeSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    /// <summary>Das nächste (oder das laufende) Warten sofort beenden. Beliebig oft aufrufbar.</summary>
    public void Wake()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* schon geweckt — ein Weckruf genügt */ }
    }

    /// <summary>Wartet höchstens <paramref name="tick"/>, kürzer nach einem <see cref="Wake"/>.
    /// Liefert <c>true</c>, wenn ein Weckruf das Warten beendet hat.</summary>
    public async Task<bool> WaitAsync(TimeSpan tick, CancellationToken ct)
        => await _signal.WaitAsync(tick, ct);
}
