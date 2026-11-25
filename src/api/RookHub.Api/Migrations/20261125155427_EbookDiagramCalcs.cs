using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookDiagramCalcs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EbookDiagramCalcs",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DiagramId = table.Column<int>(type: "int", nullable: false),
                    Tree = table.Column<string>(type: "mediumtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EbookDiagramCalcs", x => new { x.UserId, x.DiagramId });
                    table.ForeignKey(
                        name: "FK_EbookDiagramCalcs_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EbookDiagramCalcs_EbookDiagrams_DiagramId",
                        column: x => x.DiagramId,
                        principalTable: "EbookDiagrams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EbookDiagramCalcs_DiagramId",
                table: "EbookDiagramCalcs",
                column: "DiagramId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EbookDiagramCalcs");
        }
    }
}
