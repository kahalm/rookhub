using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPuzzleChallengeSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PuzzleChallenges_Puzzles_PuzzleId",
                table: "PuzzleChallenges");

            migrationBuilder.DropIndex(
                name: "IX_PuzzleChallenges_PuzzleId",
                table: "PuzzleChallenges");

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "PuzzleChallenges",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleChallenges_Source_PuzzleId",
                table: "PuzzleChallenges",
                columns: new[] { "Source", "PuzzleId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PuzzleChallenges_Source_PuzzleId",
                table: "PuzzleChallenges");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "PuzzleChallenges");

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleChallenges_PuzzleId",
                table: "PuzzleChallenges",
                column: "PuzzleId");

            migrationBuilder.AddForeignKey(
                name: "FK_PuzzleChallenges_Puzzles_PuzzleId",
                table: "PuzzleChallenges",
                column: "PuzzleId",
                principalTable: "Puzzles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
