using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ChessableImportFetchProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChaptersDone",
                table: "ChessableImports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ChaptersTotal",
                table: "ChessableImports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FetchJobId",
                table: "ChessableImports",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "LinesDone",
                table: "ChessableImports",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChaptersDone",
                table: "ChessableImports");

            migrationBuilder.DropColumn(
                name: "ChaptersTotal",
                table: "ChessableImports");

            migrationBuilder.DropColumn(
                name: "FetchJobId",
                table: "ChessableImports");

            migrationBuilder.DropColumn(
                name: "LinesDone",
                table: "ChessableImports");
        }
    }
}
