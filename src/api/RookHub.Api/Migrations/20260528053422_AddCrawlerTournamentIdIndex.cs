using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCrawlerTournamentIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TournamentSubscriptions_CrawlerTournamentId",
                table: "TournamentSubscriptions",
                column: "CrawlerTournamentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TournamentSubscriptions_CrawlerTournamentId",
                table: "TournamentSubscriptions");
        }
    }
}
