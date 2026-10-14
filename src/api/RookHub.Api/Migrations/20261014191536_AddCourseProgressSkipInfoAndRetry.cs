using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseProgressSkipInfoAndRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RetryFailedAt",
                table: "CourseProgresses",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SkipInfoLines",
                table: "CourseProgresses",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryFailedAt",
                table: "CourseProgresses");

            migrationBuilder.DropColumn(
                name: "SkipInfoLines",
                table: "CourseProgresses");
        }
    }
}
