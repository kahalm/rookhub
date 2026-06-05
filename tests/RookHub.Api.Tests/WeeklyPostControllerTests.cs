using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class WeeklyPostControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly WeeklyPostController _controller;

    public WeeklyPostControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _controller = new WeeklyPostController(_db, new WeeklyPostService(_db, NullLogger<WeeklyPostService>.Instance));
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    private const string ValidPgn = "[Event \"Test\"]\n[White \"A\"]\n[Black \"B\"]\n\n1. e4 e5 2. Nf3 *";

    private static IFormFile MakePgnFile(string content, string name = "MyGame.pgn")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream",
        };
    }

    private static T Unwrap<T>(IActionResult result) where T : class =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(result).Value!);

    private static T Unwrap<T>(ActionResult<T> result) where T : class =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(result.Result).Value!);

    [Fact]
    public async Task GetAll_ReturnsPostsSortedByScheduledDesc()
    {
        await _controller.Create(MakePgnFile(ValidPgn, "alt.pgn"), new DateTime(2026, 6, 1, 19, 0, 0), null, default);
        await _controller.Create(MakePgnFile(ValidPgn, "neu.pgn"), new DateTime(2026, 6, 8, 19, 0, 0), null, default);

        var list = Unwrap<List<WeeklyPostDto>>(await _controller.GetAll());

        Assert.Equal(2, list.Count);
        Assert.Equal(new DateTime(2026, 6, 8, 19, 0, 0), list[0].ScheduledAt);   // neueste zuerst
        Assert.Equal(new DateTime(2026, 6, 1, 19, 0, 0), list[1].ScheduledAt);
    }

    [Fact]
    public async Task Create_ValidPgn_StoresWithDefaultTitleFromFileName()
    {
        var res = Unwrap<WeeklyPostDto>(
            await _controller.Create(MakePgnFile(ValidPgn, "Taktik_Woche_1.pgn"), new DateTime(2026, 6, 8, 19, 0, 0), null, default));

        Assert.Equal("Taktik Woche 1", res.Title);            // .pgn entfernt, _ -> Leerzeichen
        Assert.Equal(new DateTime(2026, 6, 8, 19, 0, 0), res.ScheduledAt);
        Assert.True(res.FileSize > 0);

        var detail = Unwrap<WeeklyPostDetailDto>(await _controller.GetById(res.Id));
        Assert.Contains("1. e4", detail.PgnContent);
    }

    [Fact]
    public async Task Create_ExplicitTitle_IsUsed()
    {
        var res = Unwrap<WeeklyPostDto>(
            await _controller.Create(MakePgnFile(ValidPgn), new DateTime(2026, 6, 8, 19, 0, 0), "Mein Titel", default));
        Assert.Equal("Mein Titel", res.Title);
    }

    [Fact]
    public async Task Create_InvalidPgn_ReturnsBadRequest()
    {
        var res = await _controller.Create(MakePgnFile("kein gueltiges pgn hier"), new DateTime(2026, 6, 8, 19, 0, 0), null, default);
        Assert.IsType<BadRequestObjectResult>(res);
        Assert.Equal(0, await _db.WeeklyPosts.CountAsync());
    }

    [Fact]
    public async Task Create_NoFile_ReturnsBadRequest()
    {
        var res = await _controller.Create(null!, new DateTime(2026, 6, 8, 19, 0, 0), null, default);
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        Assert.IsType<NotFoundObjectResult>(await _controller.GetById(999));
    }

    // ChessBase-Trainings-PGN mit [%tqu]-Marker -> ergibt ein Puzzle (wie Bücher).
    private const string TrainingPgn = "[Event \"WP\"]\n[Round \"1.1\"]\n" +
        "[FEN \"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2\"]\n\n" +
        "{ [%tqu \"En\",\"Finde den Zug\"] Pointe. } 2.Nf3 Nc6 3. Bb5 *";

    [Fact]
    public async Task GetPuzzles_ParsesPgnIntoSequence()
    {
        var created = Unwrap<WeeklyPostDto>(
            await _controller.Create(MakePgnFile(TrainingPgn, "woche.pgn"), new DateTime(2026, 6, 8, 19, 0, 0), "Woche 1", default));

        var play = Unwrap<WeeklyPlayDto>(await _controller.GetPuzzles(created.Id));

        Assert.Equal("Woche 1", play.Title);
        var puzzle = Assert.Single(play.Puzzles);
        Assert.Equal(0, puzzle.Id);                       // lokaler Index
        Assert.False(string.IsNullOrEmpty(puzzle.Moves)); // UCI-Zugfolge vorhanden
    }

    [Fact]
    public async Task GetPuzzles_NotFound_Returns404()
    {
        Assert.IsType<NotFoundObjectResult>(await _controller.GetPuzzles(999));
    }

    [Fact]
    public async Task Update_ChangesTitleAndSchedule()
    {
        var created = Unwrap<WeeklyPostDto>(
            await _controller.Create(MakePgnFile(ValidPgn), new DateTime(2026, 6, 8, 19, 0, 0), null, default));

        var newDate = new DateTime(2026, 6, 15, 19, 0, 0);
        var updated = Unwrap<WeeklyPostDto>(
            await _controller.Update(created.Id, new UpdateWeeklyPostDto { Title = "Neu", ScheduledAt = newDate }));

        Assert.Equal("Neu", updated.Title);
        Assert.Equal(newDate, updated.ScheduledAt);
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        Assert.IsType<NotFoundObjectResult>(
            await _controller.Update(999, new UpdateWeeklyPostDto { Title = "x" }));
    }

    [Fact]
    public async Task Delete_RemovesPost()
    {
        var created = Unwrap<WeeklyPostDto>(
            await _controller.Create(MakePgnFile(ValidPgn), new DateTime(2026, 6, 8, 19, 0, 0), null, default));

        Assert.IsType<NoContentResult>(await _controller.Delete(created.Id));
        Assert.Equal(0, await _db.WeeklyPosts.CountAsync());
        Assert.IsType<NotFoundObjectResult>(await _controller.Delete(created.Id));
    }

    // --- Per-User-Fortschritt -------------------------------------------------

    // Zwei Trainings-Puzzles (je [%tqu]) → Sequenz mit 2 Puzzles.
    private const string TwoPuzzlePgn =
        "[Event \"WP\"]\n[Round \"1.1\"]\n" +
        "[FEN \"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2\"]\n\n" +
        "{ [%tqu \"En\",\"Finde den Zug\"] Pointe. } 2.Nf3 Nc6 3. Bb5 *\n\n" +
        "[Event \"WP\"]\n[Round \"1.2\"]\n" +
        "[FEN \"rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2\"]\n\n" +
        "{ [%tqu \"En\",\"Finde den Zug\"] Pointe. } 2.Nf3 Nc6 3. Bb5 *";

    private async Task<int> CreateTwoPuzzlePostAsync()
    {
        var created = Unwrap<WeeklyPostDto>(
            await _controller.Create(MakePgnFile(TwoPuzzlePgn, "woche.pgn"), new DateTime(2026, 6, 8, 19, 0, 0), "Woche", default));
        // Sicherstellen, dass wirklich 2 Puzzles geparst werden (sonst sagt der Test nichts aus).
        var play = Unwrap<WeeklyPlayDto>(await _controller.GetPuzzles(created.Id));
        Assert.Equal(2, play.Puzzles.Count);
        return created.Id;
    }

    [Fact]
    public async Task RecordAttempt_TracksPlayedSolvedAndCompletion()
    {
        var id = await CreateTwoPuzzlePostAsync();
        SetUser(1);

        var p1 = Unwrap<WeeklyPostProgressDto>(
            await _controller.RecordAttempt(id, new RecordWeeklyAttemptDto { PuzzleIndex = 0, Solved = true, TimeSeconds = 30 }));
        Assert.Equal(2, p1.Total);
        Assert.Equal(1, p1.PlayedCount);
        Assert.Equal(1, p1.SolvedCount);
        Assert.False(p1.Completed);
        Assert.Equal(new[] { 0 }, p1.PlayedIndices);   // für „zum ersten neuen Puzzle springen"

        // Zweites Puzzle NICHT gelöst → trotzdem „gespielt" → erledigt (alle gespielt).
        var p2 = Unwrap<WeeklyPostProgressDto>(
            await _controller.RecordAttempt(id, new RecordWeeklyAttemptDto { PuzzleIndex = 1, Solved = false, TimeSeconds = 12 }));
        Assert.Equal(2, p2.PlayedCount);
        Assert.Equal(1, p2.SolvedCount);
        Assert.True(p2.Completed);
        Assert.Equal(new[] { 0, 1 }, p2.PlayedIndices);
        Assert.Equal(42, p2.TotalSeconds);   // 30 + 12 (eigene Gesamtzeit)
    }

    [Fact]
    public async Task RecordAttempt_EmitsWeeklyPostAttemptLog_OncePerNewPuzzle()
    {
        var id = await CreateTwoPuzzlePostAsync();
        SetUser(7);
        var logger = new TestLogger<WeeklyPostService>();
        var controller = new WeeklyPostController(_db, new WeeklyPostService(_db, logger))
        {
            ControllerContext = _controller.ControllerContext
        };

        await controller.RecordAttempt(id, new RecordWeeklyAttemptDto { PuzzleIndex = 0, Solved = true, TimeSeconds = 20 });
        // Idempotenter Wiederholungsversuch desselben Index → KEIN zweites Log (sonst Doppelzählung in Kibana).
        await controller.RecordAttempt(id, new RecordWeeklyAttemptDto { PuzzleIndex = 0, Solved = false, TimeSeconds = 9 });

        var log = Assert.Single(logger.Messages, m => m.Contains("WeeklyPostAttempt"));
        Assert.Contains("User 7", log);
        Assert.Contains($"weekly-post {id}", log);
        Assert.Contains("puzzle 0", log);
        Assert.Contains("solved", log);
        Assert.Contains("in 20s", log);
    }

    [Fact]
    public async Task RecordAttempt_IsIdempotentPerIndex_FirstResultWins()
    {
        var id = await CreateTwoPuzzlePostAsync();
        SetUser(1);

        await _controller.RecordAttempt(id, new RecordWeeklyAttemptDto { PuzzleIndex = 0, Solved = true, TimeSeconds = 5 });
        // erneuter Versuch desselben Index (diesmal nicht gelöst) ändert nichts.
        var p = Unwrap<WeeklyPostProgressDto>(
            await _controller.RecordAttempt(id, new RecordWeeklyAttemptDto { PuzzleIndex = 0, Solved = false, TimeSeconds = 5 }));
        Assert.Equal(1, p.PlayedCount);
        Assert.Equal(1, p.SolvedCount);   // erster (gelöster) Versuch bleibt
    }

    [Fact]
    public async Task RecordAttempt_IndexOutOfRange_Returns404()
    {
        var id = await CreateTwoPuzzlePostAsync();
        SetUser(1);
        var res = await _controller.RecordAttempt(id, new RecordWeeklyAttemptDto { PuzzleIndex = 9, Solved = true });
        Assert.IsType<NotFoundObjectResult>(res.Result);
    }

    [Fact]
    public async Task GetProgress_UnknownPost_Returns404()
    {
        SetUser(1);
        var res = await _controller.GetProgress(999);
        Assert.IsType<NotFoundObjectResult>(res.Result);
    }

    [Fact]
    public async Task Progress_IsPerUser()
    {
        var id = await CreateTwoPuzzlePostAsync();
        SetUser(1);
        await _controller.RecordAttempt(id, new RecordWeeklyAttemptDto { PuzzleIndex = 0, Solved = true });

        SetUser(2);
        var p = Unwrap<WeeklyPostProgressDto>(await _controller.GetProgress(id));
        Assert.Equal(0, p.PlayedCount);   // anderer User: kein Fortschritt
    }

    [Fact]
    public async Task GetAllProgress_ReturnsOnlyPostsWithAttempts_PerUser()
    {
        var id = await CreateTwoPuzzlePostAsync();
        SetUser(1);
        await _controller.RecordAttempt(id, new RecordWeeklyAttemptDto { PuzzleIndex = 0, Solved = true, TimeSeconds = 20 });
        await _controller.RecordAttempt(id, new RecordWeeklyAttemptDto { PuzzleIndex = 1, Solved = false, TimeSeconds = 7 });

        // User 1: genau ein Post mit Versuchen, played 2/2, solved 1, completed.
        var list = Unwrap<List<WeeklyPostProgressDto>>(await _controller.GetAllProgress());
        var p = Assert.Single(list);
        Assert.Equal(id, p.WeeklyPostId);
        Assert.Equal(2, p.PlayedCount);
        Assert.Equal(1, p.SolvedCount);
        Assert.True(p.Completed);
        Assert.Equal(27, p.TotalSeconds);   // 20 + 7 (Gesamtzeit in der Übersicht)

        // User 2: keine Versuche → leere Liste.
        SetUser(2);
        var empty = Unwrap<List<WeeklyPostProgressDto>>(await _controller.GetAllProgress());
        Assert.Empty(empty);
    }

    [Fact]
    public async Task GetResults_AggregatesPerUser_WithDiscordTimeAndCompleted()
    {
        var id = await CreateTwoPuzzlePostAsync();
        var u = new AppUser
        {
            Username = "alice", Email = "a@t.com", PasswordHash = "h",
            Profile = new UserProfile { DiscordId = "d1", DiscordUsername = "alice#1", DisplayName = "Alice" },
        };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();

        SetUser(u.Id);
        await _controller.RecordAttempt(id, new RecordWeeklyAttemptDto { PuzzleIndex = 0, Solved = true, TimeSeconds = 30 });
        await _controller.RecordAttempt(id, new RecordWeeklyAttemptDto { PuzzleIndex = 1, Solved = false, TimeSeconds = 12 });

        var res = Unwrap<WeeklyPostResultsDto>(await _controller.GetResults(id));
        Assert.Equal(2, res.Total);
        Assert.Equal(1, res.CompletedCount);
        var p = Assert.Single(res.Players);
        Assert.Equal("Alice", p.Name);          // DisplayName bevorzugt
        Assert.Equal("d1", p.DiscordId);
        Assert.Equal(2, p.PlayedCount);
        Assert.Equal(1, p.SolvedCount);
        Assert.Equal(42, p.TotalSeconds);       // 30 + 12 (Gesamtzeit)
        Assert.True(p.Completed);
    }

    [Fact]
    public async Task GetResults_UnknownPost_Returns404()
    {
        var res = await _controller.GetResults(999);
        Assert.IsType<NotFoundObjectResult>(res.Result);
    }
}
