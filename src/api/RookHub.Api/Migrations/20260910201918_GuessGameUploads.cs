using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class GuessGameUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShareAsHouseEngine",
                table: "LichessEngineCredentials",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EngineOwnerUserId",
                table: "GameAnalyses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "GameAnalyses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EngineOwnerUserId",
                table: "AnalysisJobs",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShareAsHouseEngine",
                table: "LichessEngineCredentials");

            migrationBuilder.DropColumn(
                name: "EngineOwnerUserId",
                table: "GameAnalyses");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "GameAnalyses");

            migrationBuilder.DropColumn(
                name: "EngineOwnerUserId",
                table: "AnalysisJobs");
        }
    }
}
