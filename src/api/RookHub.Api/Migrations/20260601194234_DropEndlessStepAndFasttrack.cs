using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropEndlessStepAndFasttrack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Fasttrack",
                table: "EndlessProgresses");

            migrationBuilder.DropColumn(
                name: "Step",
                table: "EndlessProgresses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Fasttrack",
                table: "EndlessProgresses",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Step",
                table: "EndlessProgresses",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
