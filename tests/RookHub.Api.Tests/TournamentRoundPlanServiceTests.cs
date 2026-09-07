using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Spieltermine langlaufender Turniere.
///
/// <para>Der Fehler, um den es geht: ein Verzeichniseintrag traegt Start und Ende, und der
/// Kalender zeichnet ein mehrtaegiges Turnier an jedem Tag dazwischen. Bei einer Liga
/// („26.09.2026 bis 17.04.2027", elf Runden mit Wochen Abstand) sind das rund 200 Tage, an denen
/// nichts gespielt wird — und sie verdecken die Turniere, die wirklich stattfinden.</para>
/// </summary>
public class TournamentRoundPlanServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    /// <summary>Gemerkt, damit ein Test einen ZWEITEN Kontext auf dieselbe Datenbank oeffnen kann
    /// — nur so ist zu sehen, was wirklich gespeichert wurde und was bloss verfolgt wird.</summary>
    private readonly string _dbName = Guid.NewGuid().ToString();

    public TournamentRoundPlanServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options);
    }

    public void Dispose() => _db.Dispose();

    private TournamentRoundPlanService CreateService(
        string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler(json, status);
        return new TournamentRoundPlanService(_db, new StubClientFactory(handler),
            new TestLogger<TournamentRoundPlanService>());
    }

    /// <summary>Ein langlaufendes Turnier, wie es der Durchgang aufgreifen soll.</summary>
    private async Task<TournamentDirectoryEntry> AddLeagueAsync(
        string id = "1479344", int rounds = 11,
        DateOnly? start = null, DateOnly? end = null)
    {
        var entry = new TournamentDirectoryEntry
        {
            PublicId = id, ChessResultsId = id,
            Name = "TMM 1.Klasse 2026/2027",
            Federation = "AUT",
            StartDate = start ?? new DateOnly(2026, 9, 26),
            EndDate = end ?? new DateOnly(2027, 4, 17),
            Rounds = rounds,
        };
        _db.TournamentDirectoryEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    private const string ElevenRounds = """
        [{"round":1,"date":"2026-09-26","time":"14:00 Uhr"},
         {"round":2,"date":"2026-10-10","time":"14:00 Uhr"},
         {"round":3,"date":"2026-10-24","time":"14:00 Uhr"},
         {"round":11,"date":"2027-04-17","time":"14:00 Uhr"}]
        """;

    [Fact]
    public async Task RunAsync_LongRunningTournament_StoresItsRoundDates()
    {
        await AddLeagueAsync();

        var result = await CreateService(ElevenRounds).RunAsync(10);

        Assert.Equal(1, result.Checked);
        Assert.Equal(1, result.WithPlan);
        var entry = await _db.TournamentDirectoryEntries.Include(e => e.RoundDates).SingleAsync();
        Assert.Equal(4, entry.RoundDates.Count);
        Assert.Equal(new DateOnly(2026, 9, 26), entry.RoundDates.OrderBy(r => r.Number).First().Date);
        Assert.Equal("14:00 Uhr", entry.RoundDates.First().TimeText);
        Assert.NotNull(entry.RoundPlanCheckedAt);
    }

    /// <summary>
    /// Der Vermerk „geprueft" sagt nur, DASS nachgesehen wurde — nicht womit. Nach der
    /// Parser-Reparatur trugen 452 Eintraege einen aus der Zeit davor, u. a. tnr1438343 mit null
    /// gespeicherten von neun abrufbaren Terminen; „geprueft" verhinderte jede Wiederholung. Eine
    /// aeltere FASSUNG holt der naechste Durchgang deshalb von selbst nach.
    /// </summary>
    [Fact]
    public async Task RunAsync_AnOlderVersion_IsFetchedAgain()
    {
        var entry = await AddLeagueAsync();
        entry.RoundPlanCheckedAt = DateTime.UtcNow.AddHours(-1);
        entry.RoundPlanVersion = 0;                       // Stand vor der Reparatur
        await _db.SaveChangesAsync();

        var result = await CreateService(ElevenRounds).RunAsync(10);

        Assert.Equal(1, result.Checked);
        var after = await _db.TournamentDirectoryEntries.Include(e => e.RoundDates).SingleAsync();
        Assert.Equal(4, after.RoundDates.Count);
        Assert.Equal(TournamentRoundPlanService.CurrentVersion, after.RoundPlanVersion);
    }

    /// <summary>Auf dem aktuellen Stand wird NICHT erneut geholt — sonst waere die Fassung wertlos.</summary>
    [Fact]
    public async Task RunAsync_TheCurrentVersion_IsLeftAlone()
    {
        var entry = await AddLeagueAsync();
        entry.RoundPlanCheckedAt = DateTime.UtcNow.AddHours(-1);
        entry.RoundPlanVersion = TournamentRoundPlanService.CurrentVersion;
        await _db.SaveChangesAsync();

        var result = await CreateService(ElevenRounds).RunAsync(10);

        Assert.Equal(0, result.Checked);
    }

    /// <summary>
    /// Ein Wochenend-Open braucht das nicht — dort spielt man an aufeinanderfolgenden Tagen, und
    /// der Zeitraum ist die richtige Auskunft. Jeder Abruf, der hier gespart wird, ist eine
    /// Anfrage weniger an chess-results.
    /// </summary>
    [Fact]
    public async Task RunAsync_ShortTournament_IsNotFetchedAtAll()
    {
        await AddLeagueAsync(start: new DateOnly(2026, 12, 18), end: new DateOnly(2026, 12, 20));

        var result = await CreateService(ElevenRounds).RunAsync(10);

        Assert.Equal(0, result.Checked);
        Assert.Empty(await _db.TournamentDirectoryRounds.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_SingleRoundTournament_IsNotFetched()
    {
        await AddLeagueAsync(rounds: 1);

        Assert.Equal(0, (await CreateService(ElevenRounds).RunAsync(10)).Checked);
    }

    /// <summary>
    /// Ein Turnier, das schon vorbei ist, kostet einen Abruf fuer eine Auskunft, die niemand
    /// sucht.
    /// </summary>
    [Fact]
    public async Task RunAsync_FinishedTournament_IsNotFetched()
    {
        await AddLeagueAsync(start: new DateOnly(2024, 9, 1), end: new DateOnly(2025, 4, 1));

        Assert.Equal(0, (await CreateService(ElevenRounds).RunAsync(10)).Checked);
    }

    /// <summary>
    /// Kein hinterlegter Plan ist bei vielen Turnieren der Normalfall — und muss sich MERKEN
    /// lassen, sonst holt der naechtliche Durchgang jede Nacht dieselben Seiten. Der Vermerk wird
    /// deshalb auch dann gesetzt, wenn nichts dabei herauskam.
    /// </summary>
    [Fact]
    public async Task RunAsync_NoPlanPublished_IsRemembered()
    {
        await AddLeagueAsync();

        var result = await CreateService("[]").RunAsync(10);

        Assert.Equal(1, result.Checked);
        Assert.Equal(0, result.WithPlan);
        Assert.NotNull((await _db.TournamentDirectoryEntries.SingleAsync()).RoundPlanCheckedAt);
        Assert.Equal(0, (await CreateService("[]").RunAsync(10)).Checked);   // kein zweiter Abruf
    }

    /// <summary>
    /// Ein NETZfehler ist dagegen keine Auskunft ueber das Turnier — der Vermerk bleibt leer,
    /// damit der naechste Durchgang es wieder vornimmt.
    /// </summary>
    [Fact]
    public async Task RunAsync_FetchFails_IsRetriedNextTime()
    {
        await AddLeagueAsync();

        var result = await CreateService("boom", HttpStatusCode.InternalServerError).RunAsync(10);

        Assert.Equal(1, result.Failed);
        Assert.Null((await _db.TournamentDirectoryEntries.SingleAsync()).RoundPlanCheckedAt);
        Assert.Equal(1, (await CreateService(ElevenRounds).RunAsync(10)).WithPlan);
    }

    /// <summary>
    /// chess-results fuehrt in manchen Ligen die Runden ALLER Gruppen in einer Tabelle. Ein
    /// Termin jenseits des eigenen Enddatums gehoert dann nicht zu diesem Eintrag — und ein
    /// Turnier darf nicht an Tagen erscheinen, an denen es laut eigener Angabe vorbei ist.
    /// </summary>
    [Fact]
    public async Task RunAsync_RoundOutsideTheOwnSpan_IsDropped()
    {
        await AddLeagueAsync(end: new DateOnly(2026, 10, 24));

        await CreateService(ElevenRounds).RunAsync(10);

        var entry = await _db.TournamentDirectoryEntries.Include(e => e.RoundDates).SingleAsync();
        Assert.Equal(3, entry.RoundDates.Count);
        Assert.DoesNotContain(entry.RoundDates, r => r.Date > new DateOnly(2026, 10, 24));
    }

    /// <summary>
    /// Ein zweiter Durchgang ERSETZT die Termine. Ergaenzte er sie, stuende ein verschobener
    /// Termin zweimal im Kalender — einmal alt, einmal neu.
    /// </summary>
    [Fact]
    public async Task RunAsync_SecondPass_ReplacesInsteadOfAdding()
    {
        var entry = await AddLeagueAsync();
        await CreateService(ElevenRounds).RunAsync(10);

        // Termin verschoben: der Sweep leert den Vermerk, der Durchgang nimmt es wieder vor.
        entry.RoundPlanCheckedAt = null;
        await _db.SaveChangesAsync();

        await CreateService("""[{"round":1,"date":"2026-10-03","time":"15:00 Uhr"}]""").RunAsync(10);

        var rounds = await _db.TournamentDirectoryRounds.ToListAsync();
        Assert.Equal(new DateOnly(2026, 10, 3), Assert.Single(rounds).Date);
    }

    [Fact]
    public async Task RunAsync_ZeroLimit_DoesNothing()
    {
        await AddLeagueAsync();

        Assert.Equal(0, (await CreateService(ElevenRounds).RunAsync(0)).Checked);
    }

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }

    /// <summary>
    /// Ein Durchgang ueber 200 Turniere dauert rund zehn Minuten (Rate-Limiter des Crawlers plus
    /// VPN-Rotation, ~3 s je Abruf). Wurde erst am ENDE gespeichert, verwarf ein Abbruch in
    /// dieser Zeit die ganze Arbeit — auf Dev nachgemessen: nach sechs Minuten standen 0
    /// Spieltermine in der Datenbank, und der naechste Durchgang haette bei denselben Turnieren
    /// begonnen. Geprueft wird deshalb gegen einen FRISCHEN Kontext: nur was gespeichert ist,
    /// ist dort zu sehen.
    /// </summary>
    [Fact]
    public async Task RunAsync_Abbruch_BehaeltDenZwischenstand()
    {
        for (var i = 0; i < 5; i++)
            await AddLeagueAsync($"14793{i}0");

        using var cts = new CancellationTokenSource();
        var service = new TournamentRoundPlanService(
            _db, new StubClientFactory(new CancellingHandler(ElevenRounds, cancelAfter: 4, cts)),
            new TestLogger<TournamentRoundPlanService>())
        { SaveEvery = 2 };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RunAsync(10, retryEmpty: false, cts.Token));

        // Frischer Kontext auf DERSELBEN InMemory-Datenbank: sieht ausschliesslich Gespeichertes.
        using var fresh = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options);
        var gesichert = await fresh.TournamentDirectoryEntries
            .CountAsync(e => e.RoundPlanCheckedAt != null);

        Assert.Equal(4, gesichert);
        Assert.Equal(4, await fresh.TournamentDirectoryRounds
            .Select(r => r.TournamentDirectoryEntryId).Distinct().CountAsync());
    }

    /// <summary>
    /// Ein Eintrag, der als geprueft gilt und KEINEN Termin hat, wird mit `retryEmpty` erneut
    /// vorgenommen. Der Schalter bleibt fuer den Fall noetig, in dem die Fassung NICHT hilft: das
    /// Holen war zur aktuellen Fassung erfolgreich, hat aber nichts gefunden — und man will
    /// trotzdem nachsehen (etwa weil der Veranstalter den Plan inzwischen nachgetragen hat).
    ///
    /// <para>Der andere Fall — geprueft mit einer AELTEREN Fassung — braucht ihn seit
    /// `RoundPlanVersion` nicht mehr; deshalb steht hier ausdruecklich die aktuelle Fassung.</para>
    /// </summary>
    [Fact]
    public async Task RunAsync_RetryEmpty_NimmtLeerGeprueftesErneutVor()
    {
        var entry = await AddLeagueAsync();
        entry.RoundPlanCheckedAt = DateTime.UtcNow.AddDays(-1);
        entry.RoundPlanVersion = TournamentRoundPlanService.CurrentVersion;
        await _db.SaveChangesAsync();

        // Ohne den Schalter bleibt es liegen.
        Assert.Equal(0, (await CreateService(ElevenRounds).RunAsync(10)).Checked);

        var result = await CreateService(ElevenRounds).RunAsync(10, retryEmpty: true);

        Assert.Equal(1, result.Checked);
        Assert.Equal(1, result.WithPlan);
        Assert.Equal(4, (await _db.TournamentDirectoryEntries
            .Include(e => e.RoundDates).SingleAsync()).RoundDates.Count);
    }

    /// <summary>
    /// Wer schon Termine hat, wird auch mit dem Schalter NICHT erneut geholt — der Sinn ist die
    /// Reparatur der Leeren, nicht ein Rundumschlag.
    /// </summary>
    [Fact]
    public async Task RunAsync_RetryEmpty_LaesstVorhandenePlaeneInRuhe()
    {
        await AddLeagueAsync();
        await CreateService(ElevenRounds).RunAsync(10);

        Assert.Equal(0, (await CreateService(ElevenRounds).RunAsync(10, retryEmpty: true)).Checked);
    }

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    /// <summary>
    /// Rundenzahl 0 heisst „unbekannt", nicht „eine Runde" — so ein Eintrag MUSS gefragt werden.
    ///
    /// <para>Die chess-results-Turniersuche laesst die Spalte oft leer. Die Auswahl verlangte
    /// <c>Rounds &gt; 1</c> und schloss diese Eintraege damit fuer immer aus, ohne sie je gefragt
    /// zu haben — waehrend der Kalender sie ueber ihren ganzen Zeitraum zeichnet. Auf Dev
    /// gezaehlt: 132 langlaufende Eintraege mit Rundenzahl 0, und in einer Stichprobe von fuenf
    /// hatten VIER einen veroeffentlichten Plan (einer mit zwoelf Terminen von Oktober bis
    /// Maerz). So gemeldet an tnr1474416.</para>
    /// </summary>
    [Fact]
    public async Task RunAsync_UnknownRoundCount_IsStillFetched()
    {
        await AddLeagueAsync(rounds: 0);

        var result = await CreateService(ElevenRounds).RunAsync(10);

        Assert.Equal(1, result.Checked);
        Assert.Equal(1, result.WithPlan);
        Assert.NotEmpty(await _db.TournamentDirectoryRounds.ToListAsync());
    }

    /// <summary>
    /// Mit <c>retryEmpty</c> muss ein ZWEITER Durchgang andere Turniere vornehmen als der erste.
    ///
    /// <para><b>Warum das ein eigener Test ist.</b> Die Auswahl lief nach Startdatum, und damit
    /// nahm jeder Durchgang wieder die vordersten — nach dem ersten Durchgang genau die, von denen
    /// man schon WEISS, dass sie keinen Plan haben. Auf Dev nachgemessen: von den 200 eines
    /// zweiten Durchgangs waeren 159 gerade erst geprueft gewesen, waehrend 252 aeltere Vermerke
    /// nie an die Reihe gekommen waeren. Wiederholtes Aufrufen konvergierte also nicht, es lief im
    /// Kreis — und zwar mit einem Seitenabruf je Runde. Sortiert wird deshalb nach dem ALTER des
    /// Vermerks (nie geprueft zuerst).</para>
    /// </summary>
    [Fact]
    public async Task RunAsync_WithRetryEmpty_TakesTheLeastRecentlyCheckedFirst()
    {
        // Frueher Termin, gerade eben geprueft: nach Startdatum waere das der erste Kandidat.
        var justChecked = await AddLeagueAsync("1000001", start: new DateOnly(2026, 9, 26));
        justChecked.RoundPlanCheckedAt = DateTime.UtcNow;

        // Spaeterer Termin, aber der Vermerk ist alt — dieses gehoert vorgenommen.
        var stale = await AddLeagueAsync("1000002", start: new DateOnly(2026, 11, 1),
            end: new DateOnly(2027, 5, 1));
        stale.RoundPlanCheckedAt = DateTime.UtcNow.AddHours(-5);

        // Und ein nie geprueftes mit dem SPAETESTEN Termin: das muss trotzdem zuerst kommen.
        var never = await AddLeagueAsync("1000003", start: new DateOnly(2026, 12, 1),
            end: new DateOnly(2027, 6, 1));
        await _db.SaveChangesAsync();

        var result = await CreateService(ElevenRounds).RunAsync(1, retryEmpty: true);

        // Geprueft wird, WELCHER Eintrag drankam — nicht wie viele Runden die Vorlage traegt
        // (der Dienst uebernimmt nur Termine INNERHALB des Zeitraums des Turniers).
        Assert.Equal(1, result.Checked);
        Assert.NotEmpty(await Rounds(never));
        Assert.Empty(await Rounds(stale));
        Assert.Empty(await Rounds(justChecked));

        // Zweiter Durchgang: jetzt der ALTE Vermerk, nicht wieder der eben gepruefte.
        Assert.Equal(1, (await CreateService(ElevenRounds).RunAsync(1, retryEmpty: true)).Checked);
        Assert.NotEmpty(await Rounds(stale));
        Assert.Empty(await Rounds(justChecked));
    }

    private async Task<List<TournamentDirectoryRound>> Rounds(TournamentDirectoryEntry entry) =>
        await _db.TournamentDirectoryRounds
            .Where(r => r.TournamentDirectoryEntryId == entry.Id)
            .ToListAsync();

    /// <summary>
    /// Antwortet wie <see cref="StubHandler"/>, bricht den Durchgang aber NACH der
    /// <paramref name="cancelAfter"/>-ten Anfrage ab — so, wie ein abgebrochener Aufruf, ein
    /// API-Neustart oder ein Deploy mitten in einem zehnminuetigen Lauf es tut.
    /// </summary>
    private sealed class CancellingHandler(string body, int cancelAfter, CancellationTokenSource cts)
        : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (++_calls >= cancelAfter) cts.Cancel();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
