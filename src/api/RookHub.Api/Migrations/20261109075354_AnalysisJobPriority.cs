using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisJobPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Priority",
                table: "AnalysisJobs",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0);

            // Offene Aufträge einstufen (JobPriority): eigene Vertiefung 1, Stapel vorläufig 3 (alte Vereinspartie),
            // dann nach Herkunft der Analyse — über die Stellung (Partie-Analyse) bzw. die Vergleichslinie.
            migrationBuilder.Sql("UPDATE AnalysisJobs SET Priority = 1 WHERE Background = 1 AND Batch = 0 AND Status IN (0,1,2)");
            migrationBuilder.Sql("UPDATE AnalysisJobs SET Priority = 3 WHERE Batch = 1 AND Status IN (0,1,2)");
            migrationBuilder.Sql(@"UPDATE AnalysisJobs j
                JOIN GameAnalysisPositions p ON p.AnalysisJobId = j.Id
                JOIN GameAnalyses g ON g.Id = p.GameAnalysisId
                SET j.Priority = CASE g.Origin WHEN 5 THEN 4 WHEN 3 THEN 5
                    ELSE IF(g.CreatedAt >= UTC_TIMESTAMP() - INTERVAL 14 DAY, 2, 3) END
                WHERE j.Batch = 1 AND j.Status IN (0,1,2)");
            migrationBuilder.Sql(@"UPDATE AnalysisJobs j
                JOIN MoveComparisonLines l ON l.AnalysisJobId = j.Id
                JOIN MoveComparisons c ON c.Id = l.MoveComparisonId
                JOIN GameAnalyses g ON g.Id = c.GameAnalysisId
                SET j.Priority = IF(g.CreatedAt >= UTC_TIMESTAMP() - INTERVAL 14 DAY, 2, 3)
                WHERE j.Batch = 1 AND j.Status IN (0,1,2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Priority",
                table: "AnalysisJobs");
        }
    }
}
