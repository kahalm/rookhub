using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCalculationTrainingValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChosenSan",
                table: "CalculationTrees",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ChosenUci",
                table: "CalculationTrees",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Grade",
                table: "CalculationTrees",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondsSpent",
                table: "CalculationTrees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SecondsToken",
                table: "CalculationTrees",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "SecondsTokenApplied",
                table: "CalculationTrees",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChosenSan",
                table: "CalculationTrees");

            migrationBuilder.DropColumn(
                name: "ChosenUci",
                table: "CalculationTrees");

            migrationBuilder.DropColumn(
                name: "Grade",
                table: "CalculationTrees");

            migrationBuilder.DropColumn(
                name: "SecondsSpent",
                table: "CalculationTrees");

            migrationBuilder.DropColumn(
                name: "SecondsToken",
                table: "CalculationTrees");

            migrationBuilder.DropColumn(
                name: "SecondsTokenApplied",
                table: "CalculationTrees");
        }
    }
}
