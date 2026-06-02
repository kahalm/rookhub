using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookPuzzleAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookPuzzleAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BookPuzzleId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Solved = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TimeSeconds = table.Column<int>(type: "int", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookPuzzleAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookPuzzleAttempts_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookPuzzleAttempts_BookPuzzles_BookPuzzleId",
                        column: x => x.BookPuzzleId,
                        principalTable: "BookPuzzles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BookPuzzleAttempts_BookPuzzleId_AttemptedAt",
                table: "BookPuzzleAttempts",
                columns: new[] { "BookPuzzleId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BookPuzzleAttempts_BookPuzzleId_UserId",
                table: "BookPuzzleAttempts",
                columns: new[] { "BookPuzzleId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BookPuzzleAttempts_UserId",
                table: "BookPuzzleAttempts",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookPuzzleAttempts");
        }
    }
}
