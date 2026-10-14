using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookCoverSkipBlankPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0.776.0: Titelbild = erste nicht leere Seite — vorhandene Titelbilder aus PDF/DJVU neu rendern (viele alte
            // Bücher und Scans zeigten ein leeres Vorsatzblatt).
            migrationBuilder.Sql("UPDATE `Ebooks` e SET e.`CoverState` = 0 WHERE e.`CoverState` = 1 AND (EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'pdf') OR (EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'djvu') AND NOT EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` IN ('epub', 'cbz'))));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
