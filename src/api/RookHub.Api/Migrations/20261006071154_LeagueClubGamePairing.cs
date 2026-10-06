using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LeagueClubGamePairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LeagueGameId",
                table: "LeagueClubGames",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClubGames_LeagueGameId",
                table: "LeagueClubGames",
                column: "LeagueGameId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeagueClubGames_LeagueGameId",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "LeagueGameId",
                table: "LeagueClubGames");
        }
    }
}
