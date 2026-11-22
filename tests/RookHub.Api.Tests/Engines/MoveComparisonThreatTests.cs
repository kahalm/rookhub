using RookHub.Api.Models;
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
