using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LeagueClubGameRealNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlackRealFide",
                table: "LeagueClubGames",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "BlackRealName",
                table: "LeagueClubGames",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "WhiteRealFide",
                table: "LeagueClubGames",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "WhiteRealName",
                table: "LeagueClubGames",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlackRealFide",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "BlackRealName",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "WhiteRealFide",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "WhiteRealName",
                table: "LeagueClubGames");
        }
    }
}
