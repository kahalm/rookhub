using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisJobWorkerIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AnalysisJobs_EngineId_LastRunAt",
                table: "AnalysisJobs",
                columns: new[] { "EngineId", "LastRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisJobs_EngineId_Status_Priority_Background_Batch_Creat~",
                table: "AnalysisJobs",
                columns: new[] { "EngineId", "Status", "Priority", "Background", "Batch", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisJobs_Status_EngineId",
                table: "AnalysisJobs",
                columns: new[] { "Status", "EngineId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnalysisJobs_EngineId_LastRunAt",
                table: "AnalysisJobs");

            migrationBuilder.DropIndex(
                name: "IX_AnalysisJobs_EngineId_Status_Priority_Background_Batch_Creat~",
                table: "AnalysisJobs");

            migrationBuilder.DropIndex(
                name: "IX_AnalysisJobs_Status_EngineId",
                table: "AnalysisJobs");
        }
    }
}
