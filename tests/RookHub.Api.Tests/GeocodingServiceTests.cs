using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Aufloesung der Freitext-Spielorte gegen den lokalen Gazetteer. Die Testdaten sind echte
/// Ortstexte aus der chess-results-Trefferliste - genau daran haengt, ob ein Pin auf der Karte
/// steht oder nicht.
/// </summary>
public class GeocodingServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GeocodingService _service;

    public GeocodingServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = new GeocodingService(_db);
    }

    public void Dispose() => _db.Dispose();

    private void Seed(params GeoPlace[] places)
    {
        foreach (var p in places) p.NameNormalized = GeoTextNormalizer.Normalize(p.Name);
        _db.GeoPlaces.AddRange(places);
        _db.SaveChanges();
    }

    private static GeoPlace Postal(string country, string code, string name, double lat, double lon) =>
        new() { Country = country, PostalCode = code, Name = name, Lat = lat, Lon = lon, Kind = GeoPlaceKind.PostalCode };

    private static GeoPlace City(string country, string name, double lat, double lon, int population) =>
        new() { Country = country, Name = name, Lat = lat, Lon = lon, Kind = GeoPlaceKind.City, Population = population };

    private static GeoPlace Region(string country, string name, double lat, double lon) =>
        new() { Country = country, Name = name, Lat = lat, Lon = lon, Kind = GeoPlaceKind.Region };

    [Fact]
    public async Task ResolveAsync_PostalCodeInAddress_Wins()
    {
        Seed(Postal("AT", "5400", "Hallein", 47.6833, 13.1),
             City("AT", "Wien", 48.2082, 16.3738, 1_900_000));

        var result = await _service.ResolveAsync("Rifer Hauptstraße 37 5400 Hallein (RIF)", "Salzburg", "AUT");

        Assert.NotNull(result);
        Assert.Equal(GeoSource.PostalCode, result!.Source);
        Assert.Equal("Hallein", result.PlaceName);
    }

    [Fact]
    public async Task ResolveAsync_HouseNumberThatLooksLikeAPostalCode_DoesNotWin()
    {
        // "351" ist eine Hausnummer, "8051" die PLZ. Beide sind Ziffernfolgen; nur die PLZ steht
        // im Gazetteer, und ihr Ortsname steht auch im Text.
        Seed(Postal("AT", "8051", "Graz", 47.0707, 15.4395),
             Postal("AT", "351", "Irgendwo", 40.0, 10.0));

        var result = await _service.ResolveAsync("Shopping Nord, Wienerstraße 351, 8051 Graz", "Steiermark", "AUT");

        Assert.Equal("Graz", result!.PlaceName);
    }

    [Fact]
    public async Task ResolveAsync_NoPostalCode_FallsBackToPlaceName()
    {
        Seed(City("AT", "Ranshofen", 48.2333, 13.0333, 3000));

        var result = await _service.ResolveAsync("Ranshofen", "Salzburg", "AUT");

        Assert.Equal(GeoSource.City, result!.Source);
        Assert.Equal("Ranshofen", result.PlaceName);
    }

    [Fact]
    public async Task ResolveAsync_MultiWordPlace_BeatsItsSingleWordParts()
    {
        Seed(City("AT", "Bad Ischl", 47.7117, 13.6231, 14000),
             City("AT", "Ischl", 40.0, 10.0, 500_000));

        var result = await _service.ResolveAsync("Sparkassensaal Bad Ischl, Auböckplatz 1", null, "AUT");

        // Trotz kleinerer Einwohnerzahl muss der laengere Name gewinnen.
        Assert.Equal("Bad Ischl", result!.PlaceName);
    }

    [Fact]
    public async Task ResolveAsync_AmbiguousName_LargestPlaceWins()
    {
        Seed(City("DE", "Neustadt", 49.35, 8.14, 53_000),
             City("DE", "Neustadt", 51.02, 13.75, 900));

        var result = await _service.ResolveAsync("Turnhalle Neustadt", null, "GER");

        Assert.Equal(49.35, result!.Lat, 2);
    }

    [Fact]
    public async Task ResolveAsync_UnknownPlace_FallsBackToRegionCentroid()
    {
        Seed(Region("AT", "Steiermark", 47.2, 15.0));

        var result = await _service.ResolveAsync("Volkshaus Irgendwo", "Steiermark", "AUT");

        Assert.Equal(GeoSource.Region, result!.Source);
        Assert.Equal(47.2, result.Lat, 2);
    }

    [Fact]
    public async Task ResolveAsync_StateIsDash_IsNotTreatedAsARegion()
    {
        // chess-results setzt in der State-Spalte gelegentlich einen blossen Bindestrich.
        Seed(Region("AT", "-", 0, 0));

        Assert.Null(await _service.ResolveAsync("Nirgendwo", "-", "AUT"));
    }

    [Fact]
    public async Task ResolveAsync_NothingMatches_ReturnsNull_InsteadOfAWrongPin()
    {
        Seed(City("AT", "Wien", 48.2082, 16.3738, 1_900_000));

        Assert.Null(await _service.ResolveAsync("Unbekannter Ort", null, "AUT"));
    }

    [Fact]
    public async Task ResolveAsync_UnknownFederation_StillResolvesByPlaceName()
    {
        // Fuer exotische Foederationen gibt es keine ISO-Zuordnung; die Ortssuche laeuft dann
        // ohne Landfilter weiter, statt aufzugeben.
        Seed(City("MN", "Ulaanbaatar", 47.9077, 106.8832, 1_400_000));

        var result = await _service.ResolveAsync("Ulaanbaatar", null, "MGL");

        Assert.Equal("Ulaanbaatar", result!.PlaceName);
    }

    [Fact]
    public async Task ResolveAsync_PostalCodeOfAnotherCountry_IsNotUsed()
    {
        Seed(Postal("DE", "5400", "Irgendwo in Deutschland", 51.0, 10.0));

        Assert.Null(await _service.ResolveAsync("5400 Hallein", null, "AUT"));
    }
}

public class GazetteerImportParsingTests
{
    [Fact]
    public void ParsePostalLines_BuildsPlacesAndRegionCentroids()
    {
        string[] lines =
        [
            "AT\t5400\tHallein\tSalzburg\t05\tHallein\t504\t\t\t47.6833\t13.1000\t4",
            "AT\t5020\tSalzburg\tSalzburg\t05\tSalzburg\t501\t\t\t47.8000\t13.0333\t4",
            "AT\t8010\tGraz\tSteiermark\t06\tGraz\t601\t\t\t47.0667\t15.4500\t4",
        ];

        var places = GazetteerImportService.ParsePostalLines("AT", lines, out var skipped);

        Assert.Equal(0, skipped);
        Assert.Equal(3, places.Count(p => p.Kind == GeoPlaceKind.PostalCode));

        var regions = places.Where(p => p.Kind == GeoPlaceKind.Region).ToList();
        Assert.Equal(2, regions.Count);
        var salzburg = regions.Single(r => r.Name == "Salzburg");
        Assert.Equal((47.6833 + 47.8000) / 2, salzburg.Lat, 4);
        Assert.Equal("salzburg", salzburg.NameNormalized);
    }

    [Fact]
    public void ParsePostalLines_SkipsTruncatedAndUnparsableRows()
    {
        string[] lines =
        [
            "AT\t5400\tHallein",                                                  // zu kurz
            "AT\t5020\tSalzburg\tSalzburg\t05\t\t\t\t\tkeine-zahl\t13.0\t4",      // Koordinate kaputt
            "AT\t\tOhnePlz\tSalzburg\t05\t\t\t\t\t47.8\t13.0\t4",                 // ohne PLZ
            "AT\t8010\tGraz\tSteiermark\t06\t\t\t\t\t47.0667\t15.4500\t4",        // gut
        ];

        var places = GazetteerImportService.ParsePostalLines("AT", lines, out var skipped);

        Assert.Equal(3, skipped);
        Assert.Single(places, p => p.Kind == GeoPlaceKind.PostalCode);
    }

    [Fact]
    public void ParseCityLines_ReadsNameCountryCoordinatesAndPopulation()
    {
        var line = string.Join('\t',
            "2761369", "Vienna", "Vienna", "Wien,Wien city", "48.20849", "16.37208",
            "P", "PPLC", "AT", "", "09", "900", "90001", "", "1691468", "", "175",
            "Europe/Vienna", "2026-01-01");

        var places = GazetteerImportService.ParseCityLines([line], out var skipped);

        var city = Assert.Single(places);
        Assert.Equal(0, skipped);
        Assert.Equal("Vienna", city.Name);
        Assert.Equal("AT", city.Country);
        Assert.Equal(48.20849, city.Lat, 5);
        Assert.Equal(1_691_468, city.Population);
        Assert.Equal(GeoPlaceKind.City, city.Kind);
    }
}
