using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class GameAnalysisLeagueClubGame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LeagueClubGameId",
                table: "GameAnalyses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameAnalyses_LeagueClubGameId",
                table: "GameAnalyses",
                column: "LeagueClubGameId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameAnalyses_LeagueClubGameId",
                table: "GameAnalyses");

            migrationBuilder.DropColumn(
                name: "LeagueClubGameId",
                table: "GameAnalyses");
        }
    }
}
