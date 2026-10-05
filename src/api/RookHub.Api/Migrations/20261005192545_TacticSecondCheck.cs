using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class TacticSecondCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SecondAgrees",
                table: "TacticCandidates",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondAttempts",
                table: "TacticCandidates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SecondBest",
                table: "TacticCandidates",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SecondEval",
                table: "TacticCandidates",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SecondHereJson",
                table: "TacticCandidates",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "SecondJobId",
                table: "TacticCandidates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondStage",
                table: "TacticCandidates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_TacticCandidates_SecondJobId",
                table: "TacticCandidates",
                column: "SecondJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TacticCandidates_SecondJobId",
                table: "TacticCandidates");

            migrationBuilder.DropColumn(
                name: "SecondAgrees",
                table: "TacticCandidates");

            migrationBuilder.DropColumn(
                name: "SecondAttempts",
                table: "TacticCandidates");

            migrationBuilder.DropColumn(
                name: "SecondBest",
                table: "TacticCandidates");

            migrationBuilder.DropColumn(
                name: "SecondEval",
                table: "TacticCandidates");

            migrationBuilder.DropColumn(
                name: "SecondHereJson",
                table: "TacticCandidates");

            migrationBuilder.DropColumn(
                name: "SecondJobId",
                table: "TacticCandidates");

            migrationBuilder.DropColumn(
                name: "SecondStage",
                table: "TacticCandidates");
        }
    }
}
