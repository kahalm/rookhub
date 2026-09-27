using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AutoFavoriteDismissalsAndHistoryCrawls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HistoryTournamentCrawls",
                columns: table => new
                {
                    ChessResultsId = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastRequestedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    FoundAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoryTournamentCrawls", x => x.ChessResultsId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TournamentFavoriteDismissals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CrawlerTournamentId = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PlayerSnr = table.Column<int>(type: "int", nullable: false),
                    DismissedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentFavoriteDismissals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentFavoriteDismissals_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentFavoriteDismissals_UserId_CrawlerTournamentId_Play~",
                table: "TournamentFavoriteDismissals",
                columns: new[] { "UserId", "CrawlerTournamentId", "PlayerSnr" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoryTournamentCrawls");

            migrationBuilder.DropTable(
                name: "TournamentFavoriteDismissals");
        }
    }
}
