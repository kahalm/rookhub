using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Scoresheet;

namespace RookHub.Api.Tests.Scoresheet;

/// <summary>Löschweg und Fristen der Formular-Fotos (A6-016): geleert wird Foto + Lesung, die Zeile bleibt fürs Kontingent.
/// Feste Uhr; die Fotos liegen in der Datenbank (InMemory statt Dateisystem).</summary>
public class ScoresheetPhotoRetentionTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 25, 3, 0, 0, DateTimeKind.Utc);
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private static readonly byte[] Jpeg = { 1, 2, 3 };

    private int Scan(int? userId, ScoresheetScanStatus status, int ageDays, string? purpose = null, int? gameId = null, long cost = 0)
    {
        var s = new ScoresheetScan
        {
            UserId = userId, Status = status, Purpose = purpose, SavedGameId = gameId, Photo = Jpeg, FileName = "blatt.jpg",
            TranscriptionJson = "{\"white\":\"Gegner, Name\"}", ResolutionJson = "{}", AccessKey = userId == null ? "geheim" : null,
            CreatedAt = Now.AddDays(-ageDays), FinishedAt = status is ScoresheetScanStatus.Done or ScoresheetScanStatus.Failed ? Now.AddDays(-ageDays) : null,
            CostMicroUsd = cost, PageCount = 2,
        };
        s.Pages.Add(new ScoresheetScanPage { Page = 2, Photo = Jpeg });
        s.Views.Add(new ScoresheetScanView { Page = 1, View = 1, Photo = Jpeg });
        _db.ScoresheetScans.Add(s);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return s.Id;
    }

    private ScoresheetScan Row(int id) => _db.ScoresheetScans.AsNoTracking().Single(s => s.Id == id);
    private bool Cleared(int id) => Row(id) is { Photo.Length: 0, TranscriptionJson: null, ResolutionJson: null, AccessKey: null } r
        && r.FileName == ScoresheetScanService.DiscardedMark
        && !_db.ScoresheetScanPages.Any(p => p.ScoresheetScanId == id) && !_db.ScoresheetScanViews.Any(v => v.ScoresheetScanId == id);

    [Fact]
    public async Task DeleteOwn_Failed_ClearsPhotoAndReading_RowStaysWithItsCost()
    {
        var id = Scan(7, ScoresheetScanStatus.Failed, 1, cost: 4200);
        Assert.Null(await ScoresheetPhotoRetention.DeleteOwnAsync(_db, 7, id));
        Assert.True(Cleared(id));
        Assert.Equal((ScoresheetScanStatus.Failed, 4200L), (Row(id).Status, Row(id).CostMicroUsd));   // Kontingent/Budget zählen weiter
        Assert.Equal("notFound", await ScoresheetPhotoRetention.DeleteOwnAsync(_db, 7, id));            // schon gelöscht
    }

    [Fact]
    public async Task DeleteOwn_Done_KeepsTheGame_ButNotItsPhoto()
    {
        _db.SavedGames.Add(new SavedGame { Id = 50, UserId = 7, Pgn = "1. e4 *" });
        await _db.SaveChangesAsync();
        var id = Scan(7, ScoresheetScanStatus.Done, 1, gameId: 50);
        Assert.Null(await ScoresheetPhotoRetention.DeleteOwnAsync(_db, 7, id));
        Assert.True(Cleared(id));
        Assert.Null(Row(id).SavedGameId);
        Assert.True(await _db.SavedGames.AnyAsync(g => g.Id == 50));
    }

    [Fact]
    public async Task DeleteOwn_Foreign_League_Busy_AreRefused()
    {
        var foreign = Scan(8, ScoresheetScanStatus.Failed, 1);
        var league = Scan(7, ScoresheetScanStatus.Done, 1, ScoresheetScan.PurposeLeague);
        var pending = Scan(7, ScoresheetScanStatus.Pending, 1);
        var running = Scan(7, ScoresheetScanStatus.Running, 1);
        Assert.Equal("notFound", await ScoresheetPhotoRetention.DeleteOwnAsync(_db, 7, foreign));
        Assert.Equal("notFound", await ScoresheetPhotoRetention.DeleteOwnAsync(_db, 7, league));
        Assert.Equal("busy", await ScoresheetPhotoRetention.DeleteOwnAsync(_db, 7, pending));
        Assert.Equal("busy", await ScoresheetPhotoRetention.DeleteOwnAsync(_db, 7, running));
        Assert.All(new[] { foreign, league, pending, running }, id => Assert.Equal(3, Row(id).Photo.Length));
    }

    [Fact]
    public async Task Prune_FailedAfter30_OpenAnonymousLeagueAfter14_RestStays()
    {
        var failedOld = Scan(7, ScoresheetScanStatus.Failed, 31);
        var failedYoung = Scan(7, ScoresheetScanStatus.Failed, 29);
        var anonOpenOld = Scan(null, ScoresheetScanStatus.Done, 15, ScoresheetScan.PurposeLeague);
        var anonPendingOld = Scan(null, ScoresheetScanStatus.Pending, 15, ScoresheetScan.PurposeLeague);
        var anonRunning = Scan(null, ScoresheetScanStatus.Running, 15, ScoresheetScan.PurposeLeague);
        var anonOpenYoung = Scan(null, ScoresheetScanStatus.Done, 13, ScoresheetScan.PurposeLeague);
        var accountLeagueOld = Scan(7, ScoresheetScanStatus.Done, 60, ScoresheetScan.PurposeLeague);   // Verwalter prüfen noch
        var ownGameOld = Scan(7, ScoresheetScanStatus.Done, 400);                                      // Original der Partie
        _db.ScoresheetScanArchives.AddRange(
            new ScoresheetScanArchive { ScoresheetScanId = ownGameOld, Page = 1, Photo = Jpeg, ArchivedAt = Now.AddDays(-366), ExpiresAt = Now.AddDays(-1) },
            new ScoresheetScanArchive { ScoresheetScanId = ownGameOld, Page = 2, Photo = Jpeg, ArchivedAt = Now.AddDays(-10), ExpiresAt = Now.AddDays(355) });
        await _db.SaveChangesAsync();

        var (cleared, archive) = await ScoresheetPhotoRetention.PruneAsync(_db, Now, new RetentionOptions());

        Assert.Equal((3, 1), (cleared, archive));
        Assert.All(new[] { failedOld, anonOpenOld, anonPendingOld }, id => Assert.True(Cleared(id)));
        Assert.All(new[] { failedYoung, anonRunning, anonOpenYoung, accountLeagueOld, ownGameOld }, id => Assert.Equal(3, Row(id).Photo.Length));
        Assert.Equal(2, _db.ScoresheetScanArchives.Single().Page);
        Assert.Equal((0, 0), await ScoresheetPhotoRetention.PruneAsync(_db, Now, new RetentionOptions()));   // idempotent
    }

    [Fact]
    public async Task Prune_RespectsConfiguredDays()
    {
        var id = Scan(7, ScoresheetScanStatus.Failed, 10);
        Assert.Equal(0, (await ScoresheetPhotoRetention.PruneAsync(_db, Now, new RetentionOptions())).Cleared);
        Assert.Equal(1, (await ScoresheetPhotoRetention.PruneAsync(_db, Now, new RetentionOptions { ScoresheetFailedDays = 7 })).Cleared);
        Assert.True(Cleared(id));
    }
}
