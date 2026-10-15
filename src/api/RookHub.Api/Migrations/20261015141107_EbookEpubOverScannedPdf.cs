using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookEpubOverScannedPdf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0.795.0: Bücher mit PDF UND EPUB neu erkennen — beim gescannten PDF gewinnen jetzt die EPUB-Bilder (Swindler).
            migrationBuilder.Sql("UPDATE `Ebooks` e SET e.`DiagramState` = 0 WHERE e.`DiagramsCurated` = 0 AND EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'pdf') AND EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'epub');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
