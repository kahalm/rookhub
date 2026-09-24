using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// <c>GameAnalyses.AccuracyWhite</c>/<c>AccuracyBlack</c>: die Genauigkeit beider Seiten (Lichess-Formel,
    /// <c>GameAccuracy</c>), gerechnet beim Fertigwerden der Analyse und hier abgelegt, damit die Partienliste
    /// sie zeigen kann, ohne je Partie die Stellungen zu laden. Nullbar: der Altbestand wird beim ersten
    /// Listenaufruf nachgetragen, und eine Seite ohne bewertbaren Zug hat keine Zahl.
    /// </summary>
    public partial class GameAnalysisAccuracy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AccuracyBlack",
                table: "GameAnalyses",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AccuracyWhite",
                table: "GameAnalyses",
                type: "double",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccuracyBlack",
                table: "GameAnalyses");

            migrationBuilder.DropColumn(
                name: "AccuracyWhite",
                table: "GameAnalyses");
        }
    }
}
