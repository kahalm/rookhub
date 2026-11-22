using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>
/// Bei welchem Zug der Materialvorsprung entsteht (0.999.7, Befund Vereinspartie 260, Fehler 4, 20...Td8 statt 20...b6,
/// 22.11.2026): der Text brach „21...Sxb4 22.axb4 Txd1 23.Tfxd1“ ab und nannte Weiß „materiell vorn“ — nach 23.Tfxd1 hat
/// Schwarz aber Dame weniger, Turm und Springer mehr; erst 25.Td8+ Df8 26.Txf8+ gewinnt die Dame für den Turm.
/// </summary>
public class ExplanationFactsDecidedAtTests
{
    /// <summary>Die Stellung vor 20...Td8 aus Vereinspartie 260 (Vergleich 2828 auf Prod).</summary>
    private const string Before20 = "r1b2rk1/1p3ppp/p3p3/q1Nn4/8/P5P1/1P2PPBP/2RQ1RK1 b - - 0 20";

    /// <summary>Die Engine-Linie der anderen Verteidigung 21...Sxb4 (Tiefe 30, so in der Datenbank).</summary>
    private static readonly string[] AfterNxb4 =
        "axb4 Rxd1 Rfxd1 Qb6 Na4 Qxb4 Rd8+ Qf8 Rxf8+ Kxf8 Nb6 Rb8 Nxc8 b5 Na7 b4 Nc6 Rc8 Rb1 a5 e3 b3 Rxb3 Rxc6 Bxc6".Split(' ');

    [Fact]
    public void LineEvents_Sxb4_nenntDenZugDerDasMaterialEntscheidet()
    {
        var events = ExplanationFacts.LineEvents(Before20, AfterNxb4, gainerWhite: true, lead: "Rd8", leads: ["b4", "Nxb4"]);
        Assert.Contains(events, e => e.StartsWith("White ends up ahead in material by 7"));
        Assert.Contains("material is decided only at 26.Rxf8+ — the line must be shown up to there: 25.Rd8+ Qf8 26.Rxf8+ Kxf8 "
                        + "(in this exchange White takes queen, Black takes rook); before that White is not ahead for good", events);
    }

    [Fact]
    public void LineEvents_frueherGewinn_keinZusatz()
    {
        // 21.b4 Sxb4 22.axb4: der Springer ist sofort weg und Weiß bleibt vorne — kein „decided only at“.
        var events = ExplanationFacts.LineEvents(Before20, ["Nxb4", "axb4", "Qb6"], gainerWhite: true, lead: "Rd8", leads: ["b4"]);
        Assert.Contains(events, e => e.StartsWith("White ends up ahead in material by 2"));
        Assert.DoesNotContain(events, e => e.StartsWith("material is decided only at"));
    }

    [Fact]
    public void Auftraege_LinieNieVorDemMaterialentscheidendenZugAbbrechen()
    {
        var p = ClubFableExplanations.SystemPrompt.Replace("\r", "");
        Assert.Contains("Brich eine Linie nie vor dem Zug ab, der das Material tatsächlich entscheidet", p);
        Assert.Contains("die Grenze von zwei bis vier\n   Halbzügen gilt dann nicht", p);
        Assert.Contains("„… 25.Td8+ Df8 26.Txf8+ – Weiß gewinnt die Dame für den Turm“", p);
        var local = MoveComparisonPrompts.SystemPrompt("de").Replace("\r", "");
        Assert.Contains("says \"material is decided only at\" a later move, name that move too", local);
    }
}
