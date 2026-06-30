using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAnonBookAttemptUniqueDedup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defensiv: etwaige anonyme Doppel-Lösungen (gleiches (BookPuzzleId, AnonymousSessionId))
            // entfernen, bevor der Unique-Index angelegt wird — sonst scheitert die Migration. Behält die
            // jeweils älteste Zeile (kleinste Id). Authentifizierte Zeilen (AnonymousSessionId IS NULL)
            // sind nicht betroffen (NULL != NULL).
            migrationBuilder.Sql(@"
                DELETE a1 FROM BookPuzzleAttempts a1
                INNER JOIN BookPuzzleAttempts a2
                    ON a1.BookPuzzleId = a2.BookPuzzleId
                   AND a1.AnonymousSessionId = a2.AnonymousSessionId
                   AND a1.AnonymousSessionId IS NOT NULL
                   AND a1.Id > a2.Id;");

            migrationBuilder.DropIndex(
                name: "IX_BookPuzzleAttempts_BookPuzzleId_AnonymousSessionId",
                table: "BookPuzzleAttempts");

            migrationBuilder.CreateIndex(
                name: "IX_BookPuzzleAttempts_BookPuzzleId_AnonymousSessionId",
                table: "BookPuzzleAttempts",
                columns: new[] { "BookPuzzleId", "AnonymousSessionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookPuzzleAttempts_BookPuzzleId_AnonymousSessionId",
                table: "BookPuzzleAttempts");

            migrationBuilder.CreateIndex(
                name: "IX_BookPuzzleAttempts_BookPuzzleId_AnonymousSessionId",
                table: "BookPuzzleAttempts",
                columns: new[] { "BookPuzzleId", "AnonymousSessionId" });
        }
    }
}
