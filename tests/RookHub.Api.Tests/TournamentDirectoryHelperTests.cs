using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die reinen Hilfsfunktionen rund ums Turnierverzeichnis: Bedenkzeit-Einordnung, Textfaltung
/// fuers Geocoding, Distanzrechnung und der Aenderungs-Hash.
/// </summary>
public class TournamentSpeedClassifierTests
{
    [Theory]
    // Echte Bedenkzeit-Texte aus der AUT-Trefferliste.
    [InlineData("90 min/40 moves + 15 min rest + 30 sec", TournamentSpeed.Standard)]
    [InlineData("120 min/40 Zuege + 30 Minuten fuer Rest + 30 sec", TournamentSpeed.Standard)]
    [InlineData("90min + 30s/move", TournamentSpeed.Standard)]
    [InlineData("10 Minuten + 3 Sekunden / Zug", TournamentSpeed.Rapid)]
    [InlineData("15 Minuten", TournamentSpeed.Rapid)]
    [InlineData("1 Std.", TournamentSpeed.Rapid)]
    [InlineData("3min + 2sek/Zug", TournamentSpeed.Blitz)]
    [InlineData("1min+2sec", TournamentSpeed.Blitz)]
    [InlineData("7 min + 5 sec/Zug", TournamentSpeed.Rapid)]
    public void Classify_RealWorldTimeControls(string text, TournamentSpeed expected)
        => Assert.Equal(expected, TournamentSpeedClassifier.Classify(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nach Vereinbarung")]
    public void Classify_WithoutUsableTime_IsUnknown(string? text)
        => Assert.Equal(TournamentSpeed.Unknown, TournamentSpeedClassifier.Classify(text));

    [Fact]
    public void TotalMinutes_UsesFirstTimeAndAddsIncrement()
    {
        // "90 min fuer 40 Zuege + 30 min Rest" ist ein 90-Minuten-Turnier mit Zusatzphase,
        // kein 30-Minuten-Turnier: die ERSTE Angabe zaehlt.
        Assert.Equal(120, TournamentSpeedClassifier.TotalMinutes("90 min/40 Z + 30 min Rest + 30 Sek"));
    }

    [Fact]
    public void TotalMinutes_AbsurdValue_IsRejected()
        => Assert.Null(TournamentSpeedClassifier.TotalMinutes("9000 min"));
}

public class GeoTextNormalizerTests
{
    [Theory]
    [InlineData("Hauptstraße", "hauptstrasse")]
    [InlineData("Mürzzuschlag", "murzzuschlag")]
    [InlineData("Sankt Pölten", "sankt polten")]
    [InlineData("Zürich", "zurich")]
    [InlineData("Plzeň", "plzen")]
    [InlineData("  Wien , 1010 ", "wien 1010")]
    [InlineData(null, "")]
    public void Normalize_FoldsUmlautsAndDiacritics(string? input, string expected)
        => Assert.Equal(expected, GeoTextNormalizer.Normalize(input));

    [Fact]
    public void PostalCandidates_FindsCodeAndIgnoresShortHouseNumbers()
    {
        var candidates = GeoTextNormalizer.PostalCandidates("Rifer Hauptstraße 37 5400 Hallein (RIF)");

        Assert.Contains("5400", candidates);
        Assert.DoesNotContain("37", candidates);
    }

    [Fact]
    public void PostalCandidates_KeepsCompactVariantOfSeparatedCodes()
    {
        // "SE-114 35" / "00-950": der Gazetteer speichert mal mit, mal ohne Trenner.
        var candidates = GeoTextNormalizer.PostalCandidates("Storgatan 1, 114 35 Stockholm");

        Assert.Contains("114 35", candidates);
        Assert.Contains("11435", candidates);
    }

    [Fact]
    public void PlaceCandidates_LongestPhrasesFirst()
    {
        var candidates = GeoTextNormalizer.PlaceCandidates("Sparkassensaal Bad Ischl, Auböckplatz 1");

        var badIschl = candidates.IndexOf("bad ischl");
        var ischl = candidates.IndexOf("ischl");
        Assert.True(badIschl >= 0 && ischl >= 0);
        // "bad ischl" muss VOR "ischl" probiert werden, sonst gewinnt der falsche Ort.
        Assert.True(badIschl < ischl);
    }

    [Fact]
    public void PlaceCandidates_DropsPureDigits()
    {
        var candidates = GeoTextNormalizer.PlaceCandidates("1090 Wien");

        Assert.Contains("wien", candidates);
        Assert.DoesNotContain(candidates, c => c.All(char.IsDigit));
    }
}

public class GeoDistanceTests
{
    [Fact]
    public void Haversine_KnownDistance_WienGraz()
    {
        // Wien (48.2082/16.3738) -> Graz (47.0707/15.4395): Luftlinie ca. 145 km.
        var km = GeoDistance.Haversine(48.2082, 16.3738, 47.0707, 15.4395);
        Assert.InRange(km, 140, 152);
    }

    [Fact]
    public void Haversine_SamePoint_IsZero()
        => Assert.Equal(0, GeoDistance.Haversine(48.2, 16.3, 48.2, 16.3), 6);

    [Fact]
    public void BoundingBox_EnclosesTheCircle()
    {
        var box = GeoDistance.BoundingBox(48.2082, 16.3738, 100);

        // Ein Punkt genau 100 km noerdlich muss noch in der Box liegen.
        Assert.True(box.MaxLat >= 48.2082 + 100 / 111.33);
        Assert.True(box.MinLat <= 48.2082 - 100 / 111.33);
        // Laengengrade sind auf 48 Grad Breite enger -> groesserer Gradabstand als bei der Breite.
        Assert.True(box.MaxLon - 16.3738 > box.MaxLat - 48.2082);
    }

    [Fact]
    public void BoundingBox_NearPole_DoesNotProduceNaN()
    {
        var box = GeoDistance.BoundingBox(90.0, 0.0, 500);

        Assert.False(double.IsNaN(box.MinLon) || double.IsNaN(box.MaxLon));
        Assert.Equal(90.0, box.MaxLat);
        Assert.InRange(box.MaxLon, 0, 180);
    }
}

public class FideCountryCodesTests
{
    [Theory]
    [InlineData("AUT", "AT")]
    [InlineData("GER", "DE")]
    [InlineData("SUI", "CH")]
    [InlineData("SVK", "SK")]
    [InlineData("SLO", "SI")]
    [InlineData("CRO", "HR")]
    [InlineData("ENG", "GB")]
    [InlineData("SCO", "GB")]
    public void ToIso2_MapsKnownFederations(string fide, string iso) =>
        Assert.Equal(iso, FideCountryCodes.ToIso2(fide));

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("")]
    [InlineData(null)]
    public void ToIso2_UnknownCode_ReturnsNull_RatherThanGuessing(string? fide) =>
        Assert.Null(FideCountryCodes.ToIso2(fide));
}

public class FederationCatalogTests
{
    [Fact]
    public void All_CoversTheSearchDropdown()
    {
        // Das Laenderfeld der Turniersuche hatte am 2026-09-06 257 dreibuchstabige Codes.
        Assert.InRange(FederationCatalog.All.Count, 200, 300);
        Assert.All(FederationCatalog.All, code => Assert.Matches("^[A-Z]{3}$", code));
        Assert.Equal(FederationCatalog.All.Count, FederationCatalog.All.Distinct().Count());
    }

    [Fact]
    public void All_ContainsTheNeighbourFederations()
    {
        foreach (var code in new[] { "AUT", "GER", "SUI", "ITA", "CZE", "SVK", "HUN", "SLO", "LIE" })
            Assert.Contains(code, FederationCatalog.All);
    }
}

/// <summary>
/// Zeitsteuerung und Auswahl der gestaffelten Sweeps.
/// </summary>
public class TournamentDirectorySchedulerTests : IDisposable
{
    private readonly RookHub.Api.Data.AppDbContext _db;

    public TournamentDirectorySchedulerTests()
    {
        _db = new RookHub.Api.Data.AppDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<RookHub.Api.Data.AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void TimeUntilNextRun_BeforeThreeUtc_WaitsUntilToday()
    {
        var delay = TournamentDirectoryScheduler.TimeUntilNextRun(new DateTime(2026, 9, 6, 1, 0, 0, DateTimeKind.Utc));
        Assert.Equal(TimeSpan.FromHours(2), delay);
    }

    [Fact]
    public void TimeUntilNextRun_AfterThreeUtc_WaitsUntilTomorrow()
    {
        var delay = TournamentDirectoryScheduler.TimeUntilNextRun(new DateTime(2026, 9, 6, 5, 0, 0, DateTimeKind.Utc));
        Assert.Equal(TimeSpan.FromHours(22), delay);
    }

    [Fact]
    public void TimeUntilNextRun_ExactlyAtRunTime_DoesNotReturnZero()
    {
        // Null Wartezeit wuerde den Loop in derselben Sekunde erneut feuern.
        var delay = TournamentDirectoryScheduler.TimeUntilNextRun(new DateTime(2026, 9, 6, 3, 0, 0, DateTimeKind.Utc));
        Assert.True(delay >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task BuildRunListAsync_WithoutWeeklyBatch_IsOnlyTheDailyFederations()
    {
        var run = await TournamentDirectoryScheduler.BuildRunListAsync(_db, ["AUT", "GER"], 0, default);
        Assert.Equal(["AUT", "GER"], run);
    }

    [Fact]
    public async Task BuildRunListAsync_RotationStartsOnAnEmptySweepTable()
    {
        // Die Sweep-Tabelle ist beim ersten Lauf leer; Grundmenge ist deshalb der Katalog.
        var run = await TournamentDirectoryScheduler.BuildRunListAsync(_db, ["AUT"], 5, default);

        Assert.Equal(6, run.Count);
        Assert.Equal("AUT", run[0]);
        Assert.DoesNotContain("AUT", run.Skip(1));
    }

    [Fact]
    public async Task BuildRunListAsync_PrefersFederationsSweptLongestAgo()
    {
        var now = new DateTime(2026, 9, 6, 3, 0, 0, DateTimeKind.Utc);
        // Alle Katalog-Foederationen als "gerade eben gesweept" markieren, drei davon aelter.
        foreach (var code in FederationCatalog.All)
        {
            _db.TournamentDirectorySweeps.Add(new RookHub.Api.Models.TournamentDirectorySweep
            {
                Federation = code,
                LastSweptAt = code switch
                {
                    "FRA" => now.AddDays(-9),
                    "ESP" => now.AddDays(-8),
                    "POL" => now.AddDays(-7),
                    _ => now,
                }
            });
        }
        await _db.SaveChangesAsync();

        var run = await TournamentDirectoryScheduler.BuildRunListAsync(_db, ["AUT"], 3, default);

        Assert.Equal(["AUT", "FRA", "ESP", "POL"], run);
    }

    [Fact]
    public async Task BuildRunListAsync_NeverSweptBeatsEverything()
    {
        var now = new DateTime(2026, 9, 6, 3, 0, 0, DateTimeKind.Utc);
        foreach (var code in FederationCatalog.All.Where(c => c != "FIJ"))
        {
            _db.TournamentDirectorySweeps.Add(new RookHub.Api.Models.TournamentDirectorySweep
            {
                Federation = code, LastSweptAt = now.AddDays(-30)
            });
        }
        await _db.SaveChangesAsync();

        var run = await TournamentDirectoryScheduler.BuildRunListAsync(_db, [], 1, default);

        Assert.Equal(["FIJ"], run);
    }
}
