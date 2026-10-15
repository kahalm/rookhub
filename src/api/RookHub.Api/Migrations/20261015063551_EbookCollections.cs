using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EbookCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CollectionId",
                table: "Ebooks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EbookCollections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EbookCollections", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Ebooks_CollectionId",
                table: "Ebooks",
                column: "CollectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Ebooks_EbookCollections_CollectionId",
                table: "Ebooks",
                column: "CollectionId",
                principalTable: "EbookCollections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ebooks_EbookCollections_CollectionId",
                table: "Ebooks");

            migrationBuilder.DropTable(
                name: "EbookCollections");

            migrationBuilder.DropIndex(
                name: "IX_Ebooks_CollectionId",
                table: "Ebooks");

            migrationBuilder.DropColumn(
                name: "CollectionId",
                table: "Ebooks");
        }
    }
}
