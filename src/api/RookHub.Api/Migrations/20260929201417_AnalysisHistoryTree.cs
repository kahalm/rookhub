using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisHistoryTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Starred",
                table: "AnalysisHistoryEntries");

            migrationBuilder.AddColumn<int>(
                name: "Current",
                table: "AnalysisHistoryEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NodeCount",
                table: "AnalysisHistoryEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StarCount",
                table: "AnalysisHistoryEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TreeJson",
                table: "AnalysisHistoryEntries",
                type: "LONGTEXT",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Current",
                table: "AnalysisHistoryEntries");

            migrationBuilder.DropColumn(
                name: "NodeCount",
                table: "AnalysisHistoryEntries");

            migrationBuilder.DropColumn(
                name: "StarCount",
                table: "AnalysisHistoryEntries");

            migrationBuilder.DropColumn(
                name: "TreeJson",
                table: "AnalysisHistoryEntries");

            migrationBuilder.AddColumn<string>(
                name: "Starred",
                table: "AnalysisHistoryEntries",
                type: "varchar(400)",
                maxLength: 400,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
