using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class GameRoasts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameRoasts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SavedGameId = table.Column<int>(type: "int", nullable: false),
                    Style = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Language = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Text = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Model = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameRoasts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameRoasts_SavedGames_SavedGameId",
                        column: x => x.SavedGameId,
                        principalTable: "SavedGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GameRoasts_SavedGameId_Language_Style",
                table: "GameRoasts",
                columns: new[] { "SavedGameId", "Language", "Style" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameRoasts");
        }
    }
}
