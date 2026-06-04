using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TimeSeconds",
                table: "CoursePuzzleResults",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "GroupTrainingGoals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    PuzzleMinutes = table.Column<int>(type: "int", nullable: false),
                    BookMinutes = table.Column<int>(type: "int", nullable: false),
                    PlayMinutes = table.Column<int>(type: "int", nullable: false),
                    WeeklyDaysTarget = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupTrainingGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupTrainingGoals_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PlayTimeDailies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Platform = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Seconds = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayTimeDailies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayTimeDailies_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PlayTimeSyncs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Platform = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastGameTimestamp = table.Column<long>(type: "bigint", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastError = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayTimeSyncs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayTimeSyncs_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserTrainingGoals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PuzzleMinutes = table.Column<int>(type: "int", nullable: false),
                    BookMinutes = table.Column<int>(type: "int", nullable: false),
                    PlayMinutes = table.Column<int>(type: "int", nullable: false),
                    WeeklyDaysTarget = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTrainingGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTrainingGoals_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GroupTrainingGoals_GroupId",
                table: "GroupTrainingGoals",
                column: "GroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayTimeDailies_UserId_Date_Platform",
                table: "PlayTimeDailies",
                columns: new[] { "UserId", "Date", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayTimeSyncs_UserId_Platform",
                table: "PlayTimeSyncs",
                columns: new[] { "UserId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTrainingGoals_UserId",
                table: "UserTrainingGoals",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupTrainingGoals");

            migrationBuilder.DropTable(
                name: "PlayTimeDailies");

            migrationBuilder.DropTable(
                name: "PlayTimeSyncs");

            migrationBuilder.DropTable(
                name: "UserTrainingGoals");

            migrationBuilder.DropColumn(
                name: "TimeSeconds",
                table: "CoursePuzzleResults");
        }
    }
}
