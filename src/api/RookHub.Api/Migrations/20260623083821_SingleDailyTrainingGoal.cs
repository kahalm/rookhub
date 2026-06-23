using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class SingleDailyTrainingGoal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bestandsziele zusammenführen: das eine Tageszeit-Ziel = Summe der bisherigen drei
            // Minuten-Ziele (gedeckelt auf 600), BEVOR die beiden Zusatzspalten gedroppt werden.
            // PuzzleMinutes wird anschließend in DailyMinutes umbenannt und trägt damit die Summe.
            migrationBuilder.Sql(
                "UPDATE UserTrainingGoals SET PuzzleMinutes = LEAST(600, PuzzleMinutes + BookMinutes + ChessableMinutes);");
            migrationBuilder.Sql(
                "UPDATE GroupTrainingGoals SET PuzzleMinutes = LEAST(600, PuzzleMinutes + BookMinutes + ChessableMinutes);");

            migrationBuilder.DropColumn(
                name: "BookMinutes",
                table: "UserTrainingGoals");

            migrationBuilder.DropColumn(
                name: "ChessableMinutes",
                table: "UserTrainingGoals");

            migrationBuilder.DropColumn(
                name: "BookMinutes",
                table: "GroupTrainingGoals");

            migrationBuilder.DropColumn(
                name: "ChessableMinutes",
                table: "GroupTrainingGoals");

            migrationBuilder.RenameColumn(
                name: "PuzzleMinutes",
                table: "UserTrainingGoals",
                newName: "DailyMinutes");

            migrationBuilder.RenameColumn(
                name: "PuzzleMinutes",
                table: "GroupTrainingGoals",
                newName: "DailyMinutes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DailyMinutes",
                table: "UserTrainingGoals",
                newName: "PuzzleMinutes");

            migrationBuilder.RenameColumn(
                name: "DailyMinutes",
                table: "GroupTrainingGoals",
                newName: "PuzzleMinutes");

            migrationBuilder.AddColumn<int>(
                name: "BookMinutes",
                table: "UserTrainingGoals",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ChessableMinutes",
                table: "UserTrainingGoals",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BookMinutes",
                table: "GroupTrainingGoals",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ChessableMinutes",
                table: "GroupTrainingGoals",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
