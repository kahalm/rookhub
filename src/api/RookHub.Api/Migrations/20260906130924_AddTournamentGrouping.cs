using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentGrouping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseName",
                table: "TournamentDirectoryEntries",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "GroupKey",
                table: "TournamentDirectoryEntries",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentDirectoryEntries_GroupKey",
                table: "TournamentDirectoryEntries",
                column: "GroupKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TournamentDirectoryEntries_GroupKey",
                table: "TournamentDirectoryEntries");

            migrationBuilder.DropColumn(
                name: "BaseName",
                table: "TournamentDirectoryEntries");

            migrationBuilder.DropColumn(
                name: "GroupKey",
                table: "TournamentDirectoryEntries");
        }
    }
}
