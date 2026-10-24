using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers.TournamentDirectory;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.TournamentDirectory;

namespace RookHub.Api.Tests.TournamentDirectory;

/// <summary>
/// Die Pflege-Endpunkte des Admin-Controllers, die ohne Netz auskommen: Gesundheitsblick
/// (<c>status</c>), Umschrift des Ortslexikons, Arbeitsliste der Unverorteten und die
/// Koordinaten-Korrektur von Hand.
///
/// <para>Charakterisierungstests vor der Zerlegung (Codereview 2026-09-29, Z8): sie halten die
/// ANTWORT so fest, wie sie auf der Leitung steht (MVC-Serialisierung, camelCase), damit das
/// Herausziehen der Fachlogik in einen Dienst die Form nicht unbemerkt aendert.</para>
/// </summary>
public class AdminTournamentDirectoryMaintenanceTests : IDisposable
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;

    public AdminTournamentDirectoryMaintenanceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private AdminTournamentDirectoryController Controller() => AdminTournamentDirectoryTestKit.Controller(_db);

    private static string Json(IActionResult result) =>
        JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value, Wire);

    private static readonly DateTime T1 = new(2026, 9, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 9, 2, 3, 0, 0, DateTimeKind.Utc);

    // ----- status ---------------------------------------------------------------

    [Fact]
    public async Task Status_ReportsSweepsNewestFirst_AndTheGeocodingShare()
    {
        _db.TournamentDirectorySweeps.AddRange(
            new TournamentDirectorySweep { Federation = "AUT", LastSweptAt = T1, LastAttemptedAt = T1, LastRowCount = 12 },
            new TournamentDirectorySweep
            {
                Federation = "GER", LastAttemptedAt = T2, LastError = "Netz weg", ConsecutiveFailures = 2,
            });
        _db.TournamentDirectoryEntries.AddRange(
            new TournamentDirectoryEntry { PublicId = "a", Name = "A", GeoSource = GeoSource.City, Lat = 1, Lon = 1 },
            new TournamentDirectoryEntry { PublicId = "b", Name = "B", GeoSource = GeoSource.City, Lat = 1, Lon = 1 },
            new TournamentDirectoryEntry { PublicId = "c", Name = "C", GeoSource = GeoSource.PostalCode, Lat = 1, Lon = 1 },
            new TournamentDirectoryEntry { PublicId = "d", Name = "D", GeoSource = GeoSource.None },
            // Zurueckgezogen: zaehlt nirgends mit.
            new TournamentDirectoryEntry { PublicId = "e", Name = "E", GeoSource = GeoSource.City, RemovedAt = T1 });
        _db.GeoPlaces.Add(new GeoPlace { Country = "AT", Name = "Wien", NameNormalized = "wien" });
        await _db.SaveChangesAsync();

        var json = Json(await Controller().Status(default));

        var doc = JsonDocument.Parse(json).RootElement;
        Assert.Equal(4, doc.GetProperty("totalEntries").GetInt32());
        Assert.Equal(3, doc.GetProperty("geocoded").GetInt32());
        Assert.Equal(1, doc.GetProperty("gazetteerPlaces").GetInt32());
        var bySource = doc.GetProperty("byGeoSource");
        Assert.Equal(2, bySource.GetProperty("City").GetInt32());
        Assert.Equal(1, bySource.GetProperty("PostalCode").GetInt32());
        Assert.Equal(1, bySource.GetProperty("None").GetInt32());
        Assert.Equal(3, bySource.EnumerateObject().Count());
        Assert.Equal(
            """[{"federation":"GER","lastSweptAt":null,"lastAttemptedAt":"2026-09-02T03:00:00Z","lastRowCount":0,"lastError":"Netz weg","consecutiveFailures":2},{"federation":"AUT","lastSweptAt":"2026-09-01T03:00:00Z","lastAttemptedAt":"2026-09-01T03:00:00Z","lastRowCount":12,"lastError":null,"consecutiveFailures":0}]""",
            doc.GetProperty("sweeps").GetRawText());
        Assert.Equal(
            ["totalEntries", "geocoded", "byGeoSource", "gazetteerPlaces", "sweeps"],
            doc.EnumerateObject().Select(p => p.Name));
    }

    // ----- gazetteer/transcribe -------------------------------------------------

    [Fact]
    public async Task Transcribe_FillsTheSecondSpelling_AndIsRepeatable()
    {
        _db.GeoPlaces.AddRange(
            new GeoPlace { Country = "DE", Name = "München", NameNormalized = "munchen" },
            new GeoPlace { Country = "UA", Name = "Київ", NameNormalized = "" },
            new GeoPlace { Country = "AT", Name = "Wien", NameNormalized = "wien", NameTranscribed = "wien" });
        await _db.SaveChangesAsync();

        Assert.Equal("""{"written":2}""", Json(await Controller().Transcribe(default)));
        Assert.Equal(["kyiv", "muenchen", "wien"],
            _db.GeoPlaces.AsNoTracking().Select(g => g.NameTranscribed).OrderBy(n => n).ToList());

        Assert.Equal("""{"written":0}""", Json(await Controller().Transcribe(default)));
    }

    // ----- ungeocoded -----------------------------------------------------------

    [Fact]
    public async Task Ungeocoded_ListsOpenEntriesWithoutPin_ByStartDate()
    {
        _db.TournamentDirectoryEntries.AddRange(
            new TournamentDirectoryEntry
            {
                PublicId = "late", ChessResultsId = "late", Name = "Spaet", Federation = "AUT", State = "Tirol",
                LocationText = "Irgendwo", StartDate = new DateOnly(2026, 12, 1),
            },
            new TournamentDirectoryEntry
            {
                PublicId = "early", Name = "Frueh", Federation = "ITA", StartDate = new DateOnly(2026, 10, 1),
            },
            new TournamentDirectoryEntry { PublicId = "pinned", Name = "Verortet", Lat = 1, Lon = 1 },
            new TournamentDirectoryEntry { PublicId = "gone", Name = "Weg", RemovedAt = T1 });
        await _db.SaveChangesAsync();

        Assert.Equal(
            """[{"publicId":"early","chessResultsId":null,"name":"Frueh","federation":"ITA","state":null,"locationText":null,"startDate":"2026-10-01"},{"publicId":"late","chessResultsId":"late","name":"Spaet","federation":"AUT","state":"Tirol","locationText":"Irgendwo","startDate":"2026-12-01"}]""",
            Json(await Controller().Ungeocoded()));

        // Der Deckel greift (und wird auf mindestens 1 gehoben).
        Assert.Equal(1, JsonDocument.Parse(Json(await Controller().Ungeocoded(limit: 0))).RootElement.GetArrayLength());
    }

    // ----- {id}/coordinates ------------------------------------------------------

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public async Task SetCoordinates_OutOfRange_IsABadRequest(double lat, double lon)
    {
        var result = await Controller().SetCoordinates("x",
            new AdminTournamentDirectoryController.CoordinateInput { Lat = lat, Lon = lon }, default);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("""{"message":"Coordinates out of range."}""", JsonSerializer.Serialize(bad.Value, Wire));
    }

    [Fact]
    public async Task SetCoordinates_UnknownEntry_IsNotFound()
    {
        var result = await Controller().SetCoordinates("missing",
            new AdminTournamentDirectoryController.CoordinateInput { Lat = 1, Lon = 2 }, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task SetCoordinates_StoresAManualPin_AndRegroups()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1490100", ChessResultsId = "1490100", Name = "Open Innsbruck", Federation = "AUT",
            LocationText = "Innsbruck", StartDate = new DateOnly(2026, 10, 3), GeoSource = GeoSource.Ambiguous,
        });
        await _db.SaveChangesAsync();

        var result = await Controller().SetCoordinates("1490100",
            new AdminTournamentDirectoryController.CoordinateInput
            {
                Lat = 47.26, Lon = 11.39, PlaceName = "  " + new string('x', 250) + "  ",
            }, default);

        Assert.Equal(
            """{"publicId":"1490100","chessResultsId":"1490100","lat":47.26,"lon":11.39,"geoSource":"Manual"}""",
            Json(result));
        var entry = await _db.TournamentDirectoryEntries.AsNoTracking().SingleAsync();
        Assert.Equal(GeoSource.Manual, entry.GeoSource);
        Assert.Equal(new string('x', 200), entry.GeoPlaceName);
        Assert.True(entry.UpdatedAt > T2);
        Assert.Equal(TournamentDirectoryService.ComputeGroupKey(entry), entry.GroupKey);
    }
}
