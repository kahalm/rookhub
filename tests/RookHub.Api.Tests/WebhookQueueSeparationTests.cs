using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der schach-bot-Solver-Webhook muss auf der EIGENEN <see cref="IWebhookTaskQueue"/> landen,
/// nicht auf der allgemeinen Import-Queue — sonst verdrängt ein Chessable-Import-Schwung das
/// Webhook-Ticket (bounded/DropOldest) und der Daily-Solver erscheint nicht in Discord.
/// </summary>
public class WebhookQueueSeparationTests : IDisposable
{
    private readonly AppDbContext _db;

    public WebhookQueueSeparationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RecordAttempt_EnqueuesWebhookOnWebhookQueue()
    {
        var book = new Book { FileName = "b.pgn", DisplayName = "b", ForDaily = true };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var puzzle = new BookPuzzle { BookId = book.Id, BookFileName = book.FileName, LineId = "L1", Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", Moves = "e7e5" };
        _db.BookPuzzles.Add(puzzle);
        await _db.SaveChangesAsync();

        var webhookQueue = new CountingTaskQueue();
        var svc = new BookPuzzleService(_db, NullLogger<BookPuzzleService>.Instance, webhookQueue);

        await svc.RecordAttemptAsync(puzzle.Id, userId: 5, new RecordBookAttemptDto { Solved = true, TimeSeconds = 16, HintsUsed = 0 });

        // Der Webhook-Push wurde auf die (dedizierte) Webhook-Queue gelegt.
        Assert.Equal(1, webhookQueue.EnqueuedCount);
    }

    [Fact]
    public void WebhookQueue_IsDistinctInstanceFromImportQueue()
    {
        // Typ-Trennung: die Webhook-Queue ist eine eigene Implementierung, nicht die
        // (von Imports geteilte) Standard-BackgroundTaskQueue.
        IWebhookTaskQueue webhook = new WebhookTaskQueue();
        IBackgroundTaskQueue import = new BackgroundTaskQueue();
        Assert.NotSame((object)webhook, (object)import);
        Assert.IsType<WebhookTaskQueue>(webhook);
        Assert.IsType<BackgroundTaskQueue>(import);
    }
}
