using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookKindAndCourseAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Books",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CourseAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    BookPuzzleId = table.Column<int>(type: "int", nullable: false),
                    Solved = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TimeSeconds = table.Column<int>(type: "int", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseAttempts_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseAttempts_BookPuzzles_BookPuzzleId",
                        column: x => x.BookPuzzleId,
                        principalTable: "BookPuzzles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseAttempts_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CourseAttempts_BookId",
                table: "CourseAttempts",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseAttempts_BookPuzzleId",
                table: "CourseAttempts",
                column: "BookPuzzleId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseAttempts_UserId_AttemptedAt",
                table: "CourseAttempts",
                columns: new[] { "UserId", "AttemptedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseAttempts");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Books");
        }
    }
}
