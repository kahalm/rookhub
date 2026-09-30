using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der EINE PGN-Baumparser des Servers. Er lag bis 0.499.6 als wörtliche Kopie in
/// <c>RepertoireAnalyzeService</c> UND <c>RepertoireLineSource</c>; geprüft war er nur mittelbar
/// über die beiden Dienste. Hier stehen die Tokenizer-Vektoren und die Baum-Regeln direkt —
/// samt der zwei Fälle, die dort als Bug-Kommentare dokumentiert sind (angeklebte Zugnummer,
/// <c>[FEN]</c>-Header).
/// </summary>
public class PgnMoveTreeTests
{
    private static List<string> Sans(List<PgnMove> moves) => moves.Select(m => m.San).ToList();

    // ── Tokenizer ─────────────────────────────────────────────────────────

    [Fact]
    public void Tokenize_DropsBlockComments()
        => Assert.Equal(new[] { "e4", "e5" }, PgnMoveTree.Tokenize("1. e4 {ein Kommentar} e5"));

    [Fact]
    public void Tokenize_DropsLineComments()
        => Assert.Equal(new[] { "e4", "e5" }, PgnMoveTree.Tokenize("1. e4 ; bis Zeilenende\n e5"));

    [Fact]
    public void Tokenize_DropsNags()
        => Assert.Equal(new[] { "e4", "e5" }, PgnMoveTree.Tokenize("1. e4 $3 e5 $14"));

    /// <summary>ChessBase, Fritz und SCID schreiben „1.e4" OHNE Leerzeichen. Ein solches Token fiel
    /// früher durch <c>IsMoveToken</c> (beginnt mit einer Ziffer) und wurde STILL verworfen — damit
    /// fehlten ALLE Weißzüge der Datei.</summary>
    [Fact]
    public void Tokenize_SplitsMoveNumbersGluedToTheMove()
        => Assert.Equal(new[] { "e4", "e5", "Nf3", "Nf6" }, PgnMoveTree.Tokenize("1.e4 e5 2.Nf3 Nf6"));

    [Fact]
    public void Tokenize_SplitsBlackMoveNumberGluedToTheMove()
        => Assert.Equal(new[] { "Nf6" }, PgnMoveTree.Tokenize("12...Nf6"));

    /// <summary>Klammern sind eigene Tokens, auch wenn sie am Zug kleben.</summary>
    [Fact]
    public void Tokenize_TreatsParenthesesAsOwnTokens()
        => Assert.Equal(new[] { "e4", "(", "d4", "d5", ")", "e5" }, PgnMoveTree.Tokenize("1. e4 (1. d4 d5) 1... e5"));

    // ── IsMoveToken ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("e4")]
    [InlineData("Nf3")]
    [InlineData("O-O")]
    [InlineData("Qxd8+")]
    [InlineData("a8=Q")]
    public void IsMoveToken_AcceptsMoves(string token) => Assert.True(PgnMoveTree.IsMoveToken(token));

    [Theory]
    [InlineData("")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("1.")]
    [InlineData("12...")]
    [InlineData("1-0")]
    [InlineData("0-1")]
    [InlineData("1/2-1/2")]
    [InlineData("*")]
    [InlineData("$5")]
    public void IsMoveToken_RejectsEverythingElse(string token) => Assert.False(PgnMoveTree.IsMoveToken(token));

    // ── Zugbaum ───────────────────────────────────────────────────────────

    [Fact]
    public void ParseSections_BuildsVariationTree()
    {
        var section = Assert.Single(PgnMoveTree.ParseSections("[Event \"x\"]\n\n1. e4 e5 (1... c5 2. Nf3) 2. Nf3 *"));

        Assert.Equal(new[] { "e4", "e5", "Nf3" }, Sans(section.Moves));
        // Die Variante hängt am Zug DAVOR (e5), nicht an der Hauptlinie.
        var variation = Assert.Single(section.Moves[1].Variations);
        Assert.Equal(new[] { "c5", "Nf3" }, Sans(variation));
        Assert.Empty(section.Moves[0].Variations);
    }

    [Fact]
    public void ParseSections_NestsVariationsInsideVariations()
    {
        var section = Assert.Single(PgnMoveTree.ParseSections("[Event \"x\"]\n\n1. e4 e5 (1... c5 2. Nf3 d6 (2... Nc6)) *"));

        var sicilian = Assert.Single(section.Moves[1].Variations);
        Assert.Equal(new[] { "c5", "Nf3", "d6" }, Sans(sicilian));
        Assert.Equal(new[] { "Nc6" }, Sans(Assert.Single(sicilian[2].Variations)));
    }

    /// <summary>Eine Variante VOR dem ersten Zug hat keinen Zug, an den sie gehören könnte —
    /// sie wird verworfen, und die Hauptlinie läuft ungestört weiter.</summary>
    [Fact]
    public void ParseSections_VariationBeforeTheFirstMove_IsDropped()
    {
        var section = Assert.Single(PgnMoveTree.ParseSections("[Event \"x\"]\n\n(1. d4 d5) 1. e4 e5 *"));

        Assert.Equal(new[] { "e4", "e5" }, Sans(section.Moves));
        Assert.All(section.Moves, m => Assert.Empty(m.Variations));
    }

    // ── Header ────────────────────────────────────────────────────────────

    /// <summary>Chessable-Linien starten mitten in der Partie. Ohne den <c>[FEN]</c>-Header beginnt
    /// der Walk in der Grundstellung — der erste Zug ist dort illegal (Linie fehlt still) oder
    /// zufällig legal (FALSCHE Stellungen gelten als „im Repertoire"). Fund aus v0.340.0.</summary>
    [Fact]
    public void ParseSections_ReadsFenHeader()
    {
        const string fen = "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 5 4";
        var section = Assert.Single(PgnMoveTree.ParseSections($"[Event \"x\"]\n[FEN \"{fen}\"]\n[SetUp \"1\"]\n\n4... Bc5 *"));

        Assert.Equal(fen, section.StartFen);
        Assert.Equal(new[] { "Bc5" }, Sans(section.Moves));
    }

    [Fact]
    public void ParseSections_ReadsWhiteAndBlackHeaders()
    {
        var section = Assert.Single(PgnMoveTree.ParseSections("[Event \"x\"]\n[White \"Linienname\"]\n[Black \"Kapitel 3\"]\n\n1. e4 *"));

        Assert.Equal("Linienname", section.White);
        Assert.Equal("Kapitel 3", section.Black);
        Assert.Null(section.StartFen);
    }

    [Fact]
    public void ParseSections_MissingHeaders_AreNull_NotEmptyString()
    {
        var section = Assert.Single(PgnMoveTree.ParseSections("[Event \"x\"]\n\n1. e4 *"));

        Assert.Null(section.White);
        Assert.Null(section.Black);
        Assert.Null(section.StartFen);
    }

    /// <summary>Ein <c>[FEN ""]</c> ist KEINE Startstellung — sonst stürbe der Walk an einer leeren FEN.</summary>
    [Fact]
    public void ParseSections_BlankFenHeader_CountsAsNoFen()
        => Assert.Null(Assert.Single(PgnMoveTree.ParseSections("[Event \"x\"]\n[FEN \"   \"]\n\n1. e4 *")).StartFen);

    // ── Abschnitte ────────────────────────────────────────────────────────

    /// <summary>Die POSITION eines Abschnitts trägt den <c>gameIndex</c>, über den Client und Server
    /// dieselbe Linie meinen — ein zug-loser Abschnitt (Kapitel-Intro) muss deshalb in der Liste
    /// bleiben, sonst verschieben sich alle folgenden.</summary>
    [Fact]
    public void ParseSections_KeepsMovelessSections_SoGameIndexStaysAligned()
    {
        var sections = PgnMoveTree.ParseSections(
            "[Event \"x\"]\n[White \"Intro\"]\n\n*\n\n[Event \"x\"]\n[White \"Linie\"]\n\n1. e4 e5 *");

        Assert.Equal(2, sections.Count);
        Assert.Equal("Intro", sections[0].White);
        Assert.Empty(sections[0].Moves);
        Assert.Equal("Linie", sections[1].White);
        Assert.Equal(new[] { "e4", "e5" }, Sans(sections[1].Moves));
    }

    [Fact]
    public void ParseSections_SplitsOnEventHeaderOnly_NotOnEventDate()
    {
        var sections = PgnMoveTree.ParseSections(
            "[Event \"x\"]\n[EventDate \"2026.09.21\"]\n[White \"A\"]\n\n1. e4 *\n\n[Event \"y\"]\n[White \"B\"]\n\n1. d4 *");

        Assert.Equal(2, sections.Count);
        Assert.Equal(new[] { "A", "B" }, sections.Select(s => s.White));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n \n")]
    public void ParseSections_EmptyText_IsEmpty(string text) => Assert.Empty(PgnMoveTree.ParseSections(text));

    // ── Tiefendeckel (Codereview 2026-09-29, A6-001) ────────────────────────

    /// <summary>Tiefste Varianten-Schachtelung eines Zugbaums (0 = nur Hauptlinie).</summary>
    private static int Depth(List<PgnMove> moves)
        => moves.Count == 0 ? 0 : moves.Max(m => m.Variations.Count == 0 ? 0 : 1 + m.Variations.Max(Depth));

    /// <summary><paramref name="levels"/> ineinander geschachtelte Varianten zu 1... e5, danach läuft
    /// die Hauptlinie weiter.</summary>
    private static string Nested(int levels)
        => "[Event \"x\"]\n\n1. e4 e5 " + string.Concat(Enumerable.Repeat("(1... c5 ", levels))
           + string.Concat(Enumerable.Repeat(") ", levels)) + "2. Nf3 *";

    /// <summary>Der Parser rief sich je „(" selbst auf, die Walker über den Baum je Variante noch einmal —
    /// ohne Deckel. Varianten bis <see cref="PgnMoveTree.MaxVariationDepth"/> bleiben, tiefere fallen weg,
    /// und die Hauptlinie hinter der Klammer läuft weiter.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(PgnMoveTree.MaxVariationDepth)]
    [InlineData(PgnMoveTree.MaxVariationDepth + 1)]
    [InlineData(1000)]
    public void ParseSections_NestedVariations_AreCutAtMaxDepth_MainlineContinues(int levels)
    {
        var section = Assert.Single(PgnMoveTree.ParseSections(Nested(levels)));

        Assert.Equal(new[] { "e4", "e5", "Nf3" }, Sans(section.Moves));
        Assert.Equal(Math.Min(levels, PgnMoveTree.MaxVariationDepth), Depth(section.Moves));
    }

    /// <summary>Der Fall aus dem Review: 100 000 offene Klammern (100 KB, als Repertoire-Datei hochladbar)
    /// sprengten den Stack — ein StackOverflow ist nicht fangbar und beendete die ganze API. Hier reicht es,
    /// dass der Aufruf überhaupt zurückkommt.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParseSections_HundredThousandParentheses_DoNotOverflowTheStack(bool closed)
    {
        const int n = 100_000;
        var text = "[Event \"x\"]\n\n1. e4 " + new string('(', n) + " e5"
                   + (closed ? new string(')', n) + " 1... c5 *" : "");

        var section = Assert.Single(PgnMoveTree.ParseSections(text));

        Assert.Equal(closed ? new[] { "e4", "c5" } : new[] { "e4" }, Sans(section.Moves));
        Assert.True(Depth(section.Moves) <= PgnMoveTree.MaxVariationDepth);
    }

    /// <summary>Auch der Weg ohne Abschnitte (RepertoireAnalyzeService liest den ganzen Text als Movetext)
    /// geht durch <c>ParseMoveTokens</c> — mit demselben Deckel.</summary>
    [Fact]
    public void ParseMoveTokens_WithoutSections_IsCappedToo()
    {
        var (moves, _) = PgnMoveTree.ParseMoveTokens(PgnMoveTree.Tokenize("1. e4 " + new string('(', 100_000) + " e5"), 0);

        Assert.Equal(new[] { "e4" }, Sans(moves));
    }

    [Theory]
    [InlineData("1. e4 e5 *", 0)]
    [InlineData("1. e4 (1. d4) e5 (1... c5 2. Nf3 (2. c3)) *", 2)]
    [InlineData("1. e4 {Kommentar ((((( } e5 ; auch hier (((\n 2. Nf3 (2. Nc3) *", 1)]
    [InlineData("1. e4 ) ) (1. d4 (1. c4)) *", 2)]
    public void VariationDepthOf_CountsNestingOutsideComments(string text, int expected)
        => Assert.Equal(expected, PgnMoveTree.VariationDepthOf(text));

    // ── Suffixe ───────────────────────────────────────────────────────────

    /// <summary>Bewertungs- und Schachzeichen fallen am SAN weg, damit die Schach-Lib ihn parsen kann.</summary>
    [Fact]
    public void ParseSections_StripsSanSuffixes()
        => Assert.Equal(new[] { "e4", "Nf6", "Qxf7", "Bb5" },
            Sans(Assert.Single(PgnMoveTree.ParseSections("[Event \"x\"]\n\n1. e4!? Nf6?! 2. Qxf7# Bb5+ *")).Moves));
}
