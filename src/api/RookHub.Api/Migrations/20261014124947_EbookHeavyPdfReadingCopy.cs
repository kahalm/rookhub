using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookHeavyPdfReadingCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0.775.4: übergroße Scan-PDFs (> 1,5 MB je Seite, ohne Lesefassung) bekommen eine leichte Lesefassung.
            migrationBuilder.Sql("UPDATE `Ebooks` e SET e.`ConversionState` = 1 WHERE e.`ConversionState` IN (0, 2) AND e.`PageCount` > 0 AND EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'pdf' AND f.`Derived` = 0 AND f.`SizeBytes` / e.`PageCount` > 1500000) AND NOT EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'pdf' AND f.`Derived` = 1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
