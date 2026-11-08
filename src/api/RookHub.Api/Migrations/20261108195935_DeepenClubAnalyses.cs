using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class DeepenClubAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0.909.0 („vertiefe Ligapartien auf 30"): die vorhandenen Hauptanalysen der Vereinspartien (Origin Club = 4, ohne
            // feste Engine — die Lc0-Zweitanalyse bleibt) bekommen nachträglich die Vertiefung auf 30 mit fünf Linien. Die
            // Pumpe der Partie-Analysen rechnet sie als Stapel-Hintergrundarbeit nach, die jüngste zuerst; RefinedAt bleibt leer.
            migrationBuilder.Sql(
                "UPDATE GameAnalyses SET RefineDepth = 30, RefineMultiPv = 5, RefinedAt = NULL " +
                "WHERE Origin = 4 AND EngineId IS NULL AND RefineDepth IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nicht umkehrbar ohne Verlust: welche Zeilen die Vertiefung erst hier bekamen, steht nirgends. Bewusst leer.
        }
    }
}
