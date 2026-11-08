using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGameAnalysisWatchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameAnalysisWatchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    GameAnalysisId = table.Column<int>(type: "int", nullable: true),
                    LeagueClubGameId = table.Column<int>(type: "int", nullable: true),
                    Link = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameAnalysisWatchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameAnalysisWatchers_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameAnalysisWatchers_GameAnalyses_GameAnalysisId",
                        column: x => x.GameAnalysisId,
                        principalTable: "GameAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameAnalysisWatchers_LeagueClubGames_LeagueClubGameId",
                        column: x => x.LeagueClubGameId,
                        principalTable: "LeagueClubGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GameAnalysisWatchers_GameAnalysisId_UserId",
                table: "GameAnalysisWatchers",
                columns: new[] { "GameAnalysisId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameAnalysisWatchers_LeagueClubGameId_UserId",
                table: "GameAnalysisWatchers",
                columns: new[] { "LeagueClubGameId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameAnalysisWatchers_UserId",
                table: "GameAnalysisWatchers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameAnalysisWatchers");
        }
    }
}
