using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class RepertoireTrainingServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RepertoireTrainingService _service;

    public RepertoireTrainingServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new RepertoireTrainingService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync(string username = "u")
    {
        var user = new AppUser { Username = username, Email = $"{username}@x.de", PasswordHash = "h", Profile = new UserProfile() };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> CreateRepertoireAsync(int userId)
    {
        var r = new Repertoire { UserId = userId, Name = "Französisch" };
        _db.Repertoires.Add(r);
        await _db.SaveChangesAsync();
        return r.Id;
    }

    private static ReviewCardRequest Req(string key, string move, int grade) =>
        new() { CardKey = key, ExpectedMove = move, Grade = grade };

    [Fact]
    public async Task GetCards_ForeignRepertoire_ReturnsNull()
    {
        var owner = await CreateUserAsync("owner");
        var other = await CreateUserAsync("other");
        var repId = await CreateRepertoireAsync(owner);

        Assert.Null(await _service.GetCardsAsync(other, repId, default));
    }

    [Fact]
    public async Task Review_ForeignRepertoire_ReturnsNull()
    {
        var owner = await CreateUserAsync("owner");
        var other = await CreateUserAsync("other");
        var repId = await CreateRepertoireAsync(owner);

        Assert.Null(await _service.ReviewAsync(other, repId, Req("fen1", "e6", 2), default));
    }

    [Fact]
    public async Task Review_FirstGood_CreatesCardWithOneDayInterval()
    {
        var u = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(u);

        var dto = await _service.ReviewAsync(u, repId, Req("fen1", "e6", 2), default);

        Assert.NotNull(dto);
        Assert.Equal(1, dto!.Reps);
        Assert.Equal(1, dto.IntervalDays);
        Assert.Equal("e6", dto.ExpectedMove);
        Assert.True(dto.DueAt > DateTime.UtcNow.AddHours(20));
        Assert.Single(await _db.RepertoireCardStates.ToListAsync());
    }

    [Fact]
    public async Task Review_GoodProgression_FollowsSm2Steps()
    {
        var u = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(u);

        await _service.ReviewAsync(u, repId, Req("fen1", "e6", 2), default);   // rep1 → 1d
        var r2 = await _service.ReviewAsync(u, repId, Req("fen1", "e6", 2), default);   // rep2 → 6d

        Assert.Equal(2, r2!.Reps);
        Assert.Equal(6, r2.IntervalDays);
        // Idempotent über die Stellung: weiterhin genau EINE Karte.
        Assert.Single(await _db.RepertoireCardStates.ToListAsync());
    }

    [Fact]
    public async Task Review_Again_ResetsRepsAndLowersEaseAndDueSoon()
    {
        var u = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(u);
        await _service.ReviewAsync(u, repId, Req("fen1", "e6", 2), default);   // good → reps 1
        await _service.ReviewAsync(u, repId, Req("fen1", "e6", 2), default);   // good → reps 2

        var again = await _service.ReviewAsync(u, repId, Req("fen1", "e6", 0), default);

        Assert.Equal(0, again!.Reps);
        Assert.Equal(1, again.Lapses);
        Assert.True(again.Ease < 2.5);
        Assert.True(again.DueAt < DateTime.UtcNow.AddHours(1));   // Relearn in Kürze
    }

    [Fact]
    public async Task Review_Hard_ForTolerated_LowersEaseSmallStep()
    {
        var u = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(u);

        var hard = await _service.ReviewAsync(u, repId, Req("fen1", "e6", 1), default);

        Assert.NotNull(hard);
        Assert.True(hard!.Ease < 2.5);
        Assert.True(hard.IntervalDays < 1);   // erster „hard" → kurzer Schritt
    }

    [Fact]
    public async Task GetCards_ReturnsOwnCardsOnly()
    {
        var u = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(u);
        await _service.ReviewAsync(u, repId, Req("fenA", "e6", 2), default);
        await _service.ReviewAsync(u, repId, Req("fenB", "d5", 2), default);

        var cards = await _service.GetCardsAsync(u, repId, default);

        Assert.NotNull(cards);
        Assert.Equal(2, cards!.Count);
        Assert.Contains(cards, c => c.CardKey == "fenA" && c.ExpectedMove == "e6");
    }

    [Fact]
    public async Task Reset_ForeignRepertoire_ReturnsNullAndKeepsData()
    {
        var owner = await CreateUserAsync("owner");
        var other = await CreateUserAsync("other");
        var repId = await CreateRepertoireAsync(owner);
        await _service.ReviewAsync(owner, repId, Req("fenA", "e6", 2));

        var deleted = await _service.ResetAsync(other, repId);
        Assert.Null(deleted);
        Assert.Single(await _service.GetCardsAsync(owner, repId) ?? new());
    }

    [Fact]
    public async Task Reset_OwnRepertoire_RemovesAllCards()
    {
        var owner = await CreateUserAsync("owner");
        var repId = await CreateRepertoireAsync(owner);
        await _service.ReviewAsync(owner, repId, Req("fenA", "e6", 2));
        await _service.ReviewAsync(owner, repId, Req("fenB", "c5", 2));

        var deleted = await _service.ResetAsync(owner, repId);
        Assert.Equal(2, deleted);
        Assert.Empty((await _service.GetCardsAsync(owner, repId))!);
    }

    [Fact]
    public async Task Reset_DoesNotTouchOtherRepertoiresOrUsers()
    {
        var owner = await CreateUserAsync("owner");
        var other = await CreateUserAsync("other");
        var repA = await CreateRepertoireAsync(owner);
        var repB = await CreateRepertoireAsync(owner);
        var repC = await CreateRepertoireAsync(other);
        await _service.ReviewAsync(owner, repA, Req("fx", "e4", 2));
        await _service.ReviewAsync(owner, repB, Req("fx", "d4", 2));
        await _service.ReviewAsync(other, repC, Req("fx", "e4", 2));

        Assert.Equal(1, await _service.ResetAsync(owner, repA));
        Assert.Empty((await _service.GetCardsAsync(owner, repA))!);
        Assert.Single((await _service.GetCardsAsync(owner, repB))!);
        Assert.Single((await _service.GetCardsAsync(other, repC))!);
    }
}
