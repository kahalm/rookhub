using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookDiagramsCurated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DiagramsCurated",
                table: "Ebooks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // Bücher mit per Opus nachgetragenen Diagrammen (Pongó, 14.10.) schützen.
            migrationBuilder.Sql("UPDATE `Ebooks` e SET e.`DiagramsCurated` = 1 WHERE EXISTS (SELECT 1 FROM `EbookDiagrams` d WHERE d.`EbookId` = e.`Id` AND d.`FontFamily` = 'opus');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiagramsCurated",
                table: "Ebooks");
        }
    }
}
