using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChessableActivityAndGoal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChessableMinutes",
                table: "UserTrainingGoals",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ChessableMinutes",
                table: "GroupTrainingGoals",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ChessableActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TimeSeconds = table.Column<int>(type: "int", nullable: false),
                    MovesTrained = table.Column<int>(type: "int", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChessableActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChessableActivities_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ChessableActivities_UserId_AttemptedAt",
                table: "ChessableActivities",
                columns: new[] { "UserId", "AttemptedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChessableActivities");

            migrationBuilder.DropColumn(
                name: "ChessableMinutes",
                table: "UserTrainingGoals");

            migrationBuilder.DropColumn(
                name: "ChessableMinutes",
                table: "GroupTrainingGoals");
        }
    }
}
