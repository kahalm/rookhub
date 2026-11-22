using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Engines;

/// <summary>
/// Drohung des besten Zuges (0.999.6, Befund Vereinspartie 260, Nagy – Schwaz, Fehler 1, 22.11.2026): gegen den besten
/// Zug 15...Td8 rechneten die „same answers“ 16.Tc1 und 16.a3, die einfach die Dame auf d1 einstellen, und Fable schrieb
/// „Auf 16.Tc1 oder 16.a3 geht einfach 16...Txd1 17.Tfxd1 mit Damengewinn“. Jetzt: Faktum „15...Rd8 attacks the queen on
/// d1“, die Einsteller nur noch mit Vermerk, ohne Linie, und ihre Linien sind keine erlaubten Züge mehr.
/// </summary>
public class MoveComparisonThreatTests
{
    /// <summary>Die Stellung vor 15...Sb4 aus Vereinspartie 260 (1.Sf3 Sf6 2.g3 e6 … 14.Sde4 Le7 15.Le3).</summary>
    private const string Before15 = "r1b2rk1/1pq1bppp/pnn1p3/8/4N3/2N1B1P1/PP2PPBP/R2Q1RK1 b - - 5 15";

    private static string Json(params (int cp, string moves)[] pvs)
        => "{\"depth\":22,\"time\":1000,\"nodes\":5000000,\"pvs\":["
           + string.Join(',', pvs.Select(p => $"{{\"depth\":22,\"cp\":{p.cp},\"moves\":[{string.Join(',', p.moves.Split(' ').Select(x => $"\"{x}\""))}]}}"))
           + "]}";

    private static MoveComparisonPrompts.PairFacts Facts260(IReadOnlyList<MoveComparisonBestMoveTries.Try>? tries = null)
    {
        var best = MoveComparisonBoard.PlayUci(Before15, "f8d8")!;
        var weak = MoveComparisonBoard.PlayUci(Before15, "c6b4")!;
        var bestReplies = MoveComparisonEval.Pvs(best, Json((-30, "d1c1 c8d7")));
        var weakReplies = MoveComparisonEval.Pvs(weak, Json((90, "a2a3 b4d5 c3d5"), (80, "a1c1 c7b8"), (60, "d1b3 b4d5")));
        var tests = new List<MoveComparisonPrompts.ReplyTest>
        {
            new("a3", MoveComparisonLineState.Done, "+7.01", ["Rxd1", "Rfxd1"]),
            new("Rc1", MoveComparisonLineState.Done, "+6.90", ["Rxd1", "Rfxd1"]),
            new("Qb3", MoveComparisonLineState.Done, "+0.40", ["Nd5"]),
        };
        return new MoveComparisonPrompts.PairFacts(Before15, false, 22, "Rd8", best, "+0.30", bestReplies,
            "Nb4", weak, "-0.80", weakReplies, tests, Tries: tries);
    }

    [Fact]
    public void Td8_greiftDieDameAn_dasIstEineDrohung()
    {
        var threats = Facts260().Threats();
        Assert.Equal([new MoveComparisonPrompts.Threat('Q', "d1")], threats);
    }

    [Fact]
    public void UserPrompt_nenntDieDrohung_undDieDameneinstellerNichtAlsLinie()
    {
        foreach (var detailed in new[] { false, true })
        {
            var lines = MoveComparisonPrompts.UserPrompt(Facts260(), detailed).Split('\n');
            Assert.Contains(lines, l => l.StartsWith("Concretely, 15...Rd8 attacks the queen on d1 — a direct threat: the opponent must react to it"));
            Assert.Contains("- 16.a3: ignores the threat to the queen on d1 — not a real option, do not discuss.", lines);
            Assert.Contains("- 16.Rc1: ignores the threat to the queen on d1 — not a real option, do not discuss.", lines);
            // Die echte Antwort (die Dame zieht weg) bleibt eine Linie.
            Assert.Contains(lines, l => l.StartsWith("- 16.Qb3: evaluation from the reader's view +0.40"));
            var text = string.Join('\n', lines);
            Assert.DoesNotContain("Rxd1", text);
            Assert.DoesNotContain("+7.01", text);
        }
    }

    [Fact]
    public void AllowedMoves_ohneDieLinienDerEinsteller_derBefundtextFaelltDurch()
    {
        var f = Facts260();
        var allowed = f.AllowedMoves().ToList();
        Assert.DoesNotContain("Rxd1", allowed);
        Assert.DoesNotContain("Rfxd1", allowed);
        Assert.Contains("a3", allowed);     // steht als Antwort auf den schwächeren Zug weiter da
        Assert.Contains("Qb3", allowed);
        Assert.Contains("Rd8", allowed);
        Assert.False(GameMoveExplanationService.MentionsOnly(
            ClubFableExplanations.EnglishSan("Auf 16.Tc1 oder 16.a3 geht einfach 16...Txd1 17.Tfxd1 mit Damengewinn."), allowed));
        Assert.True(GameMoveExplanationService.MentionsOnly(
            ClubFableExplanations.EnglishSan("15...Td8 greift die Dame auf d1 an, Weiß muss sie wegziehen, etwa 16.Dc1."), allowed));
    }

    [Fact]
    public void ObviousReplies_einstellerNurMitVermerk()
    {
        var best = MoveComparisonBoard.PlayUci(Before15, "f8d8")!;
        var afterH3 = MoveComparisonBoard.AfterMove(best, "h3")!;
        var tries = new List<MoveComparisonBestMoveTries.Try>
        {
            new("h3", null, 1, 2, 40, 40, afterH3, "+7.10", ["Rxd1", "Rfxd1"]),
        };
        var f = Facts260(tries);
        var text = MoveComparisonPrompts.UserPrompt(f, detailed: true);
        Assert.Contains("- 16.h3: ignores the threat to the queen on d1 — not a real option, do not discuss.", text.Split('\n'));
        Assert.DoesNotContain("this reply fails", text);
        Assert.DoesNotContain("h3", f.AllowedMoves());
    }

    [Fact]
    public void Gleichwertige_gedeckteFigur_istKeineDrohung()
    {
        // Turm greift einen gedeckten Turm an: kein Faktum, die Tests bleiben Linien.
        var withRook = "r5k1/5ppp/8/8/8/8/5PPP/R2R2K1 b - - 0 1";
        var best = MoveComparisonBoard.PlayUci(withRook, "a8d8")!;
        var f = new MoveComparisonPrompts.PairFacts(withRook, false, 22, "Rd8", best, "+0.00", [], "h6",
            MoveComparisonBoard.PlayUci(withRook, "h7h6")!, "-0.50", [],
            [new("Kf1", MoveComparisonLineState.Done, "+0.10", ["Rxd1+"])]);
        Assert.Empty(f.Threats());
        Assert.Contains("Rxd1+", f.AllowedMoves());
    }

    [Fact]
    public void Auftraege_einSatzFuerDieDrohung_undBewertungAusSichtVonWeiss()
    {
        var p = ClubFableExplanations.SystemPrompt.Replace("\r", "");
        Assert.Contains("genügt dafür EIN kurzer Satz", p);
        Assert.Contains("„15...Td8 greift die Dame auf\n   d1 an, Weiß muss sie wegziehen“", p);
        Assert.Contains("„not a real option“", p);
        Assert.Contains("IMMER aus Sicht von Weiß", p);
        Assert.Contains("Nie „aus seiner Sicht“", p);
        var user = ClubFableExplanations.UserPrompt(Facts260());
        Assert.Contains("Schwarz ist am Zug, also das Vorzeichen umdrehen", user);

        var local = MoveComparisonPrompts.SystemPrompt("de").Replace("\r", "");
        Assert.Contains("Never discuss answers marked \"not a real option\"", local);
    }
}

/// <summary>
/// Zweiter Fall (1.000.1, Vereinspartie 260, Fehler 5, 21...Tb8 statt 21...b6): der BAUER b6 greift den Springer c5 an;
/// 22.Db3 und 22.Tfd1 übergehen das und stellen nach 22...bxc5 den Springer ein — „das machen wir nicht“. 22.e4 (Gegenangriff
/// auf d5, nach 22...Dxc5 Ausgleich) bleibt eine echte Wahl. Dazu Deckungs-Fakten, Nachprüfung „ungedeckt“ und die
/// Materialbilanz in Figuren.
/// </summary>
public class MoveComparisonThreatGame260Mistake5Tests
{
    /// <summary>Die Stellung vor 21...Tb8 (Vergleich 2830 auf Prod).</summary>
    internal const string Before21 = "r1br2k1/1p3ppp/p3p3/q1Nn4/8/P5P1/1PQ1PPBP/2R2RK1 b - - 2 21";

    private static string Json(params (int cp, string moves)[] pvs)
        => "{\"depth\":22,\"time\":1000,\"nodes\":5000000,\"pvs\":["
           + string.Join(',', pvs.Select(p => $"{{\"depth\":22,\"cp\":{p.cp},\"moves\":[{string.Join(',', p.moves.Split(' ').Select(x => $"\"{x}\""))}]}}"))
           + "]}";

    internal static MoveComparisonPrompts.PairFacts Facts()
    {
        var best = MoveComparisonBoard.PlayUci(Before21, "b7b6")!;
        var weak = MoveComparisonBoard.PlayUci(Before21, "a8b8")!;
        var bestReplies = MoveComparisonEval.Pvs(best, Json((56, "c5b3 a5a4 e2e4 d5f6")));
        var weakReplies = MoveComparisonEval.Pvs(weak, Json((221, "c2b3 b7b5 f1d1 a5b6"), (166, "e2e4 d5f6 f1d1 c8d7"), (151, "f1d1 c8d7 e2e4 d5f6")));
        var tests = new List<MoveComparisonPrompts.ReplyTest>
        {
            new("Qb3", MoveComparisonLineState.Done, "+6.43", ["bxc5", "Qc2", "Rb8", "Rfd1", "Bd7", "Bxd5", "exd5", "Rxd5", "Bb5", "Rxc5"]),
            new("e4", MoveComparisonLineState.Done, "0.00", ["Qxc5", "Qxc5", "bxc5", "exd5"]),
            new("Rfd1", MoveComparisonLineState.Done, "+3.69", ["bxc5", "e4", "Nf6", "e5"]),
        };
        return new MoveComparisonPrompts.PairFacts(Before21, false, 22, "b6", best, "-0.56", bestReplies,
            "Rb8", weak, "-2.21", weakReplies, tests);
    }

    [Fact]
    public void B6_greiftDenSpringerAn_DB3undTfd1NurMitVermerk_e4BleibtLinie()
    {
        var f = Facts();
        Assert.Equal([new MoveComparisonPrompts.Threat('N', "c5")], f.Threats());
        var lines = MoveComparisonPrompts.UserPrompt(f, detailed: true).Split('\n');
        Assert.Contains(lines, l => l.StartsWith("Concretely, 21...b6 attacks the knight on c5 — a direct threat"));
        Assert.Contains("- 22.Qb3: ignores the threat to the knight on c5 — not a real option, do not discuss.", lines);
        Assert.Contains("- 22.Rfd1: ignores the threat to the knight on c5 — not a real option, do not discuss.", lines);
        Assert.Contains(lines, l => l.StartsWith("- 22.e4: evaluation from the reader's view 0.00"));
        var allowed = f.AllowedMoves().ToList();
        Assert.DoesNotContain("Rxc5", allowed);   // nur in der Linie nach 22.Db3
        Assert.Contains("Nb3", allowed);
        Assert.True(GameMoveExplanationService.MentionsOnly(
            ClubFableExplanations.EnglishSan("21...b6 greift den Springer c5 an, Weiß muss ihn mit 22.Sb3 zurückziehen."), allowed));
    }

    [Fact]
    public void Coverage_nach22Db3_derSpringerD5IstGedeckt()
    {
        var weak = MoveComparisonBoard.PlayUci(Before21, "a8b8")!;
        Assert.Contains("the knight on d5: attacked by the bishop on g2 and the queen on b3; defended by the pawn on e6 and the rook on d8",
            ExplanationFacts.Coverage(weak, ["Qb3", "b5"]));
        Assert.Contains("the knight on d5: attacked by the bishop on g2 and the queen on b3; defended by the pawn on e6 and the rook on d8",
            MoveComparisonPrompts.UserPrompt(Facts()));
    }

    [Fact]
    public void Nachpruefung_gedeckterSpringerUngedecktGenannt_wirdBeanstandet()
    {
        var f = Facts();
        Assert.Equal(["d5"], MoveComparisonClaims.FalseUndefended(
            "Motiv: Tempogewinn gegen den Springer auf c5 und Angriff auf das ungedeckte Pferd auf d5. Der Turm tut nichts.", f));
        Assert.Equal(["d5"], MoveComparisonClaims.FalseUndefended("After 22.Qb3 the knight on d5 is hanging.", f));
        // Die Dame auf a5 steht vor dem Zug wirklich ungedeckt; ein leeres Feld lässt sich nicht prüfen.
        Assert.Empty(MoveComparisonClaims.FalseUndefended("Die Dame auf a5 ist ungedeckt.", f));
        Assert.Empty(MoveComparisonClaims.FalseUndefended("Das Feld e4 bleibt ungedeckt.", f));
        Assert.Empty(MoveComparisonClaims.FalseUndefended("Der Springer auf d5 ist gut gedeckt.", f));
        Assert.Contains("ungedeckt bzw. hängend", MoveComparisonClaims.Hint(["d5"], german: true));
    }

    private sealed class QueueLlm : IClaudeJsonClient
    {
        public bool IsConfigured => true;
        public bool IsLocal => false;
        public string TranslationModel => "fable-test";
        public Queue<string?> Answers { get; } = new();
        public List<string> Prompts { get; } = new();
        public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> CompleteJsonAsync(string purpose, string system, string userPrompt, JsonNode schema, int maxTokens, CancellationToken ct = default)
        {
            Prompts.Add(userPrompt);
            return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : null);
        }
    }

    [Fact]
    public async Task Fable_gedeckteFigurUngedecktGenannt_fragtMitHinweisNeuAn()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var llm = new QueueLlm();
        llm.Answers.Enqueue("""{"explanation":"Nach 21...Tb8 22.Db3 greift die Dame das ungedeckte Pferd auf d5 an."}""");
        llm.Answers.Enqueue("""{"explanation":"21...b6 greift den Springer c5 an, Weiß muss ihn mit 22.Sb3 zurückziehen."}""");
        var fable = new ClubFableExplanations(llm, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClubMistakeComparisons:ExplainWithAnthropic"] = "true",
        }).Build(), NullLogger<ClubFableExplanations>.Instance);

        var text = await fable.ExplainAsync(db, Facts(), CancellationToken.None);

        Assert.Equal("21...b6 greift den Springer c5 an, Weiß muss ihn mit 22.Nb3 zurückziehen.", text);
        Assert.Equal(2, llm.Prompts.Count);
        Assert.Contains("nannte die Figur auf d5 ungedeckt bzw. hängend", llm.Prompts[1]);
    }

    [Fact]
    public void Materialbilanz_inFiguren_nach22Db3()
    {
        // 22.Db3 bxc5 23.Dc2 Tb8 24.Tfd1 Ld7 25.Lxd5 exd5 26.Txd5 Lb5 27.Txc5: Schwarz Springer + Läufer, Weiß Springer + 2 Bauern.
        var f = Facts();
        var events = ExplanationFacts.LineEvents(Before21, f.Tests[0].Answer, gainerWhite: false, lead: "b6", leads: ["Qb3"]);
        Assert.Contains("Black ends up ahead in material by 1 (White took knight, 2 pawns, Black took knight, bishop); "
                        + "in pieces: Black is a minor piece up for 2 pawns", events);
    }

    [Fact]
    public void InPieces_Faelle()
    {
        Assert.Equal("Black is a knight up for a pawn", ExplanationFacts.InPieces(['p'], ['n']));
        Assert.Equal("White is a queen up for a rook", ExplanationFacts.InPieces(['q', 'p'], ['r', 'p']));
        Assert.Equal("White is a pawn up", ExplanationFacts.InPieces(['p'], []));
        Assert.Equal("White is a queen up for a rook", ExplanationFacts.InPieces(['b', 'q'], ['r', 'n']));
    }

    [Fact]
    public void Auftraege_MotivAusSichtDesSpielers_MaterialNurAusDemFaktum()
    {
        var p = ClubFableExplanations.SystemPrompt.Replace("\r", "");
        Assert.Contains("Motiv benennt den Fehler des gespielten Zugs aus Sicht der Seite, die ihn spielte", p);
        Assert.Contains("mischt nicht die Pläne beider Seiten", p);
        Assert.Contains("Materialaussagen (wer wie viel Material mehr hat) nur aus dem Faktum „in pieces: …“", p);
        Assert.Contains("ein Faktum „defended by nothing“ es sagt", p);
        var local = MoveComparisonPrompts.SystemPrompt("en");
        Assert.Contains("only if a fact says \"defended by nothing\"", local);
        Assert.Contains("from the \"in pieces\" fact, never from the evaluation", local);
    }
}
