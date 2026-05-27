using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdToTournamentMonitor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TournamentMonitors_CrawlerTournamentId",
                table: "TournamentMonitors");

            // Delete existing monitors (ephemeral, 1h TTL) before adding NOT NULL UserId
            migrationBuilder.Sql("DELETE FROM TournamentMonitors;");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "TournamentMonitors",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMonitors_UserId_CrawlerTournamentId",
                table: "TournamentMonitors",
                columns: new[] { "UserId", "CrawlerTournamentId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMonitors_AppUsers_UserId",
                table: "TournamentMonitors",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMonitors_AppUsers_UserId",
                table: "TournamentMonitors");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMonitors_UserId_CrawlerTournamentId",
                table: "TournamentMonitors");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "TournamentMonitors");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMonitors_CrawlerTournamentId",
                table: "TournamentMonitors",
                column: "CrawlerTournamentId",
                unique: true);
        }
    }
}
