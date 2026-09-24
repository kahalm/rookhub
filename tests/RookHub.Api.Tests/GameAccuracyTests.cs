using RookHub.Api.DTOs;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Server-SPIEGEL der Client-Formeln (<c>features/games/game-review.util.ts</c>) — dieselben LITERALEN Werte
/// wie in <c>game-review.util.spec.ts</c>, absichtlich abgetippt und nicht importiert: laufen die beiden
/// auseinander, zeigt die Partienliste eine andere Genauigkeit als die Partie-Seite, und genau das faellt hier auf.
/// </summary>
public class GameAccuracyTests
{
    [Fact]
    public void WinPercent_Lichess_0cpIst50_pm100cpSind59_10Und40_90()
    {
        Assert.Equal(50, GameAccuracy.WinPercent(0, null)!.Value, 4);
        Assert.Equal(59.1027, GameAccuracy.WinPercent(100, null)!.Value, 3);
        Assert.Equal(40.8973, GameAccuracy.WinPercent(-100, null)!.Value, 3);
        Assert.Equal(75.1126, GameAccuracy.WinPercent(300, null)!.Value, 3);
        Assert.Null(GameAccuracy.WinPercent(null, null));
    }

    [Fact]
    public void WinPercent_MattIstDerRand_mate0EntscheidetDieSeiteAmZug()
    {
        Assert.Equal(100, GameAccuracy.WinPercent(null, 3));
        Assert.Equal(0, GameAccuracy.WinPercent(null, -2));
        Assert.Equal(0, GameAccuracy.WinPercent(null, 0, whiteToMoveHere: true));    // Weiss am Zug IST matt
        Assert.Equal(100, GameAccuracy.WinPercent(null, 0, whiteToMoveHere: false));
    }

    [Fact]
    public void MoveAccuracy_keinVerlust100_LichessWerte_samtUnsicherheitsbonus_nieUnter0()
    {
        Assert.Equal(100, GameAccuracy.MoveAccuracy(60, 60));
        Assert.Equal(100, GameAccuracy.MoveAccuracy(60, 70));
        Assert.Equal(96.6044, GameAccuracy.MoveAccuracy(80, 79), 3);
        Assert.Equal(64.5826, GameAccuracy.MoveAccuracy(60, 50), 3);
        Assert.Equal(25.7756, GameAccuracy.MoveAccuracy(70, 40), 3);
        Assert.Equal(9.5303, GameAccuracy.MoveAccuracy(50, 0), 3);
        Assert.Equal(0, GameAccuracy.MoveAccuracy(100, 0));
    }

    [Fact]
    public void WindowSize_HalbzuegeDurch10_auf2Bis8()
    {
        Assert.Equal(2, GameAccuracy.WindowSizeFor(5));
        Assert.Equal(2, GameAccuracy.WindowSizeFor(29));
        Assert.Equal(3, GameAccuracy.WindowSizeFor(30));
        Assert.Equal(8, GameAccuracy.WindowSizeFor(85));
        Assert.Equal(8, GameAccuracy.WindowSizeFor(200));
    }

    [Fact]
    public void VolatilityWeights_ZweierfensterBegrenzt_ErstesFensterWiederholt_LueckeFaelltHeraus()
    {
        Assert.Equal(new[] { 5.0, 10.0, 7.5 }, GameAccuracy.VolatilityWeights(new double?[] { 50, 60, 40, 55 }));
        Assert.Equal(new[] { 0.5, 12.0 }, GameAccuracy.VolatilityWeights(new double?[] { 50, 50, 0 }));

        var series = new List<double?> { 50, 56, 44 };
        series.AddRange(Enumerable.Repeat<double?>(50, 28));   // 31 Stellungen = 30 Zuege → Fenster 3
        var w = GameAccuracy.VolatilityWeights(series);
        Assert.Equal(30, w.Length);
        Assert.Equal(4.8990, w[0], 3);
        Assert.Equal(4.8990, w[1], 3);
        Assert.Equal(4.8990, w[2], 3);
        Assert.Equal(2.8284, w[3], 3);
        Assert.Equal(0.5, w[4]);

        Assert.Equal(new[] { 0.5, 0.5 }, GameAccuracy.VolatilityWeights(new double?[] { 50, null, 70 }));
    }

    [Fact]
    public void SideAccuracy_GewichtetesPlusHarmonischesMittelHalbiert_ohneZugNull()
    {
        Assert.Equal(70.8333, GameAccuracy.SideAccuracy(new[] { (100.0, 1.0), (50.0, 1.0) })!.Value, 3);
        Assert.Equal(70.8497, GameAccuracy.SideAccuracy(new[] { (100.0, 5.0), (50.0, 10.0), (80.0, 7.5) })!.Value, 3);
        Assert.Null(GameAccuracy.SideAccuracy(Array.Empty<(double, double)>()));
    }

    // 1.e4 c5 2.Nf3 d6 — Schwarz verdirbt mit d6 (+0,40 → +3,00): dieselbe Partie wie im Client-Spec.
    private static readonly string[] Fens =
    {
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
        "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
        "rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq c6 0 2",
        "rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2",
    };

    private static List<GameEvalPlyDto> Plies() => new()
    {
        new() { Ply = 0, Cp = 30, Depth = 20, BestUci = "e2e4", PlayedUci = "e2e4", PlayedCp = 30 },
        new() { Ply = 1, Cp = 25, Depth = 20, BestUci = "e7e5", PlayedUci = "c7c5", PlayedCp = 35 },
        new() { Ply = 2, Cp = 35, Depth = 20, BestUci = "g1f3", PlayedUci = "g1f3", PlayedCp = 35 },
        new() { Ply = 3, Cp = 40, Depth = 20, BestUci = "b8c6", PlayedUci = "d7d6", PlayedCp = 300 },
    };

    [Fact]
    public void Compute_GenauigkeitJeSeite_volatilitaetsgewichtet_wieDerClient()
    {
        var r = GameAccuracy.Compute(Plies(), new GameEvalScoreDto { Cp = 300 }, Fens, plyCount: 4);
        Assert.Equal(99.4778, r.White!.Value, 3);
        Assert.Equal(48.0122, r.Black!.Value, 3);
    }

    [Fact]
    public void Compute_fehltDieNaechsteStellung_traegtDerGespielteKandidat_fehltDieEigene_istDerZugNichtBewertbar()
    {
        // Zeile 2 fehlt: Zug 1 (Schwarz) nimmt seinen gespielten Kandidaten (+0,35), Zug 2 (Weiss) ist nicht
        // bewertbar, Zug 3 (Schwarz) hat Zeile 3 und die Endbewertung.
        var plies = Plies().Where(p => p.Ply != 2).ToList();
        var r = GameAccuracy.Compute(plies, new GameEvalScoreDto { Cp = 300 }, Fens, plyCount: 4);
        // Weiss: nur Zug 0 zaehlt (52,7588 → 52,2997 %, Verlust 0,46 → 98,958); Zug 2 faellt heraus.
        Assert.Equal(98.958, r.White!.Value, 2);
        // Schwarz: Zug 1 mit dem gespielten Kandidaten (+0,35 = dieselbe Zahl wie die fehlende Zeile 2), Zug 3
        // mit der Endbewertung — dasselbe Ergebnis wie in der vollstaendigen Partie.
        Assert.Equal(48.012, r.Black!.Value, 2);
    }

    [Fact]
    public void Compute_ohneBewertungen_beideSeitenNull()
    {
        var r = GameAccuracy.Compute(new List<GameEvalPlyDto>(), null, Fens, plyCount: 4);
        Assert.Null(r.White);
        Assert.Null(r.Black);
    }

    [Fact]
    public void FromPositions_liestKandidatenInSichtDerSeiteAmZug_undDrehtSieWieDieEvals()
    {
        // Dieselbe Partie als Positionszeilen: die Kandidaten stehen aus Sicht der Seite am Zug (Schwarz negativ),
        // GameEvals.PlyOf dreht sie nach Weiss — das Ergebnis muss dem Client-Wert entsprechen.
        var positions = new List<RookHub.Api.Models.GameAnalysisPosition>
        {
            new() { Ply = 0, Fen = Fens[0], GameMoveUci = "e2e4", Depth = 20, CandidatesJson = "[{\"uci\":\"e2e4\",\"cp\":30}]" },
            new() { Ply = 1, Fen = Fens[1], GameMoveUci = "c7c5", Depth = 20, CandidatesJson = "[{\"uci\":\"e7e5\",\"cp\":-25},{\"uci\":\"c7c5\",\"cp\":-35}]" },
            new() { Ply = 2, Fen = Fens[2], GameMoveUci = "g1f3", Depth = 20, CandidatesJson = "[{\"uci\":\"g1f3\",\"cp\":35}]" },
            new() { Ply = 3, Fen = Fens[3], GameMoveUci = "d7d6", Depth = 20, CandidatesJson = "[{\"uci\":\"b8c6\",\"cp\":-40},{\"uci\":\"d7d6\",\"cp\":-300}]" },
        };
        var r = GameAccuracy.FromPositions(positions, plyCount: 4);
        Assert.Equal(99.4778, r.White!.Value, 3);
        Assert.Equal(48.0122, r.Black!.Value, 3);
    }

    [Fact]
    public void WinPercent_wieLichess_ueber1000cpGekappt()
    {
        Assert.Equal(GameAccuracy.WinPercent(1000, null)!.Value, GameAccuracy.WinPercent(2500, null)!.Value, 6);
        Assert.Equal(GameAccuracy.WinPercent(-1000, null)!.Value, GameAccuracy.WinPercent(-4000, null)!.Value, 6);
        Assert.Equal(97.5447, GameAccuracy.WinPercent(1000, null)!.Value, 3);
    }
}
