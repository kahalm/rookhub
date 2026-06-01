using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourseProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    LastMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseProgresses_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseProgresses_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CoursePuzzleResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    BookPuzzleId = table.Column<int>(type: "int", nullable: false),
                    SolvedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoursePuzzleResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoursePuzzleResults_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoursePuzzleResults_BookPuzzles_BookPuzzleId",
                        column: x => x.BookPuzzleId,
                        principalTable: "BookPuzzles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CoursePuzzleResults_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CourseProgresses_BookId",
                table: "CourseProgresses",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseProgresses_UserId_BookId",
                table: "CourseProgresses",
                columns: new[] { "UserId", "BookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoursePuzzleResults_BookId",
                table: "CoursePuzzleResults",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_CoursePuzzleResults_BookPuzzleId",
                table: "CoursePuzzleResults",
                column: "BookPuzzleId");

            migrationBuilder.CreateIndex(
                name: "IX_CoursePuzzleResults_UserId_BookId",
                table: "CoursePuzzleResults",
                columns: new[] { "UserId", "BookId" });

            migrationBuilder.CreateIndex(
                name: "IX_CoursePuzzleResults_UserId_BookPuzzleId",
                table: "CoursePuzzleResults",
                columns: new[] { "UserId", "BookPuzzleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseProgresses");

            migrationBuilder.DropTable(
                name: "CoursePuzzleResults");
        }
    }
}
