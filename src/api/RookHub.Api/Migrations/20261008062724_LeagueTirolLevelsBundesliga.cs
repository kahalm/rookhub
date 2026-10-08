using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <summary>
    /// Österreichische Bundesliga in LeagueHub (0.719.0, Wunsch 2026-10-08): die 1. und 2. Bundesliga liegen ÜBER der Landesliga.
    /// Die Tiroler Stufen (chess-results, <c>Source IS NULL</c>) rücken um zwei nach unten — Landesliga 1 → 3, 1. Klasse 2 → 4,
    /// 2. Klasse 3 → 5, Gebietsklasse 4 → 6 —, Stufe 1/2 sind dann 1./2. Bundesliga (<c>LeagueLevels</c>). Ligamanager und
    /// Schachkreis Zugspitze (Bayern) bleiben. Die fertigen Ansichten tragen ihre alte Stufe noch im JSON (<c>level</c>, <c>hit</c>),
    /// bis „Daten aktualisieren" bzw. <c>admin/rebuild</c> sie neu rechnet.
    /// </summary>
    public partial class LeagueTirolLevelsBundesliga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE LeagueTournaments SET Level = Level + 2 WHERE Source IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nicht zerstörend: eingespielte Bundesliga-Ligen (Stufe 1/2) landen auf −1/0 — das alte Schema kennt sie nicht, die
            // Zeilen bleiben stehen (löschen per Hand, falls je zurückgegangen wird).
            migrationBuilder.Sql("UPDATE LeagueTournaments SET Level = Level - 2 WHERE Source IS NULL;");
        }
    }
}
