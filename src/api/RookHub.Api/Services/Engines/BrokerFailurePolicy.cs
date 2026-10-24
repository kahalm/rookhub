using RookHub.Api.Models;

namespace RookHub.Api.Services.Engines;

/// <summary>Einordnung einer Broker-Absage: der Übergang, und ob vorher auf eine andere Engine umgehängt werden soll
/// (gelingt das, gilt stattdessen <see cref="BrokerFailurePolicy.SwitchedEngine"/>).</summary>
internal sealed record BrokerStatusDecision(RunOutcome Outcome, bool TryOtherEngine = false);

/// <summary>
/// Fehlerklassen rund um den Broker als reine Regeln (Zerlegung Z11): nicht erreichbar, keine Kopfzeilen, und
/// Absagen mit Statuscode (<see cref="OnStatus"/>).
/// </summary>
internal static class BrokerFailurePolicy
{
    /// <summary>Auflösen der Lichess-Engine scheiterte am Netz.</summary>
    internal static RunOutcome LichessUnreachable => RunOutcome.Pause("Lichess nicht erreichbar", TimeSpan.FromSeconds(60));

    /// <summary>Verbindung zum Broker scheiterte.</summary>
    internal static RunOutcome Unreachable => RunOutcome.Pause("Broker nicht erreichbar", TimeSpan.FromSeconds(60));

    /// <summary>Der Broker nahm die Verbindung an, schickte aber binnen der Frist keine Kopfzeilen.</summary>
    internal static RunOutcome NoHeaders => RunOutcome.Pause("Broker antwortete nicht", TimeSpan.FromMinutes(5));

    /// <summary>Nach 503/504 auf eine andere Engine umgehängt: kurz warten, die neue kann sofort laufen.</summary>
    internal static RunOutcome SwitchedEngine(int code, TimeSpan backoff)
        => RunOutcome.Pause($"Broker antwortete {code} — andere Engine", backoff);

    /// <summary>
    /// Eine Absage mit Statuscode:
    /// <list type="bullet">
    /// <item>400 vom EIGENEN Broker → gescheitert: er weist nur ab, was die Stellung selbst betrifft (Sanitizer: Gegner
    /// im Schach, falsche Rochaderechte …) — das ändert sich durch Warten nicht.</item>
    /// <item>400 von Lichess → Fehlversuch (A4-006); ab <see cref="AnalysisJob.MaxFruitlessAttempts"/> gescheitert, sonst
    /// wie jede andere Absage 120 s warten. Lichess sagt nicht, WORAN es liegt — meist an einer Stellung, die Gera lädt
    /// und lila-engine abweist; ohne Zähler lief so ein Auftrag ewig im Zwei-Minuten-Takt gegen Lichess und belegte einen
    /// offenen Platz. Nicht sofort gescheitert wie beim eigenen Broker (Gutschrift bei Fortschritt).</item>
    /// <item>503/504 → für DIESE Engine ist kein Provider verbunden: erst umhängen versuchen, sonst 120 s warten. Am
    /// 2026-09-12 auf Prod gemessen: 309 solcher Antworten in 25 Minuten, 24 von 32 Aufträgen pendelten im
    /// Zwei-Minuten-Takt gegen die zwölf Engines der zweiten Maschine, während die vier der ersten allein weiterrechneten.</item>
    /// <item>Alles andere → 120 s warten.</item>
    /// </list>
    /// </summary>
    internal static BrokerStatusDecision OnStatus(int code, bool localEngine, int fruitlessAttempts, string? error)
    {
        if (code == 400 && localEngine) return new(RunOutcome.Fail($"Stellung abgewiesen: {error}"));
        var backoff = RunOutcome.Pause($"Broker antwortete {code}", TimeSpan.FromSeconds(120),
            code == 400 ? FruitlessChange.Increment : FruitlessChange.Keep);
        if (code == 400 && fruitlessAttempts + 1 >= AnalysisJob.MaxFruitlessAttempts)
            return new(RunOutcome.Fail($"Broker antwortete {code} in {fruitlessAttempts + 1} Läufen", FruitlessChange.Increment));
        return new(backoff, TryOtherEngine: code is 503 or 504);
    }
}
