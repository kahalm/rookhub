using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <summary>
    /// Keine Schema-Aenderung: die abgelegte Genauigkeit (<c>GameAnalysis.AccuracyWhite/AccuracyBlack</c>) wurde
    /// bis 0.521.1 ohne lilas „uncertainty bonus" (+1 je Zug) und ohne die ±1000-cp-Kappung gerechnet
    /// (<c>GameAccuracy</c>). Geleert rechnet sie der vorhandene Nachtrag der Partienliste neu
    /// (<c>SavedGameService.AccuracyBackfillPerCall</c> je Aufruf) — sonst zeigte die Liste andere Zahlen als die
    /// Partie-Seite, die live rechnet. Down stellt nichts wieder her (die alten Zahlen waren falsch).
    /// </summary>
    public partial class RecomputeAccuracyLichessBonus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE GameAnalyses SET AccuracyWhite = NULL, AccuracyBlack = NULL WHERE AccuracyWhite IS NOT NULL OR AccuracyBlack IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
