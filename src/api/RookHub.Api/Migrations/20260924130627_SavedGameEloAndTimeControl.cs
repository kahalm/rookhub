using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class SavedGameEloAndTimeControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BlackElo",
                table: "SavedGames",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HeadersScanned",
                table: "SavedGames",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TimeControl",
                table: "SavedGames",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "WhiteElo",
                table: "SavedGames",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlackElo",
                table: "SavedGames");

            migrationBuilder.DropColumn(
                name: "HeadersScanned",
                table: "SavedGames");

            migrationBuilder.DropColumn(
                name: "TimeControl",
                table: "SavedGames");

            migrationBuilder.DropColumn(
                name: "WhiteElo",
                table: "SavedGames");
        }
    }
}
