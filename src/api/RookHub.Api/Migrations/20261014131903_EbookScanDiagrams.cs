using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookScanDiagrams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0.776.0: PDFs ohne Schach-Font (Scans, auch die PDF-Fassung reiner DJVU-Bücher) liest jetzt die Scan-Erkennung.
            migrationBuilder.Sql("UPDATE `Ebooks` e SET e.`DiagramState` = 0 WHERE e.`DiagramState` = 3 AND EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'pdf');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
