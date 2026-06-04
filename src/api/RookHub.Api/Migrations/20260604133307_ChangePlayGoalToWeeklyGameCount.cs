using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ChangePlayGoalToWeeklyGameCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PlayMinutes",
                table: "UserTrainingGoals",
                newName: "PlayGames");

            migrationBuilder.RenameColumn(
                name: "Seconds",
                table: "PlayTimeDailies",
                newName: "Games");

            migrationBuilder.RenameColumn(
                name: "PlayMinutes",
                table: "GroupTrainingGoals",
                newName: "PlayGames");

            // Umstellung der Semantik der Spielen-Kategorie: von „Spielzeit in Minuten/Tag" auf
            // „Anzahl Rapid-/Classical-Partien pro Woche". Die per RenameColumn übernommenen Altwerte
            // sind in der neuen Bedeutung unsinnig (Sekunden ≠ Partien, Minutenziel ≠ Partienziel),
            // daher zurücksetzen. Das Tracking baut sich beim nächsten Sync neu auf (Cursor = 0 →
            // erneuter Lookback über PlayTime:FirstSyncLookbackDays). Bestehende „Spielen"-Ziele
            // müssen als Partien-pro-Woche neu gesetzt werden.
            migrationBuilder.Sql("DELETE FROM `PlayTimeDailies`;");
            migrationBuilder.Sql("UPDATE `PlayTimeSyncs` SET `LastGameTimestamp` = 0, `LastSyncedAt` = NULL, `LastError` = NULL;");
            migrationBuilder.Sql("UPDATE `GroupTrainingGoals` SET `PlayGames` = 0;");
            migrationBuilder.Sql("UPDATE `UserTrainingGoals` SET `PlayGames` = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PlayGames",
                table: "UserTrainingGoals",
                newName: "PlayMinutes");

            migrationBuilder.RenameColumn(
                name: "Games",
                table: "PlayTimeDailies",
                newName: "Seconds");

            migrationBuilder.RenameColumn(
                name: "PlayGames",
                table: "GroupTrainingGoals",
                newName: "PlayMinutes");
        }
    }
}
