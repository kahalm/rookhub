using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoardTheme",
                table: "UserProfiles",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "BookStockfishDepth",
                table: "UserProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PieceSet",
                table: "UserProfiles",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PuzzleDifficulty",
                table: "UserProfiles",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "StockfishDepth",
                table: "UserProfiles",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoardTheme",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "BookStockfishDepth",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "PieceSet",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "PuzzleDifficulty",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "StockfishDepth",
                table: "UserProfiles");
        }
    }
}
