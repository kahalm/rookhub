using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookDiagramFontsRescan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Neue Diagramm-Schriften (Hastings/Linares, UDiagramm, CHESSBOARD — 0.767.0): Bücher, die bisher als
            // „ohne bekannten Schach-Font" (Unsupported = 3) liefen oder scheiterten (Failed = 2), noch einmal vormerken.
            migrationBuilder.Sql("UPDATE `Ebooks` SET `DiagramState` = 0 WHERE `DiagramState` IN (2, 3);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
