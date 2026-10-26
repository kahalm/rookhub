using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class MarkedPositionVariation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VariationFromPly",
                table: "MarkedPositions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariationUci",
                table: "MarkedPositions",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_MarkedPositions_LeagueClubGameId",
                table: "MarkedPositions",
                column: "LeagueClubGameId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarkedPositions_LeagueClubGameId",
                table: "MarkedPositions");

            migrationBuilder.DropColumn(
                name: "VariationFromPly",
                table: "MarkedPositions");

            migrationBuilder.DropColumn(
                name: "VariationUci",
                table: "MarkedPositions");
        }
    }
}
