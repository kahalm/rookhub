using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.Engines;

/// <summary>
/// Engine-Ausgabe, die zu einer ANDEREN Stellung gehört (Provider-Versatz, 16./17.11.2026): ein Provider-Prozess blieb nach
/// einem abgerissenen Upload eine Suche hinter den Aufträgen, jeder Auftrag bekam die Zeilen der vorigen Stellung samt deren
/// <c>bestmove</c>. <see cref="BrokerCandidates.MismatchOf(string?, string)"/> erkennt das an illegalen Wurzelzügen; Matt
/// und Patt (keine Linien, kein <c>bestmove</c>) sind KEIN Merkmal. <see cref="BrokerCandidates.Parse"/> verwirft ein
/// solches Ergebnis ganz, statt die fremden Linien nur herauszufiltern.
/// </summary>
public class ForeignEngineOutputTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    /// <summary>Beide Seiten können beidseitig rochieren.</summary>
    private const string CastleFen = "r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1";
    /// <summary>Narrenmatt — Weiß ist matt.</summary>
    private const string MateFen = "rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3";
    /// <summary>Schwarz am Zug, patt.</summary>
    private const string StalemateFen = "7k/5Q2/6K1/8/8/8/8/8 b - - 0 1";

    /// <summary>Echter Fall von Prod (Auftrag 2417130, 17.11. 05:27): Schwarz am Zug, die Linien stammen aus der VORIGEN
    /// Stellung — vom Broker auf den legalen Anfang gekürzt, vier davon leer; <c>d8f8</c> hat hier keine Figur.</summary>
    private const string ProdFen = "4r1k1/1ppq1pp1/3prn1p/3Pp3/p2P4/P4N1P/1PPQ1PP1/3RR1K1 b - - 0 19";
    private const string ProdForeign = "{\"time\":20643,\"depth\":16,\"nodes\":27175381,\"pvs\":[{\"moves\":[],\"cp\":308,\"depth\":17},"
        + "{\"moves\":[],\"cp\":544,\"depth\":16},{\"moves\":[],\"cp\":548,\"depth\":16},{\"moves\":[],\"cp\":885,\"depth\":16},"
        + "{\"moves\":[\"g7g6\"],\"cp\":893,\"depth\":16}],\"bestmove\":\"d8f8\",\"ponder\":\"b3b7\"}";

    private static string Line(string? bestmove, params string[] pvs) =>
        "{\"depth\":20,\"nodes\":1000,\"pvs\":[" + string.Join(",", pvs) + "]" + (bestmove is null ? "" : $",\"bestmove\":\"{bestmove}\"") + "}";

    private static string Pv(params string[] moves) =>
        "{\"moves\":[" + string.Join(",", moves.Select(m => $"\"{m}\"")) + "],\"cp\":25,\"depth\":20}";

    // ── Erkennung ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EchterProdFall_wirdErkannt()
        => Assert.NotNull(BrokerCandidates.MismatchOf(ProdForeign, ProdFen));

    [Fact]
    public void LeereLinie_istFremd()
        // Der Broker schneidet beim ersten illegalen Zug ab — aus einer fremden Linie wird "moves":[].
        => Assert.Equal("Linie 2 ohne legalen ersten Zug", BrokerCandidates.MismatchOf(Line(null, Pv("e2e4", "e7e5"), Pv()), StartFen));

    [Fact]
    public void IllegalerErsterZug_istFremd()
        => Assert.Equal("Linie 1: e7e5 ist hier illegal", BrokerCandidates.MismatchOf(Line(null, Pv("e7e5", "e2e4")), StartFen));

    [Fact]
    public void FremdesBestmove_istFremd_auchWennAlleLinienPassen()
        => Assert.Equal("bestmove e7e5 ist hier illegal", BrokerCandidates.MismatchOf(Line("e7e5", Pv("e2e4", "e7e5")), StartFen));

    [Fact]
    public void PassendeZeile_istNichtFremd()
    {
        Assert.Null(BrokerCandidates.MismatchOf(Line(null, Pv("e2e4", "e7e5"), Pv("d2d4")), StartFen));
        Assert.Null(BrokerCandidates.MismatchOf(Line("e2e4", Pv("e2e4", "e7e5")), StartFen));
    }

    [Theory]
    [InlineData("e1h1")]   // König schlägt Turm — so schickt sie der Provider (UCI_Chess960 true)
    [InlineData("e1g1")]   // Standardform
    [InlineData("e1a1")]
    [InlineData("e1c1")]
    public void Rochade_inBeidenSchreibweisen_istLegal(string castle)
    {
        Assert.Null(BrokerCandidates.MismatchOf(Line(castle, Pv(castle, "e8h8")), CastleFen));
        Assert.NotNull(BrokerCandidates.Parse(Line(castle, Pv(castle, "e8h8")), CastleFen));
    }

    [Theory]
    [InlineData(MateFen)]
    [InlineData(StalemateFen)]
    public void MattUndPatt_keinFehlalarm(string fen)
    {
        // Stockfish: `info depth 0 score mate 0` (ohne pv) bzw. `score cp 0`, dann `bestmove (none)` — der Broker sendet
        // keine Linie und lässt bestmove weg; der Lichess-Broker schreibt "(none)" hinein.
        Assert.Null(BrokerCandidates.MismatchOf("{\"time\":0,\"depth\":0,\"nodes\":0,\"pvs\":[]}", fen));
        Assert.Null(BrokerCandidates.MismatchOf("{\"time\":0,\"depth\":0,\"nodes\":0,\"pvs\":[],\"bestmove\":\"(none)\"}", fen));
    }

    [Fact]
    public void ZeileOhneDaten_keinFehlalarm()
    {
        Assert.Null(BrokerCandidates.MismatchOf("{\"keepalive\":true}", StartFen));
        Assert.Null(BrokerCandidates.MismatchOf("kein json", StartFen));
        Assert.Null(BrokerCandidates.MismatchOf("{\"depth\":3,\"pvs\":[null]}", StartFen));
    }

    // ── Übernahme: ein fremdes Ergebnis ist GANZ unbrauchbar ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_gemischtesErgebnis_istNull()
    {
        // Früher blieb e2e4 übrig und die Bewertung der fremden Stellung landete still in der Analyse.
        Assert.Null(BrokerCandidates.Parse(Line(null, Pv("e2e4", "e7e5"), Pv("e7e5")), StartFen));
        Assert.Null(BrokerCandidates.Parse(Line(null, Pv("e2e4", "e7e5"), Pv()), StartFen));
        Assert.Null(BrokerCandidates.Parse(ProdForeign, ProdFen));
    }

    [Fact]
    public void Parse_fremdesBestmove_istNull()
        => Assert.Null(BrokerCandidates.Parse(Line("e7e5", Pv("e2e4", "e7e5")), StartFen));

    [Fact]
    public void Parse_passendesErgebnis_bleibt()
    {
        var list = BrokerCandidates.Parse(Line("e2e4", Pv("e2e4", "e7e5"), Pv("d2d4", "d7d5")), StartFen);
        Assert.NotNull(list);
        Assert.Equal(["e2e4", "d2d4"], list!.Select(c => c.Uci));
    }
}
