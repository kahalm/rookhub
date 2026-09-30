using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClubTrainersAndPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTrainer",
                table: "ClubMembers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ClubSessionPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    Image = table.Column<byte[]>(type: "LONGBLOB", nullable: false),
                    Thumb = table.Column<byte[]>(type: "MEDIUMBLOB", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubSessionPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubSessionPhotos_ClubSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ClubSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClubSessionPhotos_SessionId",
                table: "ClubSessionPhotos",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubSessionPhotos");

            migrationBuilder.DropColumn(
                name: "IsTrainer",
                table: "ClubMembers");
        }
    }
}
