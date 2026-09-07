using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentDirectoryAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AgeGroups",
                table: "TournamentDirectoryEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                table: "TournamentDirectoryEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsLeague",
                table: "TournamentDirectoryEntries",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "TournamentDirectoryEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgeGroups",
                table: "TournamentDirectoryEntries");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "TournamentDirectoryEntries");

            migrationBuilder.DropColumn(
                name: "IsLeague",
                table: "TournamentDirectoryEntries");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "TournamentDirectoryEntries");
        }
    }
}
