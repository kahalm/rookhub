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
        return new ChessableImportService(_db, _encryption, proxy, _repertoires, new PgnImportService(_db),
            NullLogger<ChessableImportService>.Instance);
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
    }

    [Fact]
    public async Task RunAsync_AlreadyCompleted_IsNoOp()
    {
        var imp = await SeedImportAsync("repertoire", status: "completed");
        await _svc.RunAsync(imp.Id);
        Assert.Equal(0, await _db.Repertoires.CountAsync());
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

    private static HttpResponseMessage JsonOk(object payload) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_fn(request));
    }
}
