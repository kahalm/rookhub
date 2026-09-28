using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>Ähnliche Namen zur Schnellauswahl (0.596.0, Wunsch 2026-09-28: „wenn du Namen nicht direkt findest, schau, ob ein
/// ähnlicher Name bei den Ligaspielern existiert"): Tippfehler, Umlaute, Reihenfolge — aber keine Verwechslung kurzer Namen.</summary>
public class LeagueRosterSimilarTests
{
    private static readonly LeagueRosterIndex Roster = new(new[]
    {
        new LeagueRosterIndex.Row(1, "Schwaz", "Spindelberger, Paul", "spindelberger, paul", "1001"),
        new LeagueRosterIndex.Row(1, "Absam", "Hengl, Philip", "hengl, philip", "222"),
        new LeagueRosterIndex.Row(1, "Absam", "Schnabl, Andreas Dr.", "schnabl, andreas", "333"),
        new LeagueRosterIndex.Row(1, "Hall", "Mühlbacher, Bernhard", "muhlbacher, bernhard", "444"),
        new LeagueRosterIndex.Row(1, "Hall", "Wolf, Anna", "wolf, anna", "555"),
        new LeagueRosterIndex.Row(1, "Kufstein", "Kinsiz, Onur", "kinsiz, onur", "6301517"),
    });

    private static string[] Names(string raw) => Roster.Similar(raw).Select(p => p.Name).ToArray();

    [Theory]
    [InlineData("Spindlberger, Paul", "Spindelberger, Paul")]        // ein Buchstabe fehlt
    [InlineData("Paul Spindelbreger", "Spindelberger, Paul")]        // Reihenfolge + zwei vertauschte Buchstaben
    [InlineData("Hengl, Phillip", "Hengl, Philip")]                  // Vorname doppelt geschrieben
    [InlineData("Schnabel, Andreas", "Schnabl, Andreas Dr.")]        // Titel in der Meldeliste, Tippfehler im Nachnamen
    [InlineData("Muehlbacher, Berhnard", "Mühlbacher, Bernhard")]   // Umlaut ausgeschrieben + Vertauschung
    [InlineData("Spindlberger, P.", "Spindelberger, Paul")]          // Tippfehler + Anfangsbuchstabe
    public void Typos_FindTheLeaguePlayer(string raw, string expected) => Assert.Contains(expected, Names(raw));

    [Theory]
    [InlineData("Golf, Anna")]          // kurze Namen: kein Tippfehler erlaubt, sonst wird aus jedem Wolf ein Golf
    [InlineData("Niemand, Kennt")]
    [InlineData("Kinsiz, Atlas")]       // Nachname passt, der Vorname ist ein anderer Mensch
    [InlineData("")]
    public void NoFalseFriends(string raw) => Assert.Empty(Names(raw));

    [Fact]
    public void Distance_CountsTypos_AdjacentSwapIsOne()
    {
        Assert.Equal(0, LeagueRosterIndex.Distance("hengl", "hengl"));
        Assert.Equal(1, LeagueRosterIndex.Distance("spindlberger", "spindelberger"));
        Assert.Equal(1, LeagueRosterIndex.Distance("berhnard", "bernhard"));
        Assert.Equal(2, LeagueRosterIndex.Distance("mueller", "muller") + 1);
    }
}
