using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExplanationViewpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Die bisherigen Texte sprachen jeden an, der den Fehler gemacht hatte — bei den Zügen des Gegners also den
            // Falschen („Your move 14. Nb5" für Weiß, obwohl der Besitzer Schwarz spielte). Sie sind aus eigener Hardware
            // billig neu zu schreiben; der Knopf „Fehler erklären lassen" erscheint wieder, sobald keine mehr da sind.
            migrationBuilder.Sql("DELETE FROM `GameMoveExplanations`;");

            migrationBuilder.AddColumn<string>(
                name: "Viewpoint",
                table: "GameMoveExplanations",
                type: "varchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Viewpoint",
                table: "GameMoveExplanations");
        }
    }
}
