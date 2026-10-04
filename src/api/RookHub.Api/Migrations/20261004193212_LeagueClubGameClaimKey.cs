using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LeagueClubGameClaimKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimKeyHash",
                table: "LeagueClubGames",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClubGames_ClaimKeyHash",
                table: "LeagueClubGames",
                column: "ClaimKeyHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeagueClubGames_ClaimKeyHash",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "ClaimKeyHash",
                table: "LeagueClubGames");
        }
    }
}
