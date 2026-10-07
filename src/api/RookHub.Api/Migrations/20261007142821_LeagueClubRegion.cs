using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <summary>
    /// Verein → Region statt Quelle (2026-10-07, Schachkreis Zugspitze als dritte Liga-Quelle): SK Weilheim spielt im
    /// Ligamanager UND im Schachkreis Zugspitze, die Startseite eines Vereins zeigt deshalb alle Ligen seiner REGION
    /// (<c>LeagueRegions</c>). Die Spalte <c>LeagueClubs.Source</c> wird umbenannt (kein Drop+Add — die Daten bleiben) und
    /// umgeschrieben: <c>NULL</c> (chess-results) → <c>tirol</c>, <c>ligamanager</c> → <c>bayern</c>; danach NOT NULL.
    /// </summary>
    public partial class LeagueClubRegion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Source",
                table: "LeagueClubs",
                newName: "Region");

            migrationBuilder.Sql(@"UPDATE LeagueClubs SET Region = CASE
                WHEN Region IS NULL OR Region = '' THEN 'tirol'
                WHEN Region = 'ligamanager' THEN 'bayern'
                ELSE Region END;");

            migrationBuilder.AlterColumn<string>(
                name: "Region",
                table: "LeagueClubs",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Region",
                table: "LeagueClubs",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(@"UPDATE LeagueClubs SET Region = CASE
                WHEN Region = 'tirol' THEN NULL
                WHEN Region = 'bayern' THEN 'ligamanager'
                ELSE Region END;");

            migrationBuilder.RenameColumn(
                name: "Region",
                table: "LeagueClubs",
                newName: "Source");
        }
    }
}
