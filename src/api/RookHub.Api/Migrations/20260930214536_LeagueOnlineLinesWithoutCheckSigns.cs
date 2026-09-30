using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <summary>
    /// Keine Schema-Aenderung (Codereview N4-001): bis hierher uebernahm <c>LeagueOnlineSync.ParseLichess</c> die Zuege des
    /// Lichess-Exports roh — mit Schach- und Mattzeichen („Bb4+", „Qxf7#") —, waehrend Brett- und chess.com-Partien ueber
    /// <c>PgnParser.CleanSan</c> ohne sie laufen. Der Eroeffnungsbaum zeigte denselben Zug deshalb zweimal, und seine
    /// Praefixsuche (<c>Line == pre || Line.StartsWith(pre + " ")</c>) verlor nach einem Schachgebot die andere Quelle. Neue
    /// Partien kommen seither bereinigt an; diese Migration macht dasselbe einmal fuer den Bestand: „+" und „#" aus
    /// <c>Line</c> und <c>Moves</c> der Online-Partien. In englischer SAN kommen beide Zeichen nur als Schach/Matt vor, und
    /// die Lichess-Zugliste traegt keine Bewertungszeichen — REPLACE ist also genau <c>CleanSan</c>. chess.com-Zeilen sind
    /// schon sauber und fallen aus der Bedingung; eine zweite Ausfuehrung findet nichts mehr. Down stellt nichts wieder
    /// her (die Zeichen waren im Baum nie gewollt). Der Rumpf ist von Hand, das Modell ist unveraendert (der Designer
    /// entspricht dem vorigen Stand).
    /// </summary>
    public partial class LeagueOnlineLinesWithoutCheckSigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE LeagueOnlineGames
                SET Line = REPLACE(REPLACE(Line, '+', ''), '#', ''),
                    Moves = REPLACE(REPLACE(Moves, '+', ''), '#', '')
                WHERE Line LIKE '%+%' OR Line LIKE '%#%' OR Moves LIKE '%+%' OR Moves LIKE '%#%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
