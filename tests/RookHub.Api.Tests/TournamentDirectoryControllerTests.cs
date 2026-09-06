using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der lesende Teil des Turnierverzeichnisses: Filter, Umkreis, Kalender - und die Frage, ob ein
/// Nutzer immer nur seine eigenen Suchprofile sieht.
/// </summary>
public class TournamentDirectoryControllerTests : IDisposable
{
    private readonly AppDbContext _db;

    public TournamentDirectoryControllerTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private TournamentDirectoryController CreateController(int userId)
    {
        var controller = new TournamentDirectoryController(new TournamentDirectoryQueryService(_db), _db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"))
            }
        };
        return controller;
    }

    private async Task<int> CreateUserAsync(string username = "tester")
    {
        var user = new AppUser { Username = username, PasswordHash = "x", Email = $"{username}@example.com" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task AddEntryAsync(string id, string name, DateOnly start, DateOnly end,
        double? lat = null, double? lon = null, string fed = "AUT",
        TournamentSpeed speed = TournamentSpeed.Standard, int players = 20,
        string? location = null, DateTime? removedAt = null)
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            ChessResultsId = id, Name = name, Federation = fed,
            StartDate = start, EndDate = end,
            StartsOnWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
            Lat = lat, Lon = lon, Speed = speed, PlayerCount = players,
            LocationText = location, RemovedAt = removedAt,
            GeoSource = lat is null ? GeoSource.None : GeoSource.City,
        });
        await _db.SaveChangesAsync();
    }

    // ----- Filter -----------------------------------------------------------

    [Fact]
    public async Task Search_DateWindow_KeepsOverlappingTournaments()
    {
        // Ein Langzeitturnier, das VOR dem Fenster beginnt und hineinragt, gehoert in die Liste.
        await AddEntryAsync("1", "Langlaeufer", new DateOnly(2026, 8, 1), new DateOnly(2026, 11, 30));
        await AddEntryAsync("2", "Im Fenster", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12));
        await AddEntryAsync("3", "Danach", new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 2));

        var page = await SearchAsync(from: "2026-10-01", to: "2026-10-31");

        Assert.Equal(2, page.Total);
        Assert.DoesNotContain(page.Items, i => i.ChessResultsId == "3");
    }

    [Fact]
    public async Task Search_CancelledEntries_AreHidden()
    {
        await AddEntryAsync("1", "Abgesagt", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12),
            removedAt: DateTime.UtcNow);

        Assert.Equal(0, (await SearchAsync()).Total);
    }

    [Fact]
    public async Task Search_Radius_ReturnsOnlyNearbyEntriesWithDistance()
    {
        await AddEntryAsync("1", "Salzburg", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12), 47.80, 13.04);
        await AddEntryAsync("2", "Wien", new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 12), 48.21, 16.37);

        var page = await SearchAsync(lat: 47.80, lon: 13.04, radiusKm: 50);

        var item = Assert.Single(page.Items);
        Assert.Equal("1", item.ChessResultsId);
        Assert.NotNull(item.DistanceKm);
        Assert.InRange(item.DistanceKm!.Value, 0, 1);
    }

    [Fact]
    public async Task Search_Radius_IgnoresEntriesWithoutCoordinates()
    {
        await AddEntryAsync("1", "Ohne Pin", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12));

        Assert.Equal(0, (await SearchAsync(lat: 47.8, lon: 13.04, radiusKm: 500)).Total);
    }

    [Fact]
    public async Task Search_TextMatchesNameAndLocation()
    {
        await AddEntryAsync("1", "Open Braunau", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12),
            location: "Ranshofen");
        await AddEntryAsync("2", "Stadtmeisterschaft", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12),
            location: "Braunau am Inn");
        await AddEntryAsync("3", "Blitzcup", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12),
            location: "Graz");

        var page = await SearchAsync(q: "Braunau");

        Assert.Equal(2, page.Total);
    }

    [Fact]
    public async Task Search_SpeedFilter()
    {
        await AddEntryAsync("1", "Standard", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12));
        await AddEntryAsync("2", "Blitz", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12),
            speed: TournamentSpeed.Blitz);

        var page = await SearchAsync(speed: "blitz");

        Assert.Equal("2", Assert.Single(page.Items).ChessResultsId);
    }

    [Fact]
    public async Task Search_WeekendOnly_UsesThePrecomputedFlag()
    {
        await AddEntryAsync("1", "Mittwoch", new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 7));
        await AddEntryAsync("2", "Samstag", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10));

        var page = await SearchAsync(weekendOnly: true);

        Assert.Equal("2", Assert.Single(page.Items).ChessResultsId);
    }

    [Fact]
    public async Task Search_MarksSubscribedTournaments()
    {
        var userId = await CreateUserAsync();
        await AddEntryAsync("1", "Gemerkt", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12));
        await AddEntryAsync("2", "Nicht gemerkt", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12));
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = userId, CrawlerTournamentId = "1", TournamentName = "Gemerkt"
        });
        await _db.SaveChangesAsync();

        var result = await CreateController(userId).Search(null, null, null, null, null, null, null, null);
        var page = Assert.IsType<DirectoryPageDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.True(page.Items.Single(i => i.ChessResultsId == "1").Subscribed);
        Assert.False(page.Items.Single(i => i.ChessResultsId == "2").Subscribed);
    }

    [Fact]
    public async Task Search_AnotherUsersSubscription_DoesNotLeak()
    {
        var mine = await CreateUserAsync("mine");
        var other = await CreateUserAsync("other");
        await AddEntryAsync("1", "Turnier", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12));
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = other, CrawlerTournamentId = "1", TournamentName = "Turnier"
        });
        await _db.SaveChangesAsync();

        var result = await CreateController(mine).Search(null, null, null, null, null, null, null, null);
        var page = Assert.IsType<DirectoryPageDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(page.Items.Single().Subscribed);
    }

    // ----- Eingabepruefung --------------------------------------------------

    [Theory]
    [InlineData("01.10.2026", null)]
    [InlineData(null, "31.10.2026")]
    public async Task Search_NonIsoDates_AreRejected(string? from, string? to)
    {
        var result = await CreateController(1).Search(from, to, null, null, null, null, null, null);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Search_EndBeforeStart_IsRejected()
    {
        var result = await CreateController(1).Search("2026-10-31", "2026-10-01", null, null, null, null, null, null);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Theory]
    [InlineData("AUSTRIA")]
    [InlineData("A1")]
    public async Task Search_InvalidFederation_IsRejected(string fed)
    {
        var result = await CreateController(1).Search(null, null, null, null, null, fed, null, null);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Search_UnknownSpeed_IsRejected()
    {
        var result = await CreateController(1).Search(null, null, null, null, null, null, "bullet", null);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Search_AbsurdRadius_IsRejected()
    {
        var result = await CreateController(1).Search(null, null, 47.8, 13.0, 99999, null, null, null);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ----- Suchprofil als Filterquelle --------------------------------------

    [Fact]
    public async Task Search_WithProfileId_UsesTheProfilesCentreAndRadius()
    {
        var userId = await CreateUserAsync();
        _db.TournamentSearchProfiles.Add(new TournamentSearchProfile
        {
            UserId = userId, Name = "Zuhause", Lat = 47.80, Lon = 13.04, RadiusKm = 50
        });
        await _db.SaveChangesAsync();
        var profileId = _db.TournamentSearchProfiles.Single().Id;

        await AddEntryAsync("1", "Nah", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12), 47.81, 13.05);
        await AddEntryAsync("2", "Fern", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12), 48.21, 16.37);

        var result = await CreateController(userId).Search(null, null, null, null, null, null, null, null,
            profileId: profileId);
        var page = Assert.IsType<DirectoryPageDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("1", Assert.Single(page.Items).ChessResultsId);
    }

    [Fact]
    public async Task Search_ForeignProfileId_IsRejected()
    {
        var mine = await CreateUserAsync("mine");
        var other = await CreateUserAsync("other");
        _db.TournamentSearchProfiles.Add(new TournamentSearchProfile
        {
            UserId = other, Name = "Fremd", Lat = 47.8, Lon = 13.0, RadiusKm = 50
        });
        await _db.SaveChangesAsync();
        var foreignId = _db.TournamentSearchProfiles.Single().Id;

        var result = await CreateController(mine).Search(null, null, null, null, null, null, null, null,
            profileId: foreignId);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ----- Kalender ---------------------------------------------------------

    [Fact]
    public async Task Calendar_MultiDayTournament_AppearsOnEveryDay()
    {
        await AddEntryAsync("1", "Dreitaeger", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12));

        var result = await CreateController(1).Calendar(2026, 10);
        var days = Assert.IsType<List<DirectoryCalendarDayDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(31, days.Count);
        Assert.Empty(days.Single(d => d.Date.Day == 9).Items);
        foreach (var day in new[] { 10, 11, 12 })
            Assert.Single(days.Single(d => d.Date.Day == day).Items);
        Assert.Empty(days.Single(d => d.Date.Day == 13).Items);
    }

    [Theory]
    [InlineData(2026, 13)]
    [InlineData(2026, 0)]
    [InlineData(1800, 5)]
    public async Task Calendar_OutOfRange_IsRejected(int year, int month)
    {
        var result = await CreateController(1).Calendar(year, month);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ----- Karte ------------------------------------------------------------

    [Fact]
    public async Task Map_ReturnsOnlyPinsInsideTheBox()
    {
        await AddEntryAsync("1", "Salzburg", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12), 47.80, 13.04);
        await AddEntryAsync("2", "Wien", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12), 48.21, 16.37);

        var result = await CreateController(1).Map("47.0,12.0,48.0,14.0");
        var pins = Assert.IsType<List<DirectoryEntryDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("1", Assert.Single(pins).ChessResultsId);
    }

    [Theory]
    [InlineData("47.0,12.0,48.0")]
    [InlineData("nord,12.0,48.0,14.0")]
    [InlineData("48.0,12.0,47.0,14.0")]   // maxLat < minLat
    [InlineData("100.0,12.0,120.0,14.0")] // ausserhalb des Gradbereichs
    public async Task Map_BadBoundingBox_IsRejected(string bbox)
    {
        var result = await CreateController(1).Map(bbox);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void TryParseBoundingBox_ParsesInvariantDecimals()
    {
        Assert.True(TournamentDirectoryController.TryParseBoundingBox("47.5,12.25,48.5,14.75", out var box));
        Assert.Equal(47.5, box.MinLat);
        Assert.Equal(14.75, box.MaxLon);
    }

    // ----- Detail -----------------------------------------------------------

    [Fact]
    public async Task Get_UnknownTournament_Is404()
    {
        var result = await CreateController(1).Get("123456", default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12345678901")]
    public async Task Get_InvalidId_IsRejected(string id)
    {
        var result = await CreateController(1).Get(id, default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ----- Ortsvorschlaege --------------------------------------------------

    [Fact]
    public async Task Places_ByPostalCodePrefix()
    {
        SeedPlaces();
        var result = await CreateController(1).Places("54", default);
        var suggestions = Assert.IsType<List<GeoPlaceSuggestionDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("5400 Hallein (AT)", Assert.Single(suggestions).Label);
    }

    [Fact]
    public async Task Places_ByNamePrefers_ExactThenLargest()
    {
        SeedPlaces();
        var result = await CreateController(1).Places("Wien", default);
        var suggestions = Assert.IsType<List<GeoPlaceSuggestionDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(2, suggestions.Count);
        Assert.StartsWith("Wien ", suggestions[0].Label);   // exakter Treffer vor "Wiener Neudorf"
    }

    [Fact]
    public async Task Places_TooShort_ReturnsEmpty()
    {
        SeedPlaces();
        var result = await CreateController(1).Places("W", default);
        Assert.Empty(Assert.IsType<List<GeoPlaceSuggestionDto>>(Assert.IsType<OkObjectResult>(result.Result).Value));
    }

    private void SeedPlaces()
    {
        _db.GeoPlaces.AddRange(
            new GeoPlace { Country = "AT", PostalCode = "5400", Name = "Hallein", NameNormalized = "hallein", Lat = 47.68, Lon = 13.1, Kind = GeoPlaceKind.PostalCode },
            new GeoPlace { Country = "AT", Name = "Wien", NameNormalized = "wien", Lat = 48.21, Lon = 16.37, Kind = GeoPlaceKind.City, Population = 1_900_000 },
            new GeoPlace { Country = "AT", Name = "Wiener Neudorf", NameNormalized = "wiener neudorf", Lat = 48.08, Lon = 16.32, Kind = GeoPlaceKind.City, Population = 9_000 });
        _db.SaveChanges();
    }

    private async Task<DirectoryPageDto> SearchAsync(
        string? from = null, string? to = null, double? lat = null, double? lon = null, int? radiusKm = null,
        string? fed = null, string? speed = null, string? q = null, bool weekendOnly = false)
    {
        var result = await CreateController(1).Search(from, to, lat, lon, radiusKm, fed, speed, q, weekendOnly);
        return Assert.IsType<DirectoryPageDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }
}
