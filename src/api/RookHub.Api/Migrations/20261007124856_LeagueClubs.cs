using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <summary>
    /// LeagueHub für mehrere Vereine (2026-10-07): Vereine als Mandanten. Legt <c>LeagueClubs</c> (1 = SK Schwaz, Tirol;
    /// 2 = SK Weilheim, Bayern/Ligamanager) und <c>LeagueClubMembers</c> an, hängt <c>ClubId</c> an Vereinspartien, Entwürfe,
    /// Stapel-Uploads, Teilen-Links und Liga-Einlesungen — ALLE Bestandszeilen gehören Verein 1 —, ordnet JEDE Gruppe, deren
    /// Rollen <c>league.view</c> tragen (außer „Everyone"), Verein 1 zu (auf Dev und Prod genau „Schwaz"), und benennt den
    /// gemeinsamen Taktik-Kurs <c>tactics-club.pgn</c> in den von Verein 1 um (<c>tactics-club-1.pgn</c>; die LineIds der
    /// Aufgaben bleiben, Fortschritt hängt an Buch- und Aufgaben-Id).
    /// </summary>
    public partial class LeagueClubs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeagueShares_Tnr_Round_Team",
                table: "LeagueShares");

            migrationBuilder.AddColumn<int>(
                name: "ClubId",
                table: "ScoresheetScans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClubId",
                table: "LeagueShares",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "ClubId",
                table: "LeagueClubGames",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "ClubId",
                table: "LeagueClubDrafts",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "ClubId",
                table: "LeagueBatchUploads",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "LeagueClubs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TeamPrefix = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AnonName = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueClubs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Die beiden ersten Vereine — vor den Fremdschlüsseln, die Bestandszeilen tragen schon ClubId = 1.
            migrationBuilder.Sql(@"INSERT INTO LeagueClubs (Id, Name, TeamPrefix, AnonName, Source, CreatedAt) VALUES
                (1, 'SK Schwaz', 'Schwaz', 'Schwaz', NULL, UTC_TIMESTAMP(6)),
                (2, 'SK Weilheim', 'SK Weilheim', 'Weilheim', 'ligamanager', UTC_TIMESTAMP(6));");
            // Liga-Einlesungen (nur die) gehören Verein 1; RookHubs eigene behalten NULL.
            migrationBuilder.Sql("UPDATE ScoresheetScans SET ClubId = 1 WHERE Purpose = 'league';");

            migrationBuilder.CreateTable(
                name: "LeagueClubMembers",
                columns: table => new
                {
                    ClubId = table.Column<int>(type: "int", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueClubMembers", x => new { x.ClubId, x.GroupId });
                    table.ForeignKey(
                        name: "FK_LeagueClubMembers_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeagueClubMembers_LeagueClubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "LeagueClubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Jede Gruppe mit league.view (die heutige Vereinsgruppe „Schwaz") gehört Verein 1 — aus dem Code nicht ableitbar,
            // daher über die Rollen; „Everyone" nie. Weilheims Gruppe ordnet der Admin später zu (POST admin/clubs/2/groups/{id}).
            migrationBuilder.Sql(@"INSERT INTO LeagueClubMembers (ClubId, GroupId)
                SELECT DISTINCT 1, gr.GroupId FROM GroupRoles gr
                JOIN RolePermissions rp ON rp.RoleId = gr.RoleId
                JOIN `Groups` g ON g.Id = gr.GroupId
                WHERE rp.Permission = 'league.view' AND g.IsEveryone = 0;");
            // Der gemeinsame Taktik-Kurs wird der von Verein 1 (TacticHarvestService.ClubBookOf).
            migrationBuilder.Sql(@"UPDATE Books SET FileName = 'tactics-club-1.pgn', DisplayName = 'Taktiken aus Vereinspartien – SK Schwaz'
                WHERE FileName = 'tactics-club.pgn';");
            migrationBuilder.Sql("UPDATE BookPuzzles SET BookFileName = 'tactics-club-1.pgn' WHERE BookFileName = 'tactics-club.pgn';");

            migrationBuilder.CreateIndex(
                name: "IX_ScoresheetScans_ClubId",
                table: "ScoresheetScans",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueShares_ClubId_Tnr_Round_Team",
                table: "LeagueShares",
                columns: new[] { "ClubId", "Tnr", "Round", "Team" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClubGames_ClubId_Year",
                table: "LeagueClubGames",
                columns: new[] { "ClubId", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClubDrafts_ClubId",
                table: "LeagueClubDrafts",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueBatchUploads_ClubId",
                table: "LeagueBatchUploads",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClubMembers_GroupId",
                table: "LeagueClubMembers",
                column: "GroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueClubs_Name",
                table: "LeagueClubs",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LeagueBatchUploads_LeagueClubs_ClubId",
                table: "LeagueBatchUploads",
                column: "ClubId",
                principalTable: "LeagueClubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LeagueClubDrafts_LeagueClubs_ClubId",
                table: "LeagueClubDrafts",
                column: "ClubId",
                principalTable: "LeagueClubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LeagueClubGames_LeagueClubs_ClubId",
                table: "LeagueClubGames",
                column: "ClubId",
                principalTable: "LeagueClubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LeagueShares_LeagueClubs_ClubId",
                table: "LeagueShares",
                column: "ClubId",
                principalTable: "LeagueClubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScoresheetScans_LeagueClubs_ClubId",
                table: "ScoresheetScans",
                column: "ClubId",
                principalTable: "LeagueClubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE BookPuzzles SET BookFileName = 'tactics-club.pgn' WHERE BookFileName = 'tactics-club-1.pgn';");
            migrationBuilder.Sql(@"UPDATE Books SET FileName = 'tactics-club.pgn', DisplayName = 'Taktiken aus Vereinspartien'
                WHERE FileName = 'tactics-club-1.pgn';");

            migrationBuilder.DropForeignKey(
                name: "FK_LeagueBatchUploads_LeagueClubs_ClubId",
                table: "LeagueBatchUploads");

            migrationBuilder.DropForeignKey(
                name: "FK_LeagueClubDrafts_LeagueClubs_ClubId",
                table: "LeagueClubDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_LeagueClubGames_LeagueClubs_ClubId",
                table: "LeagueClubGames");

            migrationBuilder.DropForeignKey(
                name: "FK_LeagueShares_LeagueClubs_ClubId",
                table: "LeagueShares");

            migrationBuilder.DropForeignKey(
                name: "FK_ScoresheetScans_LeagueClubs_ClubId",
                table: "ScoresheetScans");

            migrationBuilder.DropTable(
                name: "LeagueClubMembers");

            migrationBuilder.DropTable(
                name: "LeagueClubs");

            migrationBuilder.DropIndex(
                name: "IX_ScoresheetScans_ClubId",
                table: "ScoresheetScans");

            migrationBuilder.DropIndex(
                name: "IX_LeagueShares_ClubId_Tnr_Round_Team",
                table: "LeagueShares");

            migrationBuilder.DropIndex(
                name: "IX_LeagueClubGames_ClubId_Year",
                table: "LeagueClubGames");

            migrationBuilder.DropIndex(
                name: "IX_LeagueClubDrafts_ClubId",
                table: "LeagueClubDrafts");

            migrationBuilder.DropIndex(
                name: "IX_LeagueBatchUploads_ClubId",
                table: "LeagueBatchUploads");

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "ScoresheetScans");

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "LeagueShares");

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "LeagueClubGames");

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "LeagueClubDrafts");

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "LeagueBatchUploads");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueShares_Tnr_Round_Team",
                table: "LeagueShares",
                columns: new[] { "Tnr", "Round", "Team" },
                unique: true);
        }
    }
}
