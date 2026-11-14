using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClubGameSummaryAutoRequestedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AutoRequestedAt",
                table: "ClubGameSummaries",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubGameSummaries_AutoRequestedAt",
                table: "ClubGameSummaries",
                column: "AutoRequestedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClubGameSummaries_AutoRequestedAt",
                table: "ClubGameSummaries");

            migrationBuilder.DropColumn(
                name: "AutoRequestedAt",
                table: "ClubGameSummaries");
        }
    }
}
