using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookDiagramReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EbookDiagramReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EbookId = table.Column<int>(type: "int", nullable: false),
                    DiagramId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Page = table.Column<int>(type: "int", nullable: false),
                    Fen = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Note = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EbookDiagramReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EbookDiagramReports_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EbookDiagramReports_Ebooks_EbookId",
                        column: x => x.EbookId,
                        principalTable: "Ebooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EbookDiagramReports_DiagramId_UserId",
                table: "EbookDiagramReports",
                columns: new[] { "DiagramId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EbookDiagramReports_EbookId_CreatedAt",
                table: "EbookDiagramReports",
                columns: new[] { "EbookId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EbookDiagramReports_UserId",
                table: "EbookDiagramReports",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EbookDiagramReports");
        }
    }
}
