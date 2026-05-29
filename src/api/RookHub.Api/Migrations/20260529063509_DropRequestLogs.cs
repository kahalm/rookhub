using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropRequestLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS `RequestLogs`;");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMonitors_CrawlerTournamentId",
                table: "TournamentMonitors");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "TournamentMonitors",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "PuzzleAttempts",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "AnonymousSessionId",
                table: "PuzzleAttempts",
                type: "varchar(36)",
                maxLength: 36,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EndlessProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    AnonymousSessionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartElo = table.Column<int>(type: "int", nullable: false),
                    Step = table.Column<int>(type: "int", nullable: false),
                    Themes = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Fasttrack = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FasttrackThreshold1 = table.Column<int>(type: "int", nullable: true),
                    FasttrackThreshold2 = table.Column<int>(type: "int", nullable: true),
                    StockfishDepth = table.Column<int>(type: "int", nullable: false),
                    Highscore = table.Column<int>(type: "int", nullable: false),
                    ActiveGameState = table.Column<string>(type: "LONGTEXT", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndlessProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EndlessProgresses_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EndlessSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    AnonymousSessionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timestamp = table.Column<long>(type: "bigint", nullable: false),
                    TotalSolved = table.Column<int>(type: "int", nullable: false),
                    MaxRating = table.Column<int>(type: "int", nullable: false),
                    DurationSeconds = table.Column<int>(type: "int", nullable: false),
                    ConfigJson = table.Column<string>(type: "TEXT", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MistakeAtRatings = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndlessSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EndlessSessions_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentSubscriptions_CrawlerTournamentId",
                table: "TournamentSubscriptions",
                column: "CrawlerTournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMonitors_UserId_CrawlerTournamentId",
                table: "TournamentMonitors",
                columns: new[] { "UserId", "CrawlerTournamentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleAttempts_AnonymousSessionId",
                table: "PuzzleAttempts",
                column: "AnonymousSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_EndlessProgresses_AnonymousSessionId",
                table: "EndlessProgresses",
                column: "AnonymousSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_EndlessProgresses_UserId",
                table: "EndlessProgresses",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EndlessSessions_AnonymousSessionId",
                table: "EndlessSessions",
                column: "AnonymousSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_EndlessSessions_UserId_Timestamp",
                table: "EndlessSessions",
                columns: new[] { "UserId", "Timestamp" });

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMonitors_AppUsers_UserId",
                table: "TournamentMonitors",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMonitors_AppUsers_UserId",
                table: "TournamentMonitors");

            migrationBuilder.DropTable(
                name: "EndlessProgresses");

            migrationBuilder.DropTable(
                name: "EndlessSessions");

            migrationBuilder.DropIndex(
                name: "IX_TournamentSubscriptions_CrawlerTournamentId",
                table: "TournamentSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMonitors_UserId_CrawlerTournamentId",
                table: "TournamentMonitors");

            migrationBuilder.DropIndex(
                name: "IX_PuzzleAttempts_AnonymousSessionId",
                table: "PuzzleAttempts");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "TournamentMonitors");

            migrationBuilder.DropColumn(
                name: "AnonymousSessionId",
                table: "PuzzleAttempts");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "PuzzleAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMonitors_CrawlerTournamentId",
                table: "TournamentMonitors",
                column: "CrawlerTournamentId",
                unique: true);

            migrationBuilder.CreateTable(
                name: "RequestLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Timestamp = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Method = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Path = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QueryString = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestLogs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
