using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPuzzleSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PuzzleSignatures",
                columns: table => new
                {
                    PuzzleId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Version = table.Column<short>(type: "smallint", nullable: false),
                    Strict = table.Column<long>(type: "bigint", nullable: false),
                    Family = table.Column<long>(type: "bigint", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    MateIn = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PuzzleSignatures", x => new { x.PuzzleId, x.Kind });
                    table.ForeignKey(
                        name: "FK_PuzzleSignatures_Puzzles_PuzzleId",
                        column: x => x.PuzzleId,
                        principalTable: "Puzzles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleSignatures_Family_Rating",
                table: "PuzzleSignatures",
                columns: new[] { "Family", "Rating" });

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleSignatures_Strict_Rating",
                table: "PuzzleSignatures",
                columns: new[] { "Strict", "Rating" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PuzzleSignatures");
        }
    }
}
