using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookDjvuPdfEpub : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConversionState",
                table: "Ebooks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Locator",
                table: "EbookProgresses",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "Derived",
                table: "EbookFiles",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
            // Vorhandene Bücher, die NUR als DJVU vorliegen, zur Umwandlung vormerken (EbookConversionState.Pending = 1).
            migrationBuilder.Sql("UPDATE `Ebooks` e SET e.`ConversionState` = 1 WHERE EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'djvu') AND NOT EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'pdf');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConversionState",
                table: "Ebooks");

            migrationBuilder.DropColumn(
                name: "Locator",
                table: "EbookProgresses");

            migrationBuilder.DropColumn(
                name: "Derived",
                table: "EbookFiles");
        }
    }
}
