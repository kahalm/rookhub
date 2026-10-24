using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameId",
                table: "EbookDiagrams",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GamePly",
                table: "EbookDiagrams",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EbookGames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EbookId = table.Column<int>(type: "int", nullable: false),
                    White = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Black = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Event = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Date = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Result = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Moves = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EbookGames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EbookGames_Ebooks_EbookId",
                        column: x => x.EbookId,
                        principalTable: "Ebooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EbookDiagrams_GameId",
                table: "EbookDiagrams",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_EbookGames_EbookId",
                table: "EbookGames",
                column: "EbookId");

            migrationBuilder.AddForeignKey(
                name: "FK_EbookDiagrams_EbookGames_GameId",
                table: "EbookDiagrams",
                column: "GameId",
                principalTable: "EbookGames",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EbookDiagrams_EbookGames_GameId",
                table: "EbookDiagrams");

            migrationBuilder.DropTable(
                name: "EbookGames");

            migrationBuilder.DropIndex(
                name: "IX_EbookDiagrams_GameId",
                table: "EbookDiagrams");

            migrationBuilder.DropColumn(
                name: "GameId",
                table: "EbookDiagrams");

            migrationBuilder.DropColumn(
                name: "GamePly",
                table: "EbookDiagrams");
        }
    }
}
