using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ScoresheetArchiveViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "View",
                table: "ScoresheetScanArchives",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Erst den neuen Index, dann den alten weg — der Fremdschlüssel auf ScoresheetScanId braucht immer einen.
            migrationBuilder.CreateIndex(
                name: "IX_ScoresheetScanArchives_ScoresheetScanId_Page_View",
                table: "ScoresheetScanArchives",
                columns: new[] { "ScoresheetScanId", "Page", "View" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_ScoresheetScanArchives_ScoresheetScanId_Page",
                table: "ScoresheetScanArchives");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ScoresheetScanArchives WHERE `View` > 0;");

            migrationBuilder.CreateIndex(
                name: "IX_ScoresheetScanArchives_ScoresheetScanId_Page",
                table: "ScoresheetScanArchives",
                columns: new[] { "ScoresheetScanId", "Page" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_ScoresheetScanArchives_ScoresheetScanId_Page_View",
                table: "ScoresheetScanArchives");

            migrationBuilder.DropColumn(
                name: "View",
                table: "ScoresheetScanArchives");
        }
    }
}
