using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Broker-Zeile → Kandidatenliste. Hier werden die zwei Eigenheiten des Lichess-Brokers begradigt,
/// die sonst jede spätere Auswertung wieder einfangen müsste: Bewertung aus WEISS-Sicht und
/// Rochade als König-schlägt-Turm.
/// </summary>
public class BrokerCandidatesTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    /// <summary>Nach 1.e4 — SCHWARZ am Zug.</summary>
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

    private static string Line(params string[] pvs) => "{\"depth\":30,\"pvs\":[" + string.Join(",", pvs) + "]}";
    private static string Pv(string firstMove, int? cp = null, int? mate = null)
    {
        var eval = mate is int m ? $"\"mate\":{m}" : $"\"cp\":{cp ?? 0}";
        return $"{{\"depth\":30,{eval},\"moves\":[\"{firstMove}\",\"e7e5\"]}}";
    }

    [Fact]
    public void WeissAmZug_BewertungBleibt()
    {
        var list = BrokerCandidates.Parse(Line(Pv("e2e4", 35), Pv("d2d4", 20)), StartFen)!;
        Assert.Equal(2, list.Count);
        Assert.Equal("e2e4", list[0].Uci);
        Assert.Equal(35, list[0].Cp);
        Assert.Equal(20, list[1].Cp);
    }

    [Fact]
    public void SchwarzAmZug_VorzeichenWirdGedreht()
    {
        // Der Broker sagt „+30 für Weiss"; aus Sicht von Schwarz (am Zug) sind das -30.
        var list = BrokerCandidates.Parse(Line(Pv("e7e5", 30)), AfterE4)!;
        Assert.Equal(-30, list[0].Cp);

        // Dasselbe fuer Matt: Weiss setzt in 3 → aus Schwarz' Sicht -3.
        var mate = BrokerCandidates.Parse(Line(Pv("e7e5", mate: 3)), AfterE4)!;
        Assert.Equal(-3, mate[0].Mate);
    }

    [Fact]
    public void RochadeWirdAufDieStandardformUmgeschrieben()
    {
        // lila-engine notiert Rochaden als Koenig-schlaegt-Turm (Chess960-Notation).
        const string fen = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/2N2N2/PPPP1PPP/R1BQK2R w KQkq - 6 5";
        var list = BrokerCandidates.Parse(Line(Pv("e1h1", 25)), fen)!;
        Assert.Equal("e1g1", list[0].Uci);
    }

    [Fact]
    public void TurmzugBleibtTurmzug_wennDerKoenigWoandersSteht()
    {
        // e1h1 ist ein LEGALER Turmzug, sobald der Koenig nicht auf e1 steht — dann darf nichts
        // umgeschrieben werden. (Hier steht der Turm auf e1 und zieht nach h1.)
        const string fen = "4k3/8/8/8/8/8/8/4R2K w - - 0 1";
        var list = BrokerCandidates.Parse(Line(Pv("e1h1", 10)), fen);
        Assert.Null(list);   // e1h1 ist hier kein legaler Zug (h1 ist besetzt) → Zeile verworfen
    }

    [Fact]
    public void UnbrauchbareZeilenErgebenNull()
    {
        Assert.Null(BrokerCandidates.Parse(null, StartFen));
        Assert.Null(BrokerCandidates.Parse("kein json", StartFen));
        Assert.Null(BrokerCandidates.Parse("{\"depth\":30}", StartFen));          // keine pvs
        Assert.Null(BrokerCandidates.Parse(Line(Pv("a1a8", 10)), StartFen));      // Zug nicht legal
        Assert.Null(BrokerCandidates.Parse(Line(Pv("e2e4", 10)), "kaputte fen"));
    }

    [Fact]
    public void RundlaufJson_erhaeltZugUndBewertung()
    {
        var parsed = BrokerCandidates.Parse(Line(Pv("e2e4", 35), Pv("d2d4", mate: 5)), StartFen)!;
        var json = BrokerCandidates.ToJson(parsed);
        var back = BrokerCandidates.FromJson(json);

        Assert.Equal(2, back.Count);
        Assert.Equal("e2e4", back[0].Uci);
        Assert.Equal(0.35, back[0].Eval.Pawns, 3);
        Assert.Equal(995, back[1].Eval.Pawns, 3);   // Matt in 5 → 1000 - 5
        Assert.Equal("+0.35", BrokerCandidates.EvalTextOf(parsed));
    }
}
