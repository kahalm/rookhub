using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class OpeningTreeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LibraryGames_Status_OpeningLine",
                table: "LibraryGames",
                columns: new[] { "Status", "OpeningLine" });

            migrationBuilder.CreateIndex(
                name: "IX_GameAnalyses_IsPublic_OpeningLine",
                table: "GameAnalyses",
                columns: new[] { "IsPublic", "OpeningLine" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LibraryGames_Status_OpeningLine",
                table: "LibraryGames");

            migrationBuilder.DropIndex(
                name: "IX_GameAnalyses_IsPublic_OpeningLine",
                table: "GameAnalyses");
        }
    }
}
