namespace RookHub.Api.Models;

/// <summary>
/// Roh-PGN eines <see cref="Book"/> — per EF-Core-<b>Tabellensplitting</b> in DERSELBEN Zeile der
/// Tabelle <c>Books</c> gespeichert (Spalte <c>SourcePgn</c>, LONGTEXT), aber als eigene Entität
/// gemappt. Grund: das Roh-PGN ist groß (Ø ~480 KB, einzelne Bücher mehrere MB) und wird nur zum
/// Download, zum Neu-Aufbereiten und beim Import gebraucht. Als Property von <see cref="Book"/> lud
/// es JEDES <c>.Include(bp =&gt; bp.Book)</c> mit — bei <c>GET /api/courses/{id}/puzzles</c> in jeder
/// Puzzle-Zeile (6 MB × 1.881 Linien = 11 GB aus der DB für EINEN Request).
/// <para><b>Regeln</b></para>
/// <list type="bullet">
/// <item>Nie über <see cref="Book"/> mitladen; <c>.Include(b =&gt; b.Source)</c> bzw. <c>_db.BookSources</c> nur
/// dort, wo der Text wirklich gebraucht wird — niemals in Listen-Queries (<c>BookSourceIncludeGuardTests</c>
/// hält die erlaubten Stellen fest).</item>
/// <item>Beim Anlegen eines Buchs IMMER <see cref="Book.Source"/> setzen. EF selbst verlangt das NICHT:
/// relational fiele eine fehlende Source nicht auf (INSERT ohne die Spalte → <c>SourcePgn = NULL</c>), unter
/// InMemory fehlte die BookSource-Zeile (Include liefert dann <c>null</c> → NullReferenceException bei
/// <c>book.Source.SourcePgn</c>; der Lösch-Stub in <c>BookAdminService.DeleteBookAsync</c> wirft
/// <c>DbUpdateConcurrencyException</c>). Deshalb erzwingt es <c>AppDbContext</c> (InvalidOperationException).</item>
/// <item>Ohne Include ist <see cref="Book.Source"/> <c>null</c> — solange die BookSource nicht ohnehin im
/// selben Kontext getrackt ist (dann setzt der Fixup sie). Mit Include ist sie relational IMMER eine Instanz
/// (Pflicht-Navigation, auch bei <c>SourcePgn = NULL</c>); unter InMemory nur, wenn die Zeile existiert.</item>
/// <item>Schreiben nur über eine GELADENE Source (<c>.Include(b =&gt; b.Source)</c>).</item>
/// <item>Löschen: das BUCH löschen — die Source geht mit der Zeile (relational ein DELETE, auch ohne geladene
/// Source; unter InMemory hängt <c>BookAdminService.DeleteBookAsync</c> dafür einen Stub an).</item>
/// </list>
/// <para><b>Fallen — alle drei schreiben still <c>UPDATE Books SET SourcePgn = …</c> statt zu löschen/anzulegen:</b></para>
/// <list type="bullet">
/// <item><c>public BookSource Source { get; set; } = new();</c> an <see cref="Book"/> — der naheliegende
/// „Fix" für null ist Datenverlust: jedes ohne Include geladene Buch bekäme eine leere Source, die
/// DetectChanges als Added aufnimmt → <c>SourcePgn = NULL</c> beim nächsten SaveChanges.</item>
/// <item><c>book.Source = new BookSource { … }</c> an ein geladenes Buch hängen — ersetzt den Text.</item>
/// <item><c>_db.BookSources.Remove(src)</c> und <c>book.Source = null</c> sind KEIN Delete, sondern ein
/// Blanking (<c>SourcePgn = NULL</c>, gegen MariaDB geprüft). Beides nie tun.</item>
/// </list>
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
