using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Zusammenfassen der Gruppen EINES Turniers. Die Beispiele sind echte Namen aus der
/// chess-results-Trefferliste — inklusive der Faelle, die NICHT zusammengefasst werden duerfen.
/// </summary>
public class TournamentNameGroupingTests
{
    [Theory]
    [InlineData("Open Braunau 2026 A", "Open Braunau 2026")]
    [InlineData("Open Braunau 2026 B", "Open Braunau 2026")]
    [InlineData("Dekron Cup 2026 Gruppe A", "Dekron Cup 2026")]
    [InlineData("8. Bad Ischler Jugendturnier 2026(Gruppe A)", "8. Bad Ischler Jugendturnier 2026")]
    [InlineData("Torneo di Natale (Gr. 2)", "Torneo di Natale")]
    [InlineData("4. RLP-Jugend-Open A-Turnier", "4. RLP-Jugend-Open")]
    [InlineData("Sommercup Turnier B", "Sommercup")]
    [InlineData("Vianocny turnaj skupina C", "Vianocny turnaj")]
    [InlineData("Karacsonyi verseny csoport 2", "Karacsonyi verseny")]
    [InlineData("Herbstturnier III", "Herbstturnier")]
    public void BaseName_StripsGroupMarkers(string name, string expected)
        => Assert.Equal(expected, TournamentNameGrouping.BaseName(name));

    [Theory]
    // Der von chess-results GEKUERZTE Name — hier ist nichts mehr abzuschneiden.
    [InlineData("5° Torneo Internazionale Ortisei \"ad Gredine\" - Op")]
    // Diese duerfen NICHT verschmelzen: der Unterschied ist das Datum, keine Gruppe.
    [InlineData("KK Bardejov - 1.11.2025")]
    [InlineData("KK Bardejov - 25.10.2025")]
    // Eine Jahreszahl am Ende ist kein Gruppen-Kuerzel.
    [InlineData("Offene Wiener Landesmeisterschaft 2026")]
    // Ein Wort am Ende ebenso wenig — lieber zwei Eintraege als ein verstecktes Turnier.
    [InlineData("Open Braunau 2026 Jugend")]
    [InlineData("Grazer Sommerblitzcup 2026 - Termin 11")]
    public void BaseName_LeavesEverythingElseAlone(string name)
        => Assert.Equal(name, TournamentNameGrouping.BaseName(name));

    [Fact]
    public void BaseName_NeverShrinksToAStub()
    {
        // „Gruppe A" darf nicht auf den leeren Rest zusammenfallen.
        Assert.Equal("Gruppe A", TournamentNameGrouping.BaseName("Gruppe A"));
        Assert.Equal("A", TournamentNameGrouping.BaseName("A"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void BaseName_HandlesEmptyInput(string? name, string expected)
        => Assert.Equal(expected, TournamentNameGrouping.BaseName(name));

    [Fact]
    public void BaseName_CollapsesWhitespace()
        => Assert.Equal("Open Braunau 2026", TournamentNameGrouping.BaseName("Open   Braunau  2026   A"));

    [Theory]
    [InlineData("Open Braunau 2026 A", "A")]
    [InlineData("Dekron Cup 2026 Gruppe A", "Gruppe A")]
    [InlineData("8. Bad Ischler Jugendturnier 2026(Gruppe A)", "Gruppe A")]
    [InlineData("Herbstturnier III", "III")]
    public void GroupLabel_NamesWhatDistinguishesTheGroup(string name, string expected)
        => Assert.Equal(expected, TournamentNameGrouping.GroupLabel(name));

    [Fact]
    public void GroupLabel_IsEmpty_WhenChessResultsTruncatedTheSuffix()
        => Assert.Equal("", TournamentNameGrouping.GroupLabel("5° Torneo Internazionale Ortisei \"ad Gredine\" - Op"));
}
