using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookDiagramImageRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageRef",
                table: "EbookDiagrams",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // EPUB-Bücher ohne PDF waren „Unsupported“ — jetzt liest sie die Bilderkennung (0.775.0).
            migrationBuilder.Sql("UPDATE `Ebooks` e SET e.`DiagramState` = 0 WHERE e.`DiagramState` = 3 AND EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'epub') AND NOT EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'pdf');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageRef",
                table: "EbookDiagrams");
        }
    }
}
