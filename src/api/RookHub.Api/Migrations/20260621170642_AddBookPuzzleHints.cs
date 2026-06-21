using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookPuzzleHints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HintsUsed",
                table: "CourseAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HintsJson",
                table: "BookPuzzles",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "HintsVersion",
                table: "BookPuzzles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HintsUsed",
                table: "BookPuzzleAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HintsUsed",
                table: "CourseAttempts");

            migrationBuilder.DropColumn(
                name: "HintsJson",
                table: "BookPuzzles");

            migrationBuilder.DropColumn(
                name: "HintsVersion",
                table: "BookPuzzles");

            migrationBuilder.DropColumn(
                name: "HintsUsed",
                table: "BookPuzzleAttempts");
        }
    }
}
