using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class GameAnalysisMoveClasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MoveClasses",
                table: "GameAnalyses",
                type: "TEXT",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "MoveClassesVersion",
                table: "GameAnalyses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameAnalyses_Status_MoveClassesVersion",
                table: "GameAnalyses",
                columns: new[] { "Status", "MoveClassesVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameAnalyses_Status_MoveClassesVersion",
                table: "GameAnalyses");

            migrationBuilder.DropColumn(
                name: "MoveClasses",
                table: "GameAnalyses");

            migrationBuilder.DropColumn(
                name: "MoveClassesVersion",
                table: "GameAnalyses");
        }
    }
}
