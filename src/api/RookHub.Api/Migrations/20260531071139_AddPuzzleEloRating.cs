using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPuzzleEloRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EloAfter",
                table: "PuzzleAttempts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EloChange",
                table: "PuzzleAttempts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PuzzleElo",
                table: "AppUsers",
                type: "int",
                nullable: false,
                defaultValue: 1500);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EloAfter",
                table: "PuzzleAttempts");

            migrationBuilder.DropColumn(
                name: "EloChange",
                table: "PuzzleAttempts");

            migrationBuilder.DropColumn(
                name: "PuzzleElo",
                table: "AppUsers");
        }
    }
}
