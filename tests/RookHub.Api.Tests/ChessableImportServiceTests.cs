using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Robustheit/Resume des async Chessable-Imports: gecheckpointetes PGN (kein erneuter Fetch),
/// idempotente Schritte (kein Doppelt-Anlegen), Versuchs-Limit.
/// Tests setzen FetchedPgn vor → der ChessableProxyService (HTTP) wird nie aufgerufen.
/// </summary>
public class ChessableImportServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly RepertoireService _repertoires;
    private readonly FakeQueue _queue = new();
    private readonly ChessableImportService _svc;

    public ChessableImportServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!"
        }).Build();
        _encryption = new EncryptionService(config);
        var cache = new MemoryCache(new MemoryCacheOptions());
        _repertoires = new RepertoireService(_db, new RepertoireAnalyzeService(_db, cache));
        // Default-Proxy wird bei FetchedPgn-Tests nie aufgerufen → Handler wirft, falls doch.
        _svc = BuildSvc(new ScriptedHandler(_ => throw new InvalidOperationException("Proxy unerwartet aufgerufen")));
    }

    private ChessableImportService BuildSvc(HttpMessageHandler handler)
    {
        var proxy = new ChessableProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://piratechess-api:8080") });
        var breaker = new ChessableBearerBreaker(_db, _queue, NullLogger<ChessableBearerBreaker>.Instance);
        return new ChessableImportService(_db, _encryption, proxy, _repertoires, new PgnImportService(_db),
            _queue, new NotificationService(_db), breaker, NullLogger<ChessableImportService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<ChessableImport> SeedImportAsync(
        string target, string status = "running", string? fetchedPgn = "1. e4 e5 2. Nf3 Nc6 *",
        int attempts = 0, int? resultId = null)
    {
        if (!await _db.AppUsers.AnyAsync(u => u.Id == 7))
            _db.AppUsers.Add(new AppUser { Id = 7, Username = "u7", PasswordHash = "x" });
        var imp = new ChessableImport
        {
            UserId = 7, Bid = "b1", CourseName = "Course X", Target = target,
            Status = status, Phase = "queued", FetchedPgn = fetchedPgn, LineCount = 3,
            Attempts = attempts, ResultId = resultId, CreatedAt = DateTime.UtcNow
        };
        _db.ChessableImports.Add(imp);
        await _db.SaveChangesAsync();
        return imp;
    }

    [Fact]
    public async Task RunAsync_RepertoireWithCachedPgn_Completes()
    {
        var imp = await SeedImportAsync("repertoire");

        await _svc.RunAsync(imp.Id);

        var reloaded = await _db.ChessableImports.FindAsync(imp.Id);
        Assert.Equal("completed", reloaded!.Status);
        Assert.NotNull(reloaded.ResultId);
        Assert.Null(reloaded.FetchedPgn); // Checkpoint nach Erfolg geleert
        Assert.Equal(3, reloaded.Imported); // LineCount
        Assert.Equal(1, await _db.Repertoires.CountAsync(r => r.UserId == 7));
        Assert.Equal(1, await _db.RepertoireFiles.CountAsync());
        // Glocke: User wird über den fertigen Import benachrichtigt.
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == 7 && n.Type == NotificationType.ChessableImportCompleted));
    }

    [Fact]
    public async Task RunAsync_StampsStartedAt_AndNotifiesWithDurations()
    {
        var imp = await SeedImportAsync("repertoire");

        await _svc.RunAsync(imp.Id);

        var reloaded = await _db.ChessableImports.FindAsync(imp.Id);
        Assert.NotNull(reloaded!.StartedAt);   // Hol-Beginn gestempelt (für Wartezeit/Holzeit)
        Assert.NotNull(reloaded.CompletedAt);
        Assert.True(reloaded.StartedAt >= reloaded.CreatedAt);

        // Fertig-Benachrichtigung trägt Hol-/Wartezeit als i18n-Parameter.
        var notif = await _db.Notifications.SingleAsync(
            n => n.UserId == 7 && n.Type == NotificationType.ChessableImportCompleted);
        Assert.Contains("fetchTime", notif.DataJson);
        Assert.Contains("queueTime", notif.DataJson);
    }

    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(45, "45 s")]
    [InlineData(90, "1 min")]
    [InlineData(3661, "1 h 1 min")]
    public void FormatDuration_FormatsCompactly(int seconds, string expected)
        => Assert.Equal(expected, ChessableImportService.FormatDuration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void FormatDuration_NullOrNegative_IsDash()
    {
        Assert.Equal("—", ChessableImportService.FormatDuration(null));
        Assert.Equal("—", ChessableImportService.FormatDuration(TimeSpan.FromSeconds(-5)));
    }

    // ===== Faire Queue-Reihenfolge (Round-Robin über User) =====

    private static ChessableImport Q(int id, int userId, int round, DateTime created) =>
        new() { Id = id, UserId = userId, QueueRound = round, CreatedAt = created, Status = "running", Phase = "queued" };

    [Fact]
    public void FairOrder_NewUserGoesSecond_ThenContinuesFirstUser()
    {
        var t0 = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        // User1 reiht 3 ein (Runden 0,1,2), danach User2 einen (Runde 0).
        var order = ChessableImportService.FairOrder(new[]
        {
            Q(1, 1, 0, t0), Q(2, 1, 1, t0.AddSeconds(1)), Q(3, 1, 2, t0.AddSeconds(2)),
            Q(4, 2, 0, t0.AddSeconds(3)),
        });
        // U2 rückt auf Platz 2 (hinter U1s ersten), dann folgen U1s restliche.
        Assert.Equal(new[] { 1, 4, 2, 3 }, order.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void FairOrder_StableWhenEarlierJobsCompleted()
    {
        var t0 = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        // U1s erster (Runde 0) ist bereits fertig und raus → verbleiben U1b(1), U1c(2), U2a(0).
        var order = ChessableImportService.FairOrder(new[]
        {
            Q(2, 1, 1, t0.AddSeconds(1)), Q(3, 1, 2, t0.AddSeconds(2)), Q(4, 2, 0, t0.AddSeconds(3)),
        });
        // Eingefrorene Runde ⇒ U2a (Runde 0) kommt weiterhin vor U1s Folge-Jobs.
        Assert.Equal(new[] { 4, 2, 3 }, order.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void FairOrder_TwoUsersInterleave()
    {
        var t0 = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        // U1: 3 Jobs (0,1,2), U2: 2 Jobs (0,1) → abwechselnd, U1 zuerst je Runde.
        var order = ChessableImportService.FairOrder(new[]
        {
            Q(1, 1, 0, t0), Q(2, 1, 1, t0.AddSeconds(1)), Q(3, 1, 2, t0.AddSeconds(2)),
            Q(4, 2, 0, t0.AddSeconds(3)), Q(5, 2, 1, t0.AddSeconds(4)),
        });
        Assert.Equal(new[] { 1, 4, 2, 5, 3 }, order.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task RunNextAsync_ProcessesQueuedImportsInFairOrder()
    {
        // User 1 reiht zuerst 2 Kurse ein (Runden 0,1), dann User 2 einen (Runde 0).
        // Faire Reihenfolge: U1a, U2a, U1b — der Kurs von User2 rückt vor U1s zweiten.
        foreach (var uid in new[] { 1, 2 })
            if (!await _db.AppUsers.AnyAsync(u => u.Id == uid))
                _db.AppUsers.Add(new AppUser { Id = uid, Username = $"u{uid}", PasswordHash = "x" });
        var t0 = DateTime.UtcNow;
        var u1a = new ChessableImport { UserId = 1, Bid = "u1a", CourseName = "U1A", Target = "repertoire", Status = "running", Phase = "queued", QueueRound = 0, CreatedAt = t0, FetchedPgn = "1. e4 *", LineCount = 1 };
        var u1b = new ChessableImport { UserId = 1, Bid = "u1b", CourseName = "U1B", Target = "repertoire", Status = "running", Phase = "queued", QueueRound = 1, CreatedAt = t0.AddSeconds(1), FetchedPgn = "1. e4 *", LineCount = 1 };
        var u2a = new ChessableImport { UserId = 2, Bid = "u2a", CourseName = "U2A", Target = "repertoire", Status = "running", Phase = "queued", QueueRound = 0, CreatedAt = t0.AddSeconds(2), FetchedPgn = "1. e4 *", LineCount = 1 };
        _db.ChessableImports.AddRange(u1a, u1b, u2a);
        await _db.SaveChangesAsync();

        await _svc.RunNextAsync();
        Assert.Equal("completed", (await _db.ChessableImports.FindAsync(u1a.Id))!.Status);
        Assert.Equal("running", (await _db.ChessableImports.FindAsync(u2a.Id))!.Status); // wartet noch

        await _svc.RunNextAsync();
        // User2 ist vor U1s zweitem Kurs dran.
        Assert.Equal("completed", (await _db.ChessableImports.FindAsync(u2a.Id))!.Status);
        Assert.Equal("running", (await _db.ChessableImports.FindAsync(u1b.Id))!.Status);

        await _svc.RunNextAsync();
        Assert.Equal("completed", (await _db.ChessableImports.FindAsync(u1b.Id))!.Status);
    }

    [Fact]
    public async Task RunNextAsync_NoQueued_IsNoOp()
    {
        await _svc.RunNextAsync(); // keine wartenden Importe → kein Fehler
        Assert.Equal(0, await _db.ChessableImports.CountAsync());
    }

    [Fact]
    public async Task RunNextAsync_FastLane_OnlyClaimsFullyCached()
    {
        if (!await _db.AppUsers.AnyAsync(u => u.Id == 1))
            _db.AppUsers.Add(new AppUser { Id = 1, Username = "u1", PasswordHash = "x" });
        var t0 = DateTime.UtcNow;
        // Download-Job ist FAIR ZUERST dran (frühere CreatedAt), aber nicht gecacht.
        var dl = new ChessableImport { UserId = 1, Bid = "dl", CourseName = "DL", Target = "repertoire", Status = "running", Phase = "queued", FullyCached = false, CreatedAt = t0, FetchedPgn = "1. e4 *", LineCount = 1 };
        var cached = new ChessableImport { UserId = 1, Bid = "c", CourseName = "C", Target = "repertoire", Status = "running", Phase = "queued", FullyCached = true, CreatedAt = t0.AddSeconds(1), FetchedPgn = "1. e4 *", LineCount = 1 };
        _db.ChessableImports.AddRange(dl, cached);
        await _db.SaveChangesAsync();

        await _svc.RunNextAsync(fastLane: true);

        Assert.Equal("completed", (await _db.ChessableImports.FindAsync(cached.Id))!.Status); // gecacht verarbeitet
        Assert.Equal("queued", (await _db.ChessableImports.FindAsync(dl.Id))!.Phase);        // Download unangetastet
    }

    [Fact]
    public async Task RunNextAsync_DownloadLane_ClaimsNonCachedIncludingUnclassified()
    {
        if (!await _db.AppUsers.AnyAsync(u => u.Id == 1))
            _db.AppUsers.Add(new AppUser { Id = 1, Username = "u1", PasswordHash = "x" });
        var t0 = DateTime.UtcNow;
        // Unklassifiziert (null) gilt als Download und MUSS gegriffen werden (darf nie hängen bleiben).
        var nullJob = new ChessableImport { UserId = 1, Bid = "n", CourseName = "N", Target = "repertoire", Status = "running", Phase = "queued", FullyCached = null, CreatedAt = t0, FetchedPgn = "1. e4 *", LineCount = 1 };
        var cached = new ChessableImport { UserId = 1, Bid = "c", CourseName = "C", Target = "repertoire", Status = "running", Phase = "queued", FullyCached = true, CreatedAt = t0.AddSeconds(1), FetchedPgn = "1. e4 *", LineCount = 1 };
        _db.ChessableImports.AddRange(nullJob, cached);
        await _db.SaveChangesAsync();

        await _svc.RunNextAsync(); // Download-Lane (Default)

        Assert.Equal("completed", (await _db.ChessableImports.FindAsync(nullJob.Id))!.Status); // null = Download verarbeitet
        Assert.Equal("queued", (await _db.ChessableImports.FindAsync(cached.Id))!.Phase);      // Fast-Lane-Job unangetastet
    }

    [Fact]
    public async Task RunNextAsync_SkipsAlreadyClaimedJob_PicksGenuinelyQueuedOne()
    {
        // Atomarer Claim: ein bereits von einem anderen Worker übernommener Job (Phase != "queued")
        // darf NICHT erneut gegriffen werden. Hier ist der fair zuerst dran befindliche Job bereits
        // "fetching" → RunNextAsync überspringt ihn und bearbeitet den nächsten echten "queued"-Job.
        if (!await _db.AppUsers.AnyAsync(u => u.Id == 7))
            _db.AppUsers.Add(new AppUser { Id = 7, Username = "u7", PasswordHash = "x" });
        var t0 = DateTime.UtcNow;
        var claimed = new ChessableImport { UserId = 7, Bid = "a", CourseName = "A", Target = "repertoire", Status = "running", Phase = "fetching", QueueRound = 0, CreatedAt = t0, FetchedPgn = "1. e4 *", LineCount = 1 };
        var queued = new ChessableImport { UserId = 7, Bid = "b", CourseName = "B", Target = "repertoire", Status = "running", Phase = "queued", QueueRound = 1, CreatedAt = t0.AddSeconds(1), FetchedPgn = "1. e4 *", LineCount = 1 };
        _db.ChessableImports.AddRange(claimed, queued);
        await _db.SaveChangesAsync();

        await _svc.RunNextAsync();

        // Der schon übernommene Job bleibt unangetastet; der echte queued-Job wurde bearbeitet.
        Assert.Equal("running", (await _db.ChessableImports.FindAsync(claimed.Id))!.Status);
        Assert.Equal("fetching", (await _db.ChessableImports.FindAsync(claimed.Id))!.Phase);
        Assert.Equal("completed", (await _db.ChessableImports.FindAsync(queued.Id))!.Status);
    }

    [Fact]
    public async Task RunNextAsync_ClaimsExactlyOne_WhenInvokedConcurrently()
    {
        // Resume-Sturm: zwei parallele Tickets, EIN wartender Job. Der atomare Phase-Claim
        // ("queued" → "claimed" per ExecuteUpdate) stellt sicher, dass nur ein Lauf den Job
        // bearbeitet → keine Doppelverarbeitung (kein zweites Repertoire).
        if (!await _db.AppUsers.AnyAsync(u => u.Id == 7))
            _db.AppUsers.Add(new AppUser { Id = 7, Username = "u7", PasswordHash = "x" });
        var imp = new ChessableImport { UserId = 7, Bid = "b1", CourseName = "C", Target = "repertoire", Status = "running", Phase = "queued", QueueRound = 0, CreatedAt = DateTime.UtcNow, FetchedPgn = "1. e4 *", LineCount = 1 };
        _db.ChessableImports.Add(imp);
        await _db.SaveChangesAsync();

        // Sequentiell zwei Ticket-Läufe (InMemory ist nicht echt nebenläufig, aber der zweite Lauf
        // sieht den Job nicht mehr als "queued" → no-op statt Doppel-Import).
        await _svc.RunNextAsync();
        await _svc.RunNextAsync();

        Assert.Equal("completed", (await _db.ChessableImports.FindAsync(imp.Id))!.Status);
        Assert.Equal(1, await _db.Repertoires.CountAsync(r => r.UserId == 7)); // nicht doppelt angelegt
    }

    [Fact]
    public async Task RunNextAsync_DownloadLane_GateBlocksSecondConcurrentDrive()
    {
        // Regression (2026-06-29, zwei Kurse zogen gleichzeitig): die Download-Lane wird von ZWEI
        // unabhängigen Treibern bedient — dem Queue-Worker UND dem Watchdog (ruft RunNextAsync an der
        // Queue vorbei direkt). Der atomare Claim verhindert nur DOPPEL-Verarbeitung DESSELBEN Jobs,
        // nicht, dass jeder Treiber einen ANDEREN wartenden Job greift und parallel herunterlädt.
        // Das prozessweite Gate (SemaphoreSlim(1)) erzwingt höchstens EINEN gleichzeitigen Download:
        // solange einer läuft, kehrt ein zweiter Drive SOFORT zurück (zweiter Job bleibt "queued").
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "u7", PasswordHash = "x" });
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = 7, EncryptedBearer = _encryption.Encrypt("bearer"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        var t0 = DateTime.UtcNow;
        var j1 = new ChessableImport { UserId = 7, Bid = "j1", CourseName = "", Target = "repertoire", Status = "running", Phase = "queued", FullyCached = false, FetchedPgn = null, CreatedAt = t0 };
        var j2 = new ChessableImport { UserId = 7, Bid = "j2", CourseName = "", Target = "repertoire", Status = "running", Phase = "queued", FullyCached = false, FetchedPgn = null, CreatedAt = t0.AddSeconds(1) };
        _db.ChessableImports.AddRange(j1, j2);
        await _db.SaveChangesAsync();

        var inFlight = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var svc = BuildSvc(new ScriptedHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/course/start")) return JsonOk(new { jobId = "job-1" });
            // Der (einzige) Fetch hängt im Progress-Poll fest → das Download-Gate bleibt gehalten.
            inFlight.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            return JsonOk(new { status = "completed", chaptersDone = 1, chaptersTotal = 1, linesDone = 1, chapterCount = 1, lineCount = 1, courseName = "CN", pgn = "1. e4 e5 *", error = (string?)null });
        }));
        svc.PollDelayMs = 0;

        // Treiber 1 (z. B. Queue-Worker): hält das Gate, blockiert mitten im Fetch von j1 (fair zuerst dran).
        var first = Task.Run(() => svc.RunNextAsync());
        await inFlight.Task.WaitAsync(TimeSpan.FromSeconds(10)); // j1 ist jetzt "fetching", Gate gehalten

        try
        {
            // Treiber 2 (z. B. Watchdog) feuert parallel — MUSS sofort zurückkehren, ohne j2 zu greifen.
            await svc.RunNextAsync();
            Assert.Equal("queued", (await _db.ChessableImports.FindAsync(j2.Id))!.Phase); // Gate blockte den 2. Download
        }
        finally
        {
            release.TrySetResult();           // Fetch von j1 abschließen lassen → Gate wird freigegeben
            await first.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal("completed", (await _db.ChessableImports.FindAsync(j1.Id))!.Status);
    }

    [Fact]
    public async Task RunAsync_AlreadyCompleted_IsNoOp()
    {
        var imp = await SeedImportAsync("repertoire", status: "completed");
        await _svc.RunAsync(imp.Id);
        Assert.Equal(0, await _db.Repertoires.CountAsync());
    }

    [Fact]
    public async Task RunAsync_BearerBreakerOpen_PausesWithoutCallingProxy()
    {
        // Bearer von User 7 gesperrt → der Import darf Chessable GAR NICHT anfragen.
        await SeedCredentialAsync(7, blocked: true);
        var imp = await SeedImportAsync("repertoire", fetchedPgn: null); // braucht Fetch → Bearer-Pfad

        // _svc nutzt den Default-Handler, der bei JEDEM Proxy-Aufruf wirft. Kein Wurf = kein Request.
        await _svc.RunAsync(imp.Id);

        var reloaded = await _db.ChessableImports.FindAsync(imp.Id);
        Assert.Equal("paused", reloaded!.Status);
        Assert.Equal("bearer-blocked", reloaded.Phase);
        Assert.Equal(0, await _db.Repertoires.CountAsync());
    }

    [Fact]
    public async Task RunAsync_FetchFailsBanned_TripsBreaker()
    {
        await SeedCredentialAsync(7, blocked: false);
        var imp = await SeedImportAsync("repertoire", fetchedPgn: null);

        var svc = BuildSvc(new ScriptedHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/course/start")) return JsonOk(new { jobId = "job-1" });
            return JsonOk(new { status = "failed", chaptersDone = 0, chaptersTotal = 0, linesDone = 0,
                chapterCount = 0, lineCount = 0, courseName = (string?)null, pgn = (string?)null,
                error = "Chessable: User is banned or deleted" });
        }));
        svc.PollDelayMs = 0;

        await svc.RunAsync(imp.Id);

        var reloaded = await _db.ChessableImports.FindAsync(imp.Id);
        Assert.Equal("failed", reloaded!.Status);
        // Der fatale Fehlschlag hat den Circuit-Breaker des Bearers geöffnet.
        var cred = await _db.ChessableCredentials.SingleAsync(c => c.UserId == 7);
        Assert.NotNull(cred.BlockedAt);
    }

    private async Task SeedCredentialAsync(int userId, bool blocked)
    {
        if (!await _db.AppUsers.AnyAsync(u => u.Id == userId))
            _db.AppUsers.Add(new AppUser { Id = userId, Username = $"u{userId}", PasswordHash = "x" });
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = userId,
            EncryptedBearer = _encryption.Encrypt("dummy-bearer"),
            BlockedAt = blocked ? DateTime.UtcNow : null,
            BlockedReason = blocked ? "Chessable: User is banned or deleted" : null,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task RunAsync_Twice_DoesNotDuplicate()
    {
        var imp = await SeedImportAsync("repertoire");
        await _svc.RunAsync(imp.Id);
        await _svc.RunAsync(imp.Id); // zweiter Lauf → no-op (completed)
        Assert.Equal(1, await _db.Repertoires.CountAsync(r => r.UserId == 7));
        Assert.Equal(1, await _db.RepertoireFiles.CountAsync());
    }

    [Fact]
    public async Task RunAsync_ResumeWithExistingResult_DoesNotDuplicate()
    {
        // Simuliert Crash NACH Repertoire-Anlage (ResultId gesetzt), Status noch "running".
        var rep = await _repertoires.CreateAsync(7, new CreateRepertoireDto { Name = "Course X" });
        using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("1. e4 *")))
            await _repertoires.UploadFileAsync(rep.Id, 7, "chessable-b1.pgn", ms);

        var imp = await SeedImportAsync("repertoire", resultId: rep.Id);

        await _svc.RunAsync(imp.Id);

        var reloaded = await _db.ChessableImports.FindAsync(imp.Id);
        Assert.Equal("completed", reloaded!.Status);
        Assert.Equal(rep.Id, reloaded.ResultId);
        Assert.Equal(1, await _db.Repertoires.CountAsync(r => r.UserId == 7)); // kein zweites Repertoire
        Assert.Equal(1, await _db.RepertoireFiles.CountAsync());              // keine zweite Datei
    }

    [Fact]
    public async Task RunAsync_ExceedsMaxAttempts_Fails()
    {
        var imp = await SeedImportAsync("repertoire", attempts: ChessableImportService.MaxAttempts);
        await _svc.RunAsync(imp.Id);
        var reloaded = await _db.ChessableImports.FindAsync(imp.Id);
        Assert.Equal("failed", reloaded!.Status);
        Assert.Equal(0, await _db.Repertoires.CountAsync());
        // Glocke: User wird über den fehlgeschlagenen Import benachrichtigt.
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == 7 && n.Type == NotificationType.ChessableImportFailed));
    }

    [Fact]
    public async Task RunAsync_NoCachedPgn_PollsFetchJobThenImports()
    {
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "u7", PasswordHash = "x" });
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = 7, EncryptedBearer = _encryption.Encrypt("bearer"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        var imp = new ChessableImport
        {
            UserId = 7, Bid = "b1", CourseName = "", Target = "repertoire",
            Status = "running", Phase = "queued", CreatedAt = DateTime.UtcNow
        };
        _db.ChessableImports.Add(imp);
        await _db.SaveChangesAsync();

        var svc = BuildSvc(new ScriptedHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/course/start"))
                return JsonOk(new { jobId = "job-1" });
            return JsonOk(new
            {
                status = "completed", chaptersDone = 2, chaptersTotal = 2, linesDone = 5,
                chapterCount = 2, lineCount = 5, courseName = "Real Name", pgn = "1. e4 e5 *", error = (string?)null
            });
        }));

        await svc.RunAsync(imp.Id);

        var reloaded = await _db.ChessableImports.FindAsync(imp.Id);
        Assert.Equal("completed", reloaded!.Status);
        Assert.Equal(5, reloaded.LinesDone);
        Assert.Equal("Real Name", reloaded.CourseName); // aus dem Fetch übernommen
        Assert.NotNull(reloaded.ResultId);
        Assert.Equal(1, await _db.Repertoires.CountAsync(r => r.UserId == 7));
    }

    [Fact]
    public async Task RunAsync_ShutdownDuringFetch_LeavesImportRunning_NotFailed()
    {
        // Regression: ein durch App-Shutdown (stoppingToken) abgebrochener Import wurde faelschlich
        // als "failed" behandelt — und FailAsync konnte mit dem schon gecancelten Token nicht mehr
        // speichern → OperationCanceledException blubberte als "Background task failed" (Error) hoch.
        // Erwartung: KEIN Fehler, Job bleibt "running" → ChessableImportResumeService setzt ihn fort.
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "u7", PasswordHash = "x" });
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = 7, EncryptedBearer = _encryption.Encrypt("bearer"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        var imp = new ChessableImport
        {
            UserId = 7, Bid = "b1", CourseName = "", Target = "repertoire",
            Status = "running", Phase = "queued", CreatedAt = DateTime.UtcNow
        };
        _db.ChessableImports.Add(imp);
        await _db.SaveChangesAsync();

        using var cts = new CancellationTokenSource();
        // Simuliert Shutdown mitten in der Hol-Phase: Token canceln + Abbruch werfen.
        var svc = BuildSvc(new ScriptedHandler(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }));

        // Darf NICHT werfen (sauberer Shutdown, kein Background-Task-Fehler).
        await svc.RunAsync(imp.Id, cts.Token);

        var reloaded = await _db.ChessableImports.FindAsync(imp.Id);
        Assert.Equal("running", reloaded!.Status);   // NICHT "failed"
        Assert.Null(reloaded.Error);
    }

    // --- Fortschritts-bewusster Fetch-Timeout (Regression: fixe 15-min-Grenze killte langsame,
    //     aber gesunde Abrufe großer Kurse → TimeoutException, prod-Importe 99/100) ---

    private void SeedFetchUserAndImport(int attempts, out int importId)
    {
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "u7", PasswordHash = "x" });
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = 7, EncryptedBearer = _encryption.Encrypt("bearer"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        var imp = new ChessableImport
        {
            UserId = 7, Bid = "b1", CourseName = "", Target = "repertoire",
            Status = "running", Phase = "queued", Attempts = attempts, CreatedAt = DateTime.UtcNow
        };
        _db.ChessableImports.Add(imp);
        _db.SaveChanges();
        importId = imp.Id;
    }

    private static HttpResponseMessage FetchingStatic() => JsonOk(new
    {
        status = "fetching", chaptersDone = 1, chaptersTotal = 5, linesDone = 1,
        chapterCount = 0, lineCount = 0, courseName = (string?)null, pgn = (string?)null, error = (string?)null
    });

    [Fact]
    public async Task RunAsync_FetchStallsBelowMaxAttempts_AutoReEnqueues()
    {
        SeedFetchUserAndImport(attempts: 0, out var id);
        // Statischer „fetching"-Fortschritt → Stillstand. Kein harter Fehler, sondern Resume.
        var svc = BuildSvc(new ScriptedHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/course/start") ? JsonOk(new { jobId = "job-1" }) : FetchingStatic()));
        svc.PollDelayMs = 0; svc.FetchStallPolls = 2; svc.FetchMaxPolls = 1000;

        await svc.RunAsync(id);

        var reloaded = await _db.ChessableImports.FindAsync(id);
        Assert.Equal("running", reloaded!.Status);   // NICHT failed
        Assert.Equal(1, reloaded.Attempts);
        Assert.Equal(1, _queue.Count);               // automatisch neu eingereiht (Auto-Restart)
    }

    [Fact]
    public async Task RunAsync_SlowButProgressing_DoesNotTimeOut()
    {
        SeedFetchUserAndImport(attempts: 0, out var id);
        // Fortschritt über viele Polls hinweg (> FetchStallPolls): der Stall-Zähler wird durch
        // jeden echten Fortschritt zurückgesetzt → KEIN Timeout, am Ende completed.
        int n = 0;
        var svc = BuildSvc(new ScriptedHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/course/start")) return JsonOk(new { jobId = "job-1" });
            n++;
            return n < 5
                ? JsonOk(new { status = "fetching", chaptersDone = 1, chaptersTotal = 5, linesDone = n, chapterCount = 0, lineCount = 0, courseName = (string?)null, pgn = (string?)null, error = (string?)null })
                : JsonOk(new { status = "completed", chaptersDone = 5, chaptersTotal = 5, linesDone = 5, chapterCount = 5, lineCount = 5, courseName = "CN", pgn = "1. e4 e5 *", error = (string?)null });
        }));
        svc.PollDelayMs = 0; svc.FetchStallPolls = 2; svc.FetchMaxPolls = 1000; // 5 Polls > Stall-Fenster 2

        await svc.RunAsync(id);

        var reloaded = await _db.ChessableImports.FindAsync(id);
        Assert.Equal("completed", reloaded!.Status);
        Assert.Equal(0, _queue.Count);
    }

    [Fact]
    public async Task RunAsync_FetchStallsAtMaxAttempts_Fails()
    {
        SeedFetchUserAndImport(attempts: ChessableImportService.MaxAttempts - 1, out var id);
        var svc = BuildSvc(new ScriptedHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/course/start") ? JsonOk(new { jobId = "job-1" }) : FetchingStatic()));
        svc.PollDelayMs = 0; svc.FetchStallPolls = 2; svc.FetchMaxPolls = 1000;

        await svc.RunAsync(id);

        var reloaded = await _db.ChessableImports.FindAsync(id);
        Assert.Equal("failed", reloaded!.Status); // letzter Versuch erschöpft → terminal
        Assert.Equal(0, _queue.Count);            // kein weiterer Resume
    }

    [Fact]
    public async Task EnqueueReimport_CourseInOwnerLibrary_Enqueues()
    {
        var user = new AppUser { Username = "u", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = user.Id, EncryptedBearer = _encryption.Encrypt("bearer"),
            CachedCoursesJson = "[{\"bid\":\"128648\",\"name\":\"Mine\"}]",   // Kurs in der Bibliothek
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        // Eigentum via Cache → kein /courses-Call; nur der /cached-Lane-Check fragt piratechess.
        var svc = BuildSvc(new ScriptedHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/cached") ? JsonOk(new { cached = false }) : JsonOk(new { })));

        var id = await svc.EnqueueReimportAsync(user.Id, "128648", "repertoire", "Mine");

        Assert.NotNull(id);
        Assert.True(await _db.ChessableImports.AnyAsync(i => i.Bid == "128648" && i.UserId == user.Id));
    }

    [Fact]
    public async Task EnqueueReimport_CourseNotInOwnerLibrary_ReturnsNull_NoImport()
    {
        var user = new AppUser { Username = "u", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        _db.ChessableCredentials.Add(new ChessableCredential
        {
            UserId = user.Id, EncryptedBearer = _encryption.Encrypt("bearer"),
            CachedCoursesJson = "[{\"bid\":\"111\",\"name\":\"Mine\"}]",   // fremder bid 999 NICHT enthalten
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        // Fallback-Frischabruf /courses liefert ebenfalls NICHT 999 → Re-Fetch muss verweigert werden
        // (schließt den Reprocess-Bypass über ein selbst angelegtes Repertoire mit fremdem bid).
        var svc = BuildSvc(new ScriptedHandler(req =>
            req.RequestUri!.AbsolutePath == "/api/chessable/direct/courses"
                ? JsonOk(new[] { new { bid = "111", name = "Mine" } })
                : JsonOk(new { })));

        var id = await svc.EnqueueReimportAsync(user.Id, "999", "repertoire", "Geklaut");

        Assert.Null(id);
        Assert.False(await _db.ChessableImports.AnyAsync(i => i.Bid == "999"));
    }

    private static HttpResponseMessage JsonOk(object payload) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };

    private sealed class FakeQueue : IBackgroundTaskQueue
    {
        public int Count { get; private set; }
        public ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
        {
            Count++;
            return ValueTask.CompletedTask;
        }
        public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_fn(request));
    }
}
