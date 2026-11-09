using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>R4-020: im Pumpen-Takt werden die offenen Vertiefungen eines Kontos EINMAL gelesen (statt je wartender
/// Vertiefung erneut) — mit genau denselben Summen wie die Abfrage-Fassung, und nach einer Änderung frisch.</summary>
public class GameAnalysisTurnsTickTests : IDisposable
{
    private const int House = 990601;
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private List<GameAnalysis> Seed()
    {
        var t0 = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var origins = new[] { GameAnalysisOrigin.Club, GameAnalysisOrigin.Manual, GameAnalysisOrigin.League, GameAnalysisOrigin.Library, GameAnalysisOrigin.SavedGame };
        var list = new List<GameAnalysis>();
        for (var i = 0; i < 50; i++)
        {
            var a = new GameAnalysis
            {
                UserId = House, Pgn = "", StartFen = "x", Status = GameAnalysisStatus.Done, RefineDepth = 30,
                Origin = origins[i % origins.Length], CreatedAt = t0.AddMinutes(i % 7 == 0 ? 0 : i),   // ein paar gleiche Zeitpunkte
            };
            for (var p = 0; p < 4; p++) a.Positions.Add(new GameAnalysisPosition { Ply = p, Fen = "f", Refined = p < i % 4 });
            list.Add(a);
        }
        _db.GameAnalyses.AddRange(list);
        _db.SaveChanges();
        return list;
    }

    [Fact]
    public async Task ImTakt_einmalGelesen_gleicheSummenWieDieAbfrage()
    {
        var all = Seed();
        var plain = new GameAnalysisTurns(_db, null);
        var expected = new List<int>();
        foreach (var a in all) expected.Add(await plain.UnrefinedAheadForTestAsync(a));
        Assert.Equal(50, plain.RefineSumQueries);

        var tick = new GameAnalysisTurns(_db, null);
        tick.BeginTick();
        var actual = new List<int>();
        foreach (var a in all) actual.Add(await tick.UnrefinedAheadForTestAsync(a));
        tick.EndTick();

        Assert.Equal(expected, actual);
        Assert.Equal(1, tick.RefineSumQueries);
        Assert.Contains(expected, n => n > 0);
    }

    [Fact]
    public async Task NachAenderung_wirdNeuGelesen()
    {
        var all = Seed();
        var tick = new GameAnalysisTurns(_db, null);
        tick.BeginTick();
        var before = await tick.UnrefinedAheadForTestAsync(all[^1]);

        foreach (var p in all[0].Positions) p.Refined = true;   // im Takt vertieft
        await _db.SaveChangesAsync();
        tick.Invalidate(House);

        Assert.Equal(await new GameAnalysisTurns(_db, null).UnrefinedAheadForTestAsync(all[^1]),
            await tick.UnrefinedAheadForTestAsync(all[^1]));
        Assert.Equal(2, tick.RefineSumQueries);
        Assert.True(before >= 0);
    }
}
