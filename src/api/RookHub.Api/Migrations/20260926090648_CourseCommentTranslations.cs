using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class CourseCommentTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceHash",
                table: "CommentTexts",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "BookPuzzleId",
                table: "CommentSets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommentLanguage",
                table: "Books",
                type: "varchar(8)",
                maxLength: 8,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CourseTranslationJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    Language = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LinesTotal = table.Column<int>(type: "int", nullable: false),
                    LinesDone = table.Column<int>(type: "int", nullable: false),
                    LinesFailed = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastError = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseTranslationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseTranslationJobs_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CommentTexts_SourceHash",
                table: "CommentTexts",
                column: "SourceHash");

            migrationBuilder.CreateIndex(
                name: "IX_CommentSets_BookPuzzleId_Language",
                table: "CommentSets",
                columns: new[] { "BookPuzzleId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseTranslationJobs_BookId_Language_Status",
                table: "CourseTranslationJobs",
                columns: new[] { "BookId", "Language", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseTranslationJobs_Status_CreatedAt",
                table: "CourseTranslationJobs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_CommentSets_BookPuzzles_BookPuzzleId",
                table: "CommentSets",
                column: "BookPuzzleId",
                principalTable: "BookPuzzles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CommentSets_BookPuzzles_BookPuzzleId",
                table: "CommentSets");

            migrationBuilder.DropTable(
                name: "CourseTranslationJobs");

            migrationBuilder.DropIndex(
                name: "IX_CommentTexts_SourceHash",
                table: "CommentTexts");

            migrationBuilder.DropIndex(
                name: "IX_CommentSets_BookPuzzleId_Language",
                table: "CommentSets");

            migrationBuilder.DropColumn(
                name: "SourceHash",
                table: "CommentTexts");

            migrationBuilder.DropColumn(
                name: "BookPuzzleId",
                table: "CommentSets");

            migrationBuilder.DropColumn(
                name: "CommentLanguage",
                table: "Books");
        }
    }
}
