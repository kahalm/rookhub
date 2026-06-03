using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class BookPuzzleAttemptAnonymous : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "BookPuzzleAttempts",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "AnonymousSessionId",
                table: "BookPuzzleAttempts",
                type: "varchar(36)",
                maxLength: 36,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BookPuzzleAttempts_BookPuzzleId_AnonymousSessionId",
                table: "BookPuzzleAttempts",
                columns: new[] { "BookPuzzleId", "AnonymousSessionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookPuzzleAttempts_BookPuzzleId_AnonymousSessionId",
                table: "BookPuzzleAttempts");

            migrationBuilder.DropColumn(
                name: "AnonymousSessionId",
                table: "BookPuzzleAttempts");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "BookPuzzleAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
