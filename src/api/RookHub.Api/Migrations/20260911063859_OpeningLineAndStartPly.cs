using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class OpeningLineAndStartPly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FirstCommentedPly",
                table: "LibraryGames",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpeningLine",
                table: "LibraryGames",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "SuggestedStartPly",
                table: "GameAnalyses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryGames_OpeningLine",
                table: "LibraryGames",
                column: "OpeningLine");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LibraryGames_OpeningLine",
                table: "LibraryGames");

            migrationBuilder.DropColumn(
                name: "FirstCommentedPly",
                table: "LibraryGames");

            migrationBuilder.DropColumn(
                name: "OpeningLine",
                table: "LibraryGames");

            migrationBuilder.DropColumn(
                name: "SuggestedStartPly",
                table: "GameAnalyses");
        }
    }
}
