using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedGameUniqueDedup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defensiv: etwaige Alt-Duplikate (gleicher User+Source+ExternalId) entfernen, bevor der
            // Unique-Index angelegt wird — sonst scheitert die Migration. Behält die jeweils älteste Zeile
            // (kleinste Id). NULL-ExternalId zählt nicht als Duplikat (MySQL: NULL != NULL).
            migrationBuilder.Sql(@"
                DELETE s1 FROM SavedGames s1
                INNER JOIN SavedGames s2
                    ON s1.UserId = s2.UserId
                   AND s1.Source = s2.Source
                   AND s1.ExternalId = s2.ExternalId
                   AND s1.ExternalId IS NOT NULL
                   AND s1.Id > s2.Id;");

            migrationBuilder.DropIndex(
                name: "IX_SavedGames_UserId_Source_ExternalId",
                table: "SavedGames");

            migrationBuilder.CreateIndex(
                name: "IX_SavedGames_UserId_Source_ExternalId",
                table: "SavedGames",
                columns: new[] { "UserId", "Source", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SavedGames_UserId_Source_ExternalId",
                table: "SavedGames");

            migrationBuilder.CreateIndex(
                name: "IX_SavedGames_UserId_Source_ExternalId",
                table: "SavedGames",
                columns: new[] { "UserId", "Source", "ExternalId" });
        }
    }
}
