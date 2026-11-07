using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LocalExplorerPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocalExplorerPositions",
                columns: table => new
                {
                    KeyHash = table.Column<string>(type: "char(32)", unicode: false, fixedLength: true, maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "ascii"),
                    Payload = table.Column<byte[]>(type: "mediumblob", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalExplorerPositions", x => x.KeyHash);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LocalExplorerPositions_FetchedAt",
                table: "LocalExplorerPositions",
                column: "FetchedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocalExplorerPositions");
        }
    }
}
