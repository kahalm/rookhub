using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRepertoireUseForExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default true: bestehende Repertoires bleiben fuer die Extension aktiv (kein Verhaltensbruch).
            migrationBuilder.AddColumn<bool>(
                name: "UseForExtension",
                table: "Repertoires",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseForExtension",
                table: "Repertoires");
        }
    }
}
