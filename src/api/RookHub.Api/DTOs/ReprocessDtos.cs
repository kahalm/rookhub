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

    /// <summary>Veraltet + ohne Chessable-Abruf sofort aufbereitbar (gespeicherte Quelle vorhanden) —
    /// schließt <see cref="FromCache"/> ein.</summary>
    public int ReprocessableLocally { get; set; }

    /// <summary>Davon Chessable-Kurse bzw. -Repertoires, deren Zugtexte vorher aus dem geteilten
    /// piratechess-Linien-Cache neu erzeugt werden (Quelle trägt <c>[ChessableOid]</c>). Nur zur Auskunft (Admin) —
    /// schon in <see cref="ReprocessableLocally"/> enthalten, das Banner rechnet unverändert.</summary>
    public int FromCache { get; set; }

    /// <summary>Veraltet, keine lokale Quelle, aber per Chessable-Re-Fetch nachladbar (Hintergrund-Job).</summary>
    public int Refetchable { get; set; }

    /// <summary>Veraltet, weder lokale Quelle noch Re-Fetch möglich → nur durch erneuten manuellen Import lösbar.</summary>
    public int NeedsReimport { get; set; }
}

/// <summary>Ergebnis eines Reprocess-Laufs.</summary>
public class ReprocessResultDto
{
    /// <summary>Ohne Chessable-Abruf neu aufbereitete Datensätze (aus gespeicherter Quelle bzw. Versions-Mark,
    /// auch über den Linien-Cache — siehe <see cref="RebuiltFromCache"/>).</summary>
    public int Reprocessed { get; set; }

    /// <summary>Dabei in-place aktualisierte Einzel-Linien (nur Kurse).</summary>
    public int UpdatedLines { get; set; }

    /// <summary>Davon Kurse bzw. Repertoires, deren Zugtexte aus dem geteilten piratechess-Linien-Cache erneuert
    /// wurden (in <see cref="Reprocessed"/> enthalten).</summary>
    public int RebuiltFromCache { get; set; }

    /// <summary>Linien, deren Zugtext dabei aus dem Linien-Cache übernommen wurde (über alle Kurse bzw.
    /// Repertoire-Dateien).</summary>
    public int CacheLinesReplaced { get; set; }

    /// <summary>Als Hintergrund-Job zum Re-Fetch eingereihte Datensätze (Chessable ohne oids).</summary>
    public int Enqueued { get; set; }

    /// <summary>Veraltete Datensätze, die weder lokal noch per Re-Fetch behandelt werden konnten
    /// (keine Quelle, Re-Fetch-Backoff, Dedup, keine Linie im Linien-Cache) — ein reguläres „nichts zu tun".</summary>
    public int Skipped { get; set; }

    /// <summary>Datensätze, deren Aufbereitung mit einem FEHLER abgebrochen ist (kaputtes Quell-PGN,
    /// Parser-Sonderfall, DbUpdateException). Bewusst getrennt von <see cref="Skipped"/>: so ein Buch
    /// behält seine <c>ImportVersion</c>, bleibt also im „Aktualisieren (N)"-Banner stehen — ohne
    /// eigenen Zähler wäre nicht zu unterscheiden, ob nichts zu tun war oder etwas kaputt ist.</summary>
    public int Failed { get; set; }
}
