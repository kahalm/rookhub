using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class RepertoireTrainingPrefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RepertoireTrainingPrefs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RepertoireId = table.Column<int>(type: "int", nullable: false),
                    LineOrder = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepertoireTrainingPrefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepertoireTrainingPrefs_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepertoireTrainingPrefs_Repertoires_RepertoireId",
                        column: x => x.RepertoireId,
                        principalTable: "Repertoires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RepertoireTrainingPrefs_RepertoireId",
                table: "RepertoireTrainingPrefs",
                column: "RepertoireId");

            migrationBuilder.CreateIndex(
                name: "IX_RepertoireTrainingPrefs_UserId_RepertoireId",
                table: "RepertoireTrainingPrefs",
                columns: new[] { "UserId", "RepertoireId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepertoireTrainingPrefs");
        }
    }
}
