using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookDiagramConfidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "Confidence",
                table: "EbookDiagrams",
                type: "float",
                nullable: true);

            // 0.794.0: Scan-Bücher mit dem Faltungsnetz neu erkennen (alte Scan-Lesungen und Bücher ohne Treffer), Opus-Bücher nicht.
            migrationBuilder.Sql("UPDATE `Ebooks` e SET e.`DiagramState` = 0 WHERE e.`DiagramsCurated` = 0 AND EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'pdf') AND (e.`DiagramState` = 3 OR EXISTS (SELECT 1 FROM `EbookDiagrams` d WHERE d.`EbookId` = e.`Id` AND d.`FontFamily` = 'scan'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "EbookDiagrams");
        }
    }
}
