using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class BroadcastClubGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PlayedOn",
                table: "LeagueClubGames",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "LeagueClubGames",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SourceRound",
                table: "LeagueClubGames",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "LeagueClubGames",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "LiveUntil",
                table: "LeagueBroadcasts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRoundAt",
                table: "LeagueBroadcasts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClubGames_ClubId_SourceUrl",
                table: "LeagueClubGames",
                columns: new[] { "ClubId", "SourceUrl" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeagueClubGames_ClubId_SourceUrl",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "PlayedOn",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "SourceRound",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "LiveUntil",
                table: "LeagueBroadcasts");

            migrationBuilder.DropColumn(
                name: "NextRoundAt",
                table: "LeagueBroadcasts");
        }
    }
}
