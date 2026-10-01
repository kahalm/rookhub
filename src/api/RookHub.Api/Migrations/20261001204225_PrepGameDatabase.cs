using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class PrepGameDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrepEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Site = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KeyHash = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrepEvents", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PrepGames",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    WhiteId = table.Column<int>(type: "int", nullable: true),
                    BlackId = table.Column<int>(type: "int", nullable: true),
                    WhiteElo = table.Column<short>(type: "smallint", nullable: true),
                    BlackElo = table.Column<short>(type: "smallint", nullable: true),
                    Result = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    PlayedOn = table.Column<int>(type: "int", nullable: true),
                    EventId = table.Column<int>(type: "int", nullable: true),
                    Round = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Eco = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Plies = table.Column<short>(type: "smallint", nullable: false),
                    Moves = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MovesHash = table.Column<long>(type: "bigint", nullable: false),
                    Sources = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrepGames", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PrepImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Source = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Chunk = table.Column<int>(type: "int", nullable: false),
                    FirstGame = table.Column<long>(type: "bigint", nullable: false),
                    Read = table.Column<int>(type: "int", nullable: false),
                    Added = table.Column<int>(type: "int", nullable: false),
                    Duplicates = table.Column<int>(type: "int", nullable: false),
                    Discarded = table.Column<int>(type: "int", nullable: false),
                    DiscardReasons = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Millis = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrepImports", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PrepPlayers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NameKey = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FideId = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KeyHash = table.Column<long>(type: "bigint", nullable: false),
                    Games = table.Column<int>(type: "int", nullable: false),
                    FirstYear = table.Column<short>(type: "smallint", nullable: true),
                    LastYear = table.Column<short>(type: "smallint", nullable: true),
                    MaxElo = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrepPlayers", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PrepEvents_KeyHash",
                table: "PrepEvents",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrepGames_BlackId_PlayedOn",
                table: "PrepGames",
                columns: new[] { "BlackId", "PlayedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_PrepGames_MovesHash",
                table: "PrepGames",
                column: "MovesHash");

            migrationBuilder.CreateIndex(
                name: "IX_PrepGames_WhiteId_PlayedOn",
                table: "PrepGames",
                columns: new[] { "WhiteId", "PlayedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_PrepImports_Source_Chunk",
                table: "PrepImports",
                columns: new[] { "Source", "Chunk" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrepPlayers_FideId",
                table: "PrepPlayers",
                column: "FideId");

            migrationBuilder.CreateIndex(
                name: "IX_PrepPlayers_KeyHash",
                table: "PrepPlayers",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrepPlayers_NameKey",
                table: "PrepPlayers",
                column: "NameKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrepEvents");

            migrationBuilder.DropTable(
                name: "PrepGames");

            migrationBuilder.DropTable(
                name: "PrepImports");

            migrationBuilder.DropTable(
                name: "PrepPlayers");
        }
    }
}
