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
        var controller = new TournamentDirectoryController(new TournamentDirectoryQueryService(_db), _db,
            new AdminMessageService(_db, new NotificationService(_db)));
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

    // ----- Gruppen eines Turniers -------------------------------------------

    [Fact]
    public async Task Search_GroupsTheSectionsOfOneTournamentIntoOneEntry()
    {
        // chess-results fuehrt „Open Braunau 2026 A/B/C" als drei Zeilen mit eigenem dbkey.
        await AddGroupedAsync("Open Braunau 2026 A", "111", players: 12);
        await AddGroupedAsync("Open Braunau 2026 B", "112", players: 8);
        await AddGroupedAsync("Open Braunau 2026 C", "113", players: 5);

        var page = await SearchAsync();

        var item = Assert.Single(page.Items);
        Assert.Equal(1, page.Total);
        Assert.Equal("Open Braunau 2026", item.Name);      // ohne Kuerzel
        Assert.Equal(3, item.GroupSize);
        Assert.Equal(25, item.PlayerCount);                 // Summe ueber alle Gruppen
        Assert.Equal(["A", "B", "C"], item.Groups.Select(g => g.Label));
        Assert.Equal(["111", "112", "113"], item.Groups.Select(g => g.ChessResultsId));
    }

    [Fact]
    public async Task Search_GroupsIdenticallyNamedSections_WhenChessResultsTruncatedTheSuffix()
    {
        // Der echte Fall aus Suedtirol: der Name ist bei ~50 Zeichen abgeschnitten, alle drei
        // Gruppen heissen deshalb gleich.
        const string name = "5° Torneo Internazionale Ortisei \"ad Gredine\" - Op";
        await AddGroupedAsync(name, "201");
        await AddGroupedAsync(name, "202");
        await AddGroupedAsync(name, "203");

        var page = await SearchAsync();

        var item = Assert.Single(page.Items);
        Assert.Equal(3, item.GroupSize);
        Assert.All(item.Groups, g => Assert.Equal("", g.Label));   // kein Kuerzel mehr da
    }

    [Fact]
    public async Task Search_SameVenueAndDateButDifferentTournaments_StayApart()
    {
        // Vereinsabende am selben Ort mit Platzhalter-Datum: verschiedene Turniere, kein Verbund.
        await AddGroupedAsync("KK Bardejov - 1.11.2025", "301");
        await AddGroupedAsync("KK Bardejov - 25.10.2025", "302");
        await AddGroupedAsync("PS Bardejov - 28.6.2025", "303");

        var page = await SearchAsync();

        Assert.Equal(3, page.Total);
        Assert.All(page.Items, i => Assert.Equal(1, i.GroupSize));
    }

    [Fact]
    public async Task Search_SameNameButDifferentDates_StayApart()
    {
        await AddGroupedAsync("Sommercup A", "401", start: new DateOnly(2026, 10, 10));
        await AddGroupedAsync("Sommercup B", "402", start: new DateOnly(2026, 11, 14));

        Assert.Equal(2, (await SearchAsync()).Total);
    }

    [Fact]
    public async Task Search_PagingCountsTournaments_NotSections()
    {
        // Ein viergruppiges Open darf eine Seite nicht zu einem Viertel fuellen.
        for (var i = 0; i < 4; i++)
            await AddGroupedAsync($"Grosses Open 2026 {(char)('A' + i)}", $"50{i}");
        await AddGroupedAsync("Ein anderes Turnier", "600");

        var page = await SearchAsync();

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task Search_RadiusPath_GroupsAsWell()
    {
        await AddGroupedAsync("Open Braunau 2026 A", "111", lat: 47.81, lon: 13.05);
        await AddGroupedAsync("Open Braunau 2026 B", "112", lat: 47.81, lon: 13.05);

        var page = await SearchAsync(lat: 47.80, lon: 13.04, radiusKm: 50);

        var item = Assert.Single(page.Items);
        Assert.Equal(2, item.GroupSize);
        Assert.NotNull(item.DistanceKm);
    }

    [Fact]
    public async Task Search_EntriesWithoutGroupKey_DoNotCollapseIntoOne()
    {
        // Altbestand aus der Zeit vor der Gruppierung: NULL == NULL waere ein einziger Riesen-Topf.
        _db.TournamentDirectoryEntries.AddRange(
            NewEntry("Turnier eins", "701", groupKey: null),
            NewEntry("Turnier zwei", "702", groupKey: null));
        await _db.SaveChangesAsync();

        var page = await SearchAsync();

        Assert.Equal(2, page.Total);
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
    public async Task Search_WithProfileId_HonoursALLFederationsAndSpeeds()
    {
        // Frueher wurde ein Profil mit MEHR als einem Wert beim Filtern uebergangen, waehrend die
        // naechtliche Meldung die ganze Liste auswertet: die Ansicht zeigte dann Blitzturniere aus
        // Italien, gemeldet wurde nur AUT/GER-Standard. Ein Profil muss beides gleich beantworten.
        var userId = await CreateUserAsync();
        _db.TournamentSearchProfiles.Add(new TournamentSearchProfile
        {
            UserId = userId, Name = "Nachbarn", Lat = 47.80, Lon = 13.04, RadiusKm = 2000,
            Federations = "AUT,GER", Speeds = "Standard,Rapid",
        });
        await _db.SaveChangesAsync();
        var profileId = _db.TournamentSearchProfiles.Single().Id;

        await AddEntryAsync("1", "AUT Standard", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12),
            47.81, 13.05, fed: "AUT", speed: TournamentSpeed.Standard);
        await AddEntryAsync("2", "GER Rapid", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12),
            48.13, 11.58, fed: "GER", speed: TournamentSpeed.Rapid);
        await AddEntryAsync("3", "ITA Blitz", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12),
            46.50, 11.35, fed: "ITA", speed: TournamentSpeed.Blitz);
        await AddEntryAsync("4", "AUT Blitz", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12),
            47.82, 13.06, fed: "AUT", speed: TournamentSpeed.Blitz);

        var result = await CreateController(userId).Search(null, null, null, null, null, null, null, null,
            profileId: profileId);
        var page = Assert.IsType<DirectoryPageDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(["1", "2"], page.Items.Select(i => i.ChessResultsId).Order());
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
        var cal = Assert.IsType<DirectoryCalendarDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(31, cal.Days.Count);
        Assert.Empty(cal.Days.Single(d => d.Date.Day == 9).Ids);
        foreach (var day in new[] { 10, 11, 12 })
            Assert.Equal(["1"], cal.Days.Single(d => d.Date.Day == day).Ids);
        Assert.Empty(cal.Days.Single(d => d.Date.Day == 13).Ids);
    }

    [Fact]
    public async Task Calendar_SaysSoWhenItCouldNotShowEverything()
    {
        // Ein Deckel, den niemand sieht, ist schlimmer als ein Deckel: der Kalender saehe
        // schlicht vollstaendig aus.
        await AddEntryAsync("1", "Eins", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10));

        var result = await CreateController(1).Calendar(2026, 10);
        var cal = Assert.IsType<DirectoryCalendarDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(cal.Truncated);
    }

    [Fact]
    public async Task Calendar_DescribesEachTournamentOnlyOnce()
    {
        // Der teure Teil war die Wiederholung: ein Monat auf dem Dev-Server hatte 5962 Eintraege
        // fuer 200 verschiedene Turniere — 3 MB JSON, von denen 97 % dasselbe noch einmal sagten.
        await AddEntryAsync("1", "Dreitaeger", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12));
        await AddEntryAsync("2", "Eintaeger", new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 11));

        var result = await CreateController(1).Calendar(2026, 10);
        var cal = Assert.IsType<DirectoryCalendarDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(2, cal.Tournaments.Count);
        Assert.Equal(["1", "2"], cal.Tournaments.Select(t => t.ChessResultsId).Order());
        // Jede Nummer eines Tages muss auch beschrieben sein, sonst laeuft die Anzeige ins Leere.
        var known = cal.Tournaments.Select(t => t.ChessResultsId).ToHashSet();
        Assert.All(cal.Days.SelectMany(d => d.Ids), id => Assert.Contains(id, known));
        Assert.Equal(["1", "2"], cal.Days.Single(d => d.Date.Day == 11).Ids.Order());
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
    public async Task Get_ShowsTheWholeTournament_NotJustTheClickedSection()
    {
        // Die Liste fasst A/B/C zu einem Eintrag zusammen. Zeigte das Detail nur die angeklickte
        // Gruppe, widerspraeche der Deep-Link aus einer Benachrichtigung genau der Liste, aus der
        // er stammt.
        await AddGroupedAsync("Open Braunau 2026 A", "111", players: 12);
        await AddGroupedAsync("Open Braunau 2026 B", "112", players: 8);

        var result = await CreateController(1).Get("112", default);
        var dto = Assert.IsType<DirectoryEntryDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("112", dto.ChessResultsId);       // die angefragte Gruppe bleibt der Bezug
        Assert.Equal("Open Braunau 2026", dto.Name);   // aber der Name ist der des Turniers
        Assert.Equal(2, dto.GroupSize);
        Assert.Equal(20, dto.PlayerCount);
        Assert.Equal(["111", "112"], dto.Groups.Select(g => g.ChessResultsId));
    }

    [Fact]
    public async Task Get_SingleTournament_StaysUngrouped()
    {
        await AddGroupedAsync("Ein einzelnes Turnier", "500", players: 9);

        var result = await CreateController(1).Get("500", default);
        var dto = Assert.IsType<DirectoryEntryDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(1, dto.GroupSize);
        Assert.Empty(dto.Groups);
        Assert.Equal(9, dto.PlayerCount);
    }

    [Fact]
    public async Task Subscribing_ToOneSection_MarksTheWholeTournament()
    {
        // Gemerkt wird das TURNIER. Wer Gruppe B abonniert hat, soll nicht bei Gruppe A wieder
        // „merken" angeboten bekommen.
        var userId = await CreateUserAsync();
        await AddGroupedAsync("Open Braunau 2026 A", "111");
        await AddGroupedAsync("Open Braunau 2026 B", "112");
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = userId, CrawlerTournamentId = "112", TournamentName = "Open Braunau 2026 B"
        });
        await _db.SaveChangesAsync();

        var list = await CreateController(userId).Search(null, null, null, null, null, null, null, null);
        var page = Assert.IsType<DirectoryPageDto>(Assert.IsType<OkObjectResult>(list.Result).Value);
        Assert.True(Assert.Single(page.Items).Subscribed);

        var detail = await CreateController(userId).Get("111", default);
        var dto = Assert.IsType<DirectoryEntryDto>(Assert.IsType<OkObjectResult>(detail.Result).Value);
        Assert.True(dto.Subscribed);
    }

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

    private TournamentDirectoryEntry NewEntry(
        string name, string id, DateOnly? start = null, double? lat = null, double? lon = null,
        int players = 10, string location = "Ranshofen", bool withKey = true, string? groupKey = "auto")
    {
        var entry = new TournamentDirectoryEntry
        {
            ChessResultsId = id,
            Name = name,
            BaseName = TournamentNameGrouping.BaseName(name),
            Federation = "AUT",
            StartDate = start ?? new DateOnly(2026, 10, 10),
            EndDate = start ?? new DateOnly(2026, 10, 10),
            LocationText = location,
            PlayerCount = players,
            Lat = lat,
            Lon = lon,
            GeoSource = lat is null ? GeoSource.None : GeoSource.City,
        };
        entry.GroupKey = groupKey == "auto" ? TournamentDirectoryService.ComputeGroupKey(entry) : groupKey;
        return entry;
    }

    private async Task AddGroupedAsync(
        string name, string id, DateOnly? start = null, double? lat = null, double? lon = null,
        int players = 10, string location = "Ranshofen")
    {
        _db.TournamentDirectoryEntries.Add(NewEntry(name, id, start, lat, lon, players, location));
        await _db.SaveChangesAsync();
    }

    private async Task<DirectoryPageDto> SearchAsync(
        string? from = null, string? to = null, double? lat = null, double? lon = null, int? radiusKm = null,
        string? fed = null, string? speed = null, string? q = null, bool weekendOnly = false,
        DirectoryAudienceQuery? audience = null)
    {
        var result = await CreateController(1).Search(
            from, to, lat, lon, radiusKm, fed, speed, q, weekendOnly, audience: audience);
        return Assert.IsType<DirectoryPageDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    // ----- Publikum + Format ------------------------------------------------

    /// <summary>
    /// Ein Turnier mit Merkmalen. Die drei abgeleiteten Felder werden hier BEWUSST von Hand
    /// gesetzt statt ueber den Klassifizierer: was aus einem Namen folgt, gehoert in dessen
    /// eigene Tests — hier wird nur gefiltert.
    /// </summary>
    private async Task AddAudienceEntryAsync(
        string id, string name, TournamentKind kind = TournamentKind.Individual,
        TournamentAgeGroups ageGroups = TournamentAgeGroups.None,
        TournamentGender gender = TournamentGender.Open, bool isLeague = false)
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            ChessResultsId = id, Name = name, Federation = "AUT",
            StartDate = new DateOnly(2026, 10, 10), EndDate = new DateOnly(2026, 10, 12),
            Kind = kind, AgeGroups = ageGroups, Gender = gender, IsLeague = isLeague,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Search_KindFilter_KeepsOnlyTeamEvents()
    {
        await AddAudienceEntryAsync("1", "Open Braunau");
        await AddAudienceEntryAsync("2", "Landesliga", TournamentKind.Team, isLeague: true);

        var page = await SearchAsync(audience: new DirectoryAudienceQuery { Kinds = "team" });

        Assert.Equal("2", Assert.Single(page.Items).ChessResultsId);
    }

    /// <summary>
    /// Ueberschneidung, nicht Gleichheit: „U12" muss die U8-U18-Meisterschaft finden. Waere hier
    /// auf Gleichheit geprueft, fiele genau die grosse Ausschreibung durch, die ein Elternteil
    /// sucht.
    /// </summary>
    [Fact]
    public async Task Search_AgeGroupFilter_MatchesEventsCoveringSeveralClasses()
    {
        await AddAudienceEntryAsync("1", "Landesmeisterschaft U10 U12 U14",
            ageGroups: TournamentAgeGroups.U10 | TournamentAgeGroups.U12 | TournamentAgeGroups.U14);
        await AddAudienceEntryAsync("2", "U18 Cup", ageGroups: TournamentAgeGroups.U18);
        await AddAudienceEntryAsync("3", "Open Braunau");

        var page = await SearchAsync(audience: new DirectoryAudienceQuery { AgeGroups = "u12" });

        Assert.Equal("1", Assert.Single(page.Items).ChessResultsId);
    }

    [Fact]
    public async Task Search_GenderFilter_KeepsFemaleSections()
    {
        await AddAudienceEntryAsync("1", "U12 weiblich",
            ageGroups: TournamentAgeGroups.U12, gender: TournamentGender.Female);
        await AddAudienceEntryAsync("2", "U12 maennlich",
            ageGroups: TournamentAgeGroups.U12, gender: TournamentGender.Male);

        var page = await SearchAsync(audience: new DirectoryAudienceQuery { Genders = "female" });

        Assert.Equal("1", Assert.Single(page.Items).ChessResultsId);
    }

    /// <summary>
    /// „Nur Erwachsene" heisst: kein JUGENDmerkmal. Ein Seniorenturnier bleibt sichtbar —
    /// Seniorenschach ist Erwachsenenschach, und das Gegenteil waere eine Alterssperre nach oben.
    /// </summary>
    [Fact]
    public async Task Search_AdultsOnly_HidesYouthButKeepsSeniors()
    {
        await AddAudienceEntryAsync("1", "Open Braunau");
        await AddAudienceEntryAsync("2", "Jugendmeisterschaft", ageGroups: TournamentAgeGroups.YouthUnspecified);
        await AddAudienceEntryAsync("3", "U12 Cup", ageGroups: TournamentAgeGroups.U12);
        await AddAudienceEntryAsync("4", "Seniorenmeisterschaft", ageGroups: TournamentAgeGroups.Senior);

        var page = await SearchAsync(audience: new DirectoryAudienceQuery { AdultsOnly = true });

        Assert.Equal(["1", "4"], page.Items.Select(i => i.ChessResultsId).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Search_HideLeagues_DropsSeasonCompetitions()
    {
        await AddAudienceEntryAsync("1", "Open Braunau");
        await AddAudienceEntryAsync("2", "Landesliga", TournamentKind.Team, isLeague: true);

        var page = await SearchAsync(audience: new DirectoryAudienceQuery { HideLeagues = true });

        Assert.Equal("1", Assert.Single(page.Items).ChessResultsId);
    }

    /// <summary>
    /// Ein unbekannter Wert ist ein FEHLER. Still zu ignorieren waere schlimmer als abzulehnen:
    /// die Anzeige zeigte dann eine ungefilterte Liste und behauptete in der Filterleiste das
    /// Gegenteil.
    /// </summary>
    [Theory]
    [InlineData("u13", null, null)]
    [InlineData(null, "mixed", null)]
    [InlineData(null, null, "pairs")]
    public async Task Search_UnknownAudienceValue_IsRejected(string? ages, string? genders, string? kinds)
    {
        var result = await CreateController(1).Search(audience: new DirectoryAudienceQuery
        {
            AgeGroups = ages, Genders = genders, Kinds = kinds,
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Search_AgeGroupNames_AreReportedOnTheEntry()
    {
        await AddAudienceEntryAsync("1", "Landesmeisterschaft U10 U12",
            ageGroups: TournamentAgeGroups.U10 | TournamentAgeGroups.U12);

        var page = await SearchAsync();

        Assert.Equal(["U10", "U12"], Assert.Single(page.Items).AgeGroups);
    }

    // ----- Spieltermine im Kalender -----------------------------------------

    /// <summary>
    /// Sind Spieltermine bekannt, steht das Turnier NUR an ihnen. Eine Liga laeuft von September
    /// bis April und wurde vorher an jedem Tag dazwischen gezeichnet — rund 200 Tage, an denen
    /// nichts gespielt wird und die die Turniere verdeckten, die es wirklich gibt.
    /// </summary>
    [Fact]
    public async Task Calendar_EntryWithRoundDates_AppearsOnlyOnThoseDays()
    {
        var entry = new TournamentDirectoryEntry
        {
            ChessResultsId = "1479344", Name = "TMM 1.Klasse", Federation = "AUT",
            StartDate = new DateOnly(2026, 9, 26), EndDate = new DateOnly(2027, 4, 17),
            Rounds = 11,
            RoundDates =
            [
                new TournamentDirectoryRound { Number = 1, Date = new DateOnly(2026, 10, 10) },
                new TournamentDirectoryRound { Number = 2, Date = new DateOnly(2026, 10, 24) },
            ],
        };
        _db.TournamentDirectoryEntries.Add(entry);
        await _db.SaveChangesAsync();

        var result = await CreateController(1).Calendar(2026, 10);
        var month = Assert.IsType<DirectoryCalendarDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        var withEntry = month.Days.Where(d => d.Ids.Count > 0).Select(d => d.Date).ToList();
        Assert.Equal([new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 24)], withEntry);
        // Das Turnier selbst steht EINMAL in der Liste, samt seinen Terminen.
        Assert.Equal(2, Assert.Single(month.Tournaments).RoundDates.Count);
    }

    /// <summary>
    /// Ohne Spieltermine gilt weiter der ganze Zeitraum. Bei einem mehrtaegigen Open ist das
    /// richtig — dort wird an aufeinanderfolgenden Tagen gespielt.
    /// </summary>
    [Fact]
    public async Task Calendar_EntryWithoutRoundDates_StillSpansItsWholeRange()
    {
        await AddEntryAsync("1", "Open Braunau", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12));

        var result = await CreateController(1).Calendar(2026, 10);
        var month = Assert.IsType<DirectoryCalendarDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(3, month.Days.Count(d => d.Ids.Count > 0));
    }

    // ----- Naechster Ort zu Koordinaten -------------------------------------

    [Fact]
    public async Task NearestPlace_PrefersTheClosestPlace()
    {
        SeedPlaces();

        var result = await CreateController(1).NearestPlace(48.09, 16.31, default);
        var place = Assert.IsType<GeoPlaceSuggestionDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("Wiener Neudorf (AT)", place.Label);
    }

    /// <summary>
    /// Kein Ort in Reichweite ist kein Fehler: die Koordinaten des Nutzers gelten trotzdem, nur
    /// das Namensfeld bleibt leer. Ein 404 haette die Umkreissuche mitgerissen.
    /// </summary>
    [Fact]
    public async Task NearestPlace_NothingWithinRange_IsNoContent()
    {
        SeedPlaces();

        var result = await CreateController(1).NearestPlace(-33.86, 151.2, default);

        Assert.IsType<NoContentResult>(result.Result);
    }

    [Theory]
    [InlineData(91.0, 0.0)]
    [InlineData(0.0, 181.0)]
    public async Task NearestPlace_OutOfRange_IsRejected(double lat, double lon)
    {
        var result = await CreateController(1).NearestPlace(lat, lon, default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ----- Rueckmeldungen ---------------------------------------------------

    /// <summary>
    /// Die Falschmeldung geht in den ADMIN-Nachrichtenkanal — dort gibt es eine Oberflaeche, eine
    /// Glocke und, das Entscheidende, einen Rueckweg zum Melder.
    /// </summary>
    [Fact]
    public async Task Report_LandsInTheAdminMessageThread()
    {
        var userId = await CreateUserAsync("melder");
        await AddAudienceEntryAsync("1405166", "Tiroler Landesliga", TournamentKind.Team, isLeague: true);

        var result = await CreateController(userId).Report("1405166",
            new DirectoryReportDto { Message = "Spielort ist Mayrhofen, nicht St. Veit." },
            default);

        Assert.IsType<NoContentResult>(result);
        var message = Assert.Single(await _db.AdminMessages.ToListAsync());
        Assert.Equal(userId, message.UserId);
        Assert.False(message.FromAdmin);
        Assert.Contains("1405166", message.Body);
        Assert.Contains("Mayrhofen", message.Body);
        // Der IST-Stand gehoert dazu, sonst muss der Admin fuer jede Meldung erst nachsehen.
        Assert.Contains("Team", message.Body);
    }

    /// <summary>
    /// Die beiden Lern-Fragen des Formulars. Sie sind der eigentliche Gewinn: „bei uns heissen die
    /// Jugendturniere Schachrallye" ist eine Regel fuer ALLE kuenftigen Ausschreibungen dieser
    /// Reihe, nicht bloss eine Korrektur an diesem einen Eintrag. Darum stehen sie benannt in der
    /// Nachricht und nicht im Freitext, wo sie beim Durchsehen untergingen.
    /// </summary>
    [Fact]
    public async Task Report_LearningAnswers_AreNamedInTheMessage()
    {
        var userId = await CreateUserAsync("melder");
        await AddAudienceEntryAsync("1", "Schachrallye Telfs 2026 Gruppe B");

        await CreateController(userId).Report("1", new DirectoryReportDto
        {
            NamePattern = "Schachrallye = immer Nachwuchs",
            SourceLink = "https://www.tiroler-schachverband.at/jugend",
        }, default);

        var body = Assert.Single(await _db.AdminMessages.ToListAsync()).Body;
        Assert.Contains("Solche Turniere heissen dort: Schachrallye = immer Nachwuchs", body);
        Assert.Contains("Besser ersichtlich auf: https://www.tiroler-schachverband.at/jugend", body);
    }

    [Fact]
    public async Task Report_UnknownTournament_IsNotFound()
    {
        var userId = await CreateUserAsync("melder");

        var result = await CreateController(userId).Report("999", new DirectoryReportDto(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    /// <summary>Ein Knopfdruck ohne ein Wort ist eine gueltige Meldung („hier stimmt was nicht").</summary>
    [Fact]
    public async Task Report_WithoutAnyText_IsStillAccepted()
    {
        var userId = await CreateUserAsync("melder");
        await AddAudienceEntryAsync("1", "Open Braunau");

        var result = await CreateController(userId).Report("1", new DirectoryReportDto(), default);

        Assert.IsType<NoContentResult>(result);
        Assert.Single(await _db.AdminMessages.ToListAsync());
    }

    [Fact]
    public async Task SuggestSource_StoresTheLink()
    {
        var userId = await CreateUserAsync("melder");

        var result = await CreateController(userId).SuggestSource(new DirectorySourceSuggestionDto
        {
            Link = "https://www.tiroler-schachverband.at/termine",
            Message = "Unsere Turniere stehen nicht auf chess-results.",
        });

        Assert.IsType<NoContentResult>(result);
        Assert.Contains("tiroler-schachverband.at", Assert.Single(await _db.AdminMessages.ToListAsync()).Body);
    }

    /// <summary>
    /// Ohne Link ist der Hinweis nicht verwertbar — ein Verbandskalender laesst sich crawlen, ein
    /// Satz Freitext nicht. Und ein „javascript:"-Link waere nur eine Falle fuer den Admin, der
    /// die Meldung spaeter anklickt.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tiroler-schachverband.at")]
    [InlineData("javascript:alert(1)")]
    public async Task SuggestSource_WithoutAUsableLink_IsRejected(string? link)
    {
        var userId = await CreateUserAsync("melder");

        var result = await CreateController(userId).SuggestSource(
            new DirectorySourceSuggestionDto { Link = link, Message = "bitte crawlen" });

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
