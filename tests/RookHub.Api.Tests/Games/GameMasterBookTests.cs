using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Games;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Og;
using RookHub.Api.Services.Repertoires;

namespace RookHub.Api.Tests.Games;

/// <summary>
/// Meister-Buchzüge (0.978.0, Wunsch 2026-11-15 an Partie 234: 1.e4 e6 2.d4 d5 3.Ld3 — „ist Ld3 kein Theoriezug?"). Der
/// Explorer ist hier eine feste Tabelle, kein Netz. Dieselbe Partie mit denselben LITERALEN Bewertungen prüft
/// <c>game-review.util.spec.ts</c> („Meister-Buchzüge"); die Schwellen sind die des Zusammenfassungs-Bots
/// (<c>formulare-bot/lib/opening_book.py</c>).
/// </summary>
public class GameMasterBookTests
{
    // 1.e4 e6 2.d4 d5 3.Ld3 dxe4
    private static readonly string[] Fens =
    {
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
        "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
        "rnbqkbnr/pppp1ppp/4p3/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
        "rnbqkbnr/pppp1ppp/4p3/8/3PP3/8/PPP2PPP/RNBQKBNR b KQkq - 0 2",
        "rnbqkbnr/ppp2ppp/4p3/3p4/3PP3/8/PPP2PPP/RNBQKBNR w KQkq - 0 3",
        "rnbqkbnr/ppp2ppp/4p3/3p4/3PP3/3B4/PPP2PPP/RNBQK1NR b KQkq - 1 3",
    };
    private static readonly string[] Ucis = ["e2e4", "e7e6", "d2d4", "d7d5", "f1d3", "d5e4"];
    private static readonly string[] Sans = ["e4", "e6", "d4", "d5", "Bd3", "dxe4"];

    /// <summary>Kandidaten aus Sicht der Seite am Zug. 3.Ld3: +0,79 → −0,09 (57,22 → 49,17 %, Verlust 8,05 = Ungenauigkeit);
    /// mit <paramref name="afterBd3"/> = −60 ein Fehler (Verlust 12,72).</summary>
    private static List<GameAnalysisPosition> Positions(int afterBd3 = -9, string startFen = "") => Enumerable.Range(0, 6)
        .Select(i => new GameAnalysisPosition
        {
            Ply = i, Fen = i == 0 && startFen != "" ? startFen : Fens[i], GameMoveUci = Ucis[i], GameMoveSan = Sans[i], Depth = 20,
            CandidatesJson = i switch
            {
                4 => $$"""[{"uci":"b1c3","cp":79},{"uci":"f1d3","cp":{{afterBd3}}}]""",
                5 => $$"""[{"uci":"d5e4","cp":{{-afterBd3}}}]""",
                _ => $$"""[{"uci":"{{Ucis[i]}}","cp":{{(i % 2 == 0 ? 79 : -79)}}}]""",
            },
        }).ToList();

    /// <summary>Der Explorer als Tabelle: je Datenbank und Halbzug die Partien mit dem Partiezug (und die der Stellung).
    /// Ein fehlender Eintrag = „nicht angekommen".</summary>
    private sealed class FakeSource : IMasterBookSource
    {
        public readonly Dictionary<(string Db, int Ply), (long Played, long Total)> Table = new();
        public bool Down;
        public int Calls;
        public List<(string Db, int Count)> Asked = [];

        public Task<IReadOnlyDictionary<string, ExplorerPositionStats>> StatsAsync(IReadOnlyList<(string Key, string Fen)> positions,
            ExplorerQuery query, TimeSpan budget, CancellationToken ct)
        {
            Calls++;
            Asked.Add((query.Database, positions.Count));
            var result = new Dictionary<string, ExplorerPositionStats>();
            if (!Down)
                for (var i = 0; i < Fens.Length; i++)
                    if (positions.Any(p => p.Key == RepertoireReach.Key(Fens[i])) && Table.TryGetValue((query.Database, i), out var row))
                        result[RepertoireReach.Key(Fens[i])] = new ExplorerPositionStats(row.Total,
                            row.Played > 0 ? [new ExplorerMoveStat(Ucis[i], Sans[i], row.Played, null, null)] : []);
            return Task.FromResult<IReadOnlyDictionary<string, ExplorerPositionStats>>(result);
        }

        /// <summary>Meisterpartien je Halbzug 0..5 (gespielt); die Stellung hat jeweils 190 844 (die echte Zahl nach 2…d5).</summary>
        public FakeSource Masters(params long[] played)
        {
            for (var i = 0; i < played.Length; i++) Table[("masters", i)] = (played[i], 190844);
            return this;
        }

        public FakeSource LichessAt(int ply, long played, long total)
        {
            Table[("lichess", ply)] = (played, total);
            return this;
        }
    }

    [Theory]
    [InlineData(2, 0, 0, "Theory")]       // zwei Meisterpartien reichen
    [InlineData(1696, 0, 0, "Theory")]    // 3.Ld3 nach 1.e4 e6 2.d4 d5: 1 696 von 190 844 Meisterpartien (lokaler Explorer, 15.11.2026)
    [InlineData(1, 5, 100, "Theory")]     // eine Meisterpartie + fünf bei Lichess ab 2200
    [InlineData(1, 4, 100, "Rare")]
    [InlineData(0, 20, 200, "Common")]    // nur bei Lichess oft: kein Beleg für Güte
    [InlineData(0, 20, 201, "Rare")]      // unter 10 %
    [InlineData(0, 5, 1000, "Rare")]
    [InlineData(0, 4, 1000, "None")]
    public void Classify_SchwellenWieDerZusammenfassungsBot(long masters, long lichess, long lichessTotal, string expected)
        => Assert.Equal(expected, GameMasterBook.Classify(masters, lichess, lichessTotal).ToString());

    [Fact]
    public void Format_Parse_Rundreise()
    {
        Assert.Equal("0,1,4", GameMasterBook.Format([4, 0, 1, 1]));
        Assert.Equal([0, 1, 4], GameMasterBook.Parse("0,1,4").OrderBy(p => p));
        Assert.Empty(GameMasterBook.Parse(null));
        Assert.Empty(GameMasterBook.Parse(""));
        Assert.Equal([3], GameMasterBook.Parse("x,3,-1"));
    }

    [Fact]
    public async Task Compute_LdDrei_mitMeisterRueckhalt_istBuch_abDemErstenZug()
    {
        var source = new FakeSource().Masters(1_000_000, 200_000, 195_000, 190_844, 1696, 900);
        Assert.Equal("0,1,2,3,4,5", await new GameMasterBook(source).ComputeAsync(Positions(), 6));
        Assert.Equal([("masters", 6)], source.Asked);   // Lichess wird nicht gefragt, wenn die Meisterpraxis reicht
    }

    [Fact]
    public async Task Compute_ohneVorgaenger_endetDieTheorie_auchWennSpaetereStellungenWiederBekanntWaeren()
    {
        // 3.Ld3 ohne Meisterpartie und nur dreimal bei Lichess: „ohne Vorgänger" — danach zählt nichts mehr.
        var source = new FakeSource().Masters(1_000_000, 200_000, 195_000, 190_844, 0, 900).LichessAt(4, 3, 5000).LichessAt(5, 800, 900);
        Assert.Equal("0,1,2,3", await new GameMasterBook(source).ComputeAsync(Positions(), 6));
    }

    [Fact]
    public async Task Compute_seltenerOderNurBeiLichessHaeufigerZug_istKeinBuch_dieTheorieLaeuftAberWeiter()
    {
        // 3.Ld3: keine Meisterpartie, aber 30 % bei Lichess („häufig") → kein Buchzug; 3…dxe4 danach: die Meisterdatenbank kennt
        // die Stellung nicht mehr, Lichess entscheidet allein (800 Partien, aber ohne Meisterpartie → „häufig", kein Buch).
        var common = new FakeSource().Masters(1_000_000, 200_000, 195_000, 190_844, 0, 900).LichessAt(4, 300, 1000).LichessAt(5, 800, 900);
        Assert.Equal("0,1,2,3", await new GameMasterBook(common).ComputeAsync(Positions(), 6));
        // Eine Meisterpartie + 5 bei Lichess → Theorie; eine + 4 → „selten", kein Buch, aber 3…dxe4 danach wieder.
        var one = new FakeSource().Masters(1_000_000, 200_000, 195_000, 190_844, 1, 900).LichessAt(4, 5, 1000);
        Assert.Equal("0,1,2,3,4,5", await new GameMasterBook(one).ComputeAsync(Positions(), 6));
        var rare = new FakeSource().Masters(1_000_000, 200_000, 195_000, 190_844, 1, 900).LichessAt(4, 4, 1000);
        Assert.Equal("0,1,2,3,5", await new GameMasterBook(rare).ComputeAsync(Positions(), 6));
    }

    [Fact]
    public async Task Compute_ExplorerAusfall_istNull_nichtLeer()
    {
        Assert.Null(await new GameMasterBook(new FakeSource { Down = true }).ComputeAsync(Positions(), 6));
        // Nur ein Teil kam an (Zeit um): ebenfalls „später erneut", nichts festschreiben.
        var partial = new FakeSource().Masters(1_000_000, 200_000, 195_000);
        Assert.Null(await new GameMasterBook(partial).ComputeAsync(Positions(), 6));
        // Lichess wäre nötig (Ld3 ohne Meisterpartie), antwortet aber nicht.
        var noLichess = new FakeSource().Masters(1_000_000, 200_000, 195_000, 190_844, 0, 900);
        Assert.Null(await new GameMasterBook(noLichess).ComputeAsync(Positions(), 6));
    }

    [Fact]
    public async Task Compute_PartieNichtAbDerGrundstellung_hatKeineBuchzuege_ohneAbfrage()
    {
        var source = new FakeSource().Masters(5, 5, 5, 5, 5, 5);
        var positions = Positions(startFen: "rnbqkbnr/pppp1ppp/8/4p3/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        Assert.Equal("", await new GameMasterBook(source).ComputeAsync(positions, 6));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void Klassen_LdDrei_mitRueckhaltKeinFehler_ohneUngenauigkeit_FehlerBleibtFehler()
    {
        IReadOnlySet<int> book = GameMasterBook.Parse("0,1,2,3,4,5");
        Assert.Equal("SSSSIS", GameMoveClasses.FromPositions(Positions(), 6));                 // ohne Rückhalt wie bisher
        // Regelstand 5 (Wunsch 2026-11-15): in der Meistertheorie heißt JEDER Zug „book" (K) — auch die Bestzüge davor und danach.
        Assert.Equal("KKKKKK", GameMoveClasses.FromPositions(Positions(), 6, book));
        Assert.Equal("KKKKIS", GameMoveClasses.FromPositions(Positions(), 6, GameMasterBook.Parse("0,1,2,3")));   // Theorie vorher verlassen: ab dort normale Klassen
        Assert.Equal("KKKKMK", GameMoveClasses.FromPositions(Positions(afterBd3: -60), 6, book));   // ein Fehler bleibt sichtbar
        Assert.Equal("KKKKPK", GameMoveClasses.FromPositions(Positions(afterBd3: -400), 6, book));  // ein grober Fehler auch
        Assert.Equal("SSSSIS", GameMoveClasses.FromPositions(Positions(), 6, GameMasterBook.Parse(null)));   // nicht geprüft: wie bisher
        Assert.Equal("SSSSIS", GameMoveClasses.FromPositions(Positions(), 6, GameMasterBook.Parse("")));     // geprüft, keine Theorie
        Assert.Equal('K', GameMoveClasses.Codes["book"]);
        Assert.Equal(5, GameMoveClasses.Version);

        var flaw = Assert.Single(GameMistakes.Find(Positions(), 6));
        Assert.Equal((4, "inaccuracy", "+0.79", "-0.09"), (flaw.Ply, flaw.Class, flaw.EvalBefore, flaw.EvalAfter));
        Assert.Empty(GameMistakes.Find(Positions(), 6, book));
        Assert.Equal("mistake", Assert.Single(GameMistakes.Find(Positions(afterBd3: -60), 6, book)).Class);
        Assert.Empty(GameMistakeSelection.FromPositions(Positions(), 6, book));
        Assert.Equal(4, Assert.Single(GameMistakeSelection.FromPositions(Positions(), 6)).Ply);
    }

    [Theory]
    [InlineData(57.22, 49.17, true, "good")]
    [InlineData(57.22, 49.17, false, "inaccuracy")]
    [InlineData(57.22, 45, true, "mistake")]
    [InlineData(57.22, 30, true, "blunder")]
    [InlineData(57.22, 56, true, "excellent")]
    public void Classify_nurDieUngenauigkeitFaelltWeg(double before, double after, bool masterBacked, string expected)
        => Assert.Equal(expected, GameMistakes.Classify(before, after, false, 79, null, moverWhite: true, masterBacked));

    private static AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task HarvestBooks_legtBuchzuegeAb_rechnetKlassenNeu_undDieBewertungenTragenSie()
    {
        await using var db = Db();
        db.GameAnalyses.AddRange(
            new GameAnalysis { Id = 990201, UserId = 1, Pgn = "", StartFen = "", Status = GameAnalysisStatus.Done, PlyCount = 6,
                MoveClasses = "SSSSIS", MoveClassesVersion = GameMoveClasses.Version, Positions = Positions() },
            new GameAnalysis { Id = 990202, UserId = 1, Pgn = "", StartFen = "", Status = GameAnalysisStatus.Running, PlyCount = 6, Positions = Positions() });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var book = new GameMasterBook(new FakeSource().Masters(1_000_000, 200_000, 195_000, 190_844, 1696, 900));

        Assert.Equal(1, await MoveClassHarvestWorker.HarvestBooksAsync(db, book, 10));
        Assert.Equal(0, await MoveClassHarvestWorker.HarvestBooksAsync(db, book, 10));   // geprüft ist geprüft

        var done = await db.GameAnalyses.AsNoTracking().ToDictionaryAsync(a => a.Id);
        Assert.Equal("0,1,2,3,4,5", done[990201].MasterBookPlies);
        Assert.Equal("KKKKKK", done[990201].MoveClasses);
        Assert.Null(done[990202].MasterBookPlies);   // unfertig: nicht anfassen

        var head = await GameEvalsStore.Heads(db.GameAnalyses.Where(a => a.Id == 990201)).SingleAsync();
        var evals = await GameEvalsStore.ReadAsync(db, head, null);
        Assert.Equal([0, 1, 2, 3, 4, 5], evals.MasterBookPlies);
        Assert.Empty(evals.BookPlies);   // das Repertoire-Buch des Betrachters ist etwas anderes
    }

    [Fact]
    public async Task HarvestBooks_ExplorerAusfall_laesstOffen_undKlassenUnveraendert_spaeterNachgeholt()
    {
        await using var db = Db();
        foreach (var id in new[] { 990211, 990212, 990213, 990214, 990215 })
            db.GameAnalyses.Add(new GameAnalysis { Id = id, UserId = 1, Pgn = "", StartFen = "", Status = GameAnalysisStatus.Done, PlyCount = 6,
                MoveClasses = "SSSSIS", MoveClassesVersion = GameMoveClasses.Version, Positions = Positions() });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var source = new FakeSource { Down = true }.Masters(1_000_000, 200_000, 195_000, 190_844, 1696, 900);
        var book = new GameMasterBook(source);

        Assert.Equal(0, await MoveClassHarvestWorker.HarvestBooksAsync(db, book, 10));
        Assert.Equal(MoveClassHarvestWorker.BookMaxMisses, source.Calls);   // nach drei Analysen ohne Antwort hört der Schritt auf
        Assert.All(await db.GameAnalyses.AsNoTracking().ToListAsync(), a => { Assert.Null(a.MasterBookPlies); Assert.Equal("SSSSIS", a.MoveClasses); });
        Assert.Equal(0, await MoveClassHarvestWorker.HarvestBooksAsync(db, null, 10));   // ohne Dienst: nichts

        source.Down = false;
        Assert.Equal(5, await MoveClassHarvestWorker.HarvestBooksAsync(db, book, 10));
        Assert.All(await db.GameAnalyses.AsNoTracking().ToListAsync(), a => Assert.Equal("KKKKKK", a.MoveClasses));
    }

    [Fact]
    public void TrainMarks_MeisterBuchzug_UngenauigkeitZaehltNicht_FehlerSchon()
    {
        GameEvalsDto Game(int after, params int[] book) => new()
        {
            Status = "done", Total = 2, MasterBookPlies = book.ToList(), Plies = new()
            {
                new() { Ply = 0, Cp = 79, BestUci = "b1c3", PlayedUci = "f1d3" }, new() { Ply = 1, Cp = after },
            }, Final = new() { Cp = after },
        };
        Assert.Equal(new[] { 1 }, OgMetaService.TrainMarks(Game(-9), white: true));
        Assert.Empty(OgMetaService.TrainMarks(Game(-9, 0), white: true));
        Assert.Equal(new[] { 1 }, OgMetaService.TrainMarks(Game(-60, 0), white: true));
    }
}
