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
}
