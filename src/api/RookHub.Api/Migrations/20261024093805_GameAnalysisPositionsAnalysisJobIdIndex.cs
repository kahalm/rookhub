using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class GameAnalysisPositionsAnalysisJobIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_GameAnalysisPositions_AnalysisJobId_GameAnalysisId",
                table: "GameAnalysisPositions",
                columns: new[] { "AnalysisJobId", "GameAnalysisId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameAnalysisPositions_AnalysisJobId_GameAnalysisId",
                table: "GameAnalysisPositions");
        }
    }
}
