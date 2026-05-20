using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFirstNameLastNameToUserProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "UserProfiles",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "UserProfiles",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "UserProfiles");
        }
    }
}
