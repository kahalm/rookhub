using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClubMemberPhotoFace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "FaceR",
                table: "ClubMemberPhotos",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FaceX",
                table: "ClubMemberPhotos",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FaceY",
                table: "ClubMemberPhotos",
                type: "double",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaceR",
                table: "ClubMemberPhotos");

            migrationBuilder.DropColumn(
                name: "FaceX",
                table: "ClubMemberPhotos");

            migrationBuilder.DropColumn(
                name: "FaceY",
                table: "ClubMemberPhotos");
        }
    }
}
