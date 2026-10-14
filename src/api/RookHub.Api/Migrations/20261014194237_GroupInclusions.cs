using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class GroupInclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupClosures",
                columns: table => new
                {
                    AncestorId = table.Column<int>(type: "int", nullable: false),
                    DescendantId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupClosures", x => new { x.AncestorId, x.DescendantId });
                    table.ForeignKey(
                        name: "FK_GroupClosures_Groups_AncestorId",
                        column: x => x.AncestorId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupClosures_Groups_DescendantId",
                        column: x => x.DescendantId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GroupInclusions",
                columns: table => new
                {
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    IncludedGroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupInclusions", x => new { x.GroupId, x.IncludedGroupId });
                    table.ForeignKey(
                        name: "FK_GroupInclusions_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupInclusions_Groups_IncludedGroupId",
                        column: x => x.IncludedGroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GroupClosures_DescendantId",
                table: "GroupClosures",
                column: "DescendantId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupInclusions_IncludedGroupId",
                table: "GroupInclusions",
                column: "IncludedGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupClosures");

            migrationBuilder.DropTable(
                name: "GroupInclusions");
        }
    }
}
