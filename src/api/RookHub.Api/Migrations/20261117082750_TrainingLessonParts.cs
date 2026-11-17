using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class TrainingLessonParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bisher eine Zeile je Konto + Datum; alles Bestehende gilt zunächst als „private“. Der Re-Import teilt
            // gemischte Stunden danach in „private“ (eigene Partien) und „general“ (Lehrbeispiele/Übungen).
            migrationBuilder.AddColumn<string>(
                name: "Part",
                table: "TrainingLessons",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "private")
                .Annotation("MySql:CharSet", "utf8mb4");

            // Neuer Index ZUERST: der alte (UserId, Date) trägt den Fremdschlüssel auf UserId — MariaDB lehnt sein
            // Löschen ab (Fehler 1553), solange kein anderer Index mit UserId beginnt.
            migrationBuilder.CreateIndex(
                name: "IX_TrainingLessons_UserId_Date_Part",
                table: "TrainingLessons",
                columns: new[] { "UserId", "Date", "Part" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_TrainingLessons_UserId_Date",
                table: "TrainingLessons");

            // Stunden ganz ohne eigene Partie sind schon jetzt „allgemein“ (der Re-Import ändert an ihnen nichts mehr).
            migrationBuilder.Sql(@"UPDATE TrainingLessons l SET l.Part = 'general'
WHERE EXISTS (SELECT 1 FROM TrainingLessonGames g WHERE g.LessonId = l.Id)
  AND NOT EXISTS (SELECT 1 FROM TrainingLessonGames g WHERE g.LessonId = l.Id AND g.Kind = 'own');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Zurück auf eine Zeile je Datum: den allgemeinen Teil einer geteilten Stunde verwerfen (Kinder kaskadieren).
            migrationBuilder.Sql(@"DELETE l FROM TrainingLessons l
WHERE l.Part = 'general' AND EXISTS (SELECT 1 FROM TrainingLessons p WHERE p.UserId = l.UserId AND p.Date = l.Date AND p.Part = 'private');");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingLessons_UserId_Date",
                table: "TrainingLessons",
                columns: new[] { "UserId", "Date" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_TrainingLessons_UserId_Date_Part",
                table: "TrainingLessons");

            migrationBuilder.DropColumn(
                name: "Part",
                table: "TrainingLessons");
        }
    }
}
