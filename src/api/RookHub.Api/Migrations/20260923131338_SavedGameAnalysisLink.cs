using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <summary>
    /// <c>SavedGames.GameAnalysisId</c>: welche Partie-Analyse die Bewertungskurve der gespeicherten
    /// Partie liefert. Bewusst OHNE Fremdschluessel und ohne Index — die Analyse darf geloescht werden,
    /// ohne die Partie mitzunehmen oder am Constraint zu scheitern, und gelesen wird der Verweis nur
    /// ueber die Partie selbst (Primaerschluessel bzw. ShareToken).
    /// </summary>
    public partial class SavedGameAnalysisLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameAnalysisId",
                table: "SavedGames",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GameAnalysisId",
                table: "SavedGames");
        }
    }
}
