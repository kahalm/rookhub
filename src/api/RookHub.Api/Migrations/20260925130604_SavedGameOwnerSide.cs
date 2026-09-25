using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class SavedGameOwnerSide : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerSide",
                table: "ScoresheetScans",
                type: "varchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "auto")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "OwnerSide",
                table: "SavedGames",
                type: "varchar(5)",
                maxLength: 5,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerSide",
                table: "ScoresheetScans");

            migrationBuilder.DropColumn(
                name: "OwnerSide",
                table: "SavedGames");
        }
    }
}
