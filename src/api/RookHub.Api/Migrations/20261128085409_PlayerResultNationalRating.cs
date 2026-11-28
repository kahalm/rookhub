using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class PlayerResultNationalRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BirthYear",
                table: "PlayerTournamentResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RatingNational",
                table: "PlayerTournamentResults",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BirthYear",
                table: "PlayerTournamentResults");

            migrationBuilder.DropColumn(
                name: "RatingNational",
                table: "PlayerTournamentResults");
        }
    }
}
