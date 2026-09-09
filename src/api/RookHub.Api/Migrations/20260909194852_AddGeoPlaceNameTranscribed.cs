using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGeoPlaceNameTranscribed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameTranscribed",
                table: "GeoPlaces",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GeoPlaces_Country_NameTranscribed",
                table: "GeoPlaces",
                columns: new[] { "Country", "NameTranscribed" });

            migrationBuilder.CreateIndex(
                name: "IX_GeoPlaces_NameTranscribed",
                table: "GeoPlaces",
                column: "NameTranscribed");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GeoPlaces_Country_NameTranscribed",
                table: "GeoPlaces");

            migrationBuilder.DropIndex(
                name: "IX_GeoPlaces_NameTranscribed",
                table: "GeoPlaces");

            migrationBuilder.DropColumn(
                name: "NameTranscribed",
                table: "GeoPlaces");
        }
    }
}
