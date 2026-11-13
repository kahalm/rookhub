using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEbookDiagramSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EbookDiagramSignatures",
                columns: table => new
                {
                    DiagramId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Version = table.Column<short>(type: "smallint", nullable: false),
                    Strict = table.Column<long>(type: "bigint", nullable: false),
                    Family = table.Column<long>(type: "bigint", nullable: false),
                    EbookId = table.Column<int>(type: "int", nullable: false),
                    Solvable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LineHash = table.Column<long>(type: "bigint", nullable: false),
                    Shuffle = table.Column<int>(type: "int", nullable: false),
                    MateIn = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Chapter = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EbookDiagramSignatures", x => new { x.DiagramId, x.Kind });
                    table.ForeignKey(
                        name: "FK_EbookDiagramSignatures_EbookDiagrams_DiagramId",
                        column: x => x.DiagramId,
                        principalTable: "EbookDiagrams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EbookDiagramSignatures_Kind_Family_Shuffle",
                table: "EbookDiagramSignatures",
                columns: new[] { "Kind", "Family", "Shuffle" });

            migrationBuilder.CreateIndex(
                name: "IX_EbookDiagramSignatures_Kind_Strict_Shuffle",
                table: "EbookDiagramSignatures",
                columns: new[] { "Kind", "Strict", "Shuffle" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EbookDiagramSignatures");
        }
    }
}
