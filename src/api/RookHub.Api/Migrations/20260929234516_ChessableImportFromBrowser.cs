using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <summary>
    /// <c>ChessableImports.FromBrowser</c>: Browser-Importe (RepCheck) werden nie von den Server-Lanes abgerufen
    /// (siehe <see cref="Models.ChessableImport.FromBrowser"/>).
    ///
    /// <para>Nachtrag nur für die LAUFENDEN bzw. pausierten Browser-Importe (für abgeschlossene spielt die Spalte keine
    /// Rolle): Phase „importing" mit <c>Attempts = 0</c> gibt es nur bei ihnen — ein Server-Import zählt
    /// <c>Attempts</c> hoch, bevor er holt, und alle Wege, die <c>Attempts</c> zurücksetzen, stellen die Phase auf
    /// „queued"/„bearer-blocked"/„rate-limited". Ohne den Nachtrag reihte der Resume-Dienst beim Start mit dieser
    /// Migration genau solche Sätze als Server-Import ein.</para>
    /// </summary>
    public partial class ChessableImportFromBrowser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FromBrowser",
                table: "ChessableImports",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE `ChessableImports` SET `FromBrowser` = 1 "
                + "WHERE `Status` IN ('running', 'paused') AND `Phase` = 'importing' AND `Attempts` = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FromBrowser",
                table: "ChessableImports");
        }
    }
}
