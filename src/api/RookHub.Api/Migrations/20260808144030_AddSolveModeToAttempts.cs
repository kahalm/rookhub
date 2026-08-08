using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSolveModeToAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "EndlessSessions",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "training")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "CourseAttempts",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "training")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "BookPuzzleAttempts",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "training")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mode",
                table: "EndlessSessions");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "CourseAttempts");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "BookPuzzleAttempts");
        }
    }
}
