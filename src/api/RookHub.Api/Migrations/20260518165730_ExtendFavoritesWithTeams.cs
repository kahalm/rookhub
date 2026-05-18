using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExtendFavoritesWithTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "PlayerSnr",
                table: "TournamentFavorites",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "TeamSnr",
                table: "TournamentFavorites",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentFavorites_UserId_CrawlerTournamentId_TeamSnr",
                table: "TournamentFavorites",
                columns: new[] { "UserId", "CrawlerTournamentId", "TeamSnr" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TournamentFavorites_UserId_CrawlerTournamentId_TeamSnr",
                table: "TournamentFavorites");

            migrationBuilder.DropColumn(
                name: "TeamSnr",
                table: "TournamentFavorites");

            migrationBuilder.AlterColumn<int>(
                name: "PlayerSnr",
                table: "TournamentFavorites",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
