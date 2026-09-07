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
        => CreateService(new StubHandler(responseJson, status));

    private TournamentDirectoryService CreateService(HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        return new TournamentDirectoryService(_db, factory, new GeocodingService(_db),
            new NotificationService(_db, new NoOpTaskQueue()), new TestLogger<TournamentDirectoryService>())
        {
            // Ohne das schliefe jeder Mehr-Foederationen-Test acht Sekunden je Schritt.
            DelayBetweenFederations = TimeSpan.Zero,
        };
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
    public async Task SweepFederationAsync_HttpClientTimeout_IsRecordedInsteadOfThrown()
    {
        // Ein HttpClient-Timeout ist eine TaskCanceledException, obwohl NIEMAND abgebrochen hat.
        // Fiele sie durch, waere der Lauf hier zu Ende — in Dev genau so passiert (500 statt
        // acht erfolgreicher Foederationen).
        var service = CreateService(new TimeoutForFederationHandler("AUT", "[]"));

        var (result, _) = await service.SweepFederationAsync("AUT", Today);

        Assert.False(result.Succeeded);
        Assert.Contains("Timeout", result.Error);
        Assert.Equal(1, (await _db.TournamentDirectorySweeps.SingleAsync()).ConsecutiveFailures);
    }

    [Fact]
    public async Task RunSweepAsync_OneFederationTimingOut_DoesNotStopTheOthers()
    {
        var body = $"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Ranshofen")}]";
        var service = CreateService(new TimeoutForFederationHandler("GER", body));

        var results = await service.RunSweepAsync(["AUT", "GER", "SUI"]);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].Succeeded);
        Assert.False(results[1].Succeeded);
        Assert.True(results[2].Succeeded);
    }

    [Fact]
    public async Task RunSweepAsync_PausesBetweenFederations_ButNotBeforeTheFirst()
    {
        // Die Pause haelt die Salve von chess-results fern; vor der ERSTEN Foederation waere sie
        // nur verlorene Zeit.
        var service = CreateService("[]");
        service.DelayBetweenFederations = TimeSpan.FromMilliseconds(120);

        var start = DateTime.UtcNow;
        await service.RunSweepAsync(["AUT"]);
        var single = DateTime.UtcNow - start;

        start = DateTime.UtcNow;
        await service.RunSweepAsync(["AUT", "GER", "SUI"]);
        var triple = DateTime.UtcNow - start;

        Assert.True(single < TimeSpan.FromMilliseconds(120), $"Erste Foederation wartete {single}");
        Assert.True(triple >= TimeSpan.FromMilliseconds(240), $"Drei Foederationen brauchten nur {triple}");
    }

    [Fact]
    public async Task RunSweepAsync_CallerCancels_StillPropagates()
    {
        // Die Gegenprobe zum Timeout-Fall: ein ECHTER Abbruch durch den Aufrufer muss weiter
        // durchschlagen, sonst laeuft der Sweep beim Herunterfahren stur weiter.
        var service = CreateService("[]");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RunSweepAsync(["AUT"], cts.Token));
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
            PublicId = e.Id, ChessResultsId = e.Id, Name = e.Name, Federation = "AUT",
            Lat = e.Lat, Lon = e.Lon, StartDate = e.Start, EndDate = e.Start,
            GeoSource = e.Lat is null ? GeoSource.None : GeoSource.City,
        }).ToList();

        _db.TournamentDirectoryEntries.AddRange(models);
        await _db.SaveChangesAsync();
        return models.Select(m => m.Id).ToList();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }

    /// <summary>
    /// Antwortet fuer eine bestimmte Foederation mit einem HttpClient-TIMEOUT (also einer
    /// TaskCanceledException OHNE dass der Aufrufer abgebrochen haette) und sonst normal.
    /// </summary>
    private sealed class TimeoutForFederationHandler : HttpMessageHandler
    {
        private readonly string _federation;
        private readonly string _body;

        public TimeoutForFederationHandler(string federation, string body)
        {
            _federation = federation;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Query.Contains($"fed={_federation}", StringComparison.Ordinal))
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing.");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>
    /// Antwortet auf die Trefferliste mit <c>body</c> — und auf jeden TURNIERART-Durchgang
    /// (<c>art=0..3</c>) getrennt davon, standardmaessig mit einer leeren Liste.
    ///
    /// <para>Die Trennung ist wesentlich: ohne sie bekaeme jeder Durchgang dieselbe Liste zurueck
    /// und der Sweep ordnete JEDES Turnier der zuerst abgefragten Art zu. Die Vorgabe „ueberall
    /// leer" heisst „in keiner Art gefunden" und laesst Art und System unangetastet — genau das,
    /// was die uebrigen Tests brauchen.</para>
    ///
    /// <para><see cref="TeamBody"/> beantwortet <c>art=2</c> („Rundenturnier fuer
    /// Mannschaften"); fuer die uebrigen drei Arten gibt es <see cref="ArtBodies"/>.</para>
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public StubHandler(string body, HttpStatusCode status)
        {
            _body = body;
            _status = status;
        }

        /// <summary>Antwort je Turnierart; fehlt eine, gilt die leere Liste.</summary>
        public Dictionary<string, string> ArtBodies { get; } = [];

        /// <summary>Bequemer Zugriff auf <c>art=2</c> — der haeufigste Fall in den Tests.</summary>
        public string TeamBody
        {
            get => ArtBodies.TryGetValue("2", out var b) ? b : "[]";
            set => ArtBodies["2"] = value;
        }

        public HttpStatusCode TeamStatus { get; set; } = HttpStatusCode.OK;
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri?.ToString() ?? "";
            Requests.Add(url);

            var art = System.Text.RegularExpressions.Regex.Match(url, @"&art=(\d)");
            if (!art.Success)
            {
                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                });
            }

            var body = ArtBodies.TryGetValue(art.Groups[1].Value, out var b) ? b : "[]";
            return Task.FromResult(new HttpResponseMessage(TeamStatus)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    // ----- Publikum, Format und Turnierart ---------------------------------

    /// <summary>
    /// Einzel gegen Mannschaft kommt AUS DER QUELLE: die chess-results-Turniersuche hat ein
    /// Turnierart-Feld, und der Sweep fragt die beiden Mannschafts-Arten in einem zweiten
    /// Durchgang gezielt ab. Am Namen waere „SK Aachen 2 - SF Katernberg" nicht zu erkennen.
    /// </summary>
    [Fact]
    public async Task Sweep_TeamPass_ClassifiesTheKindFromTheSource()
    {
        var handler = new StubHandler(
            $"[{Row("111", "Landescup", "2026-10-03", "2026-10-04", "Wien")}," +
            $"{Row("222", "Open Braunau", "2026-12-18", "2026-12-20", "Ranshofen")}]",
            HttpStatusCode.OK)
        {
            TeamBody = $"[{Row("111", "Landescup", "2026-10-03", "2026-10-04", "Wien")}]",
        };

        await CreateService(handler).SweepFederationAsync("AUT", Today);

        var entries = await _db.TournamentDirectoryEntries.ToDictionaryAsync(e => e.ChessResultsId);
        Assert.Equal(TournamentKind.Team, entries["111"].Kind);
        // Art 2 ist „Rundenturnier fuer Mannschaften" — die Art traegt BEIDE Angaben.
        Assert.Equal(TournamentSystem.RoundRobin, entries["111"].System);

        // „222" steht in keiner der vier Listen: das ist eine Aussage ueber die Quelle, nicht
        // ueber das Turnier, und laesst deshalb beides unangetastet.
        Assert.Equal(TournamentKind.Unknown, entries["222"].Kind);
        Assert.Equal(TournamentSystem.Unknown, entries["222"].System);

        // Vier Zusatzabfragen, eine je Turnierart.
        Assert.Equal(4, handler.Requests.Count(r => r.Contains("&art=", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Faellt der Mannschafts-Durchgang aus, bleibt die BEKANNTE Turnierart stehen. Wuerde sie
    /// stattdessen auf „Einzel" fallen, schriebe ein einzelner Netzausfall den halben Bestand um —
    /// und der Filter „nur Mannschaftsturniere" liefe am naechsten Morgen leer.
    /// </summary>
    [Fact]
    public async Task Sweep_TeamPassFails_KeepsTheKnownKind()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "111", ChessResultsId = "111", Name = "Landescup", Federation = "AUT",
            StartDate = new DateOnly(2026, 10, 3), EndDate = new DateOnly(2026, 10, 4),
            LocationText = "Wien", Kind = TournamentKind.Team,
        });
        await _db.SaveChangesAsync();

        var handler = new StubHandler($"[{Row("111", "Landescup", "2026-10-03", "2026-10-04", "Wien")}]",
            HttpStatusCode.OK) { TeamStatus = HttpStatusCode.InternalServerError };

        var (result, _) = await CreateService(handler).SweepFederationAsync("AUT", Today);

        // Der Sweep selbst gelingt — die Turnierart ist ein Zusatz, kein Fundament.
        Assert.True(result.Succeeded);
        Assert.Equal(TournamentKind.Team, (await _db.TournamentDirectoryEntries.SingleAsync()).Kind);
    }

    /// <summary>
    /// Eine abgeschnittene Mannschaftsliste ist genauso wenig verwertbar wie ein Fehler: der
    /// fehlende Schwanz waere lauter falsche „Einzelturniere".
    /// </summary>
    [Fact]
    public async Task Sweep_TeamPassTruncated_KeepsTheKnownKind()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "111", ChessResultsId = "111", Name = "Landescup", Federation = "AUT",
            StartDate = new DateOnly(2026, 10, 3), EndDate = new DateOnly(2026, 10, 4),
            LocationText = "Wien", Kind = TournamentKind.Team,
        });
        await _db.SaveChangesAsync();

        var handler = new StubHandler($"[{Row("111", "Landescup", "2026-10-03", "2026-10-04", "Wien")}]",
            HttpStatusCode.OK)
        {
            TeamBody = $"[{Row("111", "A", "2026-10-03", "2026-10-04", "Wien")}," +
                       $"{Row("112", "B", "2026-10-03", "2026-10-04", "Wien")}]",
        };
        var service = CreateService(handler);
        service.MaxRows = 2;   // die Mannschaftsliste laeuft damit genau ins Limit

        await service.SweepFederationAsync("AUT", Today);

        Assert.Equal(TournamentKind.Team, (await _db.TournamentDirectoryEntries.SingleAsync()).Kind);
    }

    /// <summary>
    /// Alter und Geschlecht stehen nur im NAMEN — die Turniersuche kennt keine Spalte dafuer. Der
    /// Sweep wertet sie beim Schreiben aus, damit die Filterleiste in SQL filtern kann.
    /// </summary>
    [Fact]
    public async Task Sweep_ReadsAudienceFromTheTournamentName()
    {
        var service = CreateService(
            $"[{Row("111", "Landesmeisterschaft U12 weiblich", "2026-10-03", "2026-10-04", "Wien")}]");

        await service.SweepFederationAsync("AUT", Today);

        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal(TournamentAgeGroups.U12, entry.AgeGroups);
        Assert.Equal(TournamentGender.Female, entry.Gender);
        Assert.False(entry.IsLeague);
    }

    [Fact]
    public async Task Sweep_TeamEventOverAWholeSeason_IsMarkedAsALeague()
    {
        var handler = new StubHandler(
            $"[{Row("111", "Steirischer Mannschaftscup", "2026-10-01", "2027-04-15", "Graz")}]",
            HttpStatusCode.OK)
        {
            TeamBody = $"[{Row("111", "Steirischer Mannschaftscup", "2026-10-01", "2027-04-15", "Graz")}]",
        };

        await CreateService(handler).SweepFederationAsync("AUT", Today);

        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal(TournamentKind.Team, entry.Kind);
        Assert.True(entry.IsLeague);
    }

    // ----- Funde aus der Durchsicht ----------------------------------------

    [Fact]
    public async Task Sweep_MovedTournamentOutOfTheOldWindow_IsUpdated_NotInsertedTwice()
    {
        // Genau der Fall, fuer den das Aenderungs-Feature gebaut wurde: gespeichert mit Ende im
        // Maerz, vom Veranstalter auf November verlegt. Beim naechsten Lauf faellt die Zeile aus
        // dem Suchfenster, die Trefferliste bringt sie aber weiterhin — wird sie dann als NEU
        // eingefuegt, laeuft der Unique-Index auf ChessResultsId an und reisst den Lauf mit.
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "12345", ChessResultsId = "12345", Name = "Open Alt", Federation = "AUT",
            StartDate = new DateOnly(2026, 3, 10), EndDate = new DateOnly(2026, 3, 15),
            LocationText = "Salzburg", FirstSeenAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var svc = CreateService($"[{Row("12345", "Open Neu", "2026-11-10", "2026-11-15", "Salzburg")}]");
        var (result, _) = await svc.SweepFederationAsync("AUT", Today);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Updated);
        var entry = Assert.Single(_db.TournamentDirectoryEntries);
        Assert.Equal(new DateOnly(2026, 11, 10), entry.StartDate);
    }

    [Fact]
    public async Task Sweep_SameIdTwiceInOneResponse_CreatesOneRow()
    {
        var svc = CreateService($"[{Row("777", "Doppelt", "2026-10-01", "2026-10-03", "Wien")}," +
                                $"{Row("777", "Doppelt", "2026-10-01", "2026-10-03", "Wien")}]");

        await svc.SweepFederationAsync("AUT", Today);

        Assert.Single(_db.TournamentDirectoryEntries);
    }

    [Fact]
    public async Task Sweep_TruncatedResultList_DoesNotCountAnythingAsMissing()
    {
        // chess-results kappt bei 2000 Zeilen. Ueber 18 Monate liegen grosse Foederationen
        // darueber, und der Schwanz fehlt dann JEDE Nacht an derselben Stelle — die Karenz von
        // zwei Laeufen faengt einen einzelnen Ausfall ab, keine systematische Luecke. Ohne Bremse
        // meldet der zweite Lauf reihenweise Absagen fuer Turniere, die stattfinden.
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "alt", ChessResultsId = "alt", Name = "Faellt hinten runter", Federation = "GER",
            StartDate = new DateOnly(2027, 1, 5), EndDate = new DateOnly(2027, 1, 7),
            FirstSeenAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var rows = string.Join(',', Enumerable.Range(0, 2000)
            .Select(i => Row($"g{i}", $"Turnier {i}", "2026-10-01", "2026-10-02", "Berlin", "GER")));
        var svc = CreateService($"[{rows}]");

        var (result, _) = await svc.SweepFederationAsync("GER", Today);

        Assert.Equal(0, result.Removed);
        var alt = await _db.TournamentDirectoryEntries.FirstAsync(e => e.ChessResultsId == "alt");
        Assert.Equal(0, alt.MissedSweeps);
        Assert.Null(alt.RemovedAt);
    }

    [Fact]
    public void GroupKey_KeepsNonLatinNamesApart()
    {
        // Kyrillisch/Griechisch/CJK ueberlebt die Normalisierung nicht — der normalisierte Text
        // ist LEER. Zwei verschiedene Turniere am selben Ort und Termin haetten damit denselben
        // Schluessel bekommen und waeren in der Liste zu einem verschmolzen.
        TournamentDirectoryEntry Entry(string name, string place) => new()
        {
            ChessResultsId = name, Name = name, BaseName = name, Federation = "BUL",
            StartDate = new DateOnly(2026, 10, 10), EndDate = new DateOnly(2026, 10, 12),
            LocationText = place,
        };

        var a = TournamentDirectoryService.ComputeGroupKey(Entry("Софийски турнир", "София"));
        var b = TournamentDirectoryService.ComputeGroupKey(Entry("Пловдивски турнир", "София"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GroupKey_StillGroupsTheSameNonLatinTournament()
    {
        TournamentDirectoryEntry Entry(string id) => new()
        {
            ChessResultsId = id, Name = "Софийски турнир", BaseName = "Софийски турнир",
            Federation = "BUL", StartDate = new DateOnly(2026, 10, 10),
            EndDate = new DateOnly(2026, 10, 12), LocationText = "София",
        };

        Assert.Equal(TournamentDirectoryService.ComputeGroupKey(Entry("1")),
                     TournamentDirectoryService.ComputeGroupKey(Entry("2")));
    }

    // ----- Herkunft eines Eintrags ------------------------------------------

    /// <summary>
    /// Jeder Eintrag vermerkt, auf WELCHER Seite er gefunden wurde und unter welcher Nummer.
    /// Dasselbe Turnier steht auf mehreren Seiten, und es werden mehr — ohne den Vermerk ist
    /// spaeter nicht zu sagen, woher eine Angabe stammt.
    /// </summary>
    [Fact]
    public async Task Sweep_NewEntry_RecordsWhereItWasFound()
    {
        var service = CreateService($"[{Row("111", "Open Braunau", "2026-12-18", "2026-12-20", "Ranshofen")}]");

        await service.SweepFederationAsync("AUT", Today);

        var entry = await _db.TournamentDirectoryEntries.Include(e => e.Sources).SingleAsync();
        var source = Assert.Single(entry.Sources);
        Assert.Equal(DirectorySourceKind.ChessResults, source.Kind);
        Assert.Equal("111", source.ExternalId);
        Assert.Equal("https://chess-results.com/tnr111.aspx?lan=1", source.Url);
    }

    /// <summary>
    /// Ein zweiter Lauf legt keinen zweiten Vermerk an — er schreibt nur fort, wann die Quelle
    /// das Turnier zuletzt gefuehrt hat.
    /// </summary>
    [Fact]
    public async Task Sweep_SecondRun_UpdatesTheSourceInsteadOfAddingOne()
    {
        var rows = $"[{Row("111", "Open Braunau", "2026-12-18", "2026-12-20", "Ranshofen")}]";
        await CreateService(rows).SweepFederationAsync("AUT", Today);
        var first = (await _db.TournamentDirectoryEntries.Include(e => e.Sources).SingleAsync())
            .Sources[0].LastSeenAt;

        _db.ChangeTracker.Clear();
        await CreateService(rows).SweepFederationAsync("AUT", Today);

        var entry = await _db.TournamentDirectoryEntries.Include(e => e.Sources).SingleAsync();
        var source = Assert.Single(entry.Sources);
        Assert.True(source.LastSeenAt >= first);
    }

    /// <summary>
    /// Der Vermerk ist n-zu-n gedacht: ein Eintrag kann auf mehreren Seiten stehen. Eine zweite
    /// Quelle traegt sich neben die erste, sie ersetzt sie nicht.
    /// </summary>
    [Fact]
    public void NoteSource_SecondSite_IsAddedAlongsideTheFirst()
    {
        var entry = new TournamentDirectoryEntry { ChessResultsId = "111", Name = "Open" };
        var now = DateTime.UtcNow;

        TournamentDirectoryService.NoteSource(entry, DirectorySourceKind.ChessResults, "111", now);
        TournamentDirectoryService.NoteSource(entry, DirectorySourceKind.Fide, "3051", now);
        TournamentDirectoryService.NoteSource(entry, DirectorySourceKind.ChessResults, "111", now);

        Assert.Equal(2, entry.Sources.Count);
        Assert.Contains(entry.Sources, s => s.Kind == DirectorySourceKind.Fide && s.ExternalId == "3051");
    }

    /// <summary>
    /// Was ein Nutzer ausgeblendet hat, wird ihm auch nicht gemeldet. Eine Benachrichtigung ueber
    /// ein weggeklicktes Turnier ist genau die Art Meldung, die einen dazu bringt, alle
    /// abzuschalten.
    /// </summary>
    [Fact]
    public async Task NotifyNearby_IgnoredTournament_IsNotReported()
    {
        var userId = await CreateUserAsync();
        _db.TournamentSearchProfiles.Add(new TournamentSearchProfile
        {
            UserId = userId, Name = "Zuhause", Lat = 48.2, Lon = 13.0, RadiusKm = 100, NotifyNew = true,
        });
        var entry = new TournamentDirectoryEntry
        {
            PublicId = "111", ChessResultsId = "111", Name = "Landesliga", Federation = "AUT",
            StartDate = Today.AddDays(30), EndDate = Today.AddDays(31),
            Lat = 48.21, Lon = 13.01,
        };
        _db.TournamentDirectoryEntries.Add(entry);
        _db.TournamentDirectoryIgnores.Add(new TournamentDirectoryIgnore
        {
            UserId = userId, PublicId = "111",
        });
        await _db.SaveChangesAsync();

        var notified = await CreateService("[]").NotifyNearbyAsync([entry.Id], Today);

        Assert.Equal(0, notified);
        Assert.Empty(await _db.Notifications.ToListAsync());
    }

    /// <summary>Die Gegenprobe: ohne Ausblendung wird gemeldet.</summary>
    [Fact]
    public async Task NotifyNearby_WithoutIgnore_IsReported()
    {
        var userId = await CreateUserAsync();
        _db.TournamentSearchProfiles.Add(new TournamentSearchProfile
        {
            UserId = userId, Name = "Zuhause", Lat = 48.2, Lon = 13.0, RadiusKm = 100, NotifyNew = true,
        });
        var entry = new TournamentDirectoryEntry
        {
            PublicId = "111", ChessResultsId = "111", Name = "Landesliga", Federation = "AUT",
            StartDate = Today.AddDays(30), EndDate = Today.AddDays(31),
            Lat = 48.21, Lon = 13.01,
        };
        _db.TournamentDirectoryEntries.Add(entry);
        await _db.SaveChangesAsync();

        Assert.Equal(1, await CreateService("[]").NotifyNearbyAsync([entry.Id], Today));
    }
}
