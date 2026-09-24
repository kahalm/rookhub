using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class TwoPassGameAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Refined",
                table: "GameAnalysisPositions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RefineDepth",
                table: "GameAnalyses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefineMultiPv",
                table: "GameAnalyses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefinedAt",
                table: "GameAnalyses",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Background",
                table: "AnalysisJobs",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Refined",
                table: "GameAnalysisPositions");

            migrationBuilder.DropColumn(
                name: "RefineDepth",
                table: "GameAnalyses");

            migrationBuilder.DropColumn(
                name: "RefineMultiPv",
                table: "GameAnalyses");

            migrationBuilder.DropColumn(
                name: "RefinedAt",
                table: "GameAnalyses");

            migrationBuilder.DropColumn(
                name: "Background",
                table: "AnalysisJobs");
        }
    }
}
