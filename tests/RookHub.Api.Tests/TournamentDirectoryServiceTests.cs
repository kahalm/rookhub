using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der naechtliche Sweep: anlegen, aktualisieren, Aenderungen und Absagen erkennen und nur dafuer
/// Meldungen erzeugen. Die Karenz von zwei Sweeps ist der Kern - ohne sie wuerde ein einziger
/// gescheiterter Lauf jedem Abonnenten eine Absage schicken.
/// </summary>
public class TournamentDirectoryServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public TournamentDirectoryServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private static readonly DateOnly Today = new(2026, 9, 6);

    private TournamentDirectoryService CreateService(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        var crawler = new CrawlerProxyService(new HttpClient(new StubHandler(responseJson, status))
        {
            BaseAddress = new Uri("http://crawler:8080")
        });
        return new TournamentDirectoryService(_db, crawler, new GeocodingService(_db),
            new NotificationService(_db, new NoOpTaskQueue()), new TestLogger<TournamentDirectoryService>());
    }

    private static string Row(string id, string name, string start, string end, string location,
        string federation = "AUT", int players = 10, string timeControl = "90 min + 30 sec") =>
        $$"""
          {"chessResultsId":"{{id}}","name":"{{name}}","federation":"{{federation}}","state":"Salzburg",
           "startDate":"{{start}}","endDate":"{{end}}","location":"{{location}}",
           "timeControl":"{{timeControl}}","director":"D","organizer":"O","chiefArbiter":"A",
           "rounds":7,"playerCount":{{players}},"lastUpdateText":"1 Days","lastUpdatedApproxUtc":"2026-09-05T12:00:00Z"}
          """;

    private async Task<int> CreateUserAsync(string username = "tester")
    {
        var user = new AppUser { Username = username, PasswordHash = "x", Email = $"{username}@example.com" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    // ----- Anlegen und Aktualisieren ---------------------------------------

    [Fact]
    public async Task SweepFederationAsync_NewTournaments_AreInserted()
    {
        var service = CreateService($"[{Row("111", "Open Braunau", "2026-12-18", "2026-12-20", "Ranshofen")}]");

        var (result, newIds) = await service.SweepFederationAsync("AUT", Today);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Added);
        Assert.Single(newIds);

        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal("111", entry.ChessResultsId);
        Assert.Equal("AUT", entry.Federation);
        Assert.Equal(new DateOnly(2026, 12, 18), entry.StartDate);
        Assert.Equal(TournamentSpeed.Standard, entry.Speed);
        Assert.NotNull(entry.ChangeHash);
    }

    [Fact]
    public async Task SweepFederationAsync_SecondRunWithSameData_ChangesNothingAndNotifiesNobody()
    {
        var json = $"[{Row("111", "Open Braunau", "2026-12-18", "2026-12-20", "Ranshofen")}]";
        await CreateSubscriptionAsync("111");

        await CreateService(json).SweepFederationAsync("AUT", Today);
        var (result, _) = await CreateService(json).SweepFederationAsync("AUT", Today);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Changed);
        Assert.Empty(_db.Notifications);
    }

    [Fact]
    public async Task SweepFederationAsync_DateChanged_NotifiesSubscribersOnly()
    {
        var userId = await CreateSubscriptionAsync("111");
        await CreateSubscriptionAsync("999", "other");   // anderes Turnier, darf nichts bekommen

        await CreateService($"[{Row("111", "Open Braunau", "2026-12-18", "2026-12-20", "Ranshofen")}]")
            .SweepFederationAsync("AUT", Today);
        var (result, _) = await CreateService($"[{Row("111", "Open Braunau", "2027-01-15", "2027-01-17", "Ranshofen")}]")
            .SweepFederationAsync("AUT", Today);

        Assert.Equal(1, result.Changed);

        var notification = Assert.Single(_db.Notifications);
        Assert.Equal(NotificationType.TournamentChanged, notification.Type);
        Assert.Equal(userId, notification.UserId);
        Assert.Contains("2026-12-18", notification.DataJson);
        Assert.Contains("2027-01-15", notification.DataJson);
    }

    [Fact]
    public async Task SweepFederationAsync_LocationChanged_Notifies()
    {
        await CreateSubscriptionAsync("111");

        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Ranshofen")}]")
            .SweepFederationAsync("AUT", Today);
        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Salzburg Kongresshaus")}]")
            .SweepFederationAsync("AUT", Today);

        var notification = Assert.Single(_db.Notifications);
        Assert.Equal(NotificationType.TournamentChanged, notification.Type);
        Assert.Contains("Salzburg Kongresshaus", notification.DataJson);
    }

    [Fact]
    public async Task SweepFederationAsync_OnlyPlayerCountGrew_IsNoChange()
    {
        // Eine wachsende Meldeliste ist keine Terminaenderung - sonst meldet sich jedes offene
        // Turnier jede Nacht.
        await CreateSubscriptionAsync("111");

        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Ranshofen", players: 10)}]")
            .SweepFederationAsync("AUT", Today);
        var (result, _) = await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Ranshofen", players: 42)}]")
            .SweepFederationAsync("AUT", Today);

        Assert.Equal(0, result.Changed);
        Assert.Empty(_db.Notifications);
        Assert.Equal(42, (await _db.TournamentDirectoryEntries.SingleAsync()).PlayerCount);
    }

    [Fact]
    public async Task SweepFederationAsync_NoSubscribers_ChangeIsRecordedButNotAnnounced()
    {
        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Ranshofen")}]")
            .SweepFederationAsync("AUT", Today);
        var (result, _) = await CreateService($"[{Row("111", "Open", "2027-01-15", "2027-01-17", "Ranshofen")}]")
            .SweepFederationAsync("AUT", Today);

        Assert.Equal(1, result.Changed);
        Assert.Empty(_db.Notifications);
    }

    // ----- Verschwinden / Absage -------------------------------------------

    [Fact]
    public async Task SweepFederationAsync_MissingOnce_IsNotYetCancelled()
    {
        await CreateSubscriptionAsync("111");
        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Ranshofen")}]")
            .SweepFederationAsync("AUT", Today);

        var (result, _) = await CreateService("[]").SweepFederationAsync("AUT", Today);

        Assert.Equal(0, result.Removed);
        Assert.Empty(_db.Notifications);
        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal(1, entry.MissedSweeps);
        Assert.Null(entry.RemovedAt);
    }

    [Fact]
    public async Task SweepFederationAsync_MissingTwice_IsCancelledAndAnnounced()
    {
        var userId = await CreateSubscriptionAsync("111");
        await CreateService($"[{Row("111", "Open Braunau", "2026-12-18", "2026-12-20", "Ranshofen")}]")
            .SweepFederationAsync("AUT", Today);

        await CreateService("[]").SweepFederationAsync("AUT", Today);
        var (result, _) = await CreateService("[]").SweepFederationAsync("AUT", Today);

        Assert.Equal(1, result.Removed);
        var notification = Assert.Single(_db.Notifications);
        Assert.Equal(NotificationType.TournamentCancelled, notification.Type);
        Assert.Equal(userId, notification.UserId);
        Assert.NotNull((await _db.TournamentDirectoryEntries.SingleAsync()).RemovedAt);
    }

    [Fact]
    public async Task SweepFederationAsync_ReappearsAfterAMiss_CounterResets()
    {
        var json = $"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Ranshofen")}]";
        await CreateService(json).SweepFederationAsync("AUT", Today);
        await CreateService("[]").SweepFederationAsync("AUT", Today);

        await CreateService(json).SweepFederationAsync("AUT", Today);

        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal(0, entry.MissedSweeps);
        Assert.Null(entry.RemovedAt);
    }

    [Fact]
    public async Task SweepFederationAsync_CrawlerFails_LeavesLastSweptAtOldAndCancelsNothing()
    {
        await CreateSubscriptionAsync("111");
        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Ranshofen")}]")
            .SweepFederationAsync("AUT", Today);
        var sweptAfterSuccess = (await _db.TournamentDirectorySweeps.SingleAsync()).LastSweptAt;

        var (result, _) = await CreateService("upstream weg", HttpStatusCode.GatewayTimeout)
            .SweepFederationAsync("AUT", Today);

        Assert.False(result.Succeeded);
        Assert.Empty(_db.Notifications);
        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal(0, entry.MissedSweeps);   // ein Fehlschlag zaehlt NICHT als "nicht geliefert"

        var sweep = await _db.TournamentDirectorySweeps.SingleAsync();
        Assert.Equal(sweptAfterSuccess, sweep.LastSweptAt);
        Assert.Equal(1, sweep.ConsecutiveFailures);
        Assert.NotNull(sweep.LastError);
    }

    [Fact]
    public async Task SweepFederationAsync_RecordsBookkeepingPerFederation()
    {
        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Ranshofen")}]")
            .SweepFederationAsync("aut", Today);

        var sweep = await _db.TournamentDirectorySweeps.SingleAsync();
        Assert.Equal("AUT", sweep.Federation);       // normalisiert
        Assert.Equal(1, sweep.LastRowCount);
        Assert.NotNull(sweep.LastSweptAt);
        Assert.Null(sweep.LastError);
    }

    // ----- Geocoding im Sweep ----------------------------------------------

    [Fact]
    public async Task SweepFederationAsync_GeocodesNewEntries()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "AT", PostalCode = "5400", Name = "Hallein",
            NameNormalized = "hallein", Lat = 47.6833, Lon = 13.1, Kind = GeoPlaceKind.PostalCode
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Hauptstraße 37 5400 Hallein")}]")
            .SweepFederationAsync("AUT", Today);

        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal(GeoSource.PostalCode, entry.GeoSource);
        Assert.Equal(47.6833, entry.Lat!.Value, 4);
    }

    [Fact]
    public async Task SweepFederationAsync_ManualCoordinates_SurviveTheNextSweep()
    {
        var json = $"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Ranshofen")}]";
        await CreateService(json).SweepFederationAsync("AUT", Today);

        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        entry.Lat = 48.0;
        entry.Lon = 13.0;
        entry.GeoSource = GeoSource.Manual;
        await _db.SaveChangesAsync();

        // Ortstext aendert sich -> Geocoding wuerde normalerweise neu laufen.
        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Woanders")}]")
            .SweepFederationAsync("AUT", Today);

        entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal(GeoSource.Manual, entry.GeoSource);
        Assert.Equal(48.0, entry.Lat!.Value, 4);
    }

    // ----- Umkreis-Meldung --------------------------------------------------

    [Fact]
    public async Task NotifyNearbyAsync_AggregatesToOneNotificationPerProfile()
    {
        var userId = await CreateUserAsync();
        AddProfile(userId, "Zuhause", 47.8, 13.03, 100);

        var ids = await AddEntriesAsync(
            ("111", "Turnier A", 47.6833, 13.1, new DateOnly(2026, 12, 18)),
            ("222", "Turnier B", 47.81, 13.05, new DateOnly(2026, 12, 19)),
            ("333", "Weit weg", 40.0, 3.0, new DateOnly(2026, 12, 20)));

        var count = await CreateService("[]").NotifyNearbyAsync(ids, Today);

        Assert.Equal(1, count);
        var notification = Assert.Single(_db.Notifications);
        Assert.Equal(NotificationType.TournamentNearbyNew, notification.Type);
        Assert.Contains("\"count\":\"2\"", notification.DataJson);
        Assert.Contains("Zuhause", notification.DataJson);
    }

    [Fact]
    public async Task NotifyNearbyAsync_ProfileWithoutNotifyNew_IsSkipped()
    {
        var userId = await CreateUserAsync();
        AddProfile(userId, "Stumm", 47.8, 13.03, 100, notify: false);
        var ids = await AddEntriesAsync(("111", "Turnier A", 47.81, 13.05, new DateOnly(2026, 12, 18)));

        Assert.Equal(0, await CreateService("[]").NotifyNearbyAsync(ids, Today));
        Assert.Empty(_db.Notifications);
    }

    [Fact]
    public async Task NotifyNearbyAsync_PastTournaments_AreIgnored()
    {
        var userId = await CreateUserAsync();
        AddProfile(userId, "Zuhause", 47.8, 13.03, 100);
        var ids = await AddEntriesAsync(("111", "Schon vorbei", 47.81, 13.05, new DateOnly(2026, 8, 1)));

        Assert.Equal(0, await CreateService("[]").NotifyNearbyAsync(ids, Today));
    }

    [Fact]
    public async Task NotifyNearbyAsync_EntriesWithoutCoordinates_AreIgnored()
    {
        var userId = await CreateUserAsync();
        AddProfile(userId, "Zuhause", 47.8, 13.03, 100);
        var ids = await AddEntriesAsync(("111", "Ohne Pin", null, null, new DateOnly(2026, 12, 18)));

        Assert.Equal(0, await CreateService("[]").NotifyNearbyAsync(ids, Today));
    }

    // ----- Profil-Filter ----------------------------------------------------

    [Fact]
    public void MatchesProfile_OutsideRadius_IsRejected()
    {
        var profile = new TournamentSearchProfile { Lat = 48.2082, Lon = 16.3738, RadiusKm = 100 };
        var entry = new TournamentDirectoryEntry { Lat = 47.0707, Lon = 15.4395 };  // Graz, ~145 km

        Assert.False(TournamentDirectoryService.MatchesProfile(entry, profile));
    }

    [Fact]
    public void MatchesProfile_InsideRadius_IsAccepted()
    {
        var profile = new TournamentSearchProfile { Lat = 48.2082, Lon = 16.3738, RadiusKm = 150 };
        var entry = new TournamentDirectoryEntry { Lat = 47.0707, Lon = 15.4395 };

        Assert.True(TournamentDirectoryService.MatchesProfile(entry, profile));
    }

    [Fact]
    public void MatchesProfile_SpeedFilter_IsApplied()
    {
        var profile = new TournamentSearchProfile { Lat = 48.2, Lon = 16.37, RadiusKm = 50, Speeds = "Blitz,Rapid" };
        var near = new TournamentDirectoryEntry { Lat = 48.21, Lon = 16.38 };

        near.Speed = TournamentSpeed.Standard;
        Assert.False(TournamentDirectoryService.MatchesProfile(near, profile));

        near.Speed = TournamentSpeed.Rapid;
        Assert.True(TournamentDirectoryService.MatchesProfile(near, profile));
    }

    [Fact]
    public void MatchesProfile_WeekendOnly_KeepsSaturdayAndSunday()
    {
        var profile = new TournamentSearchProfile { Lat = 48.2, Lon = 16.37, RadiusKm = 50, WeekendOnly = true };
        var entry = new TournamentDirectoryEntry { Lat = 48.21, Lon = 16.38 };

        entry.StartDate = new DateOnly(2026, 9, 9);   // Mittwoch
        Assert.False(TournamentDirectoryService.MatchesProfile(entry, profile));

        entry.StartDate = new DateOnly(2026, 9, 12);  // Samstag
        Assert.True(TournamentDirectoryService.MatchesProfile(entry, profile));
    }

    [Fact]
    public void MatchesProfile_MinPlayers_IsApplied()
    {
        var profile = new TournamentSearchProfile { Lat = 48.2, Lon = 16.37, RadiusKm = 50, MinPlayers = 20 };
        var entry = new TournamentDirectoryEntry { Lat = 48.21, Lon = 16.38, PlayerCount = 5 };

        Assert.False(TournamentDirectoryService.MatchesProfile(entry, profile));
        entry.PlayerCount = 25;
        Assert.True(TournamentDirectoryService.MatchesProfile(entry, profile));
    }

    [Fact]
    public void MatchesProfile_EmptyFilters_MatchEverythingInRange()
    {
        var profile = new TournamentSearchProfile { Lat = 48.2, Lon = 16.37, RadiusKm = 50, Speeds = "", Federations = null };
        var entry = new TournamentDirectoryEntry { Lat = 48.21, Lon = 16.38, Speed = TournamentSpeed.Unknown };

        Assert.True(TournamentDirectoryService.MatchesProfile(entry, profile));
    }

    // ----- Hash + Parsing ---------------------------------------------------

    [Fact]
    public void ComputeChangeHash_CoversDatesAndLocationOnly()
    {
        var baseline = TournamentDirectoryService.ComputeChangeHash(
            new DateOnly(2026, 12, 18), new DateOnly(2026, 12, 20), "Ranshofen");

        Assert.Equal(baseline, TournamentDirectoryService.ComputeChangeHash(
            new DateOnly(2026, 12, 18), new DateOnly(2026, 12, 20), " Ranshofen "));
        Assert.NotEqual(baseline, TournamentDirectoryService.ComputeChangeHash(
            new DateOnly(2026, 12, 19), new DateOnly(2026, 12, 20), "Ranshofen"));
        Assert.NotEqual(baseline, TournamentDirectoryService.ComputeChangeHash(
            new DateOnly(2026, 12, 18), new DateOnly(2026, 12, 20), "Salzburg"));
    }

    [Fact]
    public void ParseRows_IgnoresRowsWithoutIdOrName()
    {
        var json = JsonDocument.Parse("""
            [ {"chessResultsId":"1","name":"Gut"},
              {"chessResultsId":"","name":"Ohne Id"},
              {"name":"Ohne Id-Feld"},
              {"chessResultsId":"2"} ]
            """).RootElement;

        var rows = TournamentDirectoryService.ParseRows(json);

        Assert.Single(rows);
        Assert.Equal("1", rows[0].ChessResultsId);
    }

    [Fact]
    public void ParseRows_NonArray_ReturnsEmpty()
        => Assert.Empty(TournamentDirectoryService.ParseRows(JsonDocument.Parse("{}").RootElement));

    [Fact]
    public void FormatRange_CollapsesSingleDayEvents()
    {
        Assert.Equal("2026-12-18", TournamentDirectoryService.FormatRange(
            new DateOnly(2026, 12, 18), new DateOnly(2026, 12, 18)));
        Assert.Equal("2026-12-18 - 2026-12-20", TournamentDirectoryService.FormatRange(
            new DateOnly(2026, 12, 18), new DateOnly(2026, 12, 20)));
        Assert.Null(TournamentDirectoryService.FormatRange(null, null));
    }

    // ----- Hilfsmittel ------------------------------------------------------

    private async Task<int> CreateSubscriptionAsync(string chessResultsId, string username = "tester")
    {
        var userId = await _db.AppUsers.Where(u => u.Username == username).Select(u => u.Id).FirstOrDefaultAsync();
        if (userId == 0) userId = await CreateUserAsync(username);

        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = userId, CrawlerTournamentId = chessResultsId, TournamentName = "x"
        });
        await _db.SaveChangesAsync();
        return userId;
    }

    private void AddProfile(int userId, string name, double lat, double lon, int radiusKm, bool notify = true)
    {
        _db.TournamentSearchProfiles.Add(new TournamentSearchProfile
        {
            UserId = userId, Name = name, Lat = lat, Lon = lon, RadiusKm = radiusKm, NotifyNew = notify
        });
        _db.SaveChanges();
    }

    private async Task<List<int>> AddEntriesAsync(
        params (string Id, string Name, double? Lat, double? Lon, DateOnly Start)[] entries)
    {
        var models = entries.Select(e => new TournamentDirectoryEntry
        {
            ChessResultsId = e.Id, Name = e.Name, Federation = "AUT",
            Lat = e.Lat, Lon = e.Lon, StartDate = e.Start, EndDate = e.Start,
            GeoSource = e.Lat is null ? GeoSource.None : GeoSource.City,
        }).ToList();

        _db.TournamentDirectoryEntries.AddRange(models);
        await _db.SaveChangesAsync();
        return models.Select(m => m.Id).ToList();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public StubHandler(string body, HttpStatusCode status)
        {
            _body = body;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
    }
}
