using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClubMistakeComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameAnalysisId",
                table: "MoveComparisons",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Ply",
                table: "MoveComparisons",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MistakeComparisonsAt",
                table: "GameAnalyses",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MoveComparisons_GameAnalysisId_Ply",
                table: "MoveComparisons",
                columns: new[] { "GameAnalysisId", "Ply" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MoveComparisons_GameAnalyses_GameAnalysisId",
                table: "MoveComparisons",
                column: "GameAnalysisId",
                principalTable: "GameAnalyses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MoveComparisons_GameAnalyses_GameAnalysisId",
                table: "MoveComparisons");

            migrationBuilder.DropIndex(
                name: "IX_MoveComparisons_GameAnalysisId_Ply",
                table: "MoveComparisons");

            migrationBuilder.DropColumn(
                name: "GameAnalysisId",
                table: "MoveComparisons");

            migrationBuilder.DropColumn(
                name: "Ply",
                table: "MoveComparisons");

            migrationBuilder.DropColumn(
                name: "MistakeComparisonsAt",
                table: "GameAnalyses");
        }
    }
}
