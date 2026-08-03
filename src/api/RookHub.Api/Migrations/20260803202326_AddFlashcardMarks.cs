using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFlashcardMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourseFlashcardMarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    BookPuzzleId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseFlashcardMarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseFlashcardMarks_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseFlashcardMarks_BookPuzzles_BookPuzzleId",
                        column: x => x.BookPuzzleId,
                        principalTable: "BookPuzzles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseFlashcardMarks_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RepertoireFlashcardMarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RepertoireId = table.Column<int>(type: "int", nullable: false),
                    LineKey = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepertoireFlashcardMarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepertoireFlashcardMarks_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepertoireFlashcardMarks_Repertoires_RepertoireId",
                        column: x => x.RepertoireId,
                        principalTable: "Repertoires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CourseFlashcardMarks_BookId",
                table: "CourseFlashcardMarks",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseFlashcardMarks_BookPuzzleId",
                table: "CourseFlashcardMarks",
                column: "BookPuzzleId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseFlashcardMarks_UserId_BookId",
                table: "CourseFlashcardMarks",
                columns: new[] { "UserId", "BookId" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseFlashcardMarks_UserId_BookPuzzleId",
                table: "CourseFlashcardMarks",
                columns: new[] { "UserId", "BookPuzzleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepertoireFlashcardMarks_RepertoireId",
                table: "RepertoireFlashcardMarks",
                column: "RepertoireId");

            migrationBuilder.CreateIndex(
                name: "IX_RepertoireFlashcardMarks_UserId_RepertoireId_LineKey",
                table: "RepertoireFlashcardMarks",
                columns: new[] { "UserId", "RepertoireId", "LineKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseFlashcardMarks");

            migrationBuilder.DropTable(
                name: "RepertoireFlashcardMarks");
        }
    }
}
