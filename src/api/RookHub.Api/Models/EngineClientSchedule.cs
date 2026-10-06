using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Der Zeitplan, den ein Engine-Client (Docker, Windows, Lc0) für EINE seiner Engines gemeldet hat
/// (0.679.0, Wunsch 2026-10-06: „wenn der client betriebszeiten meldet halte ich mich an die, wenn nicht
/// nehm ich die voreingestellten von rookhub").
///
/// <para><b>Warum je Engine und nicht je Client:</b> der Client schaltet bei Teillast nicht alle Engines
/// gleich ab, sondern die ersten <c>n</c> laufen und die übrigen stehen (Platz 1 = Live-Engine). Mit Platz
/// (<see cref="Slot"/>) und Gesamtzahl (<see cref="Total"/>) rechnet RookHub für jede Engine genau dasselbe
/// nach wie der Client — <see cref="Services.EngineBroker.EngineScheduleRules"/> ist die C#-Fassung von
/// <c>engine-provider/entrypoint.sh</c>.</para>
///
/// <para><b>Schlüssel ist der NAME</b>, nicht die Kennung: genau so identifiziert sich eine Engine auch bei
/// der Registrierung, und der Client kennt seine Kennungen nicht. Eine Meldung darf deshalb auch VOR der
/// ersten Registrierung eintreffen.</para>
///
/// <para>Gibt es zu einer Engine keine Zeile, gelten die Sperrzeiten von RookHub (<see cref="Services.QuietHours"/>)
/// für den Stapel, und normale Aufträge laufen jederzeit — genau das bisherige Verhalten.</para>
/// </summary>
public class EngineClientSchedule
{
    public int Id { get; set; }
    public int UserId { get; set; }

    [MaxLength(200)]
    public string EngineName { get; set; } = string.Empty;

    /// <summary>Platz der Engine im Client, 1 = Live-Engine.</summary>
    public int Slot { get; set; }

    /// <summary>Zahl der Engines dieses Clients.</summary>
    public int Total { get; set; }

    /// <summary><c>background</c> (Live-Engine bleibt an) oder <c>all</c>.</summary>
    [MaxLength(16)]
    public string Scope { get; set; } = "background";

    /// <summary>Die Regeln wie im Client, z. B. „Mo-Do 08:00-17:00 0%; Sa,So 100%".</summary>
    [MaxLength(500)]
    public string Rule { get; set; } = string.Empty;

    /// <summary>Zeitzone, in der die Uhrzeiten gemeint sind (IANA oder Windows-Kennung).</summary>
    [MaxLength(64)]
    public string TimeZone { get; set; } = string.Empty;

    public DateTime ReportedAt { get; set; }
}
