using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeoPlaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Country = table.Column<string>(type: "varchar(2)", maxLength: 2, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PostalCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NameNormalized = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Lat = table.Column<double>(type: "double", nullable: false),
                    Lon = table.Column<double>(type: "double", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Population = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeoPlaces", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TournamentDirectoryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ChessResultsId = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Federation = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StartsOnWeekend = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LocationText = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TimeControlText = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Speed = table.Column<int>(type: "int", nullable: false),
                    Organizer = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Director = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChiefArbiter = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rounds = table.Column<int>(type: "int", nullable: true),
                    PlayerCount = table.Column<int>(type: "int", nullable: true),
                    UpstreamUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Lat = table.Column<double>(type: "double", nullable: true),
                    Lon = table.Column<double>(type: "double", nullable: true),
                    GeoSource = table.Column<int>(type: "int", nullable: false),
                    GeoPlaceName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    MissedSweeps = table.Column<int>(type: "int", nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ChangeHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentDirectoryEntries", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TournamentDirectorySweeps",
                columns: table => new
                {
                    Federation = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastSweptAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastAttemptedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastRowCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentDirectorySweeps", x => x.Federation);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TournamentSearchProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PlaceQuery = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Lat = table.Column<double>(type: "double", nullable: false),
                    Lon = table.Column<double>(type: "double", nullable: false),
                    RadiusKm = table.Column<int>(type: "int", nullable: false),
                    Federations = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Speeds = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WeekendOnly = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MinPlayers = table.Column<int>(type: "int", nullable: true),
                    NotifyNew = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentSearchProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentSearchProfiles_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GeoPlaces_Country_NameNormalized",
                table: "GeoPlaces",
                columns: new[] { "Country", "NameNormalized" });

            migrationBuilder.CreateIndex(
                name: "IX_GeoPlaces_Country_PostalCode",
                table: "GeoPlaces",
                columns: new[] { "Country", "PostalCode" });

            migrationBuilder.CreateIndex(
                name: "IX_GeoPlaces_NameNormalized",
                table: "GeoPlaces",
                column: "NameNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentDirectoryEntries_ChessResultsId",
                table: "TournamentDirectoryEntries",
                column: "ChessResultsId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentDirectoryEntries_EndDate",
                table: "TournamentDirectoryEntries",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentDirectoryEntries_Federation_EndDate",
                table: "TournamentDirectoryEntries",
                columns: new[] { "Federation", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentDirectoryEntries_Lat_Lon",
                table: "TournamentDirectoryEntries",
                columns: new[] { "Lat", "Lon" });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentSearchProfiles_UserId_Name",
                table: "TournamentSearchProfiles",
                columns: new[] { "UserId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeoPlaces");

            migrationBuilder.DropTable(
                name: "TournamentDirectoryEntries");

            migrationBuilder.DropTable(
                name: "TournamentDirectorySweeps");

            migrationBuilder.DropTable(
                name: "TournamentSearchProfiles");
        }
    }
}
