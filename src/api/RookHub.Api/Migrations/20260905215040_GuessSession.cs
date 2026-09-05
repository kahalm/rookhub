using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class GuessSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuessSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    GameAnalysisId = table.Column<int>(type: "int", nullable: false),
                    GuessWhite = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StartPly = table.Column<int>(type: "int", nullable: false),
                    CurrentPly = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SecondsSpent = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuessSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuessSessions_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GuessSessions_GameAnalyses_GameAnalysisId",
                        column: x => x.GameAnalysisId,
                        principalTable: "GameAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GuessMoves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GuessSessionId = table.Column<int>(type: "int", nullable: false),
                    Ply = table.Column<int>(type: "int", nullable: false),
                    PlayedUci = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Grade = table.Column<int>(type: "int", nullable: true),
                    DiffCp = table.Column<int>(type: "int", nullable: true),
                    SecondsSpent = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuessMoves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuessMoves_GuessSessions_GuessSessionId",
                        column: x => x.GuessSessionId,
                        principalTable: "GuessSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GuessMoves_GuessSessionId_Ply",
                table: "GuessMoves",
                columns: new[] { "GuessSessionId", "Ply" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuessSessions_GameAnalysisId",
                table: "GuessSessions",
                column: "GameAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_GuessSessions_UserId_StartedAt",
                table: "GuessSessions",
                columns: new[] { "UserId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuessMoves");

            migrationBuilder.DropTable(
                name: "GuessSessions");
        }
    }
}
