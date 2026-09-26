using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExplanationMasterComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MasterLibraryGameId",
                table: "GameMoveExplanations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MasterText",
                table: "GameMoveExplanations",
                type: "varchar(600)",
                maxLength: 600,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MasterLibraryGameId",
                table: "GameMoveExplanations");

            migrationBuilder.DropColumn(
                name: "MasterText",
                table: "GameMoveExplanations");
        }
    }
}
