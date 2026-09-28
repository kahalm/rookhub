using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <summary>
    /// Holt die fünf Spalten nach, die „WorksheetSharing" und „WorksheetThemes" hätten anlegen
    /// sollen. Beide Migrationen stehen mit LEEREM <c>Up()</c> im Verzeichnis: sie wurden nach einem
    /// Rebase neu erzeugt, während der Modell-Schnappschuss die Änderungen schon enthielt — EF fand
    /// also keinen Unterschied mehr und schrieb einen leeren Rumpf. Angewendet wurden sie trotzdem
    /// (Zeile in <c>__EFMigrationsHistory</c>), ein Neustart holt das also NIE nach; jeder Zugriff
    /// auf ein Aufgabenblatt endete seither in „Unknown column 'w.ShareToken'" (HTTP 500).
    ///
    /// <para>Geschrieben als rohes SQL mit <c>IF NOT EXISTS</c> (MariaDB): Auf Datenbanken, die von
    /// Hand repariert wurden, darf diese Migration nicht ein zweites Mal scheitern — und auf einer
    /// frisch aufgebauten Datenbank legt sie die Spalten regulär an.</para>
    /// </summary>
    public partial class WorksheetColumnsRepair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Aus „WorksheetSharing": öffentlicher Link (/w/{token}) + Lösung je Aufgabe.
            migrationBuilder.Sql(
                "ALTER TABLE `Worksheets` ADD COLUMN IF NOT EXISTS `ShareToken` varchar(32) CHARACTER SET utf8mb4 NULL;");
            migrationBuilder.Sql(
                "ALTER TABLE `Worksheets` ADD COLUMN IF NOT EXISTS `SharedAt` datetime(6) NULL;");
            migrationBuilder.Sql(
                "ALTER TABLE `WorksheetItems` ADD COLUMN IF NOT EXISTS `SolutionMoves` varchar(1000) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS `IX_Worksheets_ShareToken` ON `Worksheets` (`ShareToken`);");

            // Aus „WorksheetThemes": Themen des Blatts + Quellthemen der Aufgabe.
            migrationBuilder.Sql(
                "ALTER TABLE `Worksheets` ADD COLUMN IF NOT EXISTS `Themes` varchar(300) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';");
            migrationBuilder.Sql(
                "ALTER TABLE `WorksheetItems` ADD COLUMN IF NOT EXISTS `SourceThemes` varchar(200) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_Worksheets_ShareToken` ON `Worksheets`;");
            migrationBuilder.Sql("ALTER TABLE `Worksheets` DROP COLUMN IF EXISTS `ShareToken`;");
            migrationBuilder.Sql("ALTER TABLE `Worksheets` DROP COLUMN IF EXISTS `SharedAt`;");
            migrationBuilder.Sql("ALTER TABLE `Worksheets` DROP COLUMN IF EXISTS `Themes`;");
            migrationBuilder.Sql("ALTER TABLE `WorksheetItems` DROP COLUMN IF EXISTS `SolutionMoves`;");
            migrationBuilder.Sql("ALTER TABLE `WorksheetItems` DROP COLUMN IF EXISTS `SourceThemes`;");
        }
    }
}
