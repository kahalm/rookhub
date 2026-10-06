using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LeagueClubGameArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "LeagueClubGames",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReplacedById",
                table: "LeagueClubGames",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClubGames_ArchivedAt",
                table: "LeagueClubGames",
                column: "ArchivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeagueClubGames_ArchivedAt",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "ReplacedById",
                table: "LeagueClubGames");
        }
    }
}
