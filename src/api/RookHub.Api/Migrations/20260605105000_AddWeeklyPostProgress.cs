using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyPostProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeeklyPostAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    WeeklyPostId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PuzzleIndex = table.Column<int>(type: "int", nullable: false),
                    Solved = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TimeSeconds = table.Column<int>(type: "int", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyPostAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeeklyPostAttempts_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WeeklyPostAttempts_WeeklyPosts_WeeklyPostId",
                        column: x => x.WeeklyPostId,
                        principalTable: "WeeklyPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyPostAttempts_UserId",
                table: "WeeklyPostAttempts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyPostAttempts_WeeklyPostId_UserId",
                table: "WeeklyPostAttempts",
                columns: new[] { "WeeklyPostId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyPostAttempts_WeeklyPostId_UserId_PuzzleIndex",
                table: "WeeklyPostAttempts",
                columns: new[] { "WeeklyPostId", "UserId", "PuzzleIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeeklyPostAttempts");
        }
    }
}
