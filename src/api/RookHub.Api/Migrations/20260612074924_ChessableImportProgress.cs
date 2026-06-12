using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ChessableImportProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ChessableImports",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "ChessableImports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FetchedPgn",
                table: "ChessableImports",
                type: "LONGTEXT",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "LineCount",
                table: "ChessableImports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "ChessableImports",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ChessableImports_Status",
                table: "ChessableImports",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChessableImports_Status",
                table: "ChessableImports");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "ChessableImports");

            migrationBuilder.DropColumn(
                name: "FetchedPgn",
                table: "ChessableImports");

            migrationBuilder.DropColumn(
                name: "LineCount",
                table: "ChessableImports");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "ChessableImports");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ChessableImports",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
