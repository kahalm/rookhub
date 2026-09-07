using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectoryPublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // REIHENFOLGE auf den Ausgeblendeten: Spalte anlegen, Werte uebernehmen, NEUEN Index
            // anlegen, DANN den alten wegnehmen. Zwei Gruende, beide gegen echtes MariaDB
            // gemessen (die InMemory-Tests sehen keinen davon):
            //
            // 1. MariaDB weigert sich, `IX_..._UserId_ChessResultsId` zu loeschen, solange es der
            //    einzige Index ist, der den Fremdschluessel auf `UserId` bedient („Cannot drop
            //    index …: needed in a foreign key constraint"). Der neue Index beginnt ebenfalls
            //    mit `UserId` und uebernimmt diese Aufgabe — deshalb muss er vorher stehen.
            // 2. Ein Spaltentausch mit DropColumn/AddColumn WIRFT die Werte weg. Hier haengt
            //    daran, welche Turniere ein Nutzer fuer sich ausgeblendet hat; ohne die
            //    Uebernahme waeren alle diese Entscheidungen beim Deploy still verloren.
            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "TournamentDirectoryIgnores",
                type: "varchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(
                "UPDATE TournamentDirectoryIgnores SET PublicId = ChessResultsId WHERE PublicId = ''");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentDirectoryIgnores_UserId_PublicId",
                table: "TournamentDirectoryIgnores",
                columns: new[] { "UserId", "PublicId" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_TournamentDirectoryIgnores_UserId_ChessResultsId",
                table: "TournamentDirectoryIgnores");

            migrationBuilder.DropColumn(
                name: "ChessResultsId",
                table: "TournamentDirectoryIgnores");

            migrationBuilder.AlterColumn<string>(
                name: "ChessResultsId",
                table: "TournamentDirectoryEntries",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "TournamentDirectoryEntries",
                type: "varchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            // Die bestehenden Eintraege bekommen ihre Identitaet aus der chess-results-Nummer:
            // Adressen, Abos und Teilen-Links, die es schon gibt, bleiben damit gueltig.
            //
            // Das MUSS vor dem Unique-Index laufen. Ohne diesen Nachtrag tragen alle Zeilen den
            // Vorgabewert "" und der Index scheitert an tausenden Dubletten — die Migration
            // brach dann beim Start der API ab, und zwar bei JEDEM Start.
            migrationBuilder.Sql(
                "UPDATE TournamentDirectoryEntries SET PublicId = ChessResultsId WHERE PublicId = ''");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentDirectoryEntries_PublicId",
                table: "TournamentDirectoryEntries",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TournamentDirectoryEntries_PublicId",
                table: "TournamentDirectoryEntries");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "TournamentDirectoryEntries");

            migrationBuilder.AddColumn<string>(
                name: "ChessResultsId",
                table: "TournamentDirectoryIgnores",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "TournamentDirectoryEntries",
                keyColumn: "ChessResultsId",
                keyValue: null,
                column: "ChessResultsId",
                value: "");

            migrationBuilder.AlterColumn<string>(
                name: "ChessResultsId",
                table: "TournamentDirectoryEntries",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            // Die Laengengrenze ist beim Rueckweg enger (20 statt 24). Eine Identitaet, die dort
            // nicht hineinpasst, ist eine, die es vor dieser Migration nicht geben konnte
            // (`f<FIDE-Nummer>` passt) — sie bleibt leer, statt den Rueckweg abbrechen zu lassen.
            migrationBuilder.Sql(
                "UPDATE TournamentDirectoryIgnores SET ChessResultsId = PublicId "
                + "WHERE ChessResultsId = '' AND CHAR_LENGTH(PublicId) <= 20");

            // Gleiche Reihenfolge wie hinauf, gleicher Grund: erst der alte Index (er bedient den
            // Fremdschluessel auf `UserId`), dann darf der neue weg.
            migrationBuilder.CreateIndex(
                name: "IX_TournamentDirectoryIgnores_UserId_ChessResultsId",
                table: "TournamentDirectoryIgnores",
                columns: new[] { "UserId", "ChessResultsId" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_TournamentDirectoryIgnores_UserId_PublicId",
                table: "TournamentDirectoryIgnores");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "TournamentDirectoryIgnores");
        }
    }
}
