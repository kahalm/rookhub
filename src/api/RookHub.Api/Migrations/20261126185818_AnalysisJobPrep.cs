using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisJobPrep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Prep",
                table: "AnalysisJobs",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisJobs_Prep_Fen",
                table: "AnalysisJobs",
                columns: new[] { "Prep", "Fen" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnalysisJobs_Prep_Fen",
                table: "AnalysisJobs");

            migrationBuilder.DropColumn(
                name: "Prep",
                table: "AnalysisJobs");
        }
    }
}
