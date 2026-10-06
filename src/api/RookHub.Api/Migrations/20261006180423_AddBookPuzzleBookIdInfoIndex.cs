using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookPuzzleBookIdInfoIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_BookPuzzles_BookId_IsInfoOnly",
                table: "BookPuzzles",
                columns: new[] { "BookId", "IsInfoOnly" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookPuzzles_BookId_IsInfoOnly",
                table: "BookPuzzles");
        }
    }
}
