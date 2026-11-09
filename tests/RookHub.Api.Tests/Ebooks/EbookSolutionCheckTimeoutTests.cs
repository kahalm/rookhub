using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>R3-011: Zeitüberschreitung der Engine blockiert die Warteschlange nicht mehr; „keine Antwort“ auf einen
/// ungültigen Lösungszug zählt nicht als Matt.</summary>
public class EbookSolutionCheckTimeoutTests
{
    private const string Fen = "4k3/8/8/8/8/8/4P3/R3K3 w - - 0 1";
    private const int BookId = 990812;

    /// <summary>Hängt (OperationCanceledException aus der eigenen Zeitschranke) an der ersten Stellung, sonst Matt in 2.</summary>
    private sealed class HangOnceEngine(Func<bool> hang) : IPositionEvaluator
    {
        public Task<UciResult?> EvaluateAsync(string fen, IReadOnlyList<string>? moves, int moveTimeMs, CancellationToken ct)
        {
            if (hang()) throw new OperationCanceledException("Zeitschranke");
            return Task.FromResult<UciResult?>(new UciResult(9997, true, ["a1a8", "e8d7", "a8a7"]));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task EngineTimeout_SkipsTheDiagram_RestartsTheEngine_AndFinishesTheBook()
    {
        using var kit = new EbookTestKit();
        kit.Db.Ebooks.Add(new Ebook { Id = BookId, Title = "Hänger", Source = EbookSource.Library, DiagramState = EbookDiagramState.Done,
            DiagramCount = 2, SolutionCheckState = EbookDiagramState.Pending, SolutionCheckQueuedAt = DateTime.UtcNow });
        kit.Db.EbookDiagrams.AddRange(
            new EbookDiagram { Id = 9908121, EbookId = BookId, Page = 1, Fen = Fen, FontFamily = "opus" },
            new EbookDiagram { Id = 9908122, EbookId = BookId, Page = 2, Fen = Fen, FontFamily = "opus" });
        await kit.Db.SaveChangesAsync();
        var book = await kit.Db.Ebooks.SingleAsync();
        var hung = false;
        var started = 0;

        await EbookSolutionCheckWorker.CheckBookAsync(kit.Db, book, _ =>
        {
            started++;
            return Task.FromResult<IPositionEvaluator?>(new HangOnceEngine(() => !hung && (hung = true)));
        }, 10, NullLogger.Instance, default);

        Assert.Equal(EbookDiagramState.Done, book.SolutionCheckState);
        Assert.Equal(2, started);   // nach dem Hänger neu gestartet
        var d = await kit.Db.EbookDiagrams.ToDictionaryAsync(x => x.Id);
        Assert.Equal(EbookSolutionCheck.Unchecked, d[9908121].SolutionCheck);   // ausgelassen
        Assert.Equal(EbookSolutionCheck.Engine, d[9908122].SolutionCheck);      // weiter geprüft
    }

    [Fact]
    public async Task ShutdownCancellation_StillPropagates()
    {
        using var kit = new EbookTestKit();
        kit.Db.Ebooks.Add(new Ebook { Id = BookId, Title = "Abbruch", Source = EbookSource.Library, DiagramState = EbookDiagramState.Done,
            DiagramCount = 1, SolutionCheckState = EbookDiagramState.Pending });
        kit.Db.EbookDiagrams.Add(new EbookDiagram { Id = 9908123, EbookId = BookId, Page = 1, Fen = Fen, FontFamily = "opus" });
        await kit.Db.SaveChangesAsync();
        var book = await kit.Db.Ebooks.SingleAsync();
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EbookSolutionCheckWorker.CheckBookAsync(kit.Db, book, _ =>
        {
            cts.Cancel();
            return Task.FromResult<IPositionEvaluator?>(new HangOnceEngine(() => true));
        }, 10, NullLogger.Instance, cts.Token));
        Assert.Equal(EbookDiagramState.Pending, book.SolutionCheckState);
    }

    /// <summary>Attrappe wie die echte Sitzung: ungültige Züge und Matt-Stellungen ergeben beide <c>null</c>.</summary>
    private sealed class NullAfterMoveEngine : IPositionEvaluator
    {
        public Task<UciResult?> EvaluateAsync(string fen, IReadOnlyList<string>? moves, int moveTimeMs, CancellationToken ct)
            => Task.FromResult(moves is { Count: > 0 } ? null : new UciResult(600, false, ["e2e4"]));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("a1a8Q")]    // Umwandlungs-Großbuchstabe, kein Bauer
    [InlineData("e7e8Q")]
    [InlineData("A1A8")]
    [InlineData("a1h8")]     // Form stimmt, Zug unspielbar
    public async Task InvalidBookMove_WithoutEngineAnswer_IsNotConfirmed(string uci)
    {
        var d = new EbookDiagram { Fen = Fen, SolutionUci = uci };
        await EbookSolutionChecker.CheckAsync(new NullAfterMoveEngine(), d, 10, default);
        Assert.NotEqual(EbookSolutionCheck.Confirmed, d.SolutionCheck);
        Assert.Equal(EbookSolutionCheck.Doubtful, d.SolutionCheck);
    }

    [Fact]
    public async Task LegalBookMove_WithoutEngineAnswer_StillCountsAsMateOnTheBoard()
    {
        var d = new EbookDiagram { Fen = Fen, SolutionUci = "a1a8" };
        await EbookSolutionChecker.CheckAsync(new NullAfterMoveEngine(), d, 10, default);
        Assert.Equal(EbookSolutionCheck.Confirmed, d.SolutionCheck);
    }

    [Fact]
    public async Task UciSession_Timeout_IsReportedAsIOException()
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = Path.Combine(Path.GetTempPath(), "rh-uci-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Antwortet auf uci/isready, aber nie mit bestmove.
            var script = Path.Combine(dir, "mute-engine.sh");
            File.WriteAllText(script, "#!/bin/sh\nwhile read l; do case \"$l\" in uci) echo uciok;; isready) echo readyok;; quit) exit 0;; esac; done\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await using var session = await UciEngineSession.StartAsync(script, NullLogger.Instance, default);
            Assert.NotNull(session);
            await Assert.ThrowsAsync<IOException>(() => session.EvaluateAsync(Fen, null, 20, default));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* egal */ } }
    }
}
