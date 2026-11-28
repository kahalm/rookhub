using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LibraryPlayedYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "PlayedYear",
                table: "LibraryGames",
                type: "smallint",
                nullable: true);

            // Das Jahr des BESTANDS nachtragen (Meisterpartien-Suche, 1.038.0): aus dem Datum, sonst aus den Kopfzeilen
            // [Date] bzw. [EventDate] — dort steht bei 25 612 von 130 572 Partien nur das Jahr („1904.??.??“), und PlayedOn
            // ist dann leer. Dieselbe Regel wie LibraryGameReader.YearOf; nur die ersten 4 000 Zeichen (der Kopf), nicht
            // die 338 MB Partietext. Gemessen am echten Bestand: 2,5 s.
            migrationBuilder.Sql(@"
UPDATE LibraryGames SET PlayedYear = COALESCE(
    YEAR(PlayedOn),
    NULLIF(CAST(REGEXP_SUBSTR(REGEXP_SUBSTR(LEFT(Pgn, 4000), '\\[Date ""[0-9]{4}'), '[0-9]{4}') AS UNSIGNED), 0),
    NULLIF(CAST(REGEXP_SUBSTR(REGEXP_SUBSTR(LEFT(Pgn, 4000), '\\[EventDate ""[0-9]{4}'), '[0-9]{4}') AS UNSIGNED), 0))");
            migrationBuilder.Sql("UPDATE LibraryGames SET PlayedYear = NULL WHERE PlayedYear < 1000 OR PlayedYear > 2100");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryGames_Eco",
                table: "LibraryGames",
                column: "Eco");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryGames_PlayedYear",
                table: "LibraryGames",
                column: "PlayedYear");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LibraryGames_Eco",
                table: "LibraryGames");

            migrationBuilder.DropIndex(
                name: "IX_LibraryGames_PlayedYear",
                table: "LibraryGames");

            migrationBuilder.DropColumn(
                name: "PlayedYear",
                table: "LibraryGames");
        }
    }
}
