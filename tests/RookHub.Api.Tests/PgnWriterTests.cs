using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der gemeinsame PGN-Schreiber. Er entstand aus vier Stellen, die jede ihr eigenes
/// <c>Escape</c> mitbrachten; die Golden-Tests der Konsumenten (<c>CoursePgnExporterTests</c>,
/// <c>SharedLineServiceTests</c>, <c>SavedGamePgnTests</c>) halten deren Ausgabe fest, hier stehen
/// die Bausteine selbst.
/// </summary>
public class PgnWriterTests
{
    // ── Escape ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "")]
    [InlineData("harmlos", "harmlos")]
    [InlineData("Ti\"tel", "Ti\\\"tel")]
    [InlineData("Pfad\\zu", "Pfad\\\\zu")]
    public void Escape_MasksQuotesAndBackslashes(string input, string expected)
        => Assert.Equal(expected, PgnWriter.Escape(input));

    /// <summary>Die REIHENFOLGE ist der Punkt: erst der Backslash, dann das Anführungszeichen.
    /// Umgekehrt verdoppelte der zweite Durchgang die Backslashes, die der erste gerade gesetzt
    /// hat — aus <c>\"</c> würde <c>\\\\"</c> statt <c>\\\"</c>.</summary>
    [Fact]
    public void Escape_BackslashBeforeQuote_NotTheOtherWayRound()
        => Assert.Equal("\\\\\\\"", PgnWriter.Escape("\\\""));

    [Fact]
    public void Escape_Null_IsEmpty() => Assert.Equal("", PgnWriter.Escape(null));

    // ── Tag ───────────────────────────────────────────────────────────────

    [Fact]
    public void Tag_WritesOneLineWithEscapedValue()
        => Assert.Equal("[Event \"My \\\"Book\\\"\"]\n", PgnWriter.Tag("Event", "My \"Book\""));

    [Fact]
    public void Tag_NullValue_IsAnEmptyTag()
        => Assert.Equal("[Black \"\"]\n", PgnWriter.Tag("Black", null));

    // ── Kommentar ─────────────────────────────────────────────────────────

    /// <summary>Eine schließende Klammer im Text würde den Kommentar beenden — sie wird ersetzt;
    /// Zeilenumbrüche und Mehrfach-Leerzeichen werden zu einem Leerzeichen.</summary>
    [Fact]
    public void CleanComment_ReplacesClosingBraceAndFlattensWhitespace()
        => Assert.Equal("a ) b c", PgnWriter.CleanComment("a } b\n\n  c"));

    // ── MoveText: Nummerierung ────────────────────────────────────────────

    [Fact]
    public void MoveText_FromStartPosition_NumbersEveryWhiteMove()
        => Assert.Equal("1. e4 c5 2. Nf3 d6 *", PgnWriter.MoveText(new[] { "e4", "c5", "Nf3", "d6" }));

    /// <summary>Ab einer FEN mit SCHWARZ am Zug trägt der erste Halbzug die Nummer mit „…" —
    /// ohne sie kann ein strenger PGN-Leser ihn nicht einordnen. Die Nummer kommt aus dem
    /// sechsten FEN-Feld.</summary>
    [Fact]
    public void MoveText_FromFenWithBlackToMove_StartsWithEllipsisNumber()
        => Assert.Equal("12... Nf6 13. Bg5 Be7 *", PgnWriter.MoveText(
            new[] { "Nf6", "Bg5", "Be7" },
            "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 5 12"));

    /// <summary>Eine FEN ohne brauchbare Zugnummer fällt auf 1 zurück statt auf 0 oder einen Wurf.</summary>
    [Theory]
    [InlineData("8/8/8/8/8/8/8/8 w")]
    [InlineData("8/8/8/8/8/8/8/8 w - - 0 0")]
    [InlineData("kaputt")]
    public void MoveText_FenWithoutUsableMoveNumber_StartsAtOne(string fen)
        => Assert.StartsWith("1. e4 ", PgnWriter.MoveText(new[] { "e4" }, fen));

    // ── MoveText: Kommentare ──────────────────────────────────────────────

    [Fact]
    public void MoveText_PutsCommentsBehindTheirHalfMove_AndTheIntroFirst()
        => Assert.Equal("{Italienisch} 1. e4 {Königsbauer} e5 2. Nf3 {entwickelt} *",
            PgnWriter.MoveText(new[] { "e4", "e5", "Nf3" }, null, new Dictionary<int, string>
            {
                [-1] = "Italienisch",
                [0] = "Königsbauer",
                [2] = "entwickelt",
            }));

    /// <summary>Ein leerer Kommentar erzeugt KEIN leeres Klammerpaar.</summary>
    [Fact]
    public void MoveText_BlankComments_AreSkipped()
        => Assert.Equal("1. e4 *", PgnWriter.MoveText(new[] { "e4" }, null,
            new Dictionary<int, string> { [-1] = "  ", [0] = "" }));

    /// <summary>Eine Einleitung OHNE Züge bleibt stehen (Info-Linien eines Kurses bestehen nur
    /// aus ihr).</summary>
    [Fact]
    public void MoveText_IntroWithoutMoves_IsKept()
        => Assert.Equal("{nur Info} *", PgnWriter.MoveText(Array.Empty<string>(), null,
            new Dictionary<int, string> { [-1] = "nur Info" }));

    // ── MoveText: Marker vor dem Zug ──────────────────────────────────────

    /// <summary>Der Text VOR einem Halbzug (heute der Trainingsmarker) kommt wörtlich heraus —
    /// er ist eine Auszeichnung, keine Prosa, und darf nicht durch die Kommentar-Säuberung.
    /// Ein Schwarz-Zug direkt dahinter bekommt seine Nummer mit „…".</summary>
    [Fact]
    public void MoveText_BeforeMarker_IsWrittenVerbatimAndForcesTheBlackNumber()
        => Assert.Equal("1. e4 {MARKER} 1... e5 2. Nf3 *", PgnWriter.MoveText(
            new[] { "e4", "e5", "Nf3" }, null, null, "*",
            new Dictionary<int, string> { [1] = "{MARKER}" }));

    // ── MoveText: Ergebnis ────────────────────────────────────────────────

    [Fact]
    public void MoveText_UsesTheGivenResultToken()
        => Assert.Equal("1. e4 e5 1-0", PgnWriter.MoveText(new[] { "e4", "e5" }, result: "1-0"));

    /// <summary>Ohne Ergebnis endet der Text OHNE Leerzeichen — der Aufrufer hängt sein Ende
    /// selbst an. Das ist kein Schönheitsunterschied: der Kurs-Export schreibt deshalb bei einer
    /// leeren Linie <c>" *"</c>, die anderen beiden <c>"*"</c>, und beides steht so in Bestand.</summary>
    [Fact]
    public void MoveText_WithoutResult_IsTrimmedSoTheCallerCanAppendItsOwnEnding()
    {
        Assert.Equal("1. e4 e5", PgnWriter.MoveText(new[] { "e4", "e5" }, result: null));
        Assert.Equal("", PgnWriter.MoveText(Array.Empty<string>(), result: null));
        Assert.Equal("*", PgnWriter.MoveText(Array.Empty<string>()));
    }
}
