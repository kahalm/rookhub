using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClubMemberWithoutExtras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FideId",
                table: "ClubMembers");

            migrationBuilder.DropColumn(
                name: "NationalId",
                table: "ClubMembers");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "ClubMembers");

            migrationBuilder.DropColumn(
                name: "PhotoConsent",
                table: "ClubMembers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FideId",
                table: "ClubMembers",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                table: "ClubMembers",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "ClubMembers",
                type: "text",
                maxLength: 4000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "PhotoConsent",
                table: "ClubMembers",
                type: "tinyint(1)",
                nullable: true);
        }
    }
}
