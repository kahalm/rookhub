using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LeagueTournamentSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Boards",
                table: "LeagueTournaments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "LeagueTournaments",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SourceRef",
                table: "LeagueTournaments",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Boards",
                table: "LeagueTournaments");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "LeagueTournaments");

            migrationBuilder.DropColumn(
                name: "SourceRef",
                table: "LeagueTournaments");
        }
    }
}
