using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    SubjectUserId = table.Column<int>(type: "int", nullable: true),
                    SubjectGroupId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogGrants", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CatalogRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RequesterUserId = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    ItemType = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogRequests", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogGrants_OwnerUserId_SubjectGroupId",
                table: "CatalogGrants",
                columns: new[] { "OwnerUserId", "SubjectGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogGrants_OwnerUserId_SubjectUserId",
                table: "CatalogGrants",
                columns: new[] { "OwnerUserId", "SubjectUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogRequests_OwnerUserId_Status",
                table: "CatalogRequests",
                columns: new[] { "OwnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogRequests_RequesterUserId",
                table: "CatalogRequests",
                column: "RequesterUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogGrants");

            migrationBuilder.DropTable(
                name: "CatalogRequests");
        }
    }
}
