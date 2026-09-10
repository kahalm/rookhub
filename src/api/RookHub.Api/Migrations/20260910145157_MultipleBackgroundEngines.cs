using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class MultipleBackgroundEngines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reihenfolge ist hier der ganze Punkt: EF hat das Wegwerfen VOR das Anlegen gesetzt,
            // damit waere die hinterlegte Hintergrund-Engine jedes Nutzers verloren gewesen (und die
            // Analyse-Auftraege liefen ins Leere, bis jemand sie neu waehlt). Erst anlegen, dann den
            // Bestand hinuebertragen, dann die alte Spalte fallen lassen.
            migrationBuilder.AddColumn<string>(
                name: "BackgroundEngineIds",
                table: "LichessEngineCredentials",
                type: "varchar(600)",
                maxLength: 600,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(
                "UPDATE LichessEngineCredentials SET BackgroundEngineIds = BackgroundEngineId " +
                "WHERE BackgroundEngineId IS NOT NULL AND BackgroundEngineId <> ''");

            migrationBuilder.DropColumn(
                name: "BackgroundEngineId",
                table: "LichessEngineCredentials");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundEngineId",
                table: "LichessEngineCredentials",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // Zurueck bleibt die ERSTE Kennung — mehr traegt die alte Spalte nicht.
            migrationBuilder.Sql(
                "UPDATE LichessEngineCredentials SET BackgroundEngineId = " +
                "SUBSTRING_INDEX(BackgroundEngineIds, ',', 1) WHERE BackgroundEngineIds IS NOT NULL");

            migrationBuilder.DropColumn(
                name: "BackgroundEngineIds",
                table: "LichessEngineCredentials");
        }
    }
}
