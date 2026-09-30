using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LeagueOnlineGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Evidence",
                table: "LeagueOnlineAccounts",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(300)",
                oldMaxLength: 300,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "GameCount",
                table: "LeagueOnlineAccounts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Manual",
                table: "LeagueOnlineAccounts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "SyncCursor",
                table: "LeagueOnlineAccounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "SyncError",
                table: "LeagueOnlineAccounts",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "SyncMore",
                table: "LeagueOnlineAccounts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncedAt",
                table: "LeagueOnlineAccounts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "LeagueOnlineAccounts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LeagueOnlineGames",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    FideId = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExternalId = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PlayedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Speed = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rated = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    White = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Result = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Opponent = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OpponentRating = table.Column<int>(type: "int", nullable: true),
                    PlayerRating = table.Column<int>(type: "int", nullable: true),
                    Line = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Moves = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Plies = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueOnlineGames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeagueOnlineGames_LeagueOnlineAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "LeagueOnlineAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueOnlineGames_AccountId_ExternalId",
                table: "LeagueOnlineGames",
                columns: new[] { "AccountId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueOnlineGames_FideId_White_PlayedAt",
                table: "LeagueOnlineGames",
                columns: new[] { "FideId", "White", "PlayedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeagueOnlineGames");

            migrationBuilder.DropColumn(
                name: "GameCount",
                table: "LeagueOnlineAccounts");

            migrationBuilder.DropColumn(
                name: "Manual",
                table: "LeagueOnlineAccounts");

            migrationBuilder.DropColumn(
                name: "SyncCursor",
                table: "LeagueOnlineAccounts");

            migrationBuilder.DropColumn(
                name: "SyncError",
                table: "LeagueOnlineAccounts");

            migrationBuilder.DropColumn(
                name: "SyncMore",
                table: "LeagueOnlineAccounts");

            migrationBuilder.DropColumn(
                name: "SyncedAt",
                table: "LeagueOnlineAccounts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "LeagueOnlineAccounts");

            migrationBuilder.AlterColumn<string>(
                name: "Evidence",
                table: "LeagueOnlineAccounts",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
