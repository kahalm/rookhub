using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Auswahlregel der Spielort-Aufloesung ueber Vereinsnamen.
///
/// <para>Der Fall, der sie erzwungen hat, ist tnr1405166 („1. Frauenbundesliga AUT 2026/2027"):
/// Ortstext „Mayrhofen, St.Veit". Im Ortslexikon heisst GENAU EIN Eintrag „St. Veit" — und der
/// liegt in Tirol. Gemeint ist laut Ausschreibung „St. Veit an der Glan" in Kaernten, 250 km
/// entfernt. Aus dem Namen allein ist das nicht erkennbar, und es sieht dabei nicht einmal
/// mehrdeutig aus. Die Vereinsliste nennt „SV ASKOE St. Veit/Glan".</para>
///
/// <para>Weil die Kandidaten hier auf laengere Namen erweitert werden, MUSS die Regel streng
/// sein: ohne Beleg in einem Vereinsnamen bleibt alles, wie es war.</para>
/// </summary>
public class VenueDisambiguationServiceTests
{
    private static GeoPlace Place(string name, double lat, double lon) => new()
    {
        Country = "AT", Name = name, NameNormalized = GeoTextNormalizer.Normalize(name),
        Lat = lat, Lon = lon, Kind = GeoPlaceKind.PostalCode,
    };

    /// <summary>Die echten Vereine des Beispiel-Turniers.</summary>
    private static readonly string[] RealTeams =
    [
        "ASVÖ Pamhagen", "Grazer Schachgesellschaft", "SC Victoria Linz", "Schach ohne Grenzen",
        "SK DolomitenBank Lienz", "SK Dornbirn", "Steinitz Schach Akademie",
        "SV - Das Wien - St.Veit/Glan", "SV Raika Rapid Feffernitz", "SV Wulkaprodersdorf",
    ];

    [Fact]
    public void PickByTeamHint_TheRealCase_ResolvesStVeitToCarinthia()
    {
        var candidates = new List<GeoPlace>
        {
            Place("St. Veit", 47.3167, 11.0667),                  // Tirol — der zufaellige Namensgleiche
            Place("St. Veit an der Glan", 46.7681, 14.3603),       // Kaernten — gemeint
            Place("St. Veit an der Gölsen", 48.0432, 15.6694),
            Place("St. Veit im Jauntal", 46.5825, 14.5396),
        };

        var pick = VenueDisambiguationService.PickByTeamHint(candidates, "st veit", RealTeams);

        Assert.NotNull(pick);
        Assert.Equal("St. Veit an der Glan", pick!.Name);
    }

    [Fact]
    public void PickByTeamHint_WithoutEvidence_ChangesNothing()
    {
        // Kein Verein nennt einen der unterscheidenden Namensteile — dann bleibt es beim
        // bisherigen Stand, statt einen zweiten Fehlgriff zu wagen.
        var candidates = new List<GeoPlace>
        {
            Place("St. Veit", 47.3167, 11.0667),
            Place("St. Veit an der Glan", 46.7681, 14.3603),
        };

        Assert.Null(VenueDisambiguationService.PickByTeamHint(
            candidates, "st veit", ["SK Dornbirn", "Grazer Schachgesellschaft"]));
    }

    [Fact]
    public void PickByTeamHint_TwoCandidatesEquallySupported_ChangesNothing()
    {
        var candidates = new List<GeoPlace>
        {
            Place("St. Veit an der Glan", 46.7681, 14.3603),
            Place("St. Veit im Jauntal", 46.5825, 14.5396),
        };

        Assert.Null(VenueDisambiguationService.PickByTeamHint(
            candidates, "st veit", ["SV St. Veit/Glan", "SK Jauntal"]));
    }

    [Fact]
    public void PickByTeamHint_SameTownDifferentPostcodes_StillDecides()
    {
        // Zwei Postleitzahlen desselben Ortes sind kein Widerspruch — der Gleichstand darf die
        // Entscheidung nicht blockieren.
        var candidates = new List<GeoPlace>
        {
            Place("St. Veit an der Glan", 46.7681, 14.3603),
            Place("St. Veit an der Glan", 46.7690, 14.3610),
            Place("St. Veit", 47.3167, 11.0667),
        };

        var pick = VenueDisambiguationService.PickByTeamHint(candidates, "st veit", RealTeams);

        Assert.Equal("St. Veit an der Glan", pick!.Name);
    }

    [Fact]
    public void PickByTeamHint_FillerWordsAreNoEvidence()
    {
        // „an der" und „sankt" stehen in hunderten Ortsnamen — sie duerfen nichts entscheiden.
        var candidates = new List<GeoPlace>
        {
            Place("Neumarkt", 47.0, 14.5),
            Place("Neumarkt an der Ybbs", 48.1, 15.0),
        };

        Assert.Null(VenueDisambiguationService.PickByTeamHint(
            candidates, "neumarkt", ["SV an der Donau", "SK Sankt Irgendwas"]));
    }

    [Fact]
    public void PickByTeamHint_MatchesWholeWordsOnly()
    {
        // „glan" darf nicht in „Glanegg" treffen — sonst entscheidet ein Zufall im Vereinsnamen.
        var candidates = new List<GeoPlace>
        {
            Place("St. Veit", 47.3167, 11.0667),
            Place("St. Veit an der Glan", 46.7681, 14.3603),
        };

        Assert.Null(VenueDisambiguationService.PickByTeamHint(
            candidates, "st veit", ["SV Glanegg"]));
    }

    [Fact]
    public void PickByTeamHint_NoTeams_ChangesNothing()
    {
        var candidates = new List<GeoPlace> { Place("St. Veit an der Glan", 46.77, 14.36) };
        Assert.Null(VenueDisambiguationService.PickByTeamHint(candidates, "st veit", []));
    }
}
