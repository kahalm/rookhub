using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookDiagrams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiagramCount",
                table: "Ebooks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DiagramState",
                table: "Ebooks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "EbookDiagrams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EbookId = table.Column<int>(type: "int", nullable: false),
                    Page = table.Column<int>(type: "int", nullable: false),
                    X = table.Column<double>(type: "double", nullable: false),
                    Y = table.Column<double>(type: "double", nullable: false),
                    W = table.Column<double>(type: "double", nullable: false),
                    H = table.Column<double>(type: "double", nullable: false),
                    Fen = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Label = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Caption = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SideKnown = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FontFamily = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EbookDiagrams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EbookDiagrams_Ebooks_EbookId",
                        column: x => x.EbookId,
                        principalTable: "Ebooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Ebooks_DiagramState_Id",
                table: "Ebooks",
                columns: new[] { "DiagramState", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_EbookDiagrams_EbookId_Page_Y",
                table: "EbookDiagrams",
                columns: new[] { "EbookId", "Page", "Y" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EbookDiagrams");

            migrationBuilder.DropIndex(
                name: "IX_Ebooks_DiagramState_Id",
                table: "Ebooks");

            migrationBuilder.DropColumn(
                name: "DiagramCount",
                table: "Ebooks");

            migrationBuilder.DropColumn(
                name: "DiagramState",
                table: "Ebooks");
        }
    }
}
