using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Turniername als Datenquelle. Alter und Geschlechtsklasse stehen nirgends sonst — nicht in
/// der chess-results-Turniersuche und nicht auf der Turnierseite. Diese Tests halten die
/// Wortmuster fest, an denen sie erkannt werden, und vor allem die Faelle, in denen ein naiver
/// Vergleich falsch liegt.
/// </summary>
public class TournamentClassifierTests
{
    [Theory]
    [InlineData("Landesmeisterschaft U12", TournamentAgeGroups.U12)]
    [InlineData("Tiroler Jugend U-14 Turnier", TournamentAgeGroups.U14)]
    [InlineData("U-14 Girls Kamthi Taluka DSO Chess Tournament,2026", TournamentAgeGroups.U14)]
    [InlineData("UNDER 16 BOYS - Dragon Chess Academy", TournamentAgeGroups.U16)]
    [InlineData("UNDER 16 GIRLS - Dragon Chess Academy", TournamentAgeGroups.U16)]
    [InlineData("U8 Landescup", TournamentAgeGroups.U8)]
    [InlineData("U18 Blitz", TournamentAgeGroups.U18)]
    public void AgeGroupsOf_ReadsTheClassFromTheName(string name, TournamentAgeGroups expected)
    {
        Assert.Equal(expected, TournamentClassifier.AgeGroupsOf(name));
    }

    [Fact]
    public void AgeGroupsOf_SeveralClassesInOneEvent_ReturnsThemAll()
    {
        var groups = TournamentClassifier.AgeGroupsOf("Landesmeisterschaft U10 U12 U14");

        Assert.Equal(TournamentAgeGroups.U10 | TournamentAgeGroups.U12 | TournamentAgeGroups.U14, groups);
    }

    /// <summary>
    /// „U2000" ist eine RATINGgrenze. Fiele sie unter die Altersregex, waere jedes Amateur-Open
    /// ein Jugendturnier — und der Schalter „nur Erwachsene" haette den halben Bestand versteckt.
    /// </summary>
    [Theory]
    [InlineData("Open Braunau U2000")]
    [InlineData("Vereinsturnier U1600")]
    [InlineData("Runde 14 der Betriebsmeisterschaft")]
    public void AgeGroupsOf_RatingLimitsAndRoundNumbers_AreNotAgeClasses(string name)
    {
        Assert.Equal(TournamentAgeGroups.None, TournamentClassifier.AgeGroupsOf(name));
    }

    /// <summary>
    /// Eine Klasse, die es offiziell nicht gibt, wird aufgerundet: „U9" ist ein U10-Turnier. Ohne
    /// das faende der U10-Filter die Ausschreibung nicht, obwohl sie genau dieses Publikum meint.
    /// </summary>
    [Fact]
    public void AgeGroupsOf_UnofficialClass_RoundsUpToTheNextOne()
    {
        Assert.Equal(TournamentAgeGroups.U10, TournamentClassifier.AgeGroupsOf("Nachwuchscup U9"));
    }

    /// <summary>
    /// Jugendturnier ohne Klassenangabe. „Schachrallye" ist eine oertliche Gewohnheit (Telfs) und
    /// steckt genau deshalb in der Wortliste — solche Faelle sind der Grund, dass es sie gibt.
    /// </summary>
    [Theory]
    [InlineData("Landesjugendmeisterschaft 2026")]
    [InlineData("Schachrallye Telfs 2026Gruppe B")]
    [InlineData("Schuelerliga Bezirk Innsbruck")]
    public void AgeGroupsOf_YouthWithoutAClass_IsStillYouth(string name)
    {
        var groups = TournamentClassifier.AgeGroupsOf(name);

        Assert.Equal(TournamentAgeGroups.YouthUnspecified, groups & TournamentClassifier.YouthMask);
    }

    [Fact]
    public void AgeGroupsOf_NoMarkerAtAll_IsAnAdultOpen()
    {
        Assert.Equal(TournamentAgeGroups.None, TournamentClassifier.AgeGroupsOf("Open Braunau 2026 A"));
    }

    /// <summary>Seniorenschach ist Erwachsenenschach — es darf nicht in die Jugendmaske fallen.</summary>
    [Fact]
    public void AgeGroupsOf_Seniors_AreNotYouth()
    {
        var groups = TournamentClassifier.AgeGroupsOf("Oesterreichische Seniorenmeisterschaft");

        Assert.Equal(TournamentAgeGroups.Senior, groups);
        Assert.Equal(TournamentAgeGroups.None, groups & TournamentClassifier.YouthMask);
    }

    [Theory]
    [InlineData("U-14 Girls Kamthi Taluka", TournamentGender.Female)]
    [InlineData("UNDER 16 GIRLS - Dragon Chess Academy", TournamentGender.Female)]
    [InlineData("Landesmeisterschaft U12 weiblich", TournamentGender.Female)]
    [InlineData("Damenliga Steiermark", TournamentGender.Female)]
    [InlineData("Frauen-Bundesliga", TournamentGender.Female)]
    [InlineData("UNDER 16 BOYS - Dragon Chess Academy", TournamentGender.Male)]
    [InlineData("Knabenmeisterschaft U10", TournamentGender.Male)]
    [InlineData("Open Braunau 2026 A", TournamentGender.Open)]
    public void GenderOf_ReadsTheClassFromTheName(string name, TournamentGender expected)
    {
        Assert.Equal(expected, TournamentClassifier.GenderOf(name));
    }

    /// <summary>
    /// Die Falle, an der die erste Fassung scheiterte: „men" steckt in „women" UND in „Damen",
    /// „male" in „female". Als Teilzeichenkette gesucht war jedes Frauenturnier gleichzeitig ein
    /// Herrenturnier — und damit, weil beides gesetzt war, wieder ein offenes.
    /// </summary>
    [Theory]
    [InlineData("WOMEN Championship 2026")]
    [InlineData("Damen Landesmeisterschaft")]
    [InlineData("Female Open Vienna")]
    public void GenderOf_WordsContainingMen_StayFemale(string name)
    {
        Assert.Equal(TournamentGender.Female, TournamentClassifier.GenderOf(name));
    }

    /// <summary>
    /// Ein freistehender Buchstabe ist in diesen Namen eine GRUPPE („Open Braunau 2026 B"), keine
    /// Geschlechtsklasse. Als solche zaehlt er nur direkt hinter der Altersklasse.
    /// </summary>
    [Theory]
    [InlineData("Open Braunau 2026 M", TournamentGender.Open)]
    [InlineData("Schachrallye Telfs Gruppe W", TournamentGender.Open)]
    [InlineData("Landescup U12w", TournamentGender.Female)]
    [InlineData("Landescup U16 m", TournamentGender.Male)]
    public void GenderOf_SingleLetter_CountsOnlyBehindAnAgeClass(string name, TournamentGender expected)
    {
        Assert.Equal(expected, TournamentClassifier.GenderOf(name));
    }

    [Fact]
    public void GenderOf_BothMentioned_IsOpen()
    {
        Assert.Equal(TournamentGender.Open,
            TournamentClassifier.GenderOf("Landesmeisterschaft Damen und Herren"));
    }

    [Theory]
    [InlineData("2. Bundesliga Mitte")]
    [InlineData("Tiroler Landesliga 2026/27")]
    [InlineData("Kaerntner Landesklasse A")]
    [InlineData("Mannschaftsmeisterschaft Salzburg")]
    public void LooksLikeLeague_NameSaysSo_IsALeague(string name)
    {
        Assert.True(TournamentClassifier.LooksLikeLeague(
            name, TournamentKind.Team, new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 4)));
    }

    /// <summary>
    /// Der zweite, sprachunabhaengige Weg: eine Saison laeuft Monate, ein Mannschaftsturnier am
    /// Wochenende einen Tag. Der Name „Steirischer Mannschaftscup" verraet nichts.
    /// </summary>
    [Fact]
    public void LooksLikeLeague_TeamEventOverAWholeSeason_IsALeague()
    {
        Assert.True(TournamentClassifier.LooksLikeLeague(
            "Steirischer Mannschaftscup", TournamentKind.Team,
            new DateOnly(2026, 10, 1), new DateOnly(2027, 4, 15)));
    }

    [Fact]
    public void LooksLikeLeague_TeamEventOnASingleWeekend_IsNot()
    {
        Assert.False(TournamentClassifier.LooksLikeLeague(
            "Steirischer Mannschaftscup", TournamentKind.Team,
            new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 4)));
    }

    /// <summary>
    /// Ein monatelanges EINZELturnier ist eine Vereinsmeisterschaft, keine Liga. Griffe die
    /// Dauerregel auch dort, verschwaende der Schalter „Ligen ausblenden" genau die Turniere, die
    /// ein Vereinsspieler sucht.
    /// </summary>
    [Fact]
    public void LooksLikeLeague_LongIndividualEvent_IsNotALeague()
    {
        Assert.False(TournamentClassifier.LooksLikeLeague(
            "Vereinsmeisterschaft SK Hietzing", TournamentKind.Individual,
            new DateOnly(2026, 10, 1), new DateOnly(2027, 4, 15)));
    }
}
