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

    /// <summary>
    /// Ein Eintrag DERSELBEN Foederation ohne chess-results-Nummer darf den Sweep nicht umbringen.
    /// `ChessResultsId` ist seit 0.428.0 nullbar — FIDE-Ereignisse und die Verbandskalender fuehren
    /// keine Nummer, und die Zuordnung ueber die Nummer baut daraus ein Dictionary.
    ///
    /// <para>Am 2026-09-09 auf Dev gemessen: Sweep LIE (keine solchen Eintraege) 200, Sweep AUT (78)
    /// 500 mit `ArgumentNullException (Parameter 'key')`. Der naechtliche Lauf der HAUPTQUELLE war
    /// damit fuer jede Foederation tot, in der eine Zusatzquelle etwas beigetragen hatte — also
    /// gerade fuer die taeglichen Nachbarlaender.</para>
    /// </summary>
    [Fact]
    public async Task SweepFederationAsync_AnEntryWithoutANumber_DoesNotBreakTheRun()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "f4711",           // FIDE-Ereignis: kein chess-results-Eintrag
            ChessResultsId = null,
            Name = "European Youth Championship",
            Federation = "AUT",
            StartDate = Today.AddDays(20),
            EndDate = Today.AddDays(30),
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var service = CreateService($"[{Row("111", "Open Braunau", "2026-12-18", "2026-12-20", "Ranshofen")}]");

        var (result, _) = await service.SweepFederationAsync("AUT", Today);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Added);
        // Und der nummernlose Eintrag bleibt unangetastet daneben stehen.
        Assert.Equal(2, await _db.TournamentDirectoryEntries.CountAsync());
        Assert.NotNull(await _db.TournamentDirectoryEntries.SingleOrDefaultAsync(e => e.PublicId == "f4711"));
    }

    /// <summary>
    /// Fehlende Angaben der Trefferliste bleiben <c>NULL</c> — wie bei den Verbandseintraegen.
    /// Die eigene Kuerzungs-Kopie des Sweeps machte daraus einen Leerstring (Codereview A5-013),
    /// chess-results-Eintraege trugen fehlenden Veranstalter/Turnierleiter/Bundesland als
    /// <c>''</c>. Und ein Altbestand-Eintrag mit <c>''</c> meldet beim Umstellen KEINE Aenderung:
    /// der Aenderungs-Hash liest beides als „kein Ort".
    /// </summary>
    [Fact]
    public async Task SweepFederationAsync_MissingTextFields_StayNull_WithoutChangeNotification()
    {
        const string sparse = """
            [{"chessResultsId":"111","name":"Open Braunau","federation":"AUT",
              "startDate":"2026-12-18","endDate":"2026-12-20","rounds":7,"playerCount":10}]
            """;
        await CreateSubscriptionAsync("111");
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "111", ChessResultsId = "111", Name = "Open Braunau", Federation = "AUT",
            State = "", LocationText = "", TimeControlText = "", Organizer = "", Director = "",
            ChiefArbiter = "",
            StartDate = new DateOnly(2026, 12, 18), EndDate = new DateOnly(2026, 12, 20),
            ChangeHash = TournamentDirectoryService.ComputeChangeHash(
                new DateOnly(2026, 12, 18), new DateOnly(2026, 12, 20), ""),
            FirstSeenAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var (result, _) = await CreateService(sparse).SweepFederationAsync("AUT", Today);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Changed);
        Assert.Empty(_db.Notifications);
        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Null(entry.State);
        Assert.Null(entry.LocationText);
        Assert.Null(entry.TimeControlText);
        Assert.Null(entry.Organizer);
        Assert.Null(entry.Director);
        Assert.Null(entry.ChiefArbiter);
        Assert.Equal("Open Braunau", entry.Name);
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

    /// <summary>
    /// Aendert sich der TERMIN, sind die gespeicherten Spieltermine Makulatur — der Vermerk faellt
    /// weg, und die FASSUNG muss mit. Die beiden beantworten verschiedene Fragen („fuer diesen
    /// Termin schon nachgesehen" und „mit welchem Parser"); bliebe die Fassung stehen, haenge sie
    /// an einem Eintrag ohne Vermerk und behauptete etwas ueber einen Abruf, den es nicht mehr
    /// gibt.
    /// </summary>
    [Fact]
    public async Task SweepFederationAsync_DateChanged_ClearsTheRoundPlanMarkAndItsVersion()
    {
        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Ranshofen")}]")
            .SweepFederationAsync("AUT", Today);

        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        entry.RoundPlanCheckedAt = DateTime.UtcNow.AddDays(-1);
        entry.RoundPlanVersion = TournamentRoundPlanService.CurrentVersion;
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("111", "Open", "2027-01-08", "2027-01-10", "Ranshofen")}]")
            .SweepFederationAsync("AUT", Today);

        var after = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Null(after.RoundPlanCheckedAt);
        Assert.Equal(0, after.RoundPlanVersion);
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

    /// <summary>
    /// Eine Nacht vergehen lassen. Der Zaehler <c>MissedSweeps</c> zaehlt seit 0.455.0 NAECHTE und
    /// nicht Laeufe: zwei Durchgaenge im Abstand von Minuten (Aufhol-Lauf nach einem Deploy,
    /// <c>directory-runs.sh</c>) sind eine Nacht. Der Test muss die Karenz also wirklich
    /// verstreichen lassen, statt zweimal hintereinander zu sweepen.
    /// </summary>
    private async Task NextNightAsync()
    {
        foreach (var entry in await _db.TournamentDirectoryEntries.ToListAsync())
            if (entry.LastMissAt is { } last)
                entry.LastMissAt = last - ExternalDirectorySource.MissCooldown;
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task SweepFederationAsync_MissingTwice_IsCancelledAndAnnounced()
    {
        var userId = await CreateSubscriptionAsync("111");
        await CreateService($"[{Row("111", "Open Braunau", "2026-12-18", "2026-12-20", "Ranshofen")}]")
            .SweepFederationAsync("AUT", Today);

        await CreateService("[]").SweepFederationAsync("AUT", Today);
        await NextNightAsync();
        var (result, _) = await CreateService("[]").SweepFederationAsync("AUT", Today);

        Assert.Equal(1, result.Removed);
        var notification = Assert.Single(_db.Notifications);
        Assert.Equal(NotificationType.TournamentCancelled, notification.Type);
        Assert.Equal(userId, notification.UserId);
        Assert.NotNull((await _db.TournamentDirectoryEntries.SingleAsync()).RemovedAt);
    }

    /// <summary>
    /// Ein Turnier, das schon VORBEI ist, verschwindet still: aus dem Verzeichnis ja, aber ohne „vermutlich
    /// abgesagt" an die Abonnenten. Gemeldet am 29.09.2026 — die Schachrallye Pradl vom 27.09. kam zwei Tage
    /// später als Absage.
    /// </summary>
    [Fact]
    public async Task SweepFederationAsync_MissingTwice_AfterItTookPlace_IsRemovedWithoutAnnouncement()
    {
        await CreateSubscriptionAsync("222");
        var ended = Today.AddDays(-5).ToString("yyyy-MM-dd");
        await CreateService($"[{Row("222", "Schachrallye", ended, ended, "Innsbruck")}]")
            .SweepFederationAsync("AUT", Today);

        await CreateService("[]").SweepFederationAsync("AUT", Today);
        await NextNightAsync();
        var (result, _) = await CreateService("[]").SweepFederationAsync("AUT", Today);

        Assert.Equal(1, result.Removed);
        Assert.NotNull((await _db.TournamentDirectoryEntries.SingleAsync()).RemovedAt);
        Assert.Empty(_db.Notifications);
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

    /// <summary>
    /// Ein SPEICHERfehler in einer Foederation darf die uebrigen weder abbrechen noch vergiften.
    ///
    /// <para>SweepFederationAsync faengt nur den Abruf; sein SaveChanges steht ausserhalb. Lief es
    /// in einen Unique-Index, verliess die Ausnahme RunSweepAsync — die uebrigen Foederationen,
    /// die Umkreis-Meldung und im Nachtlauf alle folgenden Schritte entfielen. Und ohne geleerten
    /// Change-Tracker schickte die naechste Foederation den gescheiterten Eintrag mit ihrem
    /// eigenen SaveChanges erneut ab und scheiterte an derselben Ausnahme.</para>
    /// </summary>
    [Fact]
    public async Task RunSweepAsync_ASaveFailureInOneFederation_DoesNotStopOrPoisonTheOthers()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new RejectAddedInterceptor(e => e is TournamentDirectoryEntry { ChessResultsId: "222" }))
            .Options);
        var handler = new RowsPerFederationHandler(new()
        {
            ["AUT"] = $"[{Row("111", "Open Braunau", "2026-12-18", "2026-12-20", "Ranshofen")}]",
            ["GER"] = $"[{Row("222", "Open Berlin", "2026-12-18", "2026-12-20", "Berlin", federation: "GER")}]",
            ["SUI"] = $"[{Row("333", "Open Bern", "2026-12-18", "2026-12-20", "Bern", federation: "SUI")}]",
        });
        var service = new TournamentDirectoryService(db, new StubHttpClientFactory(handler), new GeocodingService(db),
            new NotificationService(db, new NoOpTaskQueue()), new TestLogger<TournamentDirectoryService>())
        {
            DelayBetweenFederations = TimeSpan.Zero,
        };

        var results = await service.RunSweepAsync(["AUT", "GER", "SUI"]);

        Assert.Equal([true, false, true], results.Select(r => r.Succeeded));
        Assert.Contains("Duplicate entry", results[1].Error);

        db.ChangeTracker.Clear();
        Assert.Equal(["111", "333"], await db.TournamentDirectoryEntries
            .OrderBy(e => e.ChessResultsId).Select(e => e.ChessResultsId!).ToListAsync());

        // Der Fehlschlag steht an der Sweep-Zeile wie ein Abruffehler: LastSweptAt bleibt leer,
        // die Rotation nimmt die Foederation wieder vor.
        var ger = await db.TournamentDirectorySweeps.SingleAsync(s => s.Federation == "GER");
        Assert.Null(ger.LastSweptAt);
        Assert.NotNull(ger.LastAttemptedAt);
        Assert.Equal(1, ger.ConsecutiveFailures);
        Assert.Contains("Duplicate entry", ger.LastError);
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

    /// <summary>
    /// Ein von der QUELLE mitgelieferter Pin ist genauer als jeder Lexikon-Treffer — dieselbe
    /// Schutzliste wie die Nachverortung von Hand (`GeocodeMissing`).
    /// </summary>
    [Fact]
    public async Task SweepFederationAsync_SourceProvidedCoordinates_SurviveTheNextSweep()
    {
        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Ranshofen")}]")
            .SweepFederationAsync("AUT", Today);

        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        entry.Lat = 48.2;
        entry.Lon = 13.0;
        entry.GeoSource = GeoSource.SourceProvided;
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "Woanders")}]")
            .SweepFederationAsync("AUT", Today);

        entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal(GeoSource.SourceProvided, entry.GeoSource);
        Assert.Equal(48.2, entry.Lat!.Value, 4);
    }

    /// <summary>
    /// Ein ueber die VEREINSNAMEN bewiesener Pin („St.Veit" -> St. Veit an der Glan) und danach
    /// ein ergaenzter Ortstext: der Sweep verortet neu und faellt dabei wieder auf das
    /// gleichnamige St. Veit in Tirol. Ohne Zuruecksetzen von `TeamHintCheckedAt` naehme die
    /// Vereinsnamen-Aufloesung den Eintrag nie wieder vor — der Pin waere endgueltig falsch bzw.
    /// weg.
    /// </summary>
    [Fact]
    public async Task SweepFederationAsync_TeamHintPin_LocationChanged_RearmsTheClubNameResolution()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "AT", Name = "St. Veit", NameNormalized = GeoTextNormalizer.Normalize("St. Veit"),
            Lat = 47.3167, Lon = 11.0667, Kind = GeoPlaceKind.City,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "St.Veit")}]")
            .SweepFederationAsync("AUT", Today);

        // Was VenueDisambiguationService nach einem Seitenabruf hinterlaesst.
        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        entry.Lat = 46.7681;
        entry.Lon = 14.3603;
        entry.GeoSource = GeoSource.TeamHint;
        entry.TeamHintCheckedAt = DateTime.UtcNow.AddDays(-1);
        entry.TeamHintVersion = VenueDisambiguationService.CurrentVersion;
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("111", "Open", "2026-12-18", "2026-12-20", "St.Veit, Mehrzweckhalle")}]")
            .SweepFederationAsync("AUT", Today);

        entry = await _db.TournamentDirectoryEntries.SingleAsync();
        // Die Aufloesung darf (und wird) wieder laufen: Vermerk weg, Eintrag wieder im Topf
        // (`GeoSource` City oder Ambiguous, siehe VenueDisambiguationService.RunAsync).
        Assert.Null(entry.TeamHintCheckedAt);
        Assert.Contains(entry.GeoSource, new[] { GeoSource.City, GeoSource.Ambiguous });
    }

    /// <summary>
    /// Derselbe Fall an einem NEBEN-Spielort — der Ausgangsfall des Dienstes: „Mayrhofen, St.Veit",
    /// der Vereinsname beweist St. Veit an der Glan fuer den ZWEITEN Abschnitt. `ApplyPick`
    /// vermerkt das nur am Spielort, `entry.GeoSource` bleibt City. Kommt in der Saison ein
    /// Spielort dazu, verortet der Sweep wieder auf das gleichnamige St. Veit in Tirol — und ohne
    /// Zuruecksetzen von `TeamHintCheckedAt` naehme die Aufloesung den Eintrag nie wieder vor.
    /// </summary>
    [Fact]
    public async Task SweepFederationAsync_SecondaryVenueTeamHint_LocationChanged_RearmsTheClubNameResolution()
    {
        foreach (var (name, lat, lon, population) in new[]
                 {
                     ("Mayrhofen", 47.17, 11.87, 3900),
                     ("St. Veit", 47.3167, 11.0667, 1500),
                     ("Zell am Ziller", 47.23, 11.88, 1900),
                 })
        {
            _db.GeoPlaces.Add(new GeoPlace
            {
                Country = "AT", Name = name, NameNormalized = GeoTextNormalizer.Normalize(name),
                Lat = lat, Lon = lon, Kind = GeoPlaceKind.City, Population = population,
            });
        }
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("111", "Liga", "2026-12-18", "2027-03-20", "Mayrhofen, St.Veit")}]")
            .SweepFederationAsync("AUT", Today);

        // Was VenueDisambiguationService.ApplyPick bei Ordinal > 0 hinterlaesst.
        var entry = await _db.TournamentDirectoryEntries.Include(e => e.Venues).SingleAsync();
        Assert.Equal(GeoSource.City, entry.GeoSource);
        var second = entry.Venues.Single(v => v.Ordinal == 1);
        second.Lat = 46.7681;
        second.Lon = 14.3603;
        second.GeoSource = GeoSource.TeamHint;
        entry.TeamHintCheckedAt = DateTime.UtcNow.AddDays(-1);
        entry.TeamHintVersion = VenueDisambiguationService.CurrentVersion;
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("111", "Liga", "2026-12-18", "2027-03-20", "Mayrhofen, St.Veit, Zell am Ziller")}]")
            .SweepFederationAsync("AUT", Today);

        entry = await _db.TournamentDirectoryEntries.Include(e => e.Venues).SingleAsync();
        Assert.Equal(3, entry.Venues.Count);
        Assert.Null(entry.TeamHintCheckedAt);
        Assert.Equal(GeoSource.City, entry.GeoSource);
    }

    /// <summary>Ohne geaenderten Ortstext bleibt ein TeamHint-Pin unangetastet — samt Vermerk.</summary>
    [Fact]
    public async Task SweepFederationAsync_TeamHintPin_SameLocation_StaysUntouched()
    {
        var json = $"[{Row("111", "Open", "2026-12-18", "2026-12-20", "St.Veit")}]";
        await CreateService(json).SweepFederationAsync("AUT", Today);

        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        var checkedAt = DateTime.UtcNow.AddDays(-1);
        entry.Lat = 46.7681;
        entry.Lon = 14.3603;
        entry.GeoSource = GeoSource.TeamHint;
        entry.TeamHintCheckedAt = checkedAt;
        await _db.SaveChangesAsync();

        await CreateService(json).SweepFederationAsync("AUT", Today);

        entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal(GeoSource.TeamHint, entry.GeoSource);
        Assert.Equal(checkedAt, entry.TeamHintCheckedAt);
        Assert.Equal(46.7681, entry.Lat!.Value, 4);
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
    /// Antwortet auf die Trefferliste JE FOEDERATION mit einer eigenen Liste (fehlt eine: leer)
    /// und auf jeden Turnierart-Durchgang mit einer leeren Liste.
    /// </summary>
    private sealed class RowsPerFederationHandler(Dictionary<string, string> bodies) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            var fed = System.Text.RegularExpressions.Regex.Match(url, @"fed=([A-Z]{3})").Groups[1].Value;
            var body = url.Contains("&art=", StringComparison.Ordinal) ? "[]" : bodies.GetValueOrDefault(fed, "[]");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
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
    public async Task NoteSource_SecondSite_IsAddedAlongsideTheFirst()
    {
        var entry = new TournamentDirectoryEntry { ChessResultsId = "111", Name = "Open" };
        var now = DateTime.UtcNow;

        await TournamentDirectoryService.NoteSourceAsync(_db, entry, DirectorySourceKind.ChessResults, "111", now);
        await TournamentDirectoryService.NoteSourceAsync(_db, entry, DirectorySourceKind.Fide, "3051", now);
        await TournamentDirectoryService.NoteSourceAsync(_db, entry, DirectorySourceKind.ChessResults, "111", now);

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

    /// <summary>
    /// Das Zeitlimit des Verzeichnis-Clients muss die LANGSAMSTE Zusatzquelle aushalten, nicht die
    /// schnellste.
    ///
    /// <para>Am 2026-09-09 gegen die echten Quellen gemessen brauchte England <b>196 s</b> fuer
    /// einen Aufruf — die robots.txt des ECF verlangt zehn Sekunden zwischen den Seiten, und bei
    /// 278 Turnieren sind das zwoelf Seiten. Die damalige Vorgabe von 180 s lag DARUNTER: die
    /// englische Quelle waere in jeder Nacht in den Timeout gelaufen, ohne je ein Turnier zu
    /// liefern, und es haette wie ein Netzproblem ausgesehen.</para>
    ///
    /// <para>Der Test haelt nicht den Messwert fest, sondern den Abstand dazu: eine Quelle wird
    /// langsamer, wenn sie waechst.</para>
    /// </summary>
    [Fact]
    public void DefaultCrawlerTimeout_SurvivesTheSlowestMeasuredSource()
    {
        const int slowestMeasuredSeconds = 196;   // England (ECF), gemessen 2026-09-09

        Assert.True(TournamentDirectoryService.DefaultCrawlerTimeoutSeconds >= 2 * slowestMeasuredSeconds,
            $"Vorgabe {TournamentDirectoryService.DefaultCrawlerTimeoutSeconds} s laesst der langsamsten "
            + $"gemessenen Quelle ({slowestMeasuredSeconds} s) keine Luft");

        // Und sie muss innerhalb der Grenzen liegen, die Program.cs zulaesst — sonst klemmt die
        // Konfiguration die eigene Vorgabe ab, und niemand sieht es.
        Assert.InRange(TournamentDirectoryService.DefaultCrawlerTimeoutSeconds, 30, 900);
    }
}
