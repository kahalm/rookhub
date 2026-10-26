using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Puzzles;

namespace RookHub.Api.Tests.Puzzles;

/// <summary>
/// Codereview A2-014 (Entscheidung 25.10.: Serverzeit): die Tageswertung rankt nach der Serveruhr seit der signierten
/// Startmarke statt nach der Zeit, die der Browser meldet. Ohne gültige Marke (offline, alter Client, gefälscht) oder
/// unter 2 s zählt die Lösung, aber ohne Rang. Feste Uhr (<see cref="ManualTime"/>), keine Tabelle.
/// </summary>
public class DailyServerTimeTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 3, 10);

    private readonly AppDbContext _db;
    private readonly ManualTime _clock = new(Noon);
    private readonly DailyStartTokens _tokens;
    private readonly BookPuzzleService _service;
    private readonly DailyLeaderboardService _leaderboard;

    public DailyServerTimeTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _tokens = new DailyStartTokens(new EphemeralDataProtectionProvider(), _clock, new MemoryCache(new MemoryCacheOptions()));
        _service = new BookPuzzleService(_db, NullLogger<BookPuzzleService>.Instance, new NoOpTaskQueue(), startTokens: _tokens);
        _leaderboard = new DailyLeaderboardService(_db);
    }

    public void Dispose() => _db.Dispose();

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private void Advance(int seconds) => _clock.Now = _clock.Now.AddSeconds(seconds);

    private async Task<BookPuzzle> PuzzleAsync(bool daily = true)
    {
        var book = new Book { FileName = $"d-{Guid.NewGuid():N}.pgn", DisplayName = "Daily", ForDaily = true, Source = new BookSource() };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var p = new BookPuzzle
        {
            LineId = $"l-{Guid.NewGuid():N}", BookFileName = book.FileName, BookId = book.Id, Round = "1",
            Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", Moves = "e7e5",
        };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();
        if (daily)
        {
            _db.DailyPuzzles.Add(new DailyPuzzle { Date = Day, BookPuzzleId = p.Id, CreatedAt = Noon.UtcDateTime });
            await _db.SaveChangesAsync();
        }
        return p;
    }

    private Task Record(int puzzleId, int userId, bool solved, int clientSeconds, string? token) =>
        _service.RecordAttemptAsync(puzzleId, userId,
            new RecordBookAttemptDto { Solved = solved, TimeSeconds = clientSeconds, StartToken = token });

    private List<BookPuzzleAttempt> Attempts(int userId) =>
        _db.BookPuzzleAttempts.Where(a => a.UserId == userId).OrderBy(a => a.Id).ToList();

    // ---- Startmarke ----

    [Fact]
    public void Token_Roundtrip_ElapsedFromServerClock()
    {
        var token = _tokens.Issue(7);
        Advance(45);
        Assert.Equal(45, _tokens.ElapsedSeconds(token, 7));
    }

    [Fact]
    public void Token_ForeignUser_Tampered_OtherDay_IsRejected()
    {
        var token = _tokens.Issue(7);
        Assert.Null(_tokens.ElapsedSeconds(token, 8));
        Assert.Null(_tokens.ElapsedSeconds(token[..^2] + "AA", 7));
        Assert.Null(_tokens.ElapsedSeconds(null, 7));
        Advance(24 * 3600);
        Assert.Null(_tokens.ElapsedSeconds(token, 7));   // Marke von gestern
    }

    [Fact]
    public void Token_ReissueSameDay_KeepsFirstStart()
    {
        _tokens.Issue(7);
        Advance(300);                     // lange nachgedacht, dann neu geladen
        var second = _tokens.Issue(7);
        Advance(5);
        Assert.Equal(305, _tokens.ElapsedSeconds(second, 7));
    }

    [Fact]
    public void DailyStartTokenFor_OnlyTodayAndLoggedIn()
    {
        Assert.NotNull(_service.DailyStartTokenFor(7, Day));
        Assert.Null(_service.DailyStartTokenFor(null, Day));
        Assert.Null(_service.DailyStartTokenFor(7, Day.AddDays(-1)));
    }

    // ---- Aufzeichnung ----

    [Fact]
    public async Task Record_ValidToken_StoresServerTime_NotClientTime()
    {
        var p = await PuzzleAsync();
        var token = _tokens.Issue(1);
        Advance(45);
        await Record(p.Id, 1, solved: true, clientSeconds: 0, token);
        Assert.Equal(45, Attempts(1).Single().TimeSeconds);
    }

    [Fact]
    public async Task Record_NoToken_SolveStoredWithoutRankedTime()
    {
        var p = await PuzzleAsync();
        await Record(p.Id, 1, solved: true, clientSeconds: 30, token: null);
        Assert.Equal(0, Attempts(1).Single().TimeSeconds);
    }

    [Fact]
    public async Task Record_UnderTwoSeconds_IsNotRanked()
    {
        var p = await PuzzleAsync();
        var token = _tokens.Issue(1);
        Advance(1);
        await Record(p.Id, 1, solved: true, clientSeconds: 1, token);
        Assert.Equal(0, Attempts(1).Single().TimeSeconds);
    }

    [Fact]
    public async Task Record_FailThenSolve_SumEqualsServerTimeSinceFirstOpen()
    {
        var p = await PuzzleAsync();
        var token = _tokens.Issue(1);
        Advance(20);
        await Record(p.Id, 1, solved: false, clientSeconds: 3, token);
        Advance(40);
        var again = _tokens.Issue(1);     // Neuladen: Startzeit bleibt die erste
        Advance(10);
        await Record(p.Id, 1, solved: true, clientSeconds: 2, again);
        var attempts = Attempts(1);
        Assert.Equal(new[] { 20, 50 }, attempts.Select(a => a.TimeSeconds));
        Assert.Equal(70, attempts.Sum(a => a.TimeSeconds));
    }

    [Fact]
    public async Task Record_NotTodaysDaily_KeepsClientTime()
    {
        var p = await PuzzleAsync(daily: false);
        await Record(p.Id, 1, solved: true, clientSeconds: 33, token: null);
        Assert.Equal(33, Attempts(1).Single().TimeSeconds);
    }

    // ---- Wertung ----

    [Fact]
    public async Task Ladder_ZeroSecondClaimWithoutToken_GetsNoRankBonus()
    {
        var p = await PuzzleAsync();
        // Nutzer 1 schummelt: kein Öffnen, timeSeconds 0, keine Marke.
        await Record(p.Id, 1, solved: true, clientSeconds: 0, token: null);
        // Nutzer 2 löst ehrlich in 40 s Serverzeit.
        var token = _tokens.Issue(2);
        Advance(40);
        await Record(p.Id, 2, solved: true, clientSeconds: 38, token);

        var ladder = await _leaderboard.GetDailyLadderAsync(2026, 3);
        var cheat = ladder.Entries.Single(e => e.Name == "#1");
        var honest = ladder.Entries.Single(e => e.Name == "#2");
        Assert.Equal(10, cheat.Points);    // gelöst im Erstversuch, aber kein Rang-Bonus
        Assert.Equal(0, cheat.Golds);
        Assert.Equal(1, cheat.Solved);
        Assert.Equal(15, honest.Points);   // 10 + 🥇 5
        Assert.Equal(1, honest.Golds);
    }

    [Fact]
    public async Task HallOfFame_And_Results_IgnoreUnrankedSolves()
    {
        var p = await PuzzleAsync();
        var t1 = _tokens.Issue(1);
        Advance(3);
        await Record(p.Id, 1, solved: false, clientSeconds: 3, t1);   // Fehlversuch mit Zeit …
        await Record(p.Id, 1, solved: true, clientSeconds: 1, token: null);   // … dann Lösung ohne Marke
        var t2 = _tokens.Issue(2);
        Advance(60);
        await Record(p.Id, 2, solved: true, clientSeconds: 50, t2);

        var hof = await _leaderboard.GetDailyHallOfFameAsync();
        Assert.Equal("#2", hof.Fastest?.Name);
        Assert.Equal(60, hof.Fastest?.TimeSeconds);

        var results = await _service.GetResultsAsync(p.Id, null);
        Assert.Equal(0, results.Solvers.Single(s => s.Name == "#1").TimeSeconds);   // keine Zeit statt „3 s“
        Assert.Equal(60, results.Solvers.Single(s => s.Name == "#2").TimeSeconds);
    }
}
