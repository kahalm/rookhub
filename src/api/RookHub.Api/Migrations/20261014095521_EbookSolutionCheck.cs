using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookSolutionCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SolutionCheckState",
                table: "Ebooks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EngineEval",
                table: "EbookDiagrams",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SolutionCheck",
                table: "EbookDiagrams",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SolutionSource",
                table: "EbookDiagrams",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SolutionCheckState",
                table: "Ebooks");

            migrationBuilder.DropColumn(
                name: "EngineEval",
                table: "EbookDiagrams");

            migrationBuilder.DropColumn(
                name: "SolutionCheck",
                table: "EbookDiagrams");

            migrationBuilder.DropColumn(
                name: "SolutionSource",
                table: "EbookDiagrams");
        }
    }
}
