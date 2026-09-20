using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReconstructionShareToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShareToken",
                table: "GameReconstructions",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "SharedAt",
                table: "GameReconstructions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameReconstructions_ShareToken",
                table: "GameReconstructions",
                column: "ShareToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameReconstructions_ShareToken",
                table: "GameReconstructions");

            migrationBuilder.DropColumn(
                name: "ShareToken",
                table: "GameReconstructions");

            migrationBuilder.DropColumn(
                name: "SharedAt",
                table: "GameReconstructions");
        }
    }
}
