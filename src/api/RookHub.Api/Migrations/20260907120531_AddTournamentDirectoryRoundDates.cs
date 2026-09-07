using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentDirectoryRoundDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RoundPlanCheckedAt",
                table: "TournamentDirectoryEntries",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TournamentDirectoryRounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TournamentDirectoryEntryId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeText = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentDirectoryRounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentDirectoryRounds_TournamentDirectoryEntries_Tournam~",
                        column: x => x.TournamentDirectoryEntryId,
                        principalTable: "TournamentDirectoryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentDirectoryRounds_Date",
                table: "TournamentDirectoryRounds",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentDirectoryRounds_TournamentDirectoryEntryId_Number",
                table: "TournamentDirectoryRounds",
                columns: new[] { "TournamentDirectoryEntryId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentDirectoryRounds");

            migrationBuilder.DropColumn(
                name: "RoundPlanCheckedAt",
                table: "TournamentDirectoryEntries");
        }
    }
}
