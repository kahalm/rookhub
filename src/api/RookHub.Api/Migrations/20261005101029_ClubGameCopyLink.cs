using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClubGameCopyLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LeagueClubGameId",
                table: "ScoresheetScanArchives",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LeagueClubGameId",
                table: "SavedGames",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScoresheetScanArchives_LeagueClubGameId",
                table: "ScoresheetScanArchives",
                column: "LeagueClubGameId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedGames_LeagueClubGameId",
                table: "SavedGames",
                column: "LeagueClubGameId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScoresheetScanArchives_LeagueClubGameId",
                table: "ScoresheetScanArchives");

            migrationBuilder.DropIndex(
                name: "IX_SavedGames_LeagueClubGameId",
                table: "SavedGames");

            migrationBuilder.DropColumn(
                name: "LeagueClubGameId",
                table: "ScoresheetScanArchives");

            migrationBuilder.DropColumn(
                name: "LeagueClubGameId",
                table: "SavedGames");
        }
    }
}
