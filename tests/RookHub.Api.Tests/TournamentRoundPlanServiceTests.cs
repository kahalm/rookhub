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
    /// vorgenommen — sonst waere die Behebung eines kaputten Holens fuer den bestehenden Bestand
    /// wirkungslos (auf Dev genau so passiert: 337 geprueft, 0 Termine, weil der Parser die
    /// Wrapper-Tabelle griff).
    /// </summary>
    [Fact]
    public async Task RunAsync_RetryEmpty_NimmtLeerGeprueftesErneutVor()
    {
        var entry = await AddLeagueAsync();
        entry.RoundPlanCheckedAt = DateTime.UtcNow.AddDays(-1);
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
