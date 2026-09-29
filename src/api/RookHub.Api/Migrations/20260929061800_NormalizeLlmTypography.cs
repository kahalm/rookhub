using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <summary>
    /// Keine Schema-Aenderung: gpt-oss-120b (auf dem Spark seit 28.09.2026) setzt in seine Texte Typografie, die keine
    /// Quelle je enthaelt — geschuetzte Bindestriche (U+2011, 4 481 Prod-Texte), weiche Trennstriche (U+00AD, unsichtbar,
    /// brechen Suche und Kopieren), schmale/normale geschuetzte Leerzeichen (U+202F/U+00A0/U+2009), Ziffernstrich und
    /// Minuszeichen (U+2012/U+2212). Seit 0.597.3 nimmt <c>OpenAiJsonClient.PlainTypography</c> sie jeder Antwort;
    /// diese Migration macht dasselbe einmal fuer den Bestand: alle Maschinentexte (<c>Model IS NOT NULL</c>) in
    /// Kommentaren, Nacherzaehlungen, Roasts und Fehler-Erklaerungen (Prod 29.09.: 5 040 + 2 + 4 + 5 Zeilen).
    /// Gedankenstriche bleiben. Unveraenderte Zeilen schreibt MariaDB nicht neu. Down stellt nichts wieder her (die
    /// Zeichen waren nie gewollt). Von Hand geschrieben, weil das Modell unveraendert ist — der Designer ist die Kopie
    /// des vorigen.
    /// </summary>
    public partial class NormalizeLlmTypography : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE CommentTexts t JOIN CommentSets s ON s.Id = t.CommentSetId SET t.Text = " + Plain("t.Text") +
                " WHERE s.Model IS NOT NULL;");
            migrationBuilder.Sql("UPDATE GameRecaps SET Text = " + Plain("Text") + " WHERE Model IS NOT NULL;");
            migrationBuilder.Sql("UPDATE GameRoasts SET Text = " + Plain("Text") + " WHERE Model IS NOT NULL;");
            migrationBuilder.Sql("UPDATE GameMoveExplanations SET Text = " + Plain("Text") + ", MasterText = " +
                Plain("MasterText") + " WHERE Model IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }

        /// <summary>Derselbe Tausch wie <c>OpenAiJsonClient.PlainTypography</c>, als SQL (REPLACE vergleicht byteweise).</summary>
        private static string Plain(string column) =>
            "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(" + column +
            ", CHAR(0xE28090 USING utf8mb4), '-'), CHAR(0xE28091 USING utf8mb4), '-'), CHAR(0xE28092 USING utf8mb4), '-')" +
            ", CHAR(0xE28892 USING utf8mb4), '-'), CHAR(0xC2AD USING utf8mb4), ''), CHAR(0xC2A0 USING utf8mb4), ' ')" +
            ", CHAR(0xE28089 USING utf8mb4), ' '), CHAR(0xE280AF USING utf8mb4), ' ')";
    }
}
