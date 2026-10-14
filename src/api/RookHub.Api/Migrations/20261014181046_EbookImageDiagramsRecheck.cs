using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookImageDiagramsRecheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0.780.5: Material-Prüfung + Entdoppelung — Bücher mit Bild-/Scan-Diagrammen neu erkennen lassen.
            migrationBuilder.Sql("UPDATE `Ebooks` e SET e.`DiagramState` = 0 WHERE e.`DiagramState` = 1 AND EXISTS (SELECT 1 FROM `EbookDiagrams` d WHERE d.`EbookId` = e.`Id` AND d.`FontFamily` IN ('scan', 'image'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
