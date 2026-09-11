using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class CommentSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommentSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LibraryGameId = table.Column<int>(type: "int", nullable: true),
                    GameAnalysisId = table.Column<int>(type: "int", nullable: true),
                    Language = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    TranslatedFrom = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Model = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentSets_GameAnalyses_GameAnalysisId",
                        column: x => x.GameAnalysisId,
                        principalTable: "GameAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommentSets_LibraryGames_LibraryGameId",
                        column: x => x.LibraryGameId,
                        principalTable: "LibraryGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentTexts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CommentSetId = table.Column<int>(type: "int", nullable: false),
                    Ply = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "LONGTEXT", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentTexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentTexts_CommentSets_CommentSetId",
                        column: x => x.CommentSetId,
                        principalTable: "CommentSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CommentSets_GameAnalysisId_Language",
                table: "CommentSets",
                columns: new[] { "GameAnalysisId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommentSets_LibraryGameId_Language",
                table: "CommentSets",
                columns: new[] { "LibraryGameId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommentTexts_CommentSetId_Ply",
                table: "CommentTexts",
                columns: new[] { "CommentSetId", "Ply" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentTexts");

            migrationBuilder.DropTable(
                name: "CommentSets");
        }
    }
}
