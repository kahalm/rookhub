using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AnonymousPuzzleAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "PuzzleAttempts",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "AnonymousSessionId",
                table: "PuzzleAttempts",
                type: "varchar(36)",
                maxLength: 36,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PuzzleAttempts_AnonymousSessionId",
                table: "PuzzleAttempts",
                column: "AnonymousSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PuzzleAttempts_AnonymousSessionId",
                table: "PuzzleAttempts");

            migrationBuilder.DropColumn(
                name: "AnonymousSessionId",
                table: "PuzzleAttempts");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "PuzzleAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
