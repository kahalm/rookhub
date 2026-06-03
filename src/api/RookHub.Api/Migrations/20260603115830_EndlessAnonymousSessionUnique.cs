using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EndlessAnonymousSessionUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EndlessProgresses_AnonymousSessionId",
                table: "EndlessProgresses");

            // Vorhandene Duplikate je anonymer Session entfernen (ältesten Eintrag behalten),
            // damit der folgende Unique-Index ohne Konflikt angelegt werden kann.
            migrationBuilder.Sql(@"
                DELETE p1 FROM EndlessProgresses p1
                INNER JOIN EndlessProgresses p2
                    ON p1.AnonymousSessionId = p2.AnonymousSessionId
                    AND p1.AnonymousSessionId IS NOT NULL
                    AND p1.Id > p2.Id;");

            migrationBuilder.CreateIndex(
                name: "IX_EndlessProgresses_AnonymousSessionId",
                table: "EndlessProgresses",
                column: "AnonymousSessionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EndlessProgresses_AnonymousSessionId",
                table: "EndlessProgresses");

            migrationBuilder.CreateIndex(
                name: "IX_EndlessProgresses_AnonymousSessionId",
                table: "EndlessProgresses",
                column: "AnonymousSessionId");
        }
    }
}
