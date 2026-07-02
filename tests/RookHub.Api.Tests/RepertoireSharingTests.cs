using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// „Repertoire mit ausgewählten Personen teilen" (analog Kurs): der Besitzer gibt ein Repertoire an
/// befreundete Nutzer frei; diese sehen/öffnen/trainieren es (eigener SR-Fortschritt), können es aber
/// nicht bearbeiten/löschen/weiterteilen.
/// </summary>
public class RepertoireSharingTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RepertoireService _svc;
    private readonly RepertoireTrainingService _training;

    public RepertoireSharingTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        var notifications = new NotificationService(_db);
        _svc = new RepertoireService(_db, new RepertoireAnalyzeService(_db, new MemoryCache(new MemoryCacheOptions())),
            new FriendService(_db, notifications), notifications);
        _training = new RepertoireTrainingService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedUserAsync(int id, string username)
    {
        _db.AppUsers.Add(new AppUser { Id = id, Username = username, PasswordHash = "x" });
        await _db.SaveChangesAsync();
    }

    private async Task MakeFriendsAsync(int a, int b)
    {
        _db.Friendships.Add(new Friendship { RequesterId = a, AddresseeId = b, Status = FriendshipStatus.Accepted });
        await _db.SaveChangesAsync();
    }

    private async Task<Repertoire> SeedRepertoireAsync(int ownerUserId)
    {
        var rep = new Repertoire { UserId = ownerUserId, Name = "My Repertoire" };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        _db.RepertoireFiles.Add(new RepertoireFile { RepertoireId = rep.Id, FileName = "l.pgn", PgnContent = "1. e4 e5 *", FileSize = 10 });
        await _db.SaveChangesAsync();
        return rep;
    }

    [Fact]
    public async Task Share_WithFriend_GrantsAccess_AndNotifies()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var rep = await SeedRepertoireAsync(1);

        var res = await _svc.ShareAsync(userId: 1, rep.Id, new List<int> { 2 }, isAdmin: false);

        Assert.Equal(1, res.Shared);
        Assert.Empty(res.Skipped);
        Assert.True(await _svc.CanAccessAsync(rep.Id, userId: 2));
        var list = await _svc.GetAllAsync(userId: 2);
        var shared = list.Single(r => r.Id == rep.Id);
        Assert.True(shared.IsShared);
        Assert.Equal("owner", shared.SharedByUsername);
        Assert.Equal(1, shared.FileCount);
        Assert.True(await _db.Notifications.AnyAsync(n => n.UserId == 2 && n.Type == NotificationType.RepertoireShared));
    }

    [Fact]
    public async Task Share_WithNonFriend_Skipped_NotFriends()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "stranger");
        var rep = await SeedRepertoireAsync(1);

        var res = await _svc.ShareAsync(userId: 1, rep.Id, new List<int> { 2 }, isAdmin: false);

        Assert.Equal(0, res.Shared);
        Assert.Equal("not_friends", res.Skipped.Single().Reason);
        Assert.False(await _svc.CanAccessAsync(rep.Id, userId: 2));
    }

    [Fact]
    public async Task Share_Admin_CanShareWithNonFriend()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "stranger");
        var rep = await SeedRepertoireAsync(1);

        var res = await _svc.ShareAsync(userId: 1, rep.Id, new List<int> { 2 }, isAdmin: true);

        Assert.Equal(1, res.Shared);
        Assert.True(await _svc.CanAccessAsync(rep.Id, userId: 2));
    }

    [Fact]
    public async Task Share_Duplicate_And_Self_And_Unknown_Skipped()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var rep = await SeedRepertoireAsync(1);
        await _svc.ShareAsync(1, rep.Id, new List<int> { 2 }, false);

        var res = await _svc.ShareAsync(1, rep.Id, new List<int> { 2, 1, 999 }, isAdmin: true);
        Assert.Equal(0, res.Shared);
        Assert.Contains(res.Skipped, s => s.UserId == 2 && s.Reason == "duplicate");
        Assert.Contains(res.Skipped, s => s.UserId == 1 && s.Reason == "self");
        Assert.Contains(res.Skipped, s => s.UserId == 999 && s.Reason == "not_found");
        Assert.Single(_db.RepertoireShares);
    }

    [Fact]
    public async Task Share_ByNonOwner_Throws()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "other");
        await SeedUserAsync(3, "friend");
        await MakeFriendsAsync(2, 3);
        var rep = await SeedRepertoireAsync(1);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _svc.ShareAsync(userId: 2, rep.Id, new List<int> { 3 }, isAdmin: false));
        Assert.Empty(_db.RepertoireShares);
    }

    [Fact]
    public async Task Recipient_CanReadDetail_AndCombinedPgn_ButNotOwner()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var rep = await SeedRepertoireAsync(1);
        await _svc.ShareAsync(1, rep.Id, new List<int> { 2 }, false);

        var detail = await _svc.GetByIdAsync(rep.Id, userId: 2);
        Assert.False(detail.IsOwner);
        Assert.Single(detail.Files);
        var pgn = await _svc.GetCombinedPgnAsync(rep.Id, userId: 2);
        Assert.Contains("e4", pgn);

        // Besitzer sieht IsOwner=true.
        Assert.True((await _svc.GetByIdAsync(rep.Id, userId: 1)).IsOwner);
    }

    [Fact]
    public async Task Recipient_CanTrain_SharedRepertoire()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var rep = await SeedRepertoireAsync(1);
        await _svc.ShareAsync(1, rep.Id, new List<int> { 2 }, false);

        // Empfänger kann Config lesen + eine Linie reviewen (eigener SR-Fortschritt).
        Assert.NotNull(await _training.GetConfigAsync(userId: 2, rep.Id));
        var state = await _training.ReviewLineAsync(userId: 2, rep.Id, new LineReviewRequest { LineKey = "d4d5", Correct = true });
        Assert.NotNull(state);
        // Der Fortschritt gehört User 2, nicht dem Besitzer.
        Assert.True(await _db.RepertoireCardStates.AnyAsync(c => c.UserId == 2 && c.RepertoireId == rep.Id));
        Assert.False(await _db.RepertoireCardStates.AnyAsync(c => c.UserId == 1 && c.RepertoireId == rep.Id));
    }

    [Fact]
    public async Task NonRecipient_CannotTrain()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(3, "outsider");
        var rep = await SeedRepertoireAsync(1);

        Assert.Null(await _training.GetConfigAsync(userId: 3, rep.Id));
        Assert.Null(await _training.ReviewLineAsync(userId: 3, rep.Id, new LineReviewRequest { LineKey = "d4d5", Correct = true }));
    }

    [Fact]
    public async Task GetShareRecipients_OwnerOnly()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var rep = await SeedRepertoireAsync(1);
        await _svc.ShareAsync(1, rep.Id, new List<int> { 2 }, false);

        Assert.Equal(2, (await _svc.GetShareRecipientsAsync(userId: 1, rep.Id)).Single().UserId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.GetShareRecipientsAsync(userId: 2, rep.Id));
    }

    [Fact]
    public async Task Unshare_RevokesAccess()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var rep = await SeedRepertoireAsync(1);
        await _svc.ShareAsync(1, rep.Id, new List<int> { 2 }, false);

        await _svc.UnshareAsync(userId: 1, rep.Id, recipientId: 2);
        Assert.Empty(_db.RepertoireShares);
        Assert.False(await _svc.CanAccessAsync(rep.Id, userId: 2));
    }

    [Fact]
    public async Task Delete_AlsoRemovesShares()
    {
        await SeedUserAsync(1, "owner");
        await SeedUserAsync(2, "friend");
        await MakeFriendsAsync(1, 2);
        var rep = await SeedRepertoireAsync(1);
        await _svc.ShareAsync(1, rep.Id, new List<int> { 2 }, false);
        Assert.Single(_db.RepertoireShares);

        await _svc.DeleteAsync(rep.Id, userId: 1);
        Assert.Empty(_db.RepertoireShares);
    }
}
