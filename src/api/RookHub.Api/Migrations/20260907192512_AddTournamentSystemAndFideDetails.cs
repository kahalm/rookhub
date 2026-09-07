using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentSystemAndFideDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FideDetailCheckedAt",
                table: "TournamentDirectoryEntries",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "System",
                table: "TournamentDirectoryEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FideDetailCheckedAt",
                table: "TournamentDirectoryEntries");

            migrationBuilder.DropColumn(
                name: "System",
                table: "TournamentDirectoryEntries");
        }
    }
}
