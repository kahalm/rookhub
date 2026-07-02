using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityThemeClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Theme",
                table: "ManualActivities",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Theme",
                table: "ActivityTimers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Theme",
                table: "ActivityPresets",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Theme",
                table: "ManualActivities");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "ActivityTimers");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "ActivityPresets");
        }
    }
}
