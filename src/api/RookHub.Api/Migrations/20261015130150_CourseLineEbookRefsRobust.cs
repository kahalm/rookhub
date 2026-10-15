using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class CourseLineEbookRefsRobust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reihenfolge von Hand (MariaDB): erst die neuen Spalten füllen, dann den neuen eindeutigen Index anlegen
            // (EbookId = 0 für alle hätte ihn verletzt) und erst danach den alten löschen — der trägt bis dahin den
            // Fremdschlüssel auf BookPuzzleId (sonst Fehler 1553).
            migrationBuilder.AddColumn<int>(
                name: "EbookId",
                table: "CourseLineEbookRefs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Page",
                table: "CourseLineEbookRefs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Placement",
                table: "CourseLineEbookRefs",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(@"UPDATE CourseLineEbookRefs r JOIN EbookDiagrams d ON d.Id = r.EbookDiagramId
                SET r.EbookId = d.EbookId, r.Page = d.Page, r.Placement = LEFT(SUBSTRING_INDEX(TRIM(d.Fen), ' ', 1), 100);");
            migrationBuilder.Sql("DELETE FROM CourseLineEbookRefs WHERE EbookId = 0;");

            migrationBuilder.CreateIndex(
                name: "IX_CourseLineEbookRefs_BookPuzzleId_EbookId",
                table: "CourseLineEbookRefs",
                columns: new[] { "BookPuzzleId", "EbookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseLineEbookRefs_EbookId",
                table: "CourseLineEbookRefs",
                column: "EbookId");

            migrationBuilder.DropForeignKey(
                name: "FK_CourseLineEbookRefs_EbookDiagrams_EbookDiagramId",
                table: "CourseLineEbookRefs");

            migrationBuilder.DropIndex(
                name: "IX_CourseLineEbookRefs_BookPuzzleId_EbookDiagramId",
                table: "CourseLineEbookRefs");

            migrationBuilder.AlterColumn<int>(
                name: "EbookDiagramId",
                table: "CourseLineEbookRefs",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_CourseLineEbookRefs_EbookDiagrams_EbookDiagramId",
                table: "CourseLineEbookRefs",
                column: "EbookDiagramId",
                principalTable: "EbookDiagrams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CourseLineEbookRefs_Ebooks_EbookId",
                table: "CourseLineEbookRefs",
                column: "EbookId",
                principalTable: "Ebooks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CourseLineEbookRefs_EbookDiagrams_EbookDiagramId",
                table: "CourseLineEbookRefs");

            migrationBuilder.DropForeignKey(
                name: "FK_CourseLineEbookRefs_Ebooks_EbookId",
                table: "CourseLineEbookRefs");

            migrationBuilder.Sql("DELETE FROM CourseLineEbookRefs WHERE EbookDiagramId IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "EbookDiagramId",
                table: "CourseLineEbookRefs",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseLineEbookRefs_BookPuzzleId_EbookDiagramId",
                table: "CourseLineEbookRefs",
                columns: new[] { "BookPuzzleId", "EbookDiagramId" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_CourseLineEbookRefs_BookPuzzleId_EbookId",
                table: "CourseLineEbookRefs");

            migrationBuilder.DropIndex(
                name: "IX_CourseLineEbookRefs_EbookId",
                table: "CourseLineEbookRefs");

            migrationBuilder.DropColumn(
                name: "EbookId",
                table: "CourseLineEbookRefs");

            migrationBuilder.DropColumn(
                name: "Page",
                table: "CourseLineEbookRefs");

            migrationBuilder.DropColumn(
                name: "Placement",
                table: "CourseLineEbookRefs");

            migrationBuilder.AddForeignKey(
                name: "FK_CourseLineEbookRefs_EbookDiagrams_EbookDiagramId",
                table: "CourseLineEbookRefs",
                column: "EbookDiagramId",
                principalTable: "EbookDiagrams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
