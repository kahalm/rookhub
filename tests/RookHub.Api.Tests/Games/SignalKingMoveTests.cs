using RookHub.Api.Services;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Games;

/// <summary>Signal-Königszug des Siegers am Ende einer übertragenen Partie (1.022.0, <see cref="SignalKingMove"/>).</summary>
public class SignalKingMoveTests
{
    /// <summary>Die Züge der Prod-Vereinspartie 268 (1-0): nach 44...Dc3+ stellt Weiß den König nach e4 — Matt in 4 für Schwarz
    /// (45...Te1+ 46.Kf4 De5+ 47.Kg4 Df5+ 48.Kh4 Dh5#).</summary>
    internal const string Game268 = "1. c4 Nf6 2. Nc3 e6 3. e4 d5 4. e5 d4 5. exf6 dxc3 6. bxc3 Qxf6 7. d4 b6 8. Be2 Bb7 9. Bf3 Bxf3 10. Nxf3 Bd6 "
        + "11. Qa4+ c6 12. O-O O-O 13. Bg5 Qf5 14. Rab1 e5 15. dxe5 Bxe5 16. Rxb6 axb6 17. Qxa8 Bxc3 18. Be7 Re8 19. Bd6 Qc8 20. Bxb8 c5 "
        + "21. Qa3 Bb4 22. Qd3 Qxb8 23. a3 Rd8 24. Qf5 Bd2 25. g3 g6 26. Qf6 Qd6 27. Qh4 Bc3 28. Ng5 Bg7 29. Qxh7+ Kf8 30. Re1 Qd2 "
        + "31. Re3 Qc1+ 32. Kg2 Rd1 33. Ne6+ fxe6 34. Rf3+ Ke7 35. Qxg7+ Kd6 36. Qf8+ Kc6 37. Qe8+ Kd6 38. Re3 Rg1+ 39. Kf3 Qxc4 "
        + "40. Qd8+ Kc6 41. Rd3 Kb5 42. Qe8+ Ka5 43. Rd7 Ka6 44. Rd8 Qc3+ 45. Ke4";

    internal static IReadOnlyList<string> Sans268 => PgnParser.ExtractMainlineSans(Game268);

    [Fact]
    public void Detect_Game268_KingToE4AllowsMateInFour_IsSignal()
    {
        var f = SignalKingMove.Detect(Sans268, "1-0");
        Assert.NotNull(f);
        Assert.Equal(89, f!.Ply);
        Assert.Equal("45.Ke4", f.Label);
        Assert.Equal(4, f.MateIn);
        Assert.True(f.WhiteMoved);
    }

    [Fact]
    public void Strip_Game268_RemovesExactlyOneHalfMove_AndNothingAfterwards()
    {
        var once = SignalKingMove.Strip(Sans268, "1-0", out var dropped);
        Assert.NotNull(dropped);
        Assert.Equal(88, once.Count);
        Assert.Equal("Qc3", once[^1]);
        // Zweimal angewandt bleibt es bei einem Halbzug (der vorletzte ist ein Damenzug des Verlierers).
        Assert.Same(once, SignalKingMove.Strip(once, "1-0", out var again));
        Assert.Null(again);
    }

    [Theory]
    [InlineData("0-1")]       // der König gehört dem Verlierer — sein Zug widerspricht nichts
    [InlineData("1/2-1/2")]
    [InlineData("*")]
    [InlineData(null)]
    public void Detect_Game268_WithoutWhiteWin_KeepsTheMove(string? result) =>
        Assert.Null(SignalKingMove.Detect(Sans268, result));

    [Fact]
    public void Detect_KingMoveOutsideTheCentre_KeepsTheMove()
    {
        // Statt 45.Ke4 der Zug nach g4 — kein Mittelfeld, also nie ein Signal, gleich was danach geht.
        var sans = Sans268.Take(88).Append("Kg4").ToList();
        Assert.Null(SignalKingMove.Detect(sans, "1-0"));
    }

    [Fact]
    public void Detect_EndgameCentralisationWithoutMate_KeepsTheMove() =>
        Assert.Null(SignalKingMove.Detect(new[] { "Ke4" }, "1-0", "8/5k2/8/8/8/4K3/8/7R w - - 0 1"));

    [Fact]
    public void Detect_LastMoveNotAKingMove_KeepsTheMove() =>
        Assert.Null(SignalKingMove.Detect(Sans268.Take(88).ToList(), "0-1"));

    [Fact]
    public void Detect_KingToCentreStalematesTheLoser_IsSignal()
    {
        var f = SignalKingMove.Detect(new[] { "Kd5" }, "1-0", "k7/8/1Q6/8/2K5/8/8/8 w - - 0 1");
        Assert.NotNull(f);
        Assert.Null(f!.MateIn);
        Assert.Equal("1.Kd5", f.Label);
    }

    [Fact]
    public void Detect_UnplayableMoves_IsNull() =>
        Assert.Null(SignalKingMove.Detect(new[] { "e4", "e5", "Ke9" }, "1-0"));

    [Fact]
    public void ForcedMateByChecks_FindsTheShortestMate()
    {
        var b = TacticsBoard.TryParse("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1")!;   // Ta8#
        Assert.Equal(1, SignalKingMove.ForcedMateByChecks(b, 5));
        Assert.Null(SignalKingMove.ForcedMateByChecks(TacticsBoard.TryParse("6k1/8/8/8/8/8/8/6K1 w - - 0 1")!, 5));
    }

    // ── Einlesewege ──────────────────────────────────────────────

    private const string Headers268 = "[Event \"Liga\"]\n[Site \"?\"]\n[Date \"2026.11.22\"]\n[Round \"5.1\"]\n[White \"Weiß, Willi\"]\n"
        + "[Black \"Schwarz, Sepp\"]\n[Result \"1-0\"]\n[WhiteFideId \"900001\"]\n[BlackFideId \"900002\"]\n"
        + "[GameURL \"https://lichess.org/broadcast/x/y/z/abcdefgh\"]\n\n";

    [Fact]
    public void BroadcastCleanGames_DropsTheSignal()
    {
        var (pgn, games) = LeagueBroadcastImport.CleanGames(Headers268 + Game268 + " 1-0\n");
        var g = Assert.Single(games);
        Assert.Equal(88, g.Sans.Count);
        Assert.Equal("45.Ke4", g.DroppedSignal?.Label);
        Assert.DoesNotContain("Ke4", pgn);
    }

    [Fact]
    public void LiveBoard_FinishedGame_ShowsThePositionBeforeTheSignal()
    {
        var g = LiveGameParser.Parse(Headers268 + Game268 + " 1-0\n")!;
        Assert.Equal(88, g.Plies);
        Assert.Equal("c4c3", g.LastMove);
        // Läuft die Partie noch, bleibt jeder Zug stehen.
        var live = LiveGameParser.Parse(Headers268.Replace("[Result \"1-0\"]", "[Result \"*\"]") + Game268 + " *\n")!;
        Assert.Equal(89, live.Plies);
    }

    [Fact]
    public void ClubPgnCheck_DropsTheSignal_AndRemembersIt()
    {
        var p = Assert.Single(ClubPgnText.ParseAll(Headers268 + Game268 + " 1-0\n", out _));
        Assert.Null(p.Error);
        Assert.Equal(88, p.Sans!.Count);
        Assert.Equal("45.Ke4", p.DroppedSignal?.Label);
    }
}
