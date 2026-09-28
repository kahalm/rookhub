using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class WorksheetSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // LEER, und das ist ein Fehler — hier hätte ShareToken/SharedAt/SolutionMoves + Unique-Index entstehen müssen.
            // Die Migration wurde nach einem Rebase neu erzeugt, während der Modell-Schnappschuss
            // die Änderung schon enthielt: EF fand keinen Unterschied und schrieb nichts hinein.
            // Der Rumpf bleibt leer, weil diese Migration auf Dev und Prod als angewendet verbucht
            // ist; nachgeholt wird sie von „WorksheetColumnsRepair" (2026-09-28).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
