using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ScoresheetScanPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PageCount",
                table: "ScoresheetScans",
                type: "int",
                nullable: false,
                // Jede bestehende Einlesung ist ein Foto (EF schlug 0 vor).
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "ScoresheetScanPages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ScoresheetScanId = table.Column<int>(type: "int", nullable: false),
                    Page = table.Column<int>(type: "int", nullable: false),
                    Photo = table.Column<byte[]>(type: "LONGBLOB", nullable: false),
                    ContentType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoresheetScanPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScoresheetScanPages_ScoresheetScans_ScoresheetScanId",
                        column: x => x.ScoresheetScanId,
                        principalTable: "ScoresheetScans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ScoresheetScanPages_ScoresheetScanId_Page",
                table: "ScoresheetScanPages",
                columns: new[] { "ScoresheetScanId", "Page" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScoresheetScanPages");

            migrationBuilder.DropColumn(
                name: "PageCount",
                table: "ScoresheetScans");
        }
    }
}
