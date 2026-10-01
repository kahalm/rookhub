using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Kleinteile, die jeder PGN-Zerleger gleich beantworten muss (<see cref="PgnTokens"/>), und
/// Spiegeltests über die Zerleger, die sie benutzen: derselbe Movetext muss im Repertoire-Parser
/// (<see cref="PgnMoveTree"/>) und im Partie-Upload (<see cref="PgnParser"/>) dieselbe Hauptlinie
/// ergeben.
/// </summary>
public class PgnTokensTests
{
    // ── „;"-Zeilenkommentar (A6-009) ──────────────────────────────────────

    [Theory]
    [InlineData("1. e4 ; Königsbauer", 0, "1. e4 ", 0)]
    [InlineData("1. e4 {Idee; frei} e5", 0, "1. e4 {Idee; frei} e5", 0)]          // „;" im Kommentar ist Text
    [InlineData("noch Kommentar; x} e5 ; Rest", 1, "noch Kommentar; x} e5 ", 0)]  // Kommentar aus der Vorzeile
    [InlineData("1. e4 ; siehe {Anm.", 0, "1. e4 ", 0)]                          // „{" hinter „;" öffnet nichts
    [InlineData("1. e4 {offen", 0, "1. e4 {offen", 1)]
    [InlineData("} e4", 0, "} e4", 0)]                                           // verirrte „}" nicht verschleppen
    public void StripLineComment_CutsOnlyOutsideBraceComments(string line, int depthIn, string text, int depthOut)
        => Assert.Equal((text, depthOut), PgnTokens.StripLineComment(line, depthIn));

    private const string Headers = "[Event \"Test\"]\n[White \"A; B\"]\n[Black \"C\"]\n\n";

    /// <summary>Spiegeltest: dieselbe Datei durch beide Zerleger. Bis 0.624.0 lieferte der
    /// Repertoire-Parser für den ersten Fall nur [e4] (alles hinter „;" weg, weil die Zeilen schon
    /// zusammengefügt waren) und der Upload las „;" und „Königsbauer" als Züge.</summary>
    [Theory]
    [InlineData("1. e4 ; Königsbauer\n1... e5 2. Nf3 Nc6 *")]
    [InlineData("1. e4 ; Königsbauer\r\n1... e5 2. Nf3 Nc6 *")]
    [InlineData("1. e4 {Idee; frei} e5 ; Rest\n2. Nf3 Nc6 *")]
    [InlineData("1. e4 {mehrzeilig;\nnoch Kommentar; x} e5\n2. Nf3 Nc6 *")]
    [InlineData("1. e4 ; siehe {Anm.\n1... e5 2. Nf3 Nc6 *")]
    [InlineData("1. e4 e5\n; eine ganze Zeile Kommentar\n2. Nf3 Nc6 *")]
    public void LineComment_BothParsersReadTheSameMainline(string movetext)
    {
        var expected = new[] { "e4", "e5", "Nf3", "Nc6" };
        var pgn = Headers + movetext;

        var tree = PgnMoveTree.ParseSections(pgn).Single(s => s.Moves.Count > 0);
        Assert.Equal(expected, tree.Moves.Select(m => m.San));

        var game = PgnParser.SplitGames(pgn).Single();
        Assert.Equal(expected, PgnParser.ExtractMainlineSans(game.MoveText));
        // Der Partie-Upload (SavedGameService.ImportPgnAsync) lehnte genau hier mit „illegal" ab.
        Assert.Equal(4, PgnParser.TryExtractUciMainline(new Chess.ChessBoard().ToFen(), game.MoveText)?.Count);
    }

    /// <summary>Ein „;" in einem Header-Wert ist kein Kommentar — in keinem der beiden Zerleger.</summary>
    [Fact]
    public void LineComment_LeavesHeaderValuesAlone()
    {
        var pgn = Headers + "1. e4 ; x\n1... e5 *";
        Assert.Equal("A; B", PgnMoveTree.ParseSections(pgn).Single().White);
        Assert.Equal("A; B", PgnParser.SplitGames(pgn).Single().Headers["White"]);
    }

    /// <summary>Eine reine Kommentarzeile ist kein Movetext: sie trennte früher als „Zugtext" die
    /// Partie von ihren Headern und erzeugte eine Phantom-Partie ohne Züge.</summary>
    [Fact]
    public void LineComment_OnlyLineIsNoMovetext()
    {
        var games = PgnParser.SplitGames("; exportiert von X\n[Event \"a\"]\n; Notiz\n[Site \"b\"]\n\n1. e4 *").ToList();
        var game = Assert.Single(games);
        Assert.Equal("b", game.Headers["Site"]);
        Assert.Equal(new[] { "e4" }, PgnParser.ExtractMainlineSans(game.MoveText));
    }

    // ── Ergebnis-Token (N11-004) ──────────────────────────────────────────

    [Theory]
    [InlineData("1-0")]
    [InlineData("0-1")]
    [InlineData("1/2-1/2")]
    [InlineData("1/2")]
    [InlineData("*")]
    [InlineData("½-½")]
    public void IsResultToken_KnowsEveryResultForm(string token) => Assert.True(PgnTokens.IsResultToken(token));

    [Theory]
    [InlineData("")]
    [InlineData("e4")]
    [InlineData("1.")]
    [InlineData("12.e4")]
    [InlineData("1/2-")]
    [InlineData("0-0")]
    public void IsResultToken_RejectsMovesAndNumbers(string token) => Assert.False(PgnTokens.IsResultToken(token));

    /// <summary>Spiegeltest über ALLE Zerleger mit denselben Ergebnis-Literalen. Bis 0.624.0 kannten
    /// nur PgnParser und PermissiveSan das nackte „1/2": der Rohbestand zählte es als Halbzug
    /// (PlyCount 5, anderer MovesHash — die Fassung mit „1/2-1/2" blieb als Dublette unerkannt),
    /// die Rekonstruktion machte daraus einen Zug.</summary>
    [Theory]
    [InlineData("1-0")]
    [InlineData("0-1")]
    [InlineData("1/2-1/2")]
    [InlineData("1/2")]
    [InlineData("*")]
    [InlineData("½-½")]
    public void ResultToken_EveryParserDropsIt(string result)
    {
        var expected = new[] { "e4", "e5", "Nf3", "Nc6" };
        var movetext = "1. e4 e5 2. Nf3 Nc6 " + result;
        var pgn = Headers + movetext;

        Assert.Equal(expected, PgnMoveTree.ParseSections(pgn).Single().Moves.Select(m => m.San));
        Assert.Equal(expected, PgnParser.ExtractMainlineSans(PgnParser.SplitGames(pgn).Single().MoveText));
        Assert.Equal(expected, ReconstructionChain.SplitMoves(movetext));

        var stats = LibraryGameReader.Analyse(movetext);
        var bare = LibraryGameReader.Analyse("1. e4 e5 2. Nf3 Nc6");
        Assert.Equal(4, stats.PlyCount);
        Assert.Equal(bare.MovesHash, stats.MovesHash);
        Assert.Equal(bare.OpeningLine, stats.OpeningLine);
    }
}
