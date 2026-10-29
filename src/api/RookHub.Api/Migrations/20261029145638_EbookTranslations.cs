using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Ebooks",
                type: "varchar(8)",
                maxLength: 8,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "LanguageSource",
                table: "Ebooks",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EbookTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EbookId = table.Column<int>(type: "int", nullable: false),
                    Language = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NotBefore = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SourceFileId = table.Column<int>(type: "int", nullable: true),
                    SourceHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RelativePath = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    SegmentsTotal = table.Column<int>(type: "int", nullable: false),
                    SegmentsDone = table.Column<int>(type: "int", nullable: false),
                    SegmentsFailed = table.Column<int>(type: "int", nullable: false),
                    Model = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Error = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Runs = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EbookTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EbookTranslations_Ebooks_EbookId",
                        column: x => x.EbookId,
                        principalTable: "Ebooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Ebooks_LanguageSource_Language",
                table: "Ebooks",
                columns: new[] { "LanguageSource", "Language" });

            migrationBuilder.CreateIndex(
                name: "IX_EbookTranslations_EbookId_Language",
                table: "EbookTranslations",
                columns: new[] { "EbookId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EbookTranslations_State_Priority",
                table: "EbookTranslations",
                columns: new[] { "State", "Priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EbookTranslations");

            migrationBuilder.DropIndex(
                name: "IX_Ebooks_LanguageSource_Language",
                table: "Ebooks");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Ebooks");

            migrationBuilder.DropColumn(
                name: "LanguageSource",
                table: "Ebooks");
        }
    }
}
