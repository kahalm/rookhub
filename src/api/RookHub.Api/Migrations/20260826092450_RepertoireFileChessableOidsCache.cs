using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class RepertoireFileChessableOidsCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChessableOidsCache",
                table: "RepertoireFiles",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ChessableOidsPgnLength",
                table: "RepertoireFiles",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChessableOidsCache",
                table: "RepertoireFiles");

            migrationBuilder.DropColumn(
                name: "ChessableOidsPgnLength",
                table: "RepertoireFiles");
        }
    }
}
