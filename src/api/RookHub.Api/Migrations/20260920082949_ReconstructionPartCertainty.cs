using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReconstructionPartCertainty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Vorgabe TRUE, nicht der CLR-Standardwert false: der Bestand wurde ohne diese Frage
            // aufgezeichnet, und „unsicher" wäre eine Aussage, die niemand getroffen hat.
            migrationBuilder.AddColumn<bool>(
                name: "Certain",
                table: "GameReconstructionParts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Certain",
                table: "GameReconstructionParts");
        }
    }
}
