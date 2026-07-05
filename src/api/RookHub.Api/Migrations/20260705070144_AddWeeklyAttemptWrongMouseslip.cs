using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyAttemptWrongMouseslip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Mouseslips",
                table: "WeeklyPostAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WrongAttempts",
                table: "WeeklyPostAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mouseslips",
                table: "WeeklyPostAttempts");

            migrationBuilder.DropColumn(
                name: "WrongAttempts",
                table: "WeeklyPostAttempts");

        }
    }
}
