using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BookId",
                table: "BookPuzzles",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Books",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FileName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Difficulty = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rating = table.Column<int>(type: "int", nullable: true),
                    Tags = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ForDaily = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ForRandom = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ForBlind = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Books", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BookPuzzles_BookId",
                table: "BookPuzzles",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_FileName",
                table: "Books",
                column: "FileName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BookPuzzles_Books_BookId",
                table: "BookPuzzles",
                column: "BookId",
                principalTable: "Books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Backfill: für jeden bereits vorhandenen BookFileName ein Book anlegen
            // (Flags default 0; Metadaten aus einem beliebigen Puzzle des Buchs übernehmen)
            // und anschließend BookPuzzles.BookId verknüpfen.
            migrationBuilder.Sql(@"
                INSERT INTO Books (FileName, DisplayName, Difficulty, Rating, Tags, ForDaily, ForRandom, ForBlind, CreatedAt, UpdatedAt)
                SELECT src.BookFileName, src.BookFileName, p.Difficulty, p.BookRating, p.Tags, 0, 0, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
                FROM (SELECT BookFileName, MIN(Id) AS minId FROM BookPuzzles
                      WHERE BookFileName IS NOT NULL AND BookFileName <> ''
                      GROUP BY BookFileName) src
                JOIN BookPuzzles p ON p.Id = src.minId
                WHERE NOT EXISTS (SELECT 1 FROM Books b WHERE b.FileName = src.BookFileName);");

            migrationBuilder.Sql(@"
                UPDATE BookPuzzles bp
                JOIN Books b ON b.FileName = bp.BookFileName
                SET bp.BookId = b.Id
                WHERE bp.BookId IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookPuzzles_Books_BookId",
                table: "BookPuzzles");

            migrationBuilder.DropTable(
                name: "Books");

            migrationBuilder.DropIndex(
                name: "IX_BookPuzzles_BookId",
                table: "BookPuzzles");

            migrationBuilder.DropColumn(
                name: "BookId",
                table: "BookPuzzles");
        }
    }
}
