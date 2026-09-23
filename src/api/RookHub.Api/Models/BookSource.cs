namespace RookHub.Api.Models;

/// <summary>
/// Roh-PGN eines <see cref="Book"/> — per EF-Core-<b>Tabellensplitting</b> in DERSELBEN Zeile der
/// Tabelle <c>Books</c> gespeichert (Spalte <c>SourcePgn</c>, LONGTEXT), aber als eigene Entität
/// gemappt. Grund: das Roh-PGN ist groß (Ø ~480 KB, einzelne Bücher mehrere MB) und wird nur zum
/// Download, zum Neu-Aufbereiten und beim Import gebraucht. Als Property von <see cref="Book"/> lud
/// es JEDES <c>.Include(bp =&gt; bp.Book)</c> mit — bei <c>GET /api/courses/{id}/puzzles</c> in jeder
/// Puzzle-Zeile (6 MB × 1.881 Linien = 11 GB aus der DB für EINEN Request).
/// <para>Regeln: nie über <see cref="Book"/> mitladen; <c>.Include(b =&gt; b.Source)</c> bzw.
/// <c>_db.BookSources</c> nur dort, wo der Text wirklich gebraucht wird — niemals in Listen-Queries.
/// Beim Anlegen eines Buchs MUSS <see cref="Book.Source"/> gesetzt werden (Pflicht-Navigation;
/// SaveChanges gegen MariaDB wirft sonst).</para>
/// </summary>
public class BookSource
{
    /// <summary>Primärschlüssel UND Fremdschlüssel auf <see cref="Book.Id"/> — dieselbe Spalte <c>Id</c>.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Roh-PGN, aus dem das Buch importiert wurde (LONGTEXT, nullable). Quelle fürs verlustfreie
    /// Neu-Aufbereiten (Reprocessing), wenn die Import-Pipeline weiterentwickelt wurde — z. B. um
    /// nachträglich Pro-Zug-Kommentare zu extrahieren. <c>null</c> bei Altbestand (vor
    /// Pipeline-Version 1), bei reinen JSON-Bulk-Importen (kein PGN) und bei von Hand angelegten Kursen.
    /// </summary>
    public string? SourcePgn { get; set; }

    public Book Book { get; set; } = null!;
}
