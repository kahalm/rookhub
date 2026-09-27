using RookHub.Api.Services;
using static RookHub.Api.Services.KidsCurriculum;

namespace RookHub.Api.Tests;

/// <summary>
/// Auswahl der „besonders einfachen" Lichess-Puzzles fuer die Kinderseite (<see cref="KidsCurriculum"/>).
/// Die Beispiele sind echte Puzzles aus dem Lichess-Bestand (CC0), damit die Themen-Erkennung an der
/// Form geprueft wird, die der Import wirklich liefert.
/// </summary>
public class KidsCurriculumTests
{
    private static Candidate Real(string lichessId, int rating, string themes, string fen, string moves,
        int id = 1, int rd = 80, int popularity = 95, int plays = 500) =>
        new(id, lichessId, rating, rd, popularity, plays, themes, fen, moves);

    // Echte Beispiele (Lichess-Id, Rating, Themen, FEN vor dem Stellungszug, Zugfolge).
    private static readonly Candidate Mate1 = Real("04jun", 764, "endgame mate mateIn1 oneMove rookEndgame",
        "1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73", "g4g3 b8h8");
    private static readonly Candidate Promote = Real("dfuUu", 895, "advancedPawn endgame equality oneMove promotion",
        "6R1/1b2n2P/kp2r3/8/8/8/3K4/8 b - - 28 64", "e7g8 h7g8q");
    private static readonly Candidate Fork = Real("01mZT", 858, "crushing endgame fork master short",
        "8/8/2k5/8/8/r2NK1P1/5P2/8 b - - 1 63", "a3a2 d3b4 c6d6 b4a2");
    private static readonly Candidate Mate2 = Real("0BdlU", 793, "endgame mate mateIn2 short",
        "7r/8/8/KPk5/3N4/8/4R3/8 w - - 7 47", "d4c6 h8a8 c6a7 a8a7");
    private static readonly Candidate Capture = Real("0CYEU", 796, "crushing endgame hangingPiece short",
        "8/2b5/8/2k2p2/1p3Pp1/1P2N1P1/3K4/8 b - - 15 62", "c7f4 g3f4 c5d4 e3f5");
    private static readonly Candidate CrowdedMate1 = Real("001KR", 645, "mate mateIn1 middlegame oneMove",
        "6Qk/p1p3pp/4N3/1p6/2q1r1n1/2B5/PP4PP/3R1R1K b - - 0 28", "h8g8 f1f8");

    [Fact]
    public void Classify_ErkenntDieThemenAnEchtenPuzzles()
    {
        Assert.Equal("mate1", Classify(Mate1));
        Assert.Equal("promote", Classify(Promote));
        Assert.Equal("fork", Classify(Fork));
        Assert.Equal("mate2", Classify(Mate2));
        Assert.Equal("capture", Classify(Capture));
    }

    [Fact]
    public void Classify_ZuVieleFigurenFallenRaus()
    {
        Assert.True(CountPieces(CrowdedMate1.Fen) > MaxPieces);
        Assert.Null(Classify(CrowdedMate1));
    }

    [Theory]
    [InlineData(901, 80, 95, 500)]  // Rating zu hoch
    [InlineData(700, 91, 95, 500)]  // Rating zu unsicher
    [InlineData(700, 80, 79, 500)]  // unbeliebt
    [InlineData(700, 80, 95, 49)]   // zu selten gespielt
    public void Classify_NurLeichteUndErprobtePuzzles(int rating, int rd, int popularity, int plays)
    {
        var c = Mate1 with { Rating = rating, RatingDeviation = rd, Popularity = popularity, NbPlays = plays };
        Assert.Null(Classify(c));
    }

    [Theory]
    [InlineData("mate mateIn1 oneMove enPassant")]
    [InlineData("mate mateIn1 oneMove castling")]
    [InlineData("promotion underPromotion oneMove")]
    public void Classify_VerwirrendeThemenFallenRaus(string themes) =>
        Assert.Null(Classify(Mate1 with { Themes = themes }));

    [Fact]
    public void Classify_DreiEigeneZuegeSindZuLang() =>
        Assert.Null(Classify(Mate1 with { Moves = "g4g3 b8h8 h2g2 h8h1 g2h1 a1a2", Themes = "mateIn3" }));

    [Fact]
    public void Classify_UmwandelnNurWennDerLoesungszugEineDameMacht() =>
        // Themen-Tag allein genuegt nicht: der eigene Zug muss selbst umwandeln.
        Assert.Null(Classify(Promote with { Moves = "e7g8 g8h8" }));

    [Fact]
    public void Classify_FreieFigurNurWennDerErsteZugSchlaegt()
    {
        // Gleiche Stellung, aber der erste eigene Zug geht auf ein leeres Feld.
        Assert.Null(Classify(Capture with { Moves = "c7f4 e3c2 c5d4 c2d4" }));
    }

    [Fact]
    public void IsCapture_RechnetDenStellungszugEin()
    {
        // Nach Lf4 (Stellungszug) steht der Laeufer auf f4 — dort schlaegt g3xf4.
        Assert.True(IsCapture(Capture.Fen, "c7f4", "g3f4"));
        // Vor dem Stellungszug war f4 ein weisser Bauer, e3 ein Springer: e3→c4 schlaegt nichts.
        Assert.False(IsCapture(Capture.Fen, "c7f4", "e3c4"));
    }

    [Fact]
    public void IsCapture_RochadeDesGegnersVersetztDenTurm()
    {
        // Schwarz rochiert kurz (Stellungszug): Turm h8 → f8. Danach steht auf f8 etwas, auf h8 nicht mehr.
        const string fen = "4k2r/8/8/8/8/8/8/5Q1K b k - 0 1";
        Assert.True(IsCapture(fen, "e8g8", "f1f8"));
        Assert.False(IsCapture(fen, "e8g8", "f1h3"));
        Assert.False(IsCapture("4k2r/8/8/8/8/8/8/K6Q b k - 0 1", "e8g8", "h1h8"));
    }

    // ---- Leiter ---------------------------------------------------------------------------------

    /// <summary>Synthetisches Puzzle mit <paramref name="pieces"/> Figuren; Themen/Zuege bestimmen das Thema.</summary>
    private static Candidate Synthetic(int id, int pieces, int rating, string themes, string moves) =>
        new(id, $"L{id:D5}", rating, 80, 95, 500, themes, FenWith(pieces), moves);

    private static string FenWith(int pieces)
    {
        var letters = "kK" + new string('P', Math.Max(0, pieces - 2));
        var ranks = Enumerable.Range(0, 8)
            .Select(r => letters.Length > r * 8 ? letters.Substring(r * 8, Math.Min(8, letters.Length - r * 8)) : "")
            .Select(row => row.Length == 8 ? row : row + (8 - row.Length))
            .ToList();
        return string.Join('/', ranks) + " w - - 0 1";
    }

    private static List<Candidate> Mate1Pool(int count, int startId = 1) =>
        Enumerable.Range(0, count)
            .Select(i => Synthetic(startId + i, 4 + i % 9, 600 + (i * 7) % 300, "mate mateIn1 oneMove", "a2a3 b2b3"))
            .ToList();

    [Fact]
    public void Select_ErsteStufeHatDieWenigstenFiguren()
    {
        var placements = Select(Mate1Pool(400));
        var first = placements.Where(p => p.Level == 1).ToList();
        Assert.Equal(PuzzlesPerLevel, first.Count);
        Assert.All(first, p => Assert.Equal("mate1", p.Theme));
        var avgByLevel = placements.GroupBy(p => p.Level).ToDictionary(g => g.Key, g => g.Average(p => p.PieceCount));
        Assert.All(avgByLevel.Where(kv => kv.Key > 1), kv => Assert.True(avgByLevel[1] <= kv.Value,
            $"Stufe 1 soll die leichtesten Aufgaben tragen (Stufe {kv.Key}: {kv.Value} Figuren im Schnitt, Stufe 1: {avgByLevel[1]})"));
        Assert.Equal(Enumerable.Range(0, PuzzlesPerLevel), first.Select(p => p.Position));
    }

    [Fact]
    public void Select_FehlendeThemenLassenKeineLueckeInDenNummern()
    {
        // Nur Matt-in-1 im Bestand: alle anderen Stufen fallen weg, die Nummern bleiben 1..n.
        var placements = Select(Mate1Pool(400));
        var levels = placements.Select(p => p.Level).Distinct().OrderBy(l => l).ToList();
        var mate1Levels = Levels.Count(l => l == "mate1");
        Assert.Equal(Enumerable.Range(1, mate1Levels), levels);
    }

    [Fact]
    public void Select_JedesPuzzleHoechstensEinmal()
    {
        var placements = Select(Mate1Pool(400));
        Assert.Equal(placements.Count, placements.Select(p => p.PuzzleId).Distinct().Count());
    }

    [Fact]
    public void Select_StufenEinesThemasWerdenSchwerer()
    {
        var placements = Select(Mate1Pool(400));
        var avgByLevel = placements.GroupBy(p => p.Level).OrderBy(g => g.Key)
            .Select(g => g.Average(p => p.PieceCount)).ToList();
        Assert.True(avgByLevel.First() < avgByLevel.Last());
    }

    [Fact]
    public void Select_ZuDuennesThemaFaelltWeg()
    {
        // Zwei Umwandlungen reichen fuer keine Stufe (Minimum 3), der Rest bleibt stehen.
        var pool = Mate1Pool(400);
        pool.Add(Synthetic(9001, 5, 700, "promotion oneMove", "a2a3 b7b8q"));
        pool.Add(Synthetic(9002, 5, 710, "promotion oneMove", "a2a3 c7c8q"));
        var placements = Select(pool);
        Assert.DoesNotContain(placements, p => p.Theme == "promote");
    }

    [Fact]
    public void Select_IstDeterministischUnabhaengigVonDerReihenfolge()
    {
        var pool = Mate1Pool(400);
        var a = Select(pool);
        var b = Select(Enumerable.Reverse(pool).ToList());
        Assert.Equal(a, b);
    }

    [Fact]
    public void Select_ZaehltEigeneZuege()
    {
        var pool = Mate1Pool(400);
        pool.AddRange(Enumerable.Range(0, 100)
            .Select(i => Synthetic(5000 + i, 5 + i % 5, 650 + i, "fork short", "a2a3 b2b3 c7c6 b3b4")));
        var placements = Select(pool);
        Assert.All(placements.Where(p => p.Theme == "fork"), p => Assert.Equal(2, p.SolverMoves));
        Assert.All(placements.Where(p => p.Theme == "mate1"), p => Assert.Equal(1, p.SolverMoves));
        Assert.Contains(placements, p => p.Theme == "fork");
    }

    [Fact]
    public void Lehrplan_KenntNurBekannteThemen() =>
        Assert.All(Levels, l => Assert.Contains(l, KidsCurriculum.Themes));
}
