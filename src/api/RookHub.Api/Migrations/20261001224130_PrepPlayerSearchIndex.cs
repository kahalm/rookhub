using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class PrepPlayerSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PrepPlayers_NameKey",
                table: "PrepPlayers");

            migrationBuilder.CreateIndex(
                name: "IX_PrepPlayers_NameKey_Games",
                table: "PrepPlayers",
                columns: new[] { "NameKey", "Games" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PrepPlayers_NameKey_Games",
                table: "PrepPlayers");

            migrationBuilder.CreateIndex(
                name: "IX_PrepPlayers_NameKey",
                table: "PrepPlayers",
                column: "NameKey");
        }
    }
}
