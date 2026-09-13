using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class RepertoireCleanupServiceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("Connection refused");
    }

    private RepertoireCleanupService Svc() => new(_db,
        new ChessableProxyService(new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://pc:8080") }),
        NullLogger<RepertoireCleanupService>.Instance);

    private static string G(string white, string moves, string? oid = null) =>
        $"[Event \"C\"]\n[White \"{white}\"]\n[Result \"*\"]\n" + (oid != null ? $"[ChessableOid \"{oid}\"]\n" : "") + "\n" + moves + "\n\n";

    private async Task<RepertoireFile> SeedAsync(string pgn)
    {
        _db.Repertoires.Add(new Repertoire { Id = 1, UserId = 7, Name = "Kurs", ChessableCourseId = "123" });
        var file = new RepertoireFile { Id = 10, RepertoireId = 1, FileName = "chessable-123.pgn", PgnContent = pgn };
        _db.RepertoireFiles.Add(file);
        await _db.SaveChangesAsync();
        return file;
    }

    [Fact]
    public async Task DryRun_ReportsTheCopies_ButWritesNothing()
    {
        var pgn = G("2", "1. e4 e5 *") + G("2", "1. e4 e5 *", "100");
        await SeedAsync(pgn);

        var report = await Svc().CleanupAllAsync(apply: false);

        Assert.Equal(1, report.Changed);
        Assert.Contains(report.ChangedFiles.Single().Actions, a => a.Action == "ausgeblendet");
        var file = await _db.RepertoireFiles.AsNoTracking().SingleAsync();
        Assert.Equal(pgn, file.PgnContent);
        Assert.Equal(0, file.CleanupVersion);
    }

    [Fact]
    public async Task Apply_HidesTheCopy_AndRemembersTheRuleVersion_ThenSkipsTheFile()
    {
        await SeedAsync(G("2", "1. e4 e5 *") + G("2", "1. e4 e5 *", "100"));

        var first = await Svc().CleanupAllAsync(apply: true);
        var file = await _db.RepertoireFiles.AsNoTracking().SingleAsync();
        var second = await Svc().CleanupAllAsync(apply: true);

        Assert.Equal(1, first.Changed);
        Assert.Contains("[RookHubHidden ", file.PgnContent);
        Assert.Equal(RepertoirePgnCleanup.CurrentVersion, file.CleanupVersion);
        Assert.Equal(0, second.Files);   // Regelstand gemerkt → nicht erneut gelesen
    }

    [Fact]
    public async Task PiratechessUnreachable_ForAnAmbiguousOid_LeavesTheFileForTheNextStart()
    {
        await SeedAsync(G("A", "1. e4 e5 *", "5") + G("B", "1. d4 d5 *", "5"));

        var report = await Svc().CleanupAllAsync(apply: true);

        Assert.Equal(1, report.WaitingForPiratechess);
        Assert.Equal(0, (await _db.RepertoireFiles.AsNoTracking().SingleAsync()).CleanupVersion);
    }
}
