using RookHub.Api.Models;
using RookHub.Api.Services.Puzzles;

namespace RookHub.Api.Tests.Puzzles;

/// <summary>Prompt-Stellung und Prüfung der Tipps gegen das Brett (Befund 23.11.: Tipps nannten Figuren, die nicht
/// auf dem Brett stehen; Buch 32 = Grundstellung + 30 Halbzüge Vorspiel).</summary>
public class HintBoardCheckTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    // Muster Buch 32: Grundstellung, StartPly 29 → 30 Halbzüge Vorspiel, in denen die Damen getauscht werden
    // (1.e4 e5 2.Sf3 Sc6 3.d4 exd4 4.Sxd4 Sxd4 5.Dxd4 Df6 6.Dxf6 Sxf6 … 15.Kb1 Lf4), danach die Lösung 16.g5 hxg5.
    private const string Book32Moves =
        "e2e4 e7e5 g1f3 b8c6 d2d4 e5d4 f3d4 c6d4 d1d4 d8f6 d4f6 g8f6 b1c3 f8b4 c1d2 e8g8 e1c1 d7d6 f2f3 c8e6 "
        + "a2a3 b4c5 g2g4 f8e8 h2h4 h7h6 d2e3 c5e3 c1b1 e3f4 g4g5 h6g5";
    private const int Book32StartPly = 29;
    private const string Book32Fen = "r3r1k1/ppp2pp1/3pbn1p/8/4PbPP/P1N2P2/1PP5/1K1R1B1R w - - 2 16";

    private static HintBoard Book32Board() => HintBoardCheck.FromPuzzle(StartFen,
        HintGenerationService.SetupMovesUci(Book32Moves, Book32StartPly),
        HintGenerationService.SolutionUci(Book32Moves, Book32StartPly))!;

    /// <summary>Stellung aus einer FEN ohne Vorspiel (wie StartPly = -1), optional mit Lösungszügen.</summary>
    private static HintBoard Board(string fen, params string[] solution) =>
        HintBoardCheck.FromPuzzle(fen, "", solution)!;

    // ---- Stellung nach dem Vorspiel + Figurenliste ----

    [Fact]
    public void FromPuzzle_Book32Pattern_PlaysSetupToSolverPosition()
    {
        var board = Book32Board();
        Assert.Equal(Book32Fen.Split(' ')[0], board.Fen.Split(' ')[0]);
        Assert.True(board.WhiteToMove);
        Assert.False(board.HasAny('q'));
        Assert.True(board.Has(true, 'n'));
        Assert.Empty(board.Promotions);
    }

    [Fact]
    public void PieceList_Book32Pattern_ListsPiecesPerSide()
    {
        var board = Book32Board();
        Assert.Equal("Kb1, Rd1, Rh1, Bf1, Nc3, pawns a3 b2 c2 e4 f3 g4 h4", HintBoardCheck.PieceList(board, true));
        Assert.Equal("Kg8, Ra8, Re8, Be6, Bf4, Nf6, pawns a7 b7 c7 d6 f7 g7 h6", HintBoardCheck.PieceList(board, false));
    }

    [Fact]
    public void BuildUserPrompt_Book32Pattern_GivesSolverPositionNotStartFen()
    {
        var bp = new BookPuzzle { LineId = "l", BookFileName = "b.pgn", Round = "1", Fen = StartFen, Moves = Book32Moves, StartPly = Book32StartPly };
        var setup = HintGenerationService.SetupMovesUci(bp.Moves, bp.StartPly);
        var solution = HintGenerationService.SolutionUci(bp.Moves, bp.StartPly);
        var prompt = HintGenerationService.BuildUserPrompt(bp, setup, solution, null, HintBoardCheck.FromPuzzle(bp.Fen, setup, solution));

        Assert.Contains(Book32Fen.Split(' ')[0], prompt);
        Assert.DoesNotContain(StartFen, prompt);                          // keine Grundstellung mehr
        Assert.DoesNotContain("Setup moves", prompt);
        Assert.Contains("Side to move (the solver, \"you\" in the hints): White", prompt);
        Assert.Contains("White: Kb1, Rd1, Rh1, Bf1, Nc3, pawns a3 b2 c2 e4 f3 g4 h4", prompt);
        Assert.Contains("Black: Kg8, Ra8, Re8, Be6, Bf4, Nf6, pawns a7 b7 c7 d6 f7 g7 h6", prompt);
        Assert.Contains("no queen at all", prompt);
        Assert.Contains("Mention only pieces that appear in this list", prompt);
        Assert.Contains("Verified solution from this position (UCI): g4g5 h6g5", prompt);
    }

    [Fact]
    public void BuildUserPrompt_WithoutBoard_FallsBackToStartFenAndSetup()
    {
        var bp = new BookPuzzle { LineId = "l", BookFileName = "b.pgn", Round = "1", Fen = StartFen, Moves = "e2e4 e7e5", StartPly = 0 };
        var prompt = HintGenerationService.BuildUserPrompt(bp, "e2e4", new[] { "e7e5" }, null, null);
        Assert.Contains($"Start FEN: {StartFen}", prompt);
        Assert.Contains("Setup moves already played (UCI): e2e4", prompt);
        Assert.Contains("Side to move in the puzzle: Black", prompt);
    }

    [Fact]
    public void FromPuzzle_IllegalSetupMove_ReturnsNull()
    {
        Assert.Null(HintBoardCheck.FromPuzzle(StartFen, "e2e5", Array.Empty<string>()));
    }

    [Fact]
    public void DescribeForPrompt_OneSideMissingPiece_NamesIt()
    {
        // Weiß: K + T + B, Schwarz: K + D.
        var text = HintBoardCheck.DescribeForPrompt(Board("3qk3/8/8/8/8/8/4P3/R3K3 w - - 0 1"));
        Assert.Contains("White has no queen", text);
        Assert.Contains("Black has no rook", text);
        Assert.Contains("no bishop at all", text);
    }

    // ---- Prüfung gegen das Brett ----

    [Theory]
    [InlineData("de", "Deine Dame entscheidet die Partie.")]
    [InlineData("de", "Mit dem Damenopfer öffnest du die Linie.")]
    [InlineData("en", "Your queen delivers the final blow.")]
    [InlineData("en", "The queen's sacrifice opens the king.")]
    [InlineData("hr", "Iskoristi kraljicu za napad.")]
    [InlineData("hr", "Dama odlučuje.")]
    public void FindInventedPiece_Book32Pattern_QueenNotOnBoard(string lang, string hint)
    {
        var bad = HintBoardCheck.FindInventedPiece(hint, lang, Book32Board());
        Assert.NotNull(bad);
        Assert.Contains("no queen on the board", bad!.Value.Reason);
    }

    [Theory]
    [InlineData("de", "Dein Springer springt mit Tempo ins Zentrum.")]
    [InlineData("de", "Der schwarze Läufer ist überlastet.")]
    [InlineData("en", "Your rook takes over the open file.")]
    [InlineData("hr", "Tvoj skakač ulazi u igru.")]
    public void FindInventedPiece_Book32Pattern_PiecesOnBoard_Pass(string lang, string hint)
    {
        Assert.Null(HintBoardCheck.FindInventedPiece(hint, lang, Book32Board()));
    }

    // Weiß (am Zug): K + T + B, Schwarz: K + D — Seitenangaben gegen das Brett.
    private const string RookVsQueen = "3qk3/8/8/8/8/8/4P3/R3K3 w - - 0 1";

    [Theory]
    [InlineData("de", "Deine Dame greift an.", "White (the solver) has no queen")]
    [InlineData("de", "Die weiße Dame greift an.", "White (the solver) has no queen")]
    [InlineData("de", "Der gegnerische Turm ist schwach.", "Black (the opponent) has no rook")]
    [InlineData("de", "Der schwarze Turm ist schwach.", "Black (the opponent) has no rook")]
    [InlineData("en", "Use your queen.", "White (the solver) has no queen")]
    [InlineData("en", "The opponent's rook is loose.", "Black (the opponent) has no rook")]
    [InlineData("en", "White's queen wins.", "White (the solver) has no queen")]
    [InlineData("hr", "Tvoja dama napada.", "White (the solver) has no queen")]
    [InlineData("hr", "Crni top je slab.", "Black (the opponent) has no rook")]
    public void FindInventedPiece_SideMismatch_IsRejected(string lang, string hint, string reason)
    {
        var bad = HintBoardCheck.FindInventedPiece(hint, lang, Board(RookVsQueen));
        Assert.NotNull(bad);
        Assert.Equal(reason, bad!.Value.Reason);
    }

    [Theory]
    [InlineData("de", "Dein Turm schneidet den König ab, die gegnerische Dame ist zu langsam.")]
    [InlineData("de", "Die Dame des Gegners kommt zu spät.")]           // ohne klare Seitenangabe vor dem Wort
    [InlineData("en", "Your rook cuts off the king while the black queen is too slow.")]
    [InlineData("hr", "Tvoj top odsijeca kralja.")]
    public void FindInventedPiece_SideMatches_Passes(string lang, string hint)
    {
        Assert.Null(HintBoardCheck.FindInventedPiece(hint, lang, Board(RookVsQueen)));
    }

    // Bauernendspiel ohne Figuren: Ausnahmen.
    private const string PawnEnding = "8/7p/8/8/8/8/P6P/4K1k1 w - - 0 1";

    [Theory]
    [InlineData("de", "Der Bauer läuft zur Dame.")]
    [InlineData("de", "Nach der Umwandlung entscheidet die neue Dame.")]
    [InlineData("de", "Der Randbauer läuft durch.")]
    [InlineData("de", "Spiele auf dem Damenflügel.")]
    [InlineData("de", "Der Plan liegt am Damen-Flügel.")]
    [InlineData("en", "Promote to a queen.")]
    [InlineData("en", "Push the rook pawn.")]
    [InlineData("en", "The knight pawn decides.")]
    [InlineData("en", "Play on the queenside.")]
    [InlineData("en", "Play on the queen's side.")]
    [InlineData("en", "The rook's pawn is the right one.")]
    [InlineData("hr", "Pješak ide do dame.")]
    [InlineData("hr", "Promocija u damu odlučuje.")]
    public void FindInventedPiece_Exceptions_Pass(string lang, string hint)
    {
        Assert.Null(HintBoardCheck.FindInventedPiece(hint, lang, Board(PawnEnding)));
    }

    [Fact]
    public void FindInventedPiece_UnderpromotionInSolution_Passes()
    {
        var board = Board("8/1P6/8/8/8/8/k7/4K3 w - - 0 1", "b7b8n");
        Assert.Contains('n', board.Promotions);
        Assert.Null(HintBoardCheck.FindInventedPiece("Der neue Springer gibt Schach.", "de", board));
        Assert.Null(HintBoardCheck.FindInventedPiece("Your knight gives check.", "en", board));
        // Andere Figurenarten bleiben erfunden.
        Assert.NotNull(HintBoardCheck.FindInventedPiece("Your bishop gives check.", "en", board));
    }

    [Theory]
    [InlineData("de", "Der Turm entscheidet.")]
    [InlineData("en", "The bishop pair dominates.")]
    [InlineData("hr", "Lovac odlučuje.")]
    public void FindInventedPiece_PawnEnding_PieceInvented(string lang, string hint)
    {
        Assert.NotNull(HintBoardCheck.FindInventedPiece(hint, lang, Board(PawnEnding)));
    }

    [Fact]
    public void FindInventedPiece_UnknownLanguage_Passes()
    {
        Assert.Null(HintBoardCheck.FindInventedPiece("La dame décide.", "fr", Board(PawnEnding)));
    }

    // ---- Validierung im Generator ----

    [Fact]
    public void ParseAndValidate_WithBoard_RejectsInventedPiece_WithReason()
    {
        var hints = HintGenerationService.ParseAndValidate(
            """{"hint1":"Ein Angriff auf den König","hint2":"Deine Dame entscheidet","hint3":"Dame schlägt auf g7"}""",
            "de", Book32Board(), out var rejection);
        Assert.Null(hints);
        Assert.Contains("Deine Dame entscheidet", rejection);
        Assert.Contains("no queen", rejection);
    }

    [Fact]
    public void ParseAndValidate_WithBoard_ChecksAlsoThirdHint()
    {
        Assert.Null(HintGenerationService.ParseAndValidate(
            """{"hint1":"Ein Angriff","hint2":"Dein Springer hilft","hint3":"Dame nach g7"}""",
            "de", Book32Board(), out _));
        Assert.NotNull(HintGenerationService.ParseAndValidate(
            """{"hint1":"Ein Angriff","hint2":"Dein Springer hilft","hint3":"Springer nach d5"}""",
            "de", Book32Board(), out var none));
        Assert.Null(none);
    }
}
