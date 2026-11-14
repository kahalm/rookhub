using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using RookHub.Api.Tests.Puzzles;
using Xunit;

namespace RookHub.Api.Tests.Courses;

/// <summary>Bestandsreparatur des Kurses „Persönliche Taktiken“ (<see cref="PersonalTacticsLineRepair"/>, 0.973.1):
/// Buchdiagramm-Linien, in denen der Löser die mattgesetzte Seite spielte, beginnen danach mit dem Gegnerzug.</summary>
public class PersonalTacticsLineRepairTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private int _n;
    private BookPuzzle Add(string fen, string moves, int startPly = -1, string? source = PersonalTacticsCourse.LineSource, string? hints = null)
    {
        var p = new BookPuzzle
        {
            LineId = $"personal-tactics-u5.pgn:club250:{++_n:D2}", BookFileName = "personal-tactics-u5.pgn", Round = _n.ToString(),
            Fen = fen, Moves = moves, StartPly = startPly, Source = source, Chapter = "Schwaz – Sorantin, Tristan (2026)",
            HintsJson = hints, HintsVersion = hints is null ? 0 : 3,
        };
        _db.BookPuzzles.Add(p);
        return p;
    }

    [Fact]
    public async Task Repair_AppliesTheSolverRuleToBookDiagramLines_OthersUntouched()
    {
        var alekhine = Add(BookSolverLineTests.AlekhineFen, BookSolverLineTests.AlekhineMoves, hints: """{"de":["a","b","c"]}""");   // Prod 160276
        var forced = Add(BookSolverLineTests.ForcedMateFen, BookSolverLineTests.ForcedMateMoves, hints: """{"de":["a","b","c"]}""");
        var fork = Add(BookSolverLineTests.ForkFen, BookSolverLineTests.ForkMoves);                    // ohne Matt, Diagramm klar gewinnend
        var unknown = Add("4k3/r7/8/3N4/8/8/4K3/8 w - - 0 1", "d5c7 e8d7");                           // ohne Matt, kein Diagramm mehr
        var weak = Add("r3k3/8/8/3N4/8/8/8/3K4 w - - 0 1", "d5c7 e8d7 c7a8");                         // ohne Matt, Diagramm nicht klar
        var illegal = Add(BookSolverLineTests.ForkFen, "a1a8");
        var lichess = Add("6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1", "a7a6 h6g7", startPly: 0);           // Lichess-Form: nie angefasst
        var foreign = Add(BookSolverLineTests.AlekhineFen, BookSolverLineTests.AlekhineMoves, source: null);   // anderer Kurs
        _db.EbookDiagrams.AddRange(
            new EbookDiagram { Id = 1, EbookId = 1, Fen = BookSolverLineTests.ForkFen, SolutionUci = BookSolverLineTests.ForkMoves, EngineEval = 600 },
            new EbookDiagram { Id = 2, EbookId = 1, Fen = BookSolverLineTests.ForkFen, SolutionUci = BookSolverLineTests.ForkMoves, EngineEval = 50 },   // dieselbe Stellung, schwächer bewertet
            new EbookDiagram { Id = 3, EbookId = 1, Fen = weak.Fen, SolutionUci = weak.Moves, EngineEval = 120 },
            new EbookDiagram { Id = 4, EbookId = 1, Fen = unknown.Fen, SolutionUci = "d5c7", EngineEval = 900 });   // andere Linie zählt nicht
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await PersonalTacticsLineRepair.RepairAsync(_db);

        Assert.Equal(new PersonalTacticsLineRepair.Result(Checked: 6, Switched: 1, Disabled: 3), result);
        _db.ChangeTracker.Clear();
        async Task<BookPuzzle> Load(BookPuzzle p) => await _db.BookPuzzles.AsNoTracking().FirstAsync(x => x.Id == p.Id);

        // Der gemeldete Fall: klassische Form, Weiß löst (31.Tb6 Txa7 32.Tb8#); Stellung und Züge bleiben, Tipps der falschen Seite weg.
        var a = await Load(alekhine);
        Assert.Equal((0, false, false), (a.StartPly, a.Retired, a.IsInfoOnly));
        Assert.Equal((BookSolverLineTests.AlekhineFen, BookSolverLineTests.AlekhineMoves), (a.Fen, a.Moves));
        Assert.Null(a.HintsJson);
        Assert.Equal(0, a.HintsVersion);

        var f = await Load(forced);
        Assert.Equal((-1, false, false), (f.StartPly, f.Retired, f.IsInfoOnly));
        Assert.NotNull(f.HintsJson);                                                 // unverändert, auch die Tipps
        Assert.Equal((-1, false, false), ((await Load(fork)).StartPly, (await Load(fork)).Retired, (await Load(fork)).IsInfoOnly));

        foreach (var gone in new[] { unknown, weak, illegal })
        {
            var g = await Load(gone);
            Assert.Equal((-1, true, true), (g.StartPly, g.Retired, g.IsInfoOnly));   // nicht bestimmbar → stillgelegt, nicht gelöscht
        }
        Assert.Equal((0, false, false), ((await Load(lichess)).StartPly, (await Load(lichess)).Retired, (await Load(lichess)).IsInfoOnly));
        Assert.Equal((-1, false, false), ((await Load(foreign)).StartPly, (await Load(foreign)).Retired, (await Load(foreign)).IsInfoOnly));
        Assert.Equal(8, await _db.BookPuzzles.CountAsync());

        // Idempotent.
        Assert.Equal(new PersonalTacticsLineRepair.Result(2, 0, 0), await PersonalTacticsLineRepair.RepairAsync(_db));
    }

    [Fact]
    public async Task RunOnce_RunsOnlyOnce_MarkedInSystemSettings()
    {
        var first = Add(BookSolverLineTests.AlekhineFen, BookSolverLineTests.AlekhineMoves);
        await _db.SaveChangesAsync();

        Assert.Equal(new PersonalTacticsLineRepair.Result(1, 1, 0), await PersonalTacticsLineRepair.RunOnceAsync(_db));
        Assert.Equal(PersonalTacticsLineRepair.DoneValue,
            (await _db.SystemSettings.AsNoTracking().SingleAsync(s => s.Key == PersonalTacticsLineRepair.DoneKey)).Value);
        Assert.Equal(0, (await _db.BookPuzzles.AsNoTracking().FirstAsync(p => p.Id == first.Id)).StartPly);

        Assert.Null(await PersonalTacticsLineRepair.RunOnceAsync(_db));              // zweiter Start: nichts mehr zu tun
    }

    [Fact]
    public async Task RunOnce_EmptyStock_StillSetsTheMark()
    {
        Assert.Equal(new PersonalTacticsLineRepair.Result(0, 0, 0), await PersonalTacticsLineRepair.RunOnceAsync(_db));
        Assert.Null(await PersonalTacticsLineRepair.RunOnceAsync(_db));
    }
}
