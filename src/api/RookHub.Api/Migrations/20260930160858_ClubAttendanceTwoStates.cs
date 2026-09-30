using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClubAttendanceTwoStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reine Datenkorrektur, kein Schema: „entschuldigt" (2) gab es nur in 0.613.0 und wird nicht mehr vergeben
            // (Wunsch 2026-09-30) — ein so erfasstes Kind war nicht da, also „gefehlt" (3). Ohne das bliebe der Eintrag
            // als unbekannter Status liegen: in der Tabelle leer, in der Quote aber mitgezählt.
            migrationBuilder.Sql("UPDATE `ClubAttendances` SET `Status` = 3 WHERE `Status` = 2;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nicht umkehrbar: welche „gefehlt" einmal „entschuldigt" waren, steht nirgends mehr.
        }
    }
}
