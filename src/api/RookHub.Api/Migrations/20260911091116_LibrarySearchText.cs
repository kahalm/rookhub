using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LibrarySearchText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "LibraryGames",
                type: "varchar(190)",
                maxLength: 190,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // Der Suchtext des BESTANDS wird hier nachgetragen — er faellt beim Einlesen ohnehin an,
            // es gab die Spalte nur noch nicht. Eine einzige Anweisung fuer 130 000 Zeilen.
            migrationBuilder.Sql(
                "UPDATE LibraryGames SET SearchText = LEFT(LOWER(CONCAT_WS(' ', White, Black, Event, Annotator)), 190)");

            // VOLLTEXT statt eines gewoehnlichen Index: gemessen am echten Bestand kostete die Suche
            // ueber die vier Einzelspalten 4,5 s, ueber eine schmale indizierte Spalte mit Sortierung
            // nach der Eignungsnote sogar 50 s — mit dem Volltext-Index sind es 13 ms. EF kann diese
            // Indexart nicht ausdruecken, deshalb als SQL (und in Down() wieder weg).
            migrationBuilder.Sql("ALTER TABLE LibraryGames ADD FULLTEXT INDEX FT_LibraryGames_SearchText (SearchText)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE LibraryGames DROP INDEX FT_LibraryGames_SearchText");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "LibraryGames");
        }
    }
}
