using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>PGN → Halbzüge (Stellung davor + Zug). Grundlage der Partie-Analyse: jede dieser
/// Stellungen wird einzeln von der Engine gerechnet, ein Fehler hier verschiebt die ganze Partie.</summary>
public class GamePliesTests
{
    private const string ShortGame = """
[Event "Testpartie"]
[White "Anderssen"]
[Black "Kieseritzky"]
[Result "1-0"]

1. e4 e5 2. f4 exf4 3. Bc4 Qh4+ 1-0
""";

    [Fact]
    public void ZerlegtDieHauptvariante_mitStellungVorJedemZug()
    {
        var (header, plies) = GamePlies.Parse(ShortGame)!.Value;

        Assert.Equal("Anderssen", header.White);
        Assert.Equal("Kieseritzky", header.Black);
        Assert.Equal("1-0", header.Result);
        Assert.Equal(6, plies.Count);

        // Erster Halbzug: Grundstellung, Weiß am Zug.
        Assert.Equal(0, plies[0].Index);
        Assert.StartsWith("rnbqkbnr/pppppppp", plies[0].Fen);
        Assert.Equal("e2e4", plies[0].Uci);
        Assert.Equal("e4", plies[0].San);

        // Zweiter: dieselbe Partie einen Zug weiter, jetzt ist Schwarz am Zug.
        Assert.Contains(" b ", plies[1].Fen);
        Assert.Equal("e7e5", plies[1].Uci);

        // Schlagzug wird als solcher notiert.
        Assert.Equal("exf4", plies[3].San);
        Assert.Equal("e5f4", plies[3].Uci);
    }

    [Fact]
    public void BeachtetDenFenHeader_alsStartstellung()
    {
        const string pgn = """
[Event "Stellung"]
[FEN "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1"]

1. e4 Kd7 *
""";
        var (header, plies) = GamePlies.Parse(pgn)!.Value;
        Assert.StartsWith("4k3/8/8/8/8/8/4P3/4K3", header.StartFen);
        Assert.Equal("e2e4", plies[0].Uci);
        Assert.Equal(2, plies.Count);
    }

    [Fact]
    public void RochadeKommtAlsKoenigszug_nichtAlsKoenigSchlaegtTurm()
    {
        // Der Broker liefert später e1h1 — der PARTIEZUG muss in der Standardform stehen, sonst
        // finden sich die beiden bei der Wertung nie.
        const string pgn = """
[Event "Rochade"]

1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5 4. O-O Nf6 *
""";
        var (_, plies) = GamePlies.Parse(pgn)!.Value;
        var rochade = plies[6];
        Assert.Equal("O-O", rochade.San);
        Assert.Equal("e1g1", rochade.Uci);
    }

    [Fact]
    public void DeckeltDieLaenge_undLehntUnspielbaresAb()
    {
        var (_, plies) = GamePlies.Parse(ShortGame, maxPlies: 3)!.Value;
        Assert.Equal(3, plies.Count);

        Assert.Null(GamePlies.Parse("kein PGN, nur Text"));
        Assert.Null(GamePlies.Parse(""));
        Assert.Null(GamePlies.Parse(null));
    }
}
