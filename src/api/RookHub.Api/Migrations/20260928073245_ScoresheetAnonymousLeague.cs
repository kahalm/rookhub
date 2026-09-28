using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ScoresheetAnonymousLeague : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "ScoresheetScans",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "AccessKey",
                table: "ScoresheetScans",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AnonIpHash",
                table: "ScoresheetScans",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ScoresheetScans_AccessKey",
                table: "ScoresheetScans",
                column: "AccessKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScoresheetScans_AnonIpHash_CreatedAt",
                table: "ScoresheetScans",
                columns: new[] { "AnonIpHash", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScoresheetScans_AccessKey",
                table: "ScoresheetScans");

            migrationBuilder.DropIndex(
                name: "IX_ScoresheetScans_AnonIpHash_CreatedAt",
                table: "ScoresheetScans");

            migrationBuilder.DropColumn(
                name: "AccessKey",
                table: "ScoresheetScans");

            migrationBuilder.DropColumn(
                name: "AnonIpHash",
                table: "ScoresheetScans");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "ScoresheetScans",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
