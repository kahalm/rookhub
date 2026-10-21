using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisJobBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Batch",
                table: "AnalysisJobs",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // Die schon offenen Stapel-Aufträge einmal nachtragen (Origin 3/4/5 = Library/Club/League); danach setzt die
            // Pumpe das Merkmal beim Anlegen. Nur offene (Queued/Running/Paused), damit der Lauf klein bleibt.
            migrationBuilder.Sql(@"
UPDATE AnalysisJobs j
  JOIN GameAnalysisPositions p ON p.AnalysisJobId = j.Id
  JOIN GameAnalyses g ON g.Id = p.GameAnalysisId
   SET j.Batch = 1
 WHERE j.Background = 1 AND j.Status IN (0, 1, 2) AND g.Origin IN (3, 4, 5);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Batch",
                table: "AnalysisJobs");
        }
    }
}
