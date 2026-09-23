using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <summary>
    /// <c>Book.SourcePgn</c> wandert per EF-Core-TABELLENSPLITTING in die eigene Entität
    /// <c>BookSource</c> — gemappt auf DIESELBE Tabelle <c>Books</c>, dieselbe Spalte <c>SourcePgn</c>
    /// (LONGTEXT, nullable) und denselben Schlüssel <c>Id</c>. Am Schema ändert sich damit NICHTS:
    /// Up/Down sind absichtlich LEER, nur der Modell-Snapshot kennt die neue Entität.
    /// <para>Grund: das Roh-PGN (Ø ~480 KB, bis 6 MB je Buch) hing an <c>Book</c> und kam mit jedem
    /// <c>.Include(bp =&gt; bp.Book)</c> in jede Puzzle-Zeile — 11 GB aus der DB für einen einzigen
    /// <c>GET /api/courses/{id}/puzzles</c>. Jetzt lädt es nur, wer <c>.Include(b =&gt; b.Source)</c>
    /// bzw. <c>_db.BookSources</c> ausdrücklich abfragt.</para>
    /// </summary>
    public partial class SplitBookSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Absichtlich leer — reine Modell-Umstellung (Tabellensplitting), kein Schemaeingriff.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Absichtlich leer — siehe Up.
        }
    }
}
