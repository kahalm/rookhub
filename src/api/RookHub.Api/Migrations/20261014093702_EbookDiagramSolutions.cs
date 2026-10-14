using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookDiagramSolutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SolutionPage",
                table: "EbookDiagrams",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SolutionSan",
                table: "EbookDiagrams",
                type: "varchar(1500)",
                maxLength: 1500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SolutionText",
                table: "EbookDiagrams",
                type: "varchar(3000)",
                maxLength: 3000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SolutionUci",
                table: "EbookDiagrams",
                type: "varchar(1200)",
                maxLength: 1200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
            // Lösungen werden beim Erkennen gesucht (0.771.0): alle schon erkannten Bücher noch einmal vormerken (Done = 1 → Pending = 0).
            migrationBuilder.Sql("UPDATE `Ebooks` SET `DiagramState` = 0 WHERE `DiagramState` = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SolutionPage",
                table: "EbookDiagrams");

            migrationBuilder.DropColumn(
                name: "SolutionSan",
                table: "EbookDiagrams");

            migrationBuilder.DropColumn(
                name: "SolutionText",
                table: "EbookDiagrams");

            migrationBuilder.DropColumn(
                name: "SolutionUci",
                table: "EbookDiagrams");
        }
    }
}
