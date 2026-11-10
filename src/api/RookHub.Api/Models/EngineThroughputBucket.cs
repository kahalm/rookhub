using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Durchsatz einer Engine in einem Zeitfenster von <see cref="BucketMinutes"/> Minuten (Admin-Seite „Engines", 0.946.0):
/// wie viele Läufe des <c>AnalysisJobWorker</c> dort endeten und wie. Ein Lauf = eine Suche auf einer Stellung (ein
/// Auftrag kann mehrere Läufe brauchen: Live-Vorrang, Abriss, Engine-Wechsel).
///
/// <para><b>Warum eine eigene kleine Tabelle:</b> fertige Aufträge verschwinden beim Einsammeln
/// (<c>GameAnalysisIngest</c> löscht sie), und die Stellungen (<c>GameAnalysisPositions</c>, Millionen Zeilen) tragen
/// weder die Engine noch einen Index auf den Zeitpunkt. Statt dort nachträglich zu indizieren, zählt der Worker beim
/// Laufende in einen Korb je (Engine, 10 Minuten) — höchstens 144 Zeilen je Engine und Tag, nach
/// <see cref="RetentionDays"/> Tagen weggeräumt.</para>
/// </summary>
public class EngineThroughputBucket
{
    /// <summary>Korbbreite in Minuten — die Fenster der Admin-Seite (1 h / 24 h / 7 T) sind auf diese Breite genau.</summary>
    public const int BucketMinutes = 10;

    /// <summary>So lange bleiben Körbe liegen (7-Tage-Fenster + ein Tag Luft).</summary>
    public const int RetentionDays = 8;

    [MaxLength(64)]
    public string EngineId { get; set; } = string.Empty;

    /// <summary>Beginn des Korbs (UTC, auf <see cref="BucketMinutes"/> abgerundet).</summary>
    public DateTime BucketStart { get; set; }

    /// <summary>Alle beendeten Läufe (Summe der folgenden Ausgänge).</summary>
    public int Runs { get; set; }
    /// <summary>Ziel erreicht — eine Stellung fertig gerechnet.</summary>
    public int Done { get; set; }
    /// <summary>Auftrag endgültig gescheitert.</summary>
    public int Failed { get; set; }
    /// <summary>Von uns unterbrochen (Live-Vorrang, Admin-Pause, Ändern/Löschen) — kein Fehler der Engine.</summary>
    public int Interrupted { get; set; }
    /// <summary>Vorzeitig beendet, wird fortgesetzt (Stream vor dem Ziel zu Ende, abgerissen).</summary>
    public int Retried { get; set; }
    /// <summary>Broker sagte 503/504: kein Provider für diese Engine verbunden (Auftrag wandert weiter).</summary>
    public int Unavailable { get; set; }
    /// <summary>Keine Kopfzeilen, keine erste Zeile oder Stillstand.</summary>
    public int Timeouts { get; set; }
    /// <summary>Sonstige Fehler (Broker nicht erreichbar, andere Statuscodes, Ausnahmen).</summary>
    public int Errors { get; set; }

    /// <summary>Wanduhr-Sekunden aller Läufe (Belegung der Engine).</summary>
    public long RunSeconds { get; set; }
    /// <summary>Rechenzeit der fertigen Aufträge über alle ihre Läufe (<c>AnalysisJob.SecondsSpent</c>) — für die
    /// mittlere Dauer je Auftrag.</summary>
    public long DoneJobSeconds { get; set; }
    /// <summary>Knoten aller Läufe (letzte Zeile je Lauf) — mit <see cref="NodeSeconds"/> ergibt das kn/s.</summary>
    public long Nodes { get; set; }
    /// <summary>Wanduhr-Sekunden der Läufe, die Knoten gemeldet haben.</summary>
    public long NodeSeconds { get; set; }
}
