using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChessableRateLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RateLimitedAt",
                table: "ChessableImports",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitLinesUsed",
                table: "ChessableCredentials",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RateLimitWindowStartedAt",
                table: "ChessableCredentials",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RateLimitedAt",
                table: "ChessableImports");

            migrationBuilder.DropColumn(
                name: "RateLimitLinesUsed",
                table: "ChessableCredentials");

            migrationBuilder.DropColumn(
                name: "RateLimitWindowStartedAt",
                table: "ChessableCredentials");
        }
    }
}
