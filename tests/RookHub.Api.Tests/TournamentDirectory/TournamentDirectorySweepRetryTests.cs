using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Social;
using RookHub.Api.Services.TournamentDirectory;
using RookHub.Api.Services.Tournaments;

namespace RookHub.Api.Tests.TournamentDirectory;

/// <summary>
/// Zweiter Versuch am Ende des Durchgangs (2026-11-17): je Nacht scheiterten rund 20 von 257 Foederationen mit „Crawler
/// returned 504: Upstream request timed out." — der Crawler wechselt seinen VPN-Ausgang, laufende Anfragen reissen ab.
/// Die voruebergehend gescheiterten ABRUFE werden nach einer kurzen Pause genau einmal wiederholt; Speicherfehler nicht.
/// </summary>
public class TournamentDirectorySweepRetryTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly TestLogger<TournamentDirectoryService> _log = new();

    public void Dispose() => _db.Dispose();

    private const string Upstream504 = """{"message":"Upstream request timed out."}""";

    /// <summary>Antwort je Aufruf: Foederation, Art (<c>null</c> = Trefferliste) und der wievielte Aufruf genau DIESER
    /// Adresse es ist (ab 1).</summary>
    private sealed class ScriptedHandler(Func<string, string?, int, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
    {
        private readonly Dictionary<string, int> _calls = new();
        public List<string> Requests { get; } = [];

        public int Main(string fed) => Requests.Count(u => u.Contains($"fed={fed}") && !u.Contains("&art="));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            _calls[url] = _calls.GetValueOrDefault(url) + 1;
            var fed = Regex.Match(url, @"fed=([A-Z]{3})").Groups[1].Value;
            var art = Regex.Match(url, @"&art=(\d)") is { Success: true } m ? m.Groups[1].Value : null;
            var (status, body) = answer(fed, art, _calls[url]);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private TournamentDirectoryService Service(HttpMessageHandler handler, AppDbContext? db = null) =>
        new(db ?? _db, new StubHttpClientFactory(handler), new GeocodingService(db ?? _db),
            new NotificationService(db ?? _db, new NoOpTaskQueue()), _log)
        {
            DelayBetweenFederations = TimeSpan.Zero,
            RetryPause = TimeSpan.Zero,
        };

    private static string Row(string id, string name, string location, string federation) =>
        $$"""
          [{"chessResultsId":"{{id}}","name":"{{name}}","federation":"{{federation}}","state":"",
           "startDate":"2027-03-12","endDate":"2027-03-14","location":"{{location}}",
           "timeControl":"90 min + 30 sec","director":"D","organizer":"O","chiefArbiter":"A",
           "rounds":7,"playerCount":40,"lastUpdateText":"1 Days","lastUpdatedApproxUtc":"2026-11-16T12:00:00Z"}]
          """;

    private List<string> Warnings => _log.Messages.Where((_, i) => _log.Levels[i] == LogLevel.Warning).ToList();

    private async Task<TournamentDirectorySweep> SweepRow(string fed) =>
        await _db.TournamentDirectorySweeps.AsNoTracking().SingleAsync(s => s.Federation == fed);

    [Fact]
    public async Task SecondAttemptSucceeds_CountsAsSuccess_AndItsNewEntriesAreReportedNearby()
    {
        // Ein Suchprofil in Berlin — nur der GER-Eintrag liegt darin, und der kommt erst im zweiten Versuch.
        var user = new AppUser { Username = "u", PasswordHash = "x", Email = "u@example.com" };
        _db.AppUsers.Add(user);
        _db.GeoPlaces.Add(new GeoPlace { Country = "DE", PostalCode = "10115", Name = "Berlin", NameNormalized = "berlin", Lat = 52.53, Lon = 13.38, Kind = GeoPlaceKind.PostalCode });
        await _db.SaveChangesAsync();
        _db.TournamentSearchProfiles.Add(new TournamentSearchProfile { UserId = user.Id, Name = "Berlin", Lat = 52.52, Lon = 13.40, RadiusKm = 30, NotifyNew = true });
        await _db.SaveChangesAsync();

        var http = new ScriptedHandler((fed, art, call) =>
            fed == "GER" && art is null && call == 1 ? (HttpStatusCode.GatewayTimeout, Upstream504)
            : fed == "GER" && art is null ? (HttpStatusCode.OK, Row("222", "Open Berlin", "Hauptstraße 1 10115 Berlin", "GER"))
            : (HttpStatusCode.OK, "[]"));

        var results = await Service(http).RunSweepAsync(["AUT", "GER", "SUI"]);

        Assert.Equal(["AUT", "GER", "SUI"], results.Select(r => r.Federation));
        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Equal(1, results[1].Added);
        Assert.Equal(2, http.Main("GER"));
        // Der zweite Versuch kommt am ENDE, nicht gleich hinterher (dann laeuft die VPN-Rotation meist noch).
        var mains = http.Requests.Where(u => !u.Contains("&art=")).Select(u => Regex.Match(u, "fed=([A-Z]{3})").Groups[1].Value);
        Assert.Equal(["AUT", "GER", "SUI", "GER"], mains);

        var ger = await SweepRow("GER");
        Assert.Equal((0, (string?)null), (ger.ConsecutiveFailures, ger.LastError));
        Assert.NotNull(ger.LastSweptAt);
        Assert.DoesNotContain(Warnings, w => w.Contains("fehlgeschlagen"));
        Assert.Contains(_log.Messages, m => m.Contains("wird am Ende des Durchgangs erneut versucht"));

        var note = Assert.Single(_db.Notifications);
        Assert.Equal(NotificationType.TournamentNearbyNew, note.Type);
    }

    [Fact]
    public async Task SecondAttemptFailsToo_OneFailure_OneWarning()
    {
        _db.TournamentDirectorySweeps.Add(new TournamentDirectorySweep { Federation = "GER", ConsecutiveFailures = 1, LastError = "alt" });
        await _db.SaveChangesAsync();
        var http = new ScriptedHandler((fed, _, _) => fed == "GER" ? (HttpStatusCode.GatewayTimeout, Upstream504) : (HttpStatusCode.OK, "[]"));

        var results = await Service(http).RunSweepAsync(["AUT", "GER"]);

        Assert.Equal([true, false], results.Select(r => r.Succeeded));
        Assert.Contains("504", results[1].Error);
        Assert.Equal(2, http.Main("GER"));                                   // genau EIN zweiter Versuch
        var ger = await SweepRow("GER");
        Assert.Equal(2, ger.ConsecutiveFailures);                            // eine Nacht = ein Fehlschlag, nicht zwei
        Assert.Contains("504", ger.LastError);
        Assert.Single(Warnings, w => w.Contains("Verzeichnis-Sweep GER fehlgeschlagen"));
    }

    [Fact]
    public async Task StorageFailure_IsNotRetried()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new RejectAddedInterceptor(e => e is TournamentDirectoryEntry { ChessResultsId: "222" }))
            .Options);
        var http = new ScriptedHandler((fed, art, _) =>
            fed == "GER" && art is null ? (HttpStatusCode.OK, Row("222", "Open Berlin", "Berlin", "GER")) : (HttpStatusCode.OK, "[]"));

        var results = await Service(http, db).RunSweepAsync(["GER", "AUT"]);

        Assert.Equal([false, true], results.Select(r => r.Succeeded));
        Assert.Contains("Duplicate entry", results[0].Error);
        Assert.Equal(1, http.Main("GER"));
        db.ChangeTracker.Clear();
        Assert.Equal(1, (await db.TournamentDirectorySweeps.SingleAsync(s => s.Federation == "GER")).ConsecutiveFailures);
        Assert.Single(Warnings, w => w.Contains("Verzeichnis-Sweep GER fehlgeschlagen"));
    }

    [Fact]
    public async Task CrawlerClientError_IsNotTransient_AndNotRetried()
    {
        var http = new ScriptedHandler((fed, _, _) => fed == "GER" ? (HttpStatusCode.BadRequest, """{"message":"fed"}""") : (HttpStatusCode.OK, "[]"));
        var results = await Service(http).RunSweepAsync(["GER"]);
        Assert.False(results[0].Succeeded);
        Assert.Equal(1, http.Main("GER"));

        Assert.True(DirectoryCrawlerClient.IsTransient(new RookHub.Api.Exceptions.CrawlerRequestException(HttpStatusCode.BadGateway, ""), default));
        Assert.True(DirectoryCrawlerClient.IsTransient(new HttpRequestException("refused"), default));
        Assert.True(DirectoryCrawlerClient.IsTransient(new TaskCanceledException("HttpClient.Timeout"), default));
        Assert.False(DirectoryCrawlerClient.IsTransient(new TaskCanceledException(), new CancellationToken(canceled: true)));
        Assert.False(DirectoryCrawlerClient.IsTransient(new System.Text.Json.JsonException(), default));
    }

    [Fact]
    public async Task CallerCancelsDuringTheRetryPause_StopsWithoutASecondAttempt()
    {
        var http = new ScriptedHandler((fed, _, _) => fed == "GER" ? (HttpStatusCode.GatewayTimeout, Upstream504) : (HttpStatusCode.OK, "[]"));
        var service = Service(http);
        service.RetryPause = TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var started = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunSweepAsync(["AUT", "GER"], cts.Token));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        Assert.Equal(1, http.Main("GER"));
        Assert.Equal(0, (await SweepRow("GER")).ConsecutiveFailures);         // nichts Halbes gezaehlt; die Rotation nimmt ihn wieder vor
    }

    [Fact]
    public async Task ArtPass_TransientFailure_IsRetriedOnce_WarningOnlyAfterTheSecond()
    {
        var team = Row("111", "Landescup", "Wien", "AUT");
        var http = new ScriptedHandler((_, art, call) => art switch
        {
            null => (HttpStatusCode.OK, team),
            "2" when call == 1 => (HttpStatusCode.GatewayTimeout, Upstream504),
            "2" => (HttpStatusCode.OK, team),
            _ => (HttpStatusCode.OK, "[]"),
        });
        var (result, _) = await Service(http).SweepFederationAsync("AUT", new DateOnly(2026, 11, 17));

        Assert.True(result.Succeeded);
        Assert.Equal(TournamentKind.Team, (await _db.TournamentDirectoryEntries.SingleAsync()).Kind);
        Assert.Equal(2, http.Requests.Count(u => u.Contains("&art=2")));
        Assert.DoesNotContain(Warnings, w => w.Contains("Turnierart-Durchgang"));

        // Scheitert auch der zweite Versuch: eine Warnung, Art bleibt unangetastet.
        var down = new ScriptedHandler((_, art, _) => art == "2" ? (HttpStatusCode.GatewayTimeout, Upstream504) : (HttpStatusCode.OK, art is null ? team : "[]"));
        await Service(down).SweepFederationAsync("AUT", new DateOnly(2026, 11, 17));
        Assert.Equal(2, down.Requests.Count(u => u.Contains("&art=2")));
        Assert.Single(Warnings, w => w.Contains("Turnierart-Durchgang AUT (Art 2) fehlgeschlagen"));
        Assert.Equal(TournamentKind.Team, (await _db.TournamentDirectoryEntries.AsNoTracking().SingleAsync()).Kind);
    }
}
