using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class ChallengeControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ChallengeService _service;
    private readonly ChallengeController _controller;

    public ChallengeControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new ChallengeService(_db, new FriendService(_db, new NotificationService(_db)), new NotificationService(_db));
        _controller = new ChallengeController(_service);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
    }

    private async Task<AppUser> CreateUserAsync(string username)
    {
        var user = new AppUser { Username = username, Email = $"{username}@test.com", PasswordHash = "hash", Profile = new UserProfile() };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task MakeFriendsAsync(int a, int b)
    {
        _db.Friendships.Add(new Friendship { RequesterId = a, AddresseeId = b, Status = FriendshipStatus.Accepted });
        await _db.SaveChangesAsync();
    }

    private async Task<Puzzle> CreatePuzzleAsync(string lichessId = "p1", int rating = 1600)
    {
        var p = new Puzzle { LichessId = lichessId, Fen = "fen", Moves = "e2e4", Rating = rating, Themes = "fork" };
        _db.Puzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    private async Task<BookPuzzle> CreateBookPuzzleAsync(string lineId = "b1", int rating = 1800, string? title = "Kapitel 1", string? tags = "pin")
    {
        var p = new BookPuzzle { LineId = lineId, BookFileName = "book.pgn", Fen = "fen", Moves = "e2e4", BookRating = rating, Title = title, Tags = tags };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    private static ChallengeBatchResultDto Batch(ActionResult<ChallengeBatchResultDto> r)
        => Assert.IsType<ChallengeBatchResultDto>(Assert.IsType<OkObjectResult>(r.Result).Value);

    // ---- Create (Batch) ----

    [Fact]
    public async Task Create_CreatesChallenge_WhenFriends()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        await MakeFriendsAsync(me.Id, friend.Id);
        var puzzle = await CreatePuzzleAsync();

        SetUser(me.Id);
        var result = await _controller.Create(new CreateChallengeBatchDto { ToUserIds = new() { friend.Id }, PuzzleId = puzzle.Id });

        Assert.Equal(1, Batch(result).Sent);
        var c = Assert.Single(_db.PuzzleChallenges);
        Assert.Equal(ChallengeStatus.Pending, c.Status);
        Assert.Equal(me.Id, c.FromUserId);
        Assert.Equal(friend.Id, c.ToUserId);
        Assert.Equal(PuzzleSource.Standard, c.Source);
    }

    [Fact]
    public async Task Create_SendsToMultipleFriends_AtOnce()
    {
        var me = await CreateUserAsync("me");
        var a = await CreateUserAsync("a");
        var b = await CreateUserAsync("b");
        await MakeFriendsAsync(me.Id, a.Id);
        await MakeFriendsAsync(me.Id, b.Id);
        var puzzle = await CreatePuzzleAsync();

        SetUser(me.Id);
        var result = await _controller.Create(new CreateChallengeBatchDto { ToUserIds = new() { a.Id, b.Id }, PuzzleId = puzzle.Id });

        Assert.Equal(2, Batch(result).Sent);
        Assert.Equal(2, _db.PuzzleChallenges.Count());
    }

    [Fact]
    public async Task Create_SkipsNonFriends_WithReason()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        var stranger = await CreateUserAsync("stranger");
        await MakeFriendsAsync(me.Id, friend.Id);
        var puzzle = await CreatePuzzleAsync();

        SetUser(me.Id);
        var result = await _controller.Create(new CreateChallengeBatchDto { ToUserIds = new() { friend.Id, stranger.Id }, PuzzleId = puzzle.Id });

        var batch = Batch(result);
        Assert.Equal(1, batch.Sent);
        var skip = Assert.Single(batch.Skipped);
        Assert.Equal(stranger.Id, skip.ToUserId);
        Assert.Equal("not_friends", skip.Reason);
        Assert.Single(_db.PuzzleChallenges);
    }

    [Fact]
    public async Task Create_SkipsDuplicatePending_WithReason()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        await MakeFriendsAsync(me.Id, friend.Id);
        var puzzle = await CreatePuzzleAsync();

        SetUser(me.Id);
        await _controller.Create(new CreateChallengeBatchDto { ToUserIds = new() { friend.Id }, PuzzleId = puzzle.Id });
        var second = await _controller.Create(new CreateChallengeBatchDto { ToUserIds = new() { friend.Id }, PuzzleId = puzzle.Id });

        var batch = Batch(second);
        Assert.Equal(0, batch.Sent);
        Assert.Equal("duplicate", Assert.Single(batch.Skipped).Reason);
        Assert.Single(_db.PuzzleChallenges);
    }

    [Fact]
    public async Task Create_Returns404_WhenPuzzleMissing()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        await MakeFriendsAsync(me.Id, friend.Id);

        SetUser(me.Id);
        var result = await _controller.Create(new CreateChallengeBatchDto { ToUserIds = new() { friend.Id }, PuzzleId = 9999 });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_CreatesBookChallenge_WhenSourceBook()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        await MakeFriendsAsync(me.Id, friend.Id);
        var book = await CreateBookPuzzleAsync();

        SetUser(me.Id);
        var result = await _controller.Create(new CreateChallengeBatchDto { ToUserIds = new() { friend.Id }, PuzzleId = book.Id, Source = PuzzleSource.Book });

        Assert.Equal(1, Batch(result).Sent);
        var c = Assert.Single(_db.PuzzleChallenges);
        Assert.Equal(PuzzleSource.Book, c.Source);
        Assert.Equal(book.Id, c.PuzzleId);
    }

    [Fact]
    public async Task Create_Returns404_WhenBookPuzzleMissing()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        await MakeFriendsAsync(me.Id, friend.Id);

        SetUser(me.Id);
        var result = await _controller.Create(new CreateChallengeBatchDto { ToUserIds = new() { friend.Id }, PuzzleId = 9999, Source = PuzzleSource.Book });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_DedupesAndIgnoresSelf_AcrossRecipients()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        await MakeFriendsAsync(me.Id, friend.Id);
        var puzzle = await CreatePuzzleAsync();

        SetUser(me.Id);
        // friend doppelt + man selbst → 1 gesendet, self übersprungen.
        var result = await _controller.Create(new CreateChallengeBatchDto { ToUserIds = new() { friend.Id, friend.Id, me.Id }, PuzzleId = puzzle.Id });

        var batch = Batch(result);
        Assert.Equal(1, batch.Sent);
        Assert.Contains(batch.Skipped, s => s.ToUserId == me.Id && s.Reason == "self");
        Assert.Single(_db.PuzzleChallenges);
    }

    // ---- Incoming / Outgoing / Count ----

    [Fact]
    public async Task Incoming_ReturnsOnlyPendingForRecipient()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        await MakeFriendsAsync(me.Id, friend.Id);
        var puzzle = await CreatePuzzleAsync();
        _db.PuzzleChallenges.Add(new PuzzleChallenge { FromUserId = friend.Id, ToUserId = me.Id, PuzzleId = puzzle.Id, Status = ChallengeStatus.Pending });
        _db.PuzzleChallenges.Add(new PuzzleChallenge { FromUserId = friend.Id, ToUserId = me.Id, PuzzleId = puzzle.Id, Status = ChallengeStatus.Solved });
        await _db.SaveChangesAsync();

        SetUser(me.Id);
        var result = await _controller.Incoming();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<IncomingChallengeDto>>(ok.Value);
        Assert.Single(list);
        Assert.Equal("friend", list[0].FromUsername);
        Assert.Equal(puzzle.Rating, list[0].Rating);
        Assert.Equal("Standard", list[0].Source);
    }

    [Fact]
    public async Task Incoming_ProjectsBookMetadata_ForBookSource()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        await MakeFriendsAsync(me.Id, friend.Id);
        var book = await CreateBookPuzzleAsync(rating: 1850, title: "Mein Kapitel", tags: "pin endgame");
        _db.PuzzleChallenges.Add(new PuzzleChallenge { FromUserId = friend.Id, ToUserId = me.Id, PuzzleId = book.Id, Source = PuzzleSource.Book, Status = ChallengeStatus.Pending });
        await _db.SaveChangesAsync();

        SetUser(me.Id);
        var result = await _controller.Incoming();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<IncomingChallengeDto>>(ok.Value);
        var dto = Assert.Single(list);
        Assert.Equal("Book", dto.Source);
        Assert.Equal(1850, dto.Rating);
        Assert.Equal("pin endgame", dto.Themes);
        Assert.Equal("Mein Kapitel", dto.Title);
    }

    [Fact]
    public async Task Outgoing_ReturnsStatusString()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        var puzzle = await CreatePuzzleAsync();
        _db.PuzzleChallenges.Add(new PuzzleChallenge { FromUserId = me.Id, ToUserId = friend.Id, PuzzleId = puzzle.Id, Status = ChallengeStatus.Solved, TimeSpentSeconds = 9 });
        await _db.SaveChangesAsync();

        SetUser(me.Id);
        var result = await _controller.Outgoing();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<OutgoingChallengeDto>>(ok.Value);
        Assert.Single(list);
        Assert.Equal("Solved", list[0].Status);
        Assert.Equal(9, list[0].TimeSpentSeconds);
    }

    [Fact]
    public async Task IncomingCount_ReturnsPendingCount()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        var puzzle = await CreatePuzzleAsync();
        _db.PuzzleChallenges.Add(new PuzzleChallenge { FromUserId = friend.Id, ToUserId = me.Id, PuzzleId = puzzle.Id, Status = ChallengeStatus.Pending });
        _db.PuzzleChallenges.Add(new PuzzleChallenge { FromUserId = friend.Id, ToUserId = me.Id, PuzzleId = puzzle.Id, Status = ChallengeStatus.Pending });
        await _db.SaveChangesAsync();

        SetUser(me.Id);
        var result = await _controller.IncomingCount();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, (int)ok.Value!.GetType().GetProperty("count")!.GetValue(ok.Value)!);
    }

    // ---- Resolve ----

    [Fact]
    public async Task Resolve_SetsSolved_WhenRecipient()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        var puzzle = await CreatePuzzleAsync();
        var challenge = new PuzzleChallenge { FromUserId = friend.Id, ToUserId = me.Id, PuzzleId = puzzle.Id, Status = ChallengeStatus.Pending };
        _db.PuzzleChallenges.Add(challenge);
        await _db.SaveChangesAsync();

        SetUser(me.Id);
        var result = await _controller.Resolve(challenge.Id, new ResolveChallengeDto { Solved = true, TimeSpentSeconds = 14 });

        Assert.IsType<OkObjectResult>(result);
        var updated = await _db.PuzzleChallenges.FindAsync(challenge.Id);
        Assert.Equal(ChallengeStatus.Solved, updated!.Status);
        Assert.Equal(14, updated.TimeSpentSeconds);
        Assert.NotNull(updated.ResolvedAt);
    }

    [Fact]
    public async Task Resolve_Returns403_WhenNotRecipient()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        var puzzle = await CreatePuzzleAsync();
        var challenge = new PuzzleChallenge { FromUserId = me.Id, ToUserId = friend.Id, PuzzleId = puzzle.Id, Status = ChallengeStatus.Pending };
        _db.PuzzleChallenges.Add(challenge);
        await _db.SaveChangesAsync();

        SetUser(me.Id);  // sender, not recipient
        var result = await _controller.Resolve(challenge.Id, new ResolveChallengeDto { Solved = true, TimeSpentSeconds = 5 });

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task Resolve_Returns409_WhenAlreadyResolved()
    {
        var me = await CreateUserAsync("me");
        var friend = await CreateUserAsync("friend");
        var puzzle = await CreatePuzzleAsync();
        var challenge = new PuzzleChallenge { FromUserId = friend.Id, ToUserId = me.Id, PuzzleId = puzzle.Id, Status = ChallengeStatus.Failed };
        _db.PuzzleChallenges.Add(challenge);
        await _db.SaveChangesAsync();

        SetUser(me.Id);
        var result = await _controller.Resolve(challenge.Id, new ResolveChallengeDto { Solved = true, TimeSpentSeconds = 5 });

        Assert.IsType<ConflictObjectResult>(result);
    }
}
