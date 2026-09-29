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

/// <summary>Derselbe Mensch zweimal in den Meldelisten (0.597.0, gemeldet 2026-09-29 an einem PGN-Import auf Dev): ohne
/// FIDE-ID mit und ohne Komma („Lenk Markus" / „Lenk, Markus"), bzw. unter zwei FIDE-IDs im selben Verein („Forster,
/// Stephan" 24649651 bis 2019/20, seither 24652091).</summary>
public class LeagueRosterSamePersonTests
{
    private static LeagueRosterIndex.Row R(string team, string name, string? fide, string season, int tnr = 1) =>
        new(tnr, team, name, LeagueNames.NameKey(name), fide, season);

    [Fact]
    public void WithoutFide_SpellingsOfOneName_AreOnePerson()
    {
        var roster = new LeagueRosterIndex(new[]
        {
            R("Schach Ohne Grenzen", "Lenk Markus", null, "2017/18"),
            R("Schach Ohne Grenzen 2", "Lenk Markus", null, "2018/19"),
            R("Schach Ohne Grenzen", "Lenk, Markus", null, "2025/26"),
        });
        var hit = roster.Match("Lenk, Markus", null);
        Assert.False(hit.Ambiguous);
        Assert.Equal("Lenk, Markus", hit.Person?.Name);                                  // jüngste Schreibweise mit Komma
        Assert.Single(roster.People);
    }

    [Fact]
    public void TwoFideIds_SameNameSameClub_AreOnePerson_TheLatestIdCounts()
    {
        var roster = new LeagueRosterIndex(new[]
        {
            R("Schach Ohne Grenzen", "Forster Stephan", "24649651", "2017/18", 296718),
            R("Schach Ohne Grenzen 1", "Forster Stephan", "24649651", "2019/20", 463191),
            R("Schach Ohne Grenzen", "Forster Stephan", "24652091", "2021/22", 577467),
            R("Schach Ohne Grenzen", "Forster, Stephan", "24652091", "2026/27", 1479342),
            R("Schach Ohne Grenzen", "Forster Stephan", null, "2022/23", 700000),       // ohne ID: gehört auch dazu
        });
        var hit = roster.Match("Forster, Stephan", null);
        Assert.False(hit.Ambiguous);
        Assert.Equal("24652091", hit.Person?.Fide);
        Assert.Same(hit.Person, roster.ByFide("24649651"));                              // die alte ID führt zu ihm
        Assert.Same(hit.Person, roster.Match("Irgendwie", "24649651").Person);
        Assert.Single(roster.People);
    }

    [Fact]
    public void TwoFideIds_SameName_DifferentClubs_StayTwoPeople()
    {
        var roster = new LeagueRosterIndex(new[]
        {
            R("Absam", "Huber, Franz", "1", "2025/26"),
            R("Hall", "Huber, Franz", "2", "2025/26"),
        });
        var hit = roster.Match("Huber, Franz", null);
        Assert.True(hit.Ambiguous);                                                       // Namensvettern: bleibt eine Frage
        Assert.Equal(2, hit.Candidates.Count);
    }

    [Theory]
    [InlineData("Schach Ohne Grenzen 2", "schach ohne grenzen")]
    [InlineData("Sk Telfs", "sk telfs")]
    [InlineData("Spg Fügen-Mayrhofen/Zillertal/", "spg fügen-mayrhofen/zillertal")]
    public void ClubBase_DropsTheTeamNumber(string team, string expected) => Assert.Equal(expected, LeagueRosterIndex.ClubBase(team));
}
