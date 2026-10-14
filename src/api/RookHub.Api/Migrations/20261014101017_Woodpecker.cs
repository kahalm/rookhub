using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class Woodpecker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WoodpeckerPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoodpeckerPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoodpeckerPlans_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WoodpeckerAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    Cycle = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    BookPuzzleId = table.Column<int>(type: "int", nullable: false),
                    Solved = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TimeSeconds = table.Column<int>(type: "int", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoodpeckerAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoodpeckerAttempts_BookPuzzles_BookPuzzleId",
                        column: x => x.BookPuzzleId,
                        principalTable: "BookPuzzles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WoodpeckerAttempts_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WoodpeckerAttempts_WoodpeckerPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "WoodpeckerPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WoodpeckerCycles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoodpeckerCycles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoodpeckerCycles_WoodpeckerPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "WoodpeckerPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WoodpeckerPlanBooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoodpeckerPlanBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoodpeckerPlanBooks_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WoodpeckerPlanBooks_WoodpeckerPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "WoodpeckerPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WoodpeckerSetItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    BookPuzzleId = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoodpeckerSetItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoodpeckerSetItems_BookPuzzles_BookPuzzleId",
                        column: x => x.BookPuzzleId,
                        principalTable: "BookPuzzles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WoodpeckerSetItems_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WoodpeckerSetItems_WoodpeckerPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "WoodpeckerPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_WoodpeckerAttempts_BookId",
                table: "WoodpeckerAttempts",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_WoodpeckerAttempts_BookPuzzleId",
                table: "WoodpeckerAttempts",
                column: "BookPuzzleId");

            migrationBuilder.CreateIndex(
                name: "IX_WoodpeckerAttempts_PlanId_Cycle_BookPuzzleId",
                table: "WoodpeckerAttempts",
                columns: new[] { "PlanId", "Cycle", "BookPuzzleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoodpeckerCycles_PlanId_Number",
                table: "WoodpeckerCycles",
                columns: new[] { "PlanId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoodpeckerPlanBooks_BookId",
                table: "WoodpeckerPlanBooks",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_WoodpeckerPlanBooks_PlanId_BookId",
                table: "WoodpeckerPlanBooks",
                columns: new[] { "PlanId", "BookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoodpeckerPlans_UserId",
                table: "WoodpeckerPlans",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WoodpeckerSetItems_BookId",
                table: "WoodpeckerSetItems",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_WoodpeckerSetItems_BookPuzzleId",
                table: "WoodpeckerSetItems",
                column: "BookPuzzleId");

            migrationBuilder.CreateIndex(
                name: "IX_WoodpeckerSetItems_PlanId_BookPuzzleId",
                table: "WoodpeckerSetItems",
                columns: new[] { "PlanId", "BookPuzzleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WoodpeckerAttempts");

            migrationBuilder.DropTable(
                name: "WoodpeckerCycles");

            migrationBuilder.DropTable(
                name: "WoodpeckerPlanBooks");

            migrationBuilder.DropTable(
                name: "WoodpeckerSetItems");

            migrationBuilder.DropTable(
                name: "WoodpeckerPlans");
        }
    }
}
