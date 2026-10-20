using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Prüflauf nur über ungeprüfte Diagramme (0.811.1): geprüfte bleiben samt Lösung und Bewertung, wie sie sind.</summary>
public class EbookSolutionCheckOnlyUncheckedTests
{
    private const string Fen = "4k3/8/8/8/8/8/4P3/R3K3 w - - 0 1";
    private const int BookId = 990811;

    /// <summary>Engine-Attrappe: jede Stellung „Matt in 2“ mit Turmzug — zählt die Bewertungen.</summary>
    private sealed class CountingEngine : IPositionEvaluator
    {
        public int Calls;
        public Task<UciResult?> EvaluateAsync(string fen, IReadOnlyList<string>? moves, int moveTimeMs, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<UciResult?>(new UciResult(9997, true, ["a1a8", "e8d7", "a8a7"]));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task<EbookTestKit> KitAsync()
    {
        var kit = new EbookTestKit();
        kit.Db.Ebooks.Add(new Ebook { Id = BookId, Title = "Teilprüfung", Source = EbookSource.Library, DiagramState = EbookDiagramState.Done,
            DiagramCount = 3, SolutionCheckState = EbookDiagramState.Pending, SolutionCheckQueuedAt = DateTime.UtcNow });
        kit.Db.EbookDiagrams.AddRange(
            // schon geprüft: Buchlösung fraglich — darf weder Lösung noch Bewertung verlieren
            new EbookDiagram { Id = 9908111, EbookId = BookId, Page = 1, Fen = Fen, FontFamily = "opus", SolutionUci = "e2e3", SolutionSan = "e3",
                SolutionSource = "book", SolutionCheck = EbookSolutionCheck.Doubtful, EngineEval = 123 },
            // schon geprüft: keine Aufgabe
            new EbookDiagram { Id = 9908112, EbookId = BookId, Page = 2, Fen = Fen, FontFamily = "opus", SolutionCheck = EbookSolutionCheck.NoPuzzle, EngineEval = 5 },
            // ungeprüft (neu bzw. extern zurückgesetzt) → Stockfish findet die Lösung
            new EbookDiagram { Id = 9908113, EbookId = BookId, Page = 3, Fen = Fen, FontFamily = "opus" },
            // ungelesen (0.811.0) → bleibt außen vor
            new EbookDiagram { Id = 9908114, EbookId = BookId, Page = 4, Fen = Fen, FontFamily = EbookDiagramVisibility.Unread });
        await kit.Db.SaveChangesAsync();
        return kit;
    }

    [Fact]
    public async Task CheckedDiagramsStay_UncheckedAreChecked_StateDone()
    {
        using var kit = await KitAsync();
        var engine = new CountingEngine();
        var book = await kit.Db.Ebooks.SingleAsync(b => b.Id == BookId);

        await EbookSolutionCheckWorker.CheckBookAsync(kit.Db, book, _ => Task.FromResult<IPositionEvaluator?>(engine), 10, NullLogger.Instance, default);

        kit.Db.ChangeTracker.Clear();
        var d = await kit.Db.EbookDiagrams.ToDictionaryAsync(x => x.Id);
        Assert.Equal(EbookSolutionCheck.Doubtful, d[9908111].SolutionCheck);
        Assert.Equal(123, d[9908111].EngineEval);
        Assert.Equal("e2e3", d[9908111].SolutionUci);
        Assert.Equal(EbookSolutionCheck.NoPuzzle, d[9908112].SolutionCheck);
        Assert.Equal(5, d[9908112].EngineEval);
        Assert.Null(d[9908112].SolutionUci);
        Assert.Equal(EbookSolutionCheck.Engine, d[9908113].SolutionCheck);
        Assert.Equal("a1a8 e8d7 a8a7", d[9908113].SolutionUci);
        Assert.Equal(9997, d[9908113].EngineEval);
        Assert.Equal(EbookSolutionCheck.Unchecked, d[9908114].SolutionCheck);
        Assert.Equal(2, engine.Calls);   // nur Diagramm 3: Wurzel + tiefe Rechnung
        Assert.Equal(EbookDiagramState.Done, (await kit.Db.Ebooks.SingleAsync()).SolutionCheckState);
    }

    [Fact]
    public async Task NothingUnchecked_DoesNotStartTheEngine_ButFinishes()
    {
        using var kit = await KitAsync();
        var third = await kit.Db.EbookDiagrams.SingleAsync(x => x.Id == 9908113);
        third.SolutionCheck = EbookSolutionCheck.Confirmed;
        // unprüfbare Stellung (kein schwarzer König) bleibt ungeprüft, startet aber keine Engine
        kit.Db.EbookDiagrams.Add(new EbookDiagram { Id = 9908115, EbookId = BookId, Page = 5, Fen = "8/8/8/8/8/8/8/4K3 w - - 0 1", FontFamily = "opus" });
        await kit.Db.SaveChangesAsync();
        var book = await kit.Db.Ebooks.SingleAsync(b => b.Id == BookId);
        var started = 0;

        await EbookSolutionCheckWorker.CheckBookAsync(kit.Db, book, _ => { started++; return Task.FromResult<IPositionEvaluator?>(new CountingEngine()); },
            10, NullLogger.Instance, default);

        Assert.Equal(0, started);
        Assert.Equal(EbookDiagramState.Done, book.SolutionCheckState);
    }

    [Fact]
    public async Task EngineMissing_MarksFailed_DiagramsUntouched()
    {
        using var kit = await KitAsync();
        var book = await kit.Db.Ebooks.SingleAsync(b => b.Id == BookId);

        await EbookSolutionCheckWorker.CheckBookAsync(kit.Db, book, _ => Task.FromResult<IPositionEvaluator?>(null), 10, NullLogger.Instance, default);

        Assert.Equal(EbookDiagramState.Failed, book.SolutionCheckState);
        Assert.Equal(EbookSolutionCheck.Unchecked, (await kit.Db.EbookDiagrams.SingleAsync(x => x.Id == 9908113)).SolutionCheck);
    }
}
