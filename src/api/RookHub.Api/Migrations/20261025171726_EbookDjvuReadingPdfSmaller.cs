using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookDjvuReadingPdfSmaller : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0.837.5: Lesefassungen aus DJVU wurden in voller Scan-Auflösung fast unkomprimiert geschrieben (bis 1,3 GB) —
            // diese Bücher wieder auf „ausstehend“, der Umwandler erzeugt die PDF am selben Pfad kleiner neu.
            migrationBuilder.Sql("UPDATE `Ebooks` e SET e.`ConversionState` = 1 WHERE e.`ConversionState` = 2 " +
                "AND EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'djvu') " +
                "AND EXISTS (SELECT 1 FROM `EbookFiles` f WHERE f.`EbookId` = e.`Id` AND f.`Format` = 'pdf' AND f.`Derived` = 1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
