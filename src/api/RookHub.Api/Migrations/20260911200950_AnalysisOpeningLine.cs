using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisOpeningLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OpeningLine",
                table: "GameAnalyses",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GameAnalyses_OpeningLine",
                table: "GameAnalyses",
                column: "OpeningLine");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameAnalyses_OpeningLine",
                table: "GameAnalyses");

            migrationBuilder.DropColumn(
                name: "OpeningLine",
                table: "GameAnalyses");
        }
    }
}
