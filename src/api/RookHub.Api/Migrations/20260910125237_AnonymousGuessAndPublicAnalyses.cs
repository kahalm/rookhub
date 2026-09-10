using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AnonymousGuessAndPublicAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "GuessSessions",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "AnonymousSessionId",
                table: "GuessSessions",
                type: "varchar(36)",
                maxLength: 36,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "GameAnalyses",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_GuessSessions_AnonymousSessionId_StartedAt",
                table: "GuessSessions",
                columns: new[] { "AnonymousSessionId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GuessSessions_AnonymousSessionId_StartedAt",
                table: "GuessSessions");

            migrationBuilder.DropColumn(
                name: "AnonymousSessionId",
                table: "GuessSessions");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "GameAnalyses");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "GuessSessions",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
