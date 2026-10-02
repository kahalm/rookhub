using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Ein Token nur aus Punkten („...", „..", „…") ist der Rest einer Zugnummer, kein Zug. chess.js schreibt eine Partie,
/// die mit Schwarz am Zug beginnt, als „4. ... Bc5 5. c3": die Nummer „4." fiel weg, die drei Punkte blieben als „Zug"
/// stehen — der PGN-Import meldete die Partie als illegal (gegen die Dev-API gemessen, 2026-10-02), und die Zähler der
/// Kommentar-/Marker-Schlüssel liefen einen Halbzug vor. Beide Wege zählen jetzt dieselben Halbzüge.
/// </summary>
public class PgnParserMoveNumberRestTests
{
    /// <summary>Zweispringerspiel nach 4.O-O — Schwarz am Zug.</summary>
    private const string FenBlackToMove = "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQ1RK1 b kq - 5 4";

    [Theory]
    [InlineData("4. ... Bc5 5. c3 d6 *")]       // so schreibt es chess.pgn()
    [InlineData("4. .. Bc5 5. c3 d6 *")]
    [InlineData("4. … Bc5 5. c3 d6 *")]         // Auslassungszeichen als ein Zeichen
    [InlineData("4... Bc5 5. c3 d6 *")]         // ging schon vorher
    [InlineData("4...Bc5 5.c3 d6 *")]
    [InlineData("Bc5 5. c3 d6 *")]
    public void ExtractMainlineSans_StandaloneDots_AreNoMove(string moveText)
        => Assert.Equal(new[] { "Bc5", "c3", "d6" }, PgnParser.ExtractMainlineSans(moveText));

    [Fact]
    public void TryExtractUciMainline_ChessJsForm_FromAPositionWithBlackToMove()
        => Assert.Equal(new[] { "f8c5", "c2c3", "d7d6" },
            PgnParser.TryExtractUciMainline(FenBlackToMove, "4. ... Bc5 5. c3 d6 *"));

    /// <summary>Die Kommentar-Schlüssel hängen am Halbzug — mit den drei Punkten als eigenem Token gehörte „Läufer raus."
    /// sonst zu Halbzug 1 (c3) statt 0 (Bc5).</summary>
    [Theory]
    [InlineData("{Aus der Stellung.} 4. ... Bc5 {Läufer raus.} 5. c3 {Bauer.} d6 *")]
    [InlineData("{Aus der Stellung.} 4... Bc5 {Läufer raus.} 5. c3 {Bauer.} d6 *")]
    public void ExtractMoveComments_KeysMatchTheMainline(string moveText)
    {
        var map = PgnParser.ExtractMoveComments(moveText)!;
        Assert.Equal(3, map.Count);
        Assert.Equal("Aus der Stellung.", map[-1]);
        Assert.Equal("Läufer raus.", map[0]);
        Assert.Equal("Bauer.", map[1]);
    }

    [Fact]
    public void Markers_tquShapesAlt_UseTheSameCount()
    {
        const string moveText = "4. ... Bc5 {[%alt Be7] [%csl Gc5]} 5. c3 {[%tqu \"x\"]} d6 *";

        Assert.Equal(1, PgnParser.FindTquMoveIndex(moveText));
        var shape = Assert.Single(Assert.Single(PgnParser.ExtractMoveShapes(moveText)!).Value);
        Assert.Equal(0, PgnParser.ExtractMoveShapes(moveText)!.Keys.Single());
        Assert.Equal("c5", shape.O);
        var alt = Assert.Single(PgnParser.ExtractAltMoves(FenBlackToMove, moveText)!);
        Assert.Equal(0, alt.Key);
        Assert.Equal(new[] { "f8e7" }, alt.Value);
    }

    /// <summary>Nichts sonst ändert sich: Nullzug, Ergebnis-Token und gewöhnliche Zugnummern bleiben, wie sie waren.</summary>
    [Fact]
    public void OtherTokens_Unchanged()
    {
        Assert.Equal(new[] { "e4", "e5", "Nf3" }, PgnParser.ExtractMainlineSans("1. e4 e5 2. Nf3 1/2-1/2"));
        Assert.Equal(new[] { "--", "e5" }, PgnParser.ExtractMainlineSans("1. -- e5 *"));
        Assert.Equal("nach dem Nullzug", PgnParser.ExtractMoveComments("1. -- {nach dem Nullzug} e5 *")![0]);
    }
}
