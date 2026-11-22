using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class WoodpeckerCycleExtendedUntil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExtendedUntil",
                table: "WoodpeckerCycles",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtendedUntil",
                table: "WoodpeckerCycles");
        }
    }
}
