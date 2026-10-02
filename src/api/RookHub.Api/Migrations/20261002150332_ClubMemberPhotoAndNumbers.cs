using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClubMemberPhotoAndNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AddColumn<long>(
                name: "PhotoVersion",
                table: "ClubMembers",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClubMemberPhotos",
                columns: table => new
                {
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    Image = table.Column<byte[]>(type: "MEDIUMBLOB", nullable: false),
                    Thumb = table.Column<byte[]>(type: "MEDIUMBLOB", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubMemberPhotos", x => x.MemberId);
                    table.ForeignKey(
                        name: "FK_ClubMemberPhotos_ClubMembers_MemberId",
                        column: x => x.MemberId,
                        principalTable: "ClubMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubMemberPhotos");

            migrationBuilder.DropColumn(
                name: "FideId",
                table: "ClubMembers");

            migrationBuilder.DropColumn(
                name: "NationalId",
                table: "ClubMembers");

            migrationBuilder.DropColumn(
                name: "PhotoVersion",
                table: "ClubMembers");
        }
    }
}
