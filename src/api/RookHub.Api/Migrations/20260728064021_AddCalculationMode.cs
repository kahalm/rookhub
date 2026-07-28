using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCalculationMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCalculation",
                table: "Books",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CalculationTrees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    BookPuzzleId = table.Column<int>(type: "int", nullable: false),
                    TreeJson = table.Column<string>(type: "LONGTEXT", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalculationTrees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalculationTrees_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CalculationTrees_BookPuzzles_BookPuzzleId",
                        column: x => x.BookPuzzleId,
                        principalTable: "BookPuzzles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalculationTrees_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CalculationTrees_BookId",
                table: "CalculationTrees",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_CalculationTrees_BookPuzzleId",
                table: "CalculationTrees",
                column: "BookPuzzleId");

            migrationBuilder.CreateIndex(
                name: "IX_CalculationTrees_UserId_BookId",
                table: "CalculationTrees",
                columns: new[] { "UserId", "BookId" });

            migrationBuilder.CreateIndex(
                name: "IX_CalculationTrees_UserId_BookPuzzleId",
                table: "CalculationTrees",
                columns: new[] { "UserId", "BookPuzzleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalculationTrees");

            migrationBuilder.DropColumn(
                name: "IsCalculation",
                table: "Books");
        }
    }
}
