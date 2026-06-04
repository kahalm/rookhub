using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using System.Net;
using System.Text;

namespace RookHub.Api.Tests;

public class PlayTimeServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public PlayTimeServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // 1700000000000 ms = 2023-11-14 22:13:20 UTC
    private const long Created = 1_700_000_000_000;
    private const long Last = 1_700_000_300_000; // +300 s

    // ---- Lichess-Parsing (rein) ------------------------------------------

    [Fact]
    public void ParseLichess_CountsRapidAndClassical_IgnoresOthers_TracksCursor()
    {
        var ndjson = string.Join('\n', new[]
        {
            $"{{\"id\":\"a\",\"speed\":\"rapid\",\"createdAt\":{Created},\"lastMoveAt\":{Last}}}",
            $"{{\"id\":\"b\",\"speed\":\"classical\",\"createdAt\":{Created},\"lastMoveAt\":{Last}}}",
            $"{{\"id\":\"c\",\"speed\":\"blitz\",\"createdAt\":{Created},\"lastMoveAt\":{Last}}}",            // zählt nicht
            $"{{\"id\":\"d\",\"speed\":\"correspondence\",\"createdAt\":{Created},\"lastMoveAt\":{Last + 999999}}}", // zählt nicht
            "", // Leerzeile wird ignoriert
        });

        var (perDay, cursor) = PlayTimeService.ParseLichess(ndjson);

        var day = new DateOnly(2023, 11, 14);
        Assert.Equal(2, perDay[day]);                   // rapid + classical
        Assert.Single(perDay);
        Assert.Equal(Last + 999999, cursor);            // Cursor = max(lastMoveAt) über ALLE Partien
    }

    [Fact]
    public void ParseLichess_CountsClassicalAsOneGame_RegardlessOfDuration()
    {
        var ndjson = $"{{\"speed\":\"classical\",\"createdAt\":{Created},\"lastMoveAt\":{Created + 99_999_000}}}";
        var (perDay, _) = PlayTimeService.ParseLichess(ndjson);
        Assert.Equal(1, perDay[new DateOnly(2023, 11, 14)]); // eine Partie, Dauer egal
    }

    // ---- chess.com-Parsing (rein) ----------------------------------------

    [Fact]
    public void ParseChessCom_CountsRapid_IgnoresBlitzAndDaily_FiltersByCursor()
    {
        var pgn = "[UTCDate \"2023.11.14\"]\n[UTCTime \"22:08:20\"]\n[EndDate \"2023.11.14\"]\n[EndTime \"22:13:20\"]\n\n1. e4 e5 *";
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            games = new[]
            {
                new { time_class = "rapid", end_time = 1700000300L, pgn },
                new { time_class = "blitz", end_time = 1700000400L, pgn },  // zählt nicht
                new { time_class = "daily", end_time = 1700000200L, pgn },  // zählt nicht (Korrespondenz)
            }
        });

        var (perDay, cursor) = PlayTimeService.ParseChessCom(json, cursor: 0);

        Assert.Equal(1, perDay[new DateOnly(2023, 11, 14)]); // nur die Rapid-Partie (Datum aus PGN UTCDate)
        Assert.Single(perDay);
        Assert.Equal(1700000400L * 1000, cursor);            // Cursor = max(end_time) über ALLE Partien
    }

    [Fact]
    public void ParseChessCom_SkipsGamesAtOrBeforeCursor()
    {
        var json = @"{ ""games"": [ { ""time_class"": ""rapid"", ""end_time"": 1700000300, ""pgn"": ""x"" } ] }";
        var cursorAfter = 1700000300L * 1000;
        var (perDay, cursor) = PlayTimeService.ParseChessCom(json, cursor: cursorAfter);
        Assert.Empty(perDay);                 // end_time·1000 <= cursor → übersprungen
        Assert.Equal(cursorAfter, cursor);
    }

    // ---- SyncUserAsync (Integration mit Fake-HTTP) -----------------------

    [Fact]
    public async Task SyncUserAsync_Lichess_PersistsGameCountAndCursor()
    {
        var user = new AppUser { Username = "u", Email = "u@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        _db.UserProfiles.Add(new UserProfile { UserId = user.Id, LichessUsername = "testuser" });
        await _db.SaveChangesAsync();

        var ndjson = string.Join('\n', new[]
        {
            $"{{\"speed\":\"rapid\",\"createdAt\":{Created},\"lastMoveAt\":{Last}}}",
            $"{{\"speed\":\"blitz\",\"createdAt\":{Created},\"lastMoveAt\":{Last}}}", // zählt nicht
        });
        var http = new HttpClient(new FakeHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(req.RequestUri!.Host.Contains("lichess") ? ndjson : "{\"games\":[]}", Encoding.UTF8),
            }));

        var service = new PlayTimeService(http, _db, new ConfigurationBuilder().Build(), NullLogger<PlayTimeService>.Instance);
        await service.SyncUserAsync(user.Id);

        var daily = await _db.PlayTimeDailies.SingleAsync(p => p.UserId == user.Id && p.Platform == PlayTimeService.Lichess);
        Assert.Equal(1, daily.Games);          // nur die Rapid-Partie
        Assert.Equal(new DateOnly(2023, 11, 14), daily.Date);

        var sync = await _db.PlayTimeSyncs.SingleAsync(s => s.UserId == user.Id && s.Platform == PlayTimeService.Lichess);
        Assert.Equal(Last, sync.LastGameTimestamp);
        Assert.Null(sync.LastError);
    }

    [Fact]
    public async Task SyncUserAsync_RecordsErrorOnHttpFailure()
    {
        var user = new AppUser { Username = "u", Email = "u@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        _db.UserProfiles.Add(new UserProfile { UserId = user.Id, LichessUsername = "testuser" });
        await _db.SaveChangesAsync();

        var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var service = new PlayTimeService(http, _db, new ConfigurationBuilder().Build(), NullLogger<PlayTimeService>.Instance);

        await service.SyncUserAsync(user.Id); // schluckt den Fehler, vermerkt ihn

        var sync = await _db.PlayTimeSyncs.SingleAsync(s => s.UserId == user.Id && s.Platform == PlayTimeService.Lichess);
        Assert.NotNull(sync.LastError);
        Assert.Equal(0, sync.LastGameTimestamp);
        Assert.False(await _db.PlayTimeDailies.AnyAsync(p => p.UserId == user.Id));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
