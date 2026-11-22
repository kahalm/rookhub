using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using Xunit.Abstractions;

namespace RookHub.Api.Tests.Engines;

/// <summary>Ein Maia, das feste Verteilungen je Stellung liefert (ohne Modell).</summary>
internal sealed class FakeMaia(Dictionary<string, List<MaiaMove>> byFen) : MaiaService(null, NullLogger<MaiaService>.Instance)
{
    public List<(string Fen, int Self, int Oppo)> Calls { get; } = [];
    public override bool Available => true;
    public override IReadOnlyList<MaiaMove>? Predict(string fen, int eloSelf, int eloOppo)
    {
        Calls.Add((fen, eloSelf, eloOppo));
        return byFen.TryGetValue(fen, out var moves) ? moves : [];
    }
}

/// <summary>
/// Die natürlichen Züge laut Maia in den Fehler-Erklärungen (1.003.0, <see cref="MaiaNaturalMoves"/>) — an den Entscheidungen
/// des Verteidigers (<see cref="MoveComparisonDefences"/>) und als Antworten auf den besten Zug
/// (<see cref="MoveComparisonBestMoveTries"/>). Regressionsfälle: Vereinspartie 259 (Vergleich 2839, nach 44.g3 fehlte 44...h3)
/// und Vereinspartie 260 (Vergleich 2832, nach 22...Ld7 fehlte 23.Sxd7 — es hält, löst die Fesselung aber nicht).
/// Die Zahlen der Engine-Zeilen sind die der Prod-Zeilen (Tiefe 30) bzw. von Stockfish nachgerechnet (Tiefe 24).
/// </summary>
public class MaiaNaturalMovesTests(ITestOutputHelper output) : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private static MaiaNaturalMoves Fixed(Dictionary<string, List<MaiaMove>> byFen, double min = 0.15, int white = 1600, int black = 1600)
        => new((fen, _, _) => byFen.TryGetValue(fen, out var m) ? m : [], white, black, min);

    private static string Play(string fen, params string[] ucis)
    {
        foreach (var uci in ucis) fen = MoveComparisonBoard.PlayUci(fen, uci)!;
        return fen;
    }

    private static MoveComparisonLine Candidate(string fen, string uci, int ordinal, string json) => new()
    {
        Kind = MoveComparisonLineKind.Candidate, CandidateUci = uci, Ordinal = ordinal, Fen = Play(fen, uci),
        State = MoveComparisonLineState.Done, ResultJson = json, ReachedDepth = 30,
    };

    // ── Schwelle, Reihenfolge, Rückfall ────────────────────────────────────────────────────────────

    [Fact]
    public void At_Schwelle_absteigend_mitStaerkeDerSeiteAmZug()
    {
        const string fen = "8/1b6/p5kb/8/2NB2pp/P5P1/1P6/5K2 b - - 0 44";
        var calls = new List<(int, int)>();
        var natural = new MaiaNaturalMoves((f, self, oppo) => { calls.Add((self, oppo)); return [new("h4g3", 0.226), new("h4h3", 0.763), new("b7d5", 0.004)]; },
            whiteElo: 1600, blackElo: 1876, minProbability: 0.15);

        var moves = natural.At(fen);
        Assert.Equal(["h4h3", "h4g3"], moves.Select(m => m.Uci));
        Assert.Equal((1876, 1600), Assert.Single(calls));   // Schwarz am Zug: elo_self = Schwarz
        Assert.Equal("a natural move for a ~1876 player (Maia: 76 %)", moves[0].Note);
        natural.At(fen);
        Assert.Single(calls);                               // je Stellung einmal
        Assert.Empty(MaiaNaturalMoves.None.At(fen));
        Assert.Equal(["h4h3"], Fixed(new() { [fen] = [new("h4h3", 0.763), new("h4g3", 0.226)] }, min: 0.3).At(fen).Select(m => m.Uci));
    }

    /// <summary>3...d6 statt 3...Sf6, Widerlegung 4.Sxe5 De7: Schwarz hat an der Stelle die Schläge dxe5/Sxe5 — und laut (hier
    /// fest vorgegebenem) Maia das ruhige 4...Sf6.</summary>
    private const string FenD6 = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 2 3";

    private static MoveComparison ComparisonD6()
    {
        var c = new MoveComparison { Fen = FenD6, Depth = 30, BestUci = "g8f6" };
        c.Lines.Add(Candidate(FenD6, "g8f6", 0, """{"depth":30,"pvs":[{"moves":["b1c3","f8b4"],"cp":20}]}"""));
        c.Lines.Add(Candidate(FenD6, "d7d6", 1, """{"depth":30,"pvs":[{"moves":["f3e5","d8e7","e5c6","b7c6"],"cp":150}]}"""));
        return c;
    }

    private static readonly string AfterNxe5 = Play(FenD6, "d7d6", "f3e5");

    private static MaiaNaturalMoves NaturalD6(double min = 0.15) => Fixed(new()
    {
        // De7 ist der Zug der Linie (zählt nicht), dxe5 ist ohnehin ein Schlag, Sf6 ist ruhig und natürlich.
        [AfterNxe5] = [new("d8e7", 0.35), new("d6e5", 0.30), new("g8f6", 0.20), new("a7a6", 0.02)],
    }, min);

    [Fact]
    public void Defences_NatuerlicheZuegeKommenDazu_vorDenAnderen_nachWahrscheinlichkeit()
    {
        Assert.Equal(["d6e5", "c6e5"], MoveComparisonDefences.Plan(ComparisonD6(), 4).Select(l => l.ReplyUci));   // ohne Maia wie vorher
        Assert.Equal(["d6e5", "c6e5"], MoveComparisonDefences.Plan(ComparisonD6(), 4, MaiaNaturalMoves.None).Select(l => l.ReplyUci));

        var lines = MoveComparisonDefences.Plan(ComparisonD6(), 4, NaturalD6());
        Assert.Equal(["d6e5", "g8f6", "c6e5"], lines.Select(l => l.ReplyUci));
        Assert.All(lines, l => Assert.Equal(1, l.Ordinal));
        // Schwelle: Sf6 (20 %) fällt bei 25 % weg; Deckel: zwei Zeilen, die natürlichen zuerst.
        Assert.Equal(["d6e5", "c6e5"], MoveComparisonDefences.Plan(ComparisonD6(), 4, NaturalD6(0.25)).Select(l => l.ReplyUci));
        Assert.Equal(["d6e5", "g8f6"], MoveComparisonDefences.Plan(ComparisonD6(), 2, NaturalD6()).Select(l => l.ReplyUci));
        Assert.Empty(MoveComparisonDefences.Plan(ComparisonD6(), 0, NaturalD6()));
    }

    private static MoveComparison ExplainedD6(MaiaNaturalMoves natural)
    {
        var c = ComparisonD6();
        foreach (var line in MoveComparisonDefences.Plan(c, 4, natural))
        {
            line.ResultJson = line.ReplyUci switch
            {
                "g8f6" => """{"depth":30,"pvs":[{"moves":["e5c6","b7c6","d2d3"],"cp":160}]}""",
                "d6e5" => """{"depth":30,"pvs":[{"moves":["d1h5"],"cp":120}]}""",
                _ => """{"depth":30,"pvs":[{"moves":["d2d4"],"cp":140}]}""",
            };
            line.State = MoveComparisonLineState.Done;
            line.ReachedDepth = 30;
            c.Lines.Add(line);
        }
        return c;
    }

    [Fact]
    public void Defences_Faktenblock_nenntDenNatuerlichenZugMitMaiaVermerk_ohneMaiaOhneVermerkAberDa()
    {
        var c = ExplainedD6(NaturalD6());
        var weak = c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == "d7d6");
        var prompt = ClubFableExplanations.UserPrompt(MoveComparisonPrompts.PairFactsOf(c, weak, natural: NaturalD6())!);
        Assert.Contains("- 4...Nf6 (a natural move for a ~1600 player (Maia: 20 %)) instead of 4...Qe7:", prompt);
        Assert.Contains("- 4...dxe5 (a natural move for a ~1600 player (Maia: 30 %)) instead of 4...Qe7:", prompt);
        Assert.Contains("- 4...Nxe5 instead of 4...Qe7:", prompt);
        Assert.True(prompt.IndexOf("- 4...Nf6", StringComparison.Ordinal) < prompt.IndexOf("- 4...Nxe5", StringComparison.Ordinal));

        // Fällt Maia zwischen Planen und Erklären aus, bleibt die gerechnete Zeile — nur ohne Vermerk.
        var without = ClubFableExplanations.UserPrompt(MoveComparisonPrompts.PairFactsOf(c, weak)!);
        Assert.Contains("- 4...Nf6 instead of 4...Qe7:", without);
        Assert.DoesNotContain("Maia:", without);
    }

    [Fact]
    public void Auftrag_NatuerlicheZuegeSindDieFragenDesLesers()
    {
        var p = ClubFableExplanations.SystemPrompt.Replace("\r", "");
        Assert.Contains("„a natural move for a ~1600 player (Maia: 38 %)“", p);
        var flat = System.Text.RegularExpressions.Regex.Replace(p, @"\s+", " ");
        Assert.Contains("er ist die Frage, die der Leser hat. Erkläre ihn immer: warum er scheitert, bzw. dass und warum er hält.", flat);
        Assert.Contains("Die Bewertung eines Zuges ist die nach der BESTEN Antwort des Gegners", flat);
        Assert.Contains("„23.Sxd7 Txd7 löst die Fesselung nicht; auf 24.e4 rettet 24...Tc7 mit Angriff auf die Dame“", flat);
        Assert.Contains("a line marked \"WEAKER answer\" never changes", MoveComparisonPrompts.SystemPrompt("de"));
    }

    // ── Vereinspartie 259, Vergleich 2839: 44.Se5+ statt 44.g3 — Schwarz antwortet natürlich 44...h3 ─────────────────

    private const string Fen2839 = "8/1b6/p5kb/8/2NB2pp/P7/1P4P1/5K2 w - - 0 44";
    private const string Best2839 = """{"depth":30,"pvs":[{"moves":["h4g3","f1g1","b7f3","b2b4","g6h5","c4e3","f3c6","d4c5","c6b5","g1g2","h6f4","e3c2","f4b8","c5e3","h5g6"],"cp":-164}]}""";
    private const string Weak2839 = """{"depth":30,"pvs":[{"moves":["g6f5","e5c4","b7c6","c4e3","f5f4","d4c5","h6g5","b2b3","c6d7","e3d5","f4e4","d5c7","a6a5","c5b6","g5d2","f1g1"],"cp":-271}]}""";
    private const string Glance2839 = """{"time":1,"depth":1,"nodes":689,"pvs":[{"moves":["h4h3","c4e5"],"cp":-258,"depth":1},{"moves":["b7d5"],"cp":-143,"depth":1},{"moves":["h4g3"],"cp":-139,"depth":1},{"moves":["h6g7","d4g7"],"cp":-51,"depth":1},{"moves":["b7e4","g3h4"],"cp":-34,"depth":1}]}""";
    private const string H3Json = """{"depth":30,"pvs":[{"moves":["f1g1","h6f8","c4e3","f8d6","g1h2","g6g5","d4b6","b7c8","b2b4","c8e6"],"cp":-86}]}""";

    private static MoveComparison Comparison2839(bool glance = true)
    {
        var c = new MoveComparison { Fen = Fen2839, Depth = 30, BestUci = "g2g3", Language = "de", Status = MoveComparisonStatus.Explaining };
        c.Lines.Add(Candidate(Fen2839, "g2g3", 0, Best2839));
        c.Lines.Add(Candidate(Fen2839, "c4e5", 1, Weak2839));
        if (glance)
            c.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.BestMoveGlance, CandidateUci = "g2g3", Ordinal = 0,
                Fen = Play(Fen2839, "g2g3"), State = MoveComparisonLineState.Done, ResultJson = Glance2839, ReachedDepth = 1 });
        return c;
    }

    /// <summary>Was Maia-3 (gemessen am echten Modell, 1876 gegen 1600) nach 44.g3 vorhersagt.</summary>
    private static MaiaNaturalMoves Natural2839() => Fixed(new()
    {
        [Play(Fen2839, "g2g3")] = [new("h4h3", 0.76), new("h4g3", 0.23), new("b7d5", 0.004)],
    }, white: 1600, black: 1876);

    [Fact]
    public void Fall2839_h3_istNatuerlich_wirdGerechnet_undStehtImPrompt_obwohlEsNichtScheitert()
    {
        var c = Comparison2839(glance: false);   // auch ohne flache Sicht (gescheitert): Maia allein reicht
        var lines = MoveComparisonBestMoveTries.Plan(c, MoveComparisonBestMoveTries.Settings.Default, Natural2839());
        // hxg3 ist die beste Antwort der tiefen Rechnung (zählt nicht); h3 ist natürlich.
        Assert.Equal("h4h3", Assert.Single(lines).ReplyUci);
        Assert.Empty(MoveComparisonBestMoveTries.Plan(c, MoveComparisonBestMoveTries.Settings.Default));   // ohne Maia, ohne flache Sicht: nichts

        c = Comparison2839();
        var row = MoveComparisonBestMoveTries.Plan(c, MoveComparisonBestMoveTries.Settings.Default, Natural2839()).First();
        Assert.Equal("h4h3", row.ReplyUci);
        row.ResultJson = H3Json;
        row.State = MoveComparisonLineState.Done;
        c.Lines.Add(row);

        var weak = c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == "c4e5");
        var facts = MoveComparisonPrompts.PairFactsOf(c, weak, natural: Natural2839())!;
        var h3 = facts.Tries!.Single(t => t.San == "h3");
        // Aus Sicht von Weiß −0,86 statt −1,64 nach 44...hxg3: für Schwarz 0,78 schlechter, unter der Schwelle von 1,0 —
        // vor 1.003.0 fiel h3 deshalb still weg.
        Assert.Equal(MoveComparisonBestMoveTries.Verdict.Weaker, h3.Verdict);
        var prompt = ClubFableExplanations.UserPrompt(facts);
        Assert.Contains("- 44...h3 (a natural move for a ~1876 player (Maia: 76 %); looks good at first sight: the shallow search (depth 1) "
            + "ranks it 1st of", prompt);
        Assert.Contains("weaker for the opponent than the best answer, but it does not lose", prompt);
        Assert.Contains("h3", facts.AllowedMoves());
        Assert.Contains("Kg1", facts.AllowedMoves());
    }

    [Fact]
    public void Fall2839_amEchtenModell_h3istNatuerlich()
    {
        if (MaiaServiceTests.ModelPath() is not { } path) return;
        using var maia = MaiaServiceTests.RealService(path);
        var natural = new MaiaNaturalMoves(maia.Predict, 1600, 1876, MaiaNaturalMoves.DefaultMinProbability);
        var fen = Play(Fen2839, "g2g3");
        var policy = maia.Predict(fen, 1876, 1600)!;
        foreach (var uci in new[] { "h4g3", "h4h3", "b7e4" })
            output.WriteLine($"{uci}: {policy.First(m => m.Uci == uci).P:P2} (Schwarz 1876 gegen 1600)");
        var at1600 = maia.Predict(fen, 1600, 1600)!;
        foreach (var uci in new[] { "h4g3", "h4h3", "b7e4" })
            output.WriteLine($"{uci}: {at1600.First(m => m.Uci == uci).P:P2} (1600 gegen 1600)");

        Assert.Equal("h4h3", natural.At(fen)[0].Uci);
        var lines = MoveComparisonBestMoveTries.Plan(Comparison2839(), MoveComparisonBestMoveTries.Settings.Default, natural);
        Assert.Equal("h4h3", lines[0].ReplyUci);
    }

    // ── Vereinspartie 260, Vergleich 2832: 22...b6 statt 22...Ld7 — 23.Sxd7 hält, löst die Fesselung aber nicht ────────

    private const string Fen2832 = "1rbr2k1/1p3ppp/p3p3/q1Nn4/8/P5P1/1PQ1PPBP/2RR2K1 b - - 4 22";
    private const string Best2832 = """{"depth":30,"pvs":[{"moves":["b2b4","a5b6","g2d5","e6d5","d1d5","d7b5","d5d8","b8d8","c1d1","d8c8","f2f3","c8e8","d1d2","b6h6","c5e4"],"cp":125}]}""";
    private const string Weak2832 = """{"depth":30,"pvs":[{"moves":["c5b3","a5a4","e2e4","h7h6","e4d5","e6d5","b3d4","a4c2","c1c2","c8g4","g2f3","g4d7","d4c6","d7c6","c2c6","d5d4"],"cp":680},{"moves":["b2b4","a5b5","e2e4","b6c5","b4c5","c8b7","e4d5","b7d5","g2d5","e6d5","c5c6","b8c8","d1d4","a6a5","a3a4","b5b6"],"cp":216},{"moves":["c2b3","b8a8","c5a4","a8b8","a4c3","c8b7","c3d5","b7d5","g2d5","e6d5","d1d4","g7g6","c1d1","b6b5","d4d5","d8d5"],"cp":205}]}""";
    private const string Glance2832 = """{"time":9,"depth":1,"nodes":1009,"pvs":[{"moves":["b2b4"],"cp":124,"depth":1},{"moves":["g2d5"],"cp":100,"depth":1},{"moves":["c5d7","d8d7"],"cp":72,"depth":1},{"moves":["c2d2","a5d2","d1d2"],"cp":66,"depth":1},{"moves":["e2e4"],"cp":62,"depth":1}]}""";
    private const string Nb3Json = """{"depth":30,"pvs":[{"moves":["a5b6","g2d5","e6d5","b3d4","b8c8","c2d2"],"cp":9}]}""";
    private const string B4Json = """{"depth":28,"pvs":[{"moves":["a5b6","g2d5","e6d5","d1d5","d7b5","d5d8","b8d8"],"cp":125}]}""";
    private const string Qb3Json = """{"depth":30,"pvs":[{"moves":["d7c6","h2h4","h7h6","g2f3","a5c7"],"cp":0}]}""";
    private const string Bxd5Json = """{"depth":30,"pvs":[{"moves":["e6d5","d1d5","d7c6","d5h5","h7h6","c5b7","a5h5","b7d8","b8d8"],"cp":127}]}""";
    /// <summary>23.Sxd7 Txd7 24.e4 Tc7! 25.Dd3 Txc1 26.Txc1 Sf6 (Stockfish Tiefe 24, Weiß +1,19).</summary>
    private const string Nxd7Json = """{"depth":24,"pvs":[{"moves":["d8d7","e2e4","d7c7","c2d3","c7c1","d1c1","d5f6"],"cp":119}]}""";

    private static MoveComparison Explained2832()
    {
        var c = new MoveComparison { Fen = Fen2832, Depth = 30, BestUci = "c8d7", Language = "de", Status = MoveComparisonStatus.Explaining };
        c.Lines.Add(Candidate(Fen2832, "c8d7", 0, Best2832));
        c.Lines.Add(Candidate(Fen2832, "b7b6", 1, Weak2832));
        var best = Play(Fen2832, "c8d7");
        var k = 0;
        foreach (var (uci, json) in new[] { ("c5b3", Nb3Json), ("b2b4", B4Json), ("c2b3", Qb3Json) })
            c.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.Reply, CandidateUci = "b7b6", ReplyUci = uci, Ordinal = k++,
                Fen = Play(best, uci), State = MoveComparisonLineState.Done, ResultJson = json, ReachedDepth = 30 });
        c.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.BestMoveGlance, CandidateUci = "c8d7", Ordinal = 0,
            Fen = best, State = MoveComparisonLineState.Done, ResultJson = Glance2832, ReachedDepth = 1 });
        return c;
    }

    [Fact]
    public void Fall2832_SxD7_schlaegtDieGezogeneFigur_wirdGerechnet_haelt_undDieFesselungStehtDa()
    {
        var c = Explained2832();
        // 23.Sxd7 ist Dritter der flachen Sicht, 0,52 hinter 23.b4 (über der Schwelle 0,5 — vor 1.003.0 kam es deshalb nie dran);
        // als Schlag der gerade gezogenen Figur reicht seit 1.003.0 das Doppelte. 23.Lxd5 ist verlockend; 23.b4 die beste Antwort.
        var plan = MoveComparisonBestMoveTries.Plan(c, MoveComparisonBestMoveTries.Settings.Default);
        Assert.Equal(["g2d5", "c5d7"], plan.Select(l => l.ReplyUci));
        foreach (var line in plan)
        {
            line.ResultJson = line.ReplyUci == "c5d7" ? Nxd7Json : Bxd5Json;
            line.State = MoveComparisonLineState.Done;
            c.Lines.Add(line);
        }

        var weak = c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == "b7b6");
        var facts = MoveComparisonPrompts.PairFactsOf(c, weak)!;
        var nxd7 = facts.Tries!.Single(t => t.San == "Nxd7");
        Assert.Equal(MoveComparisonBestMoveTries.Verdict.Holds, nxd7.Verdict);
        Assert.True(nxd7.TakesMoved);
        var prompt = ClubFableExplanations.UserPrompt(facts);
        output.WriteLine(prompt[..prompt.IndexOf("\n\nHinweis", StringComparison.Ordinal)]);

        Assert.Contains("- 23.Nxd7 (takes the bishop on d7, the piece that just moved; looks good at first sight: the shallow search (depth 1) "
            + "ranks it 3rd of", prompt);
        // Mehr als ein Tausch: die Fesselung bleibt, 24.e4 greift den gefesselten Springer an, 24...Tc7 rettet mit Angriff auf die Dame.
        Assert.Contains("about as good as the best answer, so it does NOT fail, but it is more than a trade", prompt);
        Assert.Contains("after 23.Nxd7 Rxd7 the knight on d5 is pinned to the rook on d7 by the rook on d1", prompt);
        Assert.Contains("24.e4 attacks the pinned knight on d5", prompt);
        Assert.Contains("24...Rc7 answers with an attack on the queen on c2", prompt);
        Assert.True(GameMoveExplanationService.MentionsOnly(
            ClubFableExplanations.EnglishSan("23.Sxd7 Txd7 löst die Fesselung nicht; auf 24.e4 rettet 24...Tc7 mit Angriff auf die Dame."),
            facts.AllowedMoves()));
        // 23.Lxd5 hält ebenfalls; auch dort fesselt danach 24.Txd5 den Läufer d7 vor dem Turm d8, 24...Lc6 antwortet mit Abzug.
        Assert.Contains("- 23.Bxd5 (takes the knight on d5; looks good at first sight", prompt);
        Assert.Contains("24.Rxd5 attacks the pinned bishop on d7; 24...Bc6 answers with an attack on the rook on d5", prompt);

        // (b) Der Test 23.Sb3 Db6 (≈ 0) ist eine SCHWÄCHERE Antwort: der beste Zug steht nach 23.b4 bei −1,25 (Leser Schwarz).
        Assert.Contains("- 23.Nb3: evaluation from the reader's view -0.09", prompt);
        Assert.Contains("This is a WEAKER answer for the opponent: after his best answer the better move stands at -1.25", prompt);
        Assert.DoesNotContain("- 23.b4: evaluation from the reader's view -1.25 (clearly worse). This is a WEAKER", prompt);
    }

    [Fact]
    public void Pins_StellungNach24e4_TurmZiehtWeg_FesselungWeg()
    {
        // Stellung nach 23.Sxd7 Txd7 24.e4 (vom Koordinator mit Stockfish nachgerechnet: 24...Tc7 +1,19, 24...Sf6 +1,32).
        const string fen = "1r4k1/1p1r1ppp/p3p3/q2n4/4P3/P5P1/1PQ2PBP/2RR2K1 b - - 0 24";
        var facts = ExplanationFacts.Pins(fen, ["Rc7", "Qd3"]);
        Assert.Contains("the knight on d5 is pinned to the rook on d7 by the rook on d1", facts);
        Assert.Contains("after 24...Rc7 25.Qd3 the pin on the knight on d5 is gone", facts);
        // Ohne Fesselung: nichts.
        Assert.Empty(ExplanationFacts.Pins("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", ["e5", "Nf3"]));
    }

    // ── Stärke aus der Partie ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ForAnalysis_EloAusVereinspartie_sonstGespeichertePartie_sonstPgn_sonstNichts()
    {
        _db.LeagueClubGames.Add(new LeagueClubGame { Id = 991259, ClubId = 1, Year = 2026, White = "A", Black = "B", Pgn = "", MovesHash = "h", BlackElo = 1876 });
        _db.GameAnalyses.Add(new GameAnalysis { Id = 991001, UserId = 1, Pgn = "[WhiteElo \"1750\"]\n1. e4 *", StartFen = "", LeagueClubGameId = 991259 });
        _db.GameAnalyses.Add(new GameAnalysis { Id = 991002, UserId = 1, Pgn = "[WhiteElo \"1500\"]\n[BlackElo \"1450\"]\n1. e4 *", StartFen = "" });
        _db.SavedGames.Add(new SavedGame { Id = 991003, UserId = 1, Pgn = "", WhiteElo = 2010, GameAnalysisId = 991002 });
        _db.GameAnalyses.Add(new GameAnalysis { Id = 991004, UserId = 1, Pgn = "1. e4 *", StartFen = "" });
        await _db.SaveChangesAsync();

        Assert.Equal((1750, 1876), await MaiaNaturalMoves.ForAnalysisAsync(_db, 991001));   // Schwarz aus der Partie, Weiß aus der PGN
        Assert.Equal((2010, 1450), await MaiaNaturalMoves.ForAnalysisAsync(_db, 991002));
        Assert.Equal(((int?)null, (int?)null), await MaiaNaturalMoves.ForAnalysisAsync(_db, 991004));

        var maia = new FakeMaia(new() { [Fen2839] = [new("g2g3", 0.5)] });
        var none = await MaiaNaturalMoves.ForAsync(_db, maia, 0.15, new MoveComparison { GameAnalysisId = 991004, Fen = Fen2839 });
        Assert.Equal((MaiaNaturalMoves.DefaultElo, MaiaNaturalMoves.DefaultElo), (none.WhiteElo, none.BlackElo));
        Assert.Single(none.At(Fen2839));
        Assert.Equal((1600, 1600), (maia.Calls[0].Self, maia.Calls[0].Oppo));
        // Ohne Maia (oder ohne Analyse): keine natürlichen Züge.
        Assert.Same(MaiaNaturalMoves.None, await MaiaNaturalMoves.ForAsync(_db, null, 0.15, new MoveComparison { GameAnalysisId = 991001 }));
        Assert.Same(MaiaNaturalMoves.None, await MaiaNaturalMoves.ForAsync(_db, maia, 0.15, new MoveComparison()));
    }
}
