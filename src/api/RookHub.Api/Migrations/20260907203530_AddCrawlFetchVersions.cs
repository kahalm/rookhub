using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCrawlFetchVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FideDetailVersion",
                table: "TournamentDirectoryEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RoundPlanVersion",
                table: "TournamentDirectoryEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeamHintVersion",
                table: "TournamentDirectoryEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FideDetailVersion",
                table: "TournamentDirectoryEntries");

            migrationBuilder.DropColumn(
                name: "RoundPlanVersion",
                table: "TournamentDirectoryEntries");

            migrationBuilder.DropColumn(
                name: "TeamHintVersion",
                table: "TournamentDirectoryEntries");
        }
    }
}
