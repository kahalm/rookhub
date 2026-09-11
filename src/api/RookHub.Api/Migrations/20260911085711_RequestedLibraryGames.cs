using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class RequestedLibraryGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LibraryGameId",
                table: "GameAnalyses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameAnalyses_UserId_LibraryGameId",
                table: "GameAnalyses",
                columns: new[] { "UserId", "LibraryGameId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameAnalyses_UserId_LibraryGameId",
                table: "GameAnalyses");

            migrationBuilder.DropColumn(
                name: "LibraryGameId",
                table: "GameAnalyses");
        }
    }
}
