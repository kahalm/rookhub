using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class GameAnalysisEngineName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EngineName",
                table: "GameAnalyses",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // Nachtrag aus den noch lebenden Registrierungen (R4-001): bereits aufgeräumte Kennungen lassen sich nicht mehr
            // zuordnen — die Leser vergleichen deshalb zusätzlich weiter über die Kennung.
            migrationBuilder.Sql(
                "UPDATE GameAnalyses a JOIN ExternalEngineRegistrations r ON r.Id = a.EngineId "
                + "SET a.EngineName = r.Name WHERE a.EngineId IS NOT NULL AND a.EngineName IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EngineName",
                table: "GameAnalyses");
        }
    }
}
