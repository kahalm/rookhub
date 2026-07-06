using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyPostBookChapter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceBookId",
                table: "WeeklyPosts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceChapter",
                table: "WeeklyPosts",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceBookId",
                table: "WeeklyPosts");

            migrationBuilder.DropColumn(
                name: "SourceChapter",
                table: "WeeklyPosts");
        }
    }
}
