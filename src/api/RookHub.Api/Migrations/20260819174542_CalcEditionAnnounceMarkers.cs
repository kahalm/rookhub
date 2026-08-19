using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class CalcEditionAnnounceMarkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PublishAnnouncedAt",
                table: "CalcEditions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TesterAnnouncedAt",
                table: "CalcEditions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TesterAnnouncedUserIds",
                table: "CalcEditions",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishAnnouncedAt",
                table: "CalcEditions");

            migrationBuilder.DropColumn(
                name: "TesterAnnouncedAt",
                table: "CalcEditions");

            migrationBuilder.DropColumn(
                name: "TesterAnnouncedUserIds",
                table: "CalcEditions");
        }
    }
}
