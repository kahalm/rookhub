using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookAnonymousAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnonymousSessionId",
                table: "Ebooks",
                type: "varchar(36)",
                maxLength: 36,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EbookAnonymousProgresses",
                columns: table => new
                {
                    EbookId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Page = table.Column<int>(type: "int", nullable: false),
                    Locator = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PageCount = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EbookAnonymousProgresses", x => new { x.EbookId, x.SessionId });
                    table.ForeignKey(
                        name: "FK_EbookAnonymousProgresses_Ebooks_EbookId",
                        column: x => x.EbookId,
                        principalTable: "Ebooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Ebooks_AnonymousSessionId",
                table: "Ebooks",
                column: "AnonymousSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_EbookAnonymousProgresses_SessionId_UpdatedAt",
                table: "EbookAnonymousProgresses",
                columns: new[] { "SessionId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EbookAnonymousProgresses");

            migrationBuilder.DropIndex(
                name: "IX_Ebooks_AnonymousSessionId",
                table: "Ebooks");

            migrationBuilder.DropColumn(
                name: "AnonymousSessionId",
                table: "Ebooks");
        }
    }
}
