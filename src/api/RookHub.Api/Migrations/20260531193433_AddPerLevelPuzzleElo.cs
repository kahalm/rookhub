using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPerLevelPuzzleElo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VisualizationLevel",
                table: "PuzzleAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PuzzleEloViz1",
                table: "AppUsers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PuzzleEloViz2",
                table: "AppUsers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PuzzleEloViz3",
                table: "AppUsers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PuzzleEloViz4",
                table: "AppUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleAttempts_UserId_VisualizationLevel",
                table: "PuzzleAttempts",
                columns: new[] { "UserId", "VisualizationLevel" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PuzzleAttempts_UserId_VisualizationLevel",
                table: "PuzzleAttempts");

            migrationBuilder.DropColumn(
                name: "VisualizationLevel",
                table: "PuzzleAttempts");

            migrationBuilder.DropColumn(
                name: "PuzzleEloViz1",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "PuzzleEloViz2",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "PuzzleEloViz3",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "PuzzleEloViz4",
                table: "AppUsers");
        }
    }
}
