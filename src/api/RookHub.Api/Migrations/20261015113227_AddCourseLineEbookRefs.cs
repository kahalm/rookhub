using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseLineEbookRefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourseLineEbookRefs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BookPuzzleId = table.Column<int>(type: "int", nullable: false),
                    EbookDiagramId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseLineEbookRefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseLineEbookRefs_BookPuzzles_BookPuzzleId",
                        column: x => x.BookPuzzleId,
                        principalTable: "BookPuzzles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseLineEbookRefs_EbookDiagrams_EbookDiagramId",
                        column: x => x.EbookDiagramId,
                        principalTable: "EbookDiagrams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CourseLineEbookRefs_BookPuzzleId_EbookDiagramId",
                table: "CourseLineEbookRefs",
                columns: new[] { "BookPuzzleId", "EbookDiagramId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseLineEbookRefs_EbookDiagramId",
                table: "CourseLineEbookRefs",
                column: "EbookDiagramId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseLineEbookRefs");
        }
    }
}
