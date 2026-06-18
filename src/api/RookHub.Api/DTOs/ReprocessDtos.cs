namespace RookHub.Api.DTOs;

/// <summary>
/// Status der Aufbereitungs-Versionierung einer Sektion (Kurse bzw. Repertoires) für den
/// aufrufenden Nutzer: wie viele seiner Datensätze sind „veraltet" und wie aufbereitbar.
/// Basis für den „Aktualisieren (N)"-Knopf.
/// </summary>
public class ReprocessStatusDto
{
    /// <summary>Aktuelle Pipeline-Version (Code-Konstante).</summary>
    public int CurrentVersion { get; set; }

    /// <summary>Verwaltbare Datensätze insgesamt (Admin: alle Bücher; sonst eigene).</summary>
    public int Total { get; set; }

    /// <summary>Davon veraltet (ImportVersion &lt; CurrentVersion) — Summe der drei Kategorien unten.</summary>
    public int Stale { get; set; }

    /// <summary>Veraltet + lokal sofort aufbereitbar (gespeicherte Quelle vorhanden).</summary>
    public int ReprocessableLocally { get; set; }

    /// <summary>Veraltet, keine lokale Quelle, aber per Chessable-Re-Fetch nachladbar (Hintergrund-Job).</summary>
    public int Refetchable { get; set; }

    /// <summary>Veraltet, weder lokale Quelle noch Re-Fetch möglich → nur durch erneuten manuellen Import lösbar.</summary>
    public int NeedsReimport { get; set; }
}

/// <summary>Ergebnis eines Reprocess-Laufs.</summary>
public class ReprocessResultDto
{
    /// <summary>Lokal (aus gespeicherter Quelle) neu aufbereitete Datensätze.</summary>
    public int Reprocessed { get; set; }

    /// <summary>Dabei in-place aktualisierte Einzel-Linien (nur Kurse).</summary>
    public int UpdatedLines { get; set; }

    /// <summary>Als Hintergrund-Job zum Re-Fetch eingereihte Datensätze (nur Kurse, Chessable).</summary>
    public int Enqueued { get; set; }

    /// <summary>Veraltete Datensätze, die weder lokal noch per Re-Fetch behandelt werden konnten.</summary>
    public int Skipped { get; set; }
}
