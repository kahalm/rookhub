using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Kapitel-Versatz der <c>[Round]</c>-Kopfzeilen beim laufenden Browser-Import. piratechess zählt die
/// Kapitel je Parse-Aufruf von vorn; ohne Versatz trüge die erste Linie JEDES Chunks wieder
/// „002.001" — und da die LineId eines Kurs-Puzzles <c>Datei:Round</c> ist, überschriebe Chunk 2 die
/// Linien von Chunk 1.
/// </summary>
public class ChessableRoundOffsetTests
{
    private const string Pgn = "[Event \"C\"]\n[Round \"002.001\"]\n\n1. e4 *\n\n[Event \"C\"]\n[Round \"003.012\"]\n\n1. d4 *\n";

    [Fact]
    public void Shift_MovesChapterNumbers_AndKeepsLineNumbers()
    {
        var shifted = ChessableRoundOffset.Shift(Pgn, 5);
        Assert.Contains("[Round \"007.001\"]", shifted);
        Assert.Contains("[Round \"008.012\"]", shifted);
        Assert.DoesNotContain("[Round \"002.001\"]", shifted);
        // Der Zugtext bleibt unangetastet.
        Assert.Contains("1. e4 *", shifted);
    }

    [Fact]
    public void Shift_WithoutOffset_ReturnsPgnUnchanged()
    {
        Assert.Equal(Pgn, ChessableRoundOffset.Shift(Pgn, 0));
        Assert.Equal(Pgn, ChessableRoundOffset.Shift(Pgn, -3));
    }

    [Fact]
    public void Shift_KeepsThreeDigitForm()
    {
        // Dreistellig bleibt dreistellig, damit die Lesereihenfolge (Round.Length, dann Round) stimmt.
        Assert.Contains("[Round \"012.001\"]", ChessableRoundOffset.Shift("[Round \"002.001\"]", 10));
        Assert.Contains("[Round \"102.001\"]", ChessableRoundOffset.Shift("[Round \"002.001\"]", 100));
    }

    [Fact]
    public void MaxChapter_ReadsHighestChapterFromResult()
    {
        Assert.Equal(3, ChessableRoundOffset.MaxChapter(Pgn));
        Assert.Equal(8, ChessableRoundOffset.MaxChapter(ChessableRoundOffset.Shift(Pgn, 5)));
    }

    [Fact]
    public void MaxChapter_WithoutRounds_IsZero()
    {
        Assert.Equal(0, ChessableRoundOffset.MaxChapter("[Event \"C\"]\n\n1. e4 *"));
        Assert.Equal(0, ChessableRoundOffset.MaxChapter(null));
        Assert.Equal(0, ChessableRoundOffset.MaxChapter(""));
    }

    [Fact]
    public void ChunksFollowEachOther_WithoutCollision()
    {
        // Zwei Chunks, beide vom Parser mit „002.001" geliefert: nach dem Versatz tragen sie
        // verschiedene Kapitelnummern — genau das verhindert, dass der zweite den ersten überschreibt.
        const string chunk = "[Round \"002.001\"]\n\n1. e4 *\n";
        var first = ChessableRoundOffset.Shift(chunk, 0);
        var offset = ChessableRoundOffset.MaxChapter(first);
        var second = ChessableRoundOffset.Shift(chunk, offset);

        Assert.Contains("[Round \"002.001\"]", first);
        Assert.Contains("[Round \"004.001\"]", second);
        Assert.NotEqual(ChessableRoundOffset.MaxChapter(first), ChessableRoundOffset.MaxChapter(second));
    }

    [Fact]
    public void NextOffset_ContinuesSeamlessly_WhenEveryChunkStartsAtTheSameNumber()
    {
        // piratechess beginnt jeden Einzel-Chunk bei 002. Versatz um das Maximum ergab 002, 004, 006 …
        // (Live-Import 2026-09-19); relativ zur kleinsten Nummer wird daraus 002, 003, 004.
        const string chunk = "[Round \"002.001\"]\n\n1. e4 *\n\n[Round \"002.002\"]\n\n1. d4 *\n";
        var first = ChessableRoundOffset.Shift(chunk, ChessableRoundOffset.NextOffset(0, chunk));
        var max1 = ChessableRoundOffset.MaxChapter(first);
        var second = ChessableRoundOffset.Shift(chunk, ChessableRoundOffset.NextOffset(max1, chunk));
        var max2 = ChessableRoundOffset.MaxChapter(second);
        var third = ChessableRoundOffset.Shift(chunk, ChessableRoundOffset.NextOffset(max2, chunk));

        Assert.Contains("[Round \"002.001\"]", first);
        Assert.Contains("[Round \"003.001\"]", second);
        Assert.Contains("[Round \"003.002\"]", second);
        Assert.Contains("[Round \"004.001\"]", third);
    }

    [Fact]
    public void NextOffset_KeepsChapterSpacing_InsideAMultiChapterChunk()
    {
        // Ein Chunk mit den Kapiteln 002 und 003 landet hinter Kapitel 5 als 006 und 007.
        const string chunk = "[Round \"002.001\"]\n\n1. e4 *\n\n[Round \"003.001\"]\n\n1. d4 *\n";
        var shifted = ChessableRoundOffset.Shift(chunk, ChessableRoundOffset.NextOffset(5, chunk));
        Assert.Contains("[Round \"006.001\"]", shifted);
        Assert.Contains("[Round \"007.001\"]", shifted);
    }

    [Fact]
    public void NextOffset_IsZero_WithoutPreviousOrWithoutRounds()
    {
        Assert.Equal(0, ChessableRoundOffset.NextOffset(0, "[Round \"002.001\"]"));
        Assert.Equal(0, ChessableRoundOffset.NextOffset(7, "[Event \"C\"]\n\n1. e4 *"));
        Assert.Equal(0, ChessableRoundOffset.MinChapter(null));
        Assert.Equal(2, ChessableRoundOffset.MinChapter("[Round \"005.001\"]\n[Round \"002.003\"]"));
    }

    // ---- Ein Kapitel, das auf mehrere Chunks verteilt ankommt (zu gross fuer EINEN Request) ----

    [Fact]
    public void Shift_WithLineOffset_MovesLineNumbersToo()
    {
        var shifted = ChessableRoundOffset.Shift(Pgn, 0, 40);
        Assert.Contains("[Round \"002.041\"]", shifted);
        Assert.Contains("[Round \"003.052\"]", shifted);
    }

    [Fact]
    public void SameChapterOffset_KeepsTheChunkInTheCurrentChapter()
    {
        // Teil 2 kommt wieder als „002…" an und soll unter Kapitel 7 weiterlaufen.
        var offset = ChessableRoundOffset.SameChapterOffset(7, Pgn);
        Assert.Equal(7, ChessableRoundOffset.MinChapter(ChessableRoundOffset.Shift(Pgn, offset)));
    }

    [Fact]
    public void SameChapterOffset_WithoutCurrentChapter_IsNoOp()
        => Assert.Equal(0, ChessableRoundOffset.SameChapterOffset(0, Pgn));

    [Theory]
    // Egal, ob piratechess die erste Linie als 001 oder 002 nummeriert — der Versatz wird RELATIV
    // gerechnet, damit zwei Teile eines Kapitels sich nie ueberlappen.
    [InlineData("001", "002")]
    [InlineData("002", "003")]
    public void NextLineOffset_LetsTheFollowingPartContinueWithoutCollision(string erste, string zweite)
    {
        var teil = $"[Round \"002.{erste}\"]\n[Round \"002.{zweite}\"]\n";

        var offset = ChessableRoundOffset.NextLineOffset(ChessableRoundOffset.MaxLine(teil), teil);
        var shifted2 = ChessableRoundOffset.Shift(teil, 0, offset);

        // Teil 2 setzt lueckenlos hinter Teil 1 auf ...
        Assert.Equal(ChessableRoundOffset.MaxLine(teil) + 1, ChessableRoundOffset.MinLine(shifted2));
        // ... und keine Nummer aus Teil 1 taucht erneut auf → nichts wird ueberschrieben.
        Assert.DoesNotContain($"[Round \"002.{erste}\"]", shifted2);
        Assert.DoesNotContain($"[Round \"002.{zweite}\"]", shifted2);
    }

    [Fact]
    public void NextLineOffset_WithoutPreviousLine_IsNoOp()
        => Assert.Equal(0, ChessableRoundOffset.NextLineOffset(0, Pgn));

    [Fact]
    public void MaxLine_ReadsTheHighestLineNumber()
    {
        Assert.Equal(12, ChessableRoundOffset.MaxLine(Pgn));
        Assert.Equal(0, ChessableRoundOffset.MaxLine(""));
    }
}
