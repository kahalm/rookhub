using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LeagueClubGameUploadShareHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UploadShareHash",
                table: "LeagueClubGames",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClubGames_UploadShareHash",
                table: "LeagueClubGames",
                column: "UploadShareHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeagueClubGames_UploadShareHash",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "UploadShareHash",
                table: "LeagueClubGames");
        }
    }
}
