using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookDiagramLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DiagramExternalFailed",
                table: "Ebooks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "DiagramLeaseId",
                table: "Ebooks",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "DiagramLeaseOwner",
                table: "Ebooks",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "DiagramLeaseUntil",
                table: "Ebooks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DiagramPriority",
                table: "Ebooks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiagramExternalFailed",
                table: "Ebooks");

            migrationBuilder.DropColumn(
                name: "DiagramLeaseId",
                table: "Ebooks");

            migrationBuilder.DropColumn(
                name: "DiagramLeaseOwner",
                table: "Ebooks");

            migrationBuilder.DropColumn(
                name: "DiagramLeaseUntil",
                table: "Ebooks");

            migrationBuilder.DropColumn(
                name: "DiagramPriority",
                table: "Ebooks");
        }
    }
}
