using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LeagueGameLinkKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeagueGames_Tnr_Round",
                table: "LeagueGames");

            migrationBuilder.AddColumn<int>(
                name: "LeagueBoard",
                table: "LeagueClubGames",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LeagueMatchNo",
                table: "LeagueClubGames",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LeagueRound",
                table: "LeagueClubGames",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LeagueTnr",
                table: "LeagueClubGames",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueGames_Tnr_Round_MatchNo_Board",
                table: "LeagueGames",
                columns: new[] { "Tnr", "Round", "MatchNo", "Board" });

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClubGames_LeagueTnr_LeagueRound_LeagueMatchNo_LeagueBo~",
                table: "LeagueClubGames",
                columns: new[] { "LeagueTnr", "LeagueRound", "LeagueMatchNo", "LeagueBoard" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeagueGames_Tnr_Round_MatchNo_Board",
                table: "LeagueGames");

            migrationBuilder.DropIndex(
                name: "IX_LeagueClubGames_LeagueTnr_LeagueRound_LeagueMatchNo_LeagueBo~",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "LeagueBoard",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "LeagueMatchNo",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "LeagueRound",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "LeagueTnr",
                table: "LeagueClubGames");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueGames_Tnr_Round",
                table: "LeagueGames",
                columns: new[] { "Tnr", "Round" });
        }
    }
}
