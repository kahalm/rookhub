using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Direkte Vektoren für <see cref="PermissiveSan"/> (SAN→UCI auf ILLEGALEN Diagramm-FENs).
/// Bisher lief die einzige Abdeckung indirekt über <c>PgnParser.TryExtractUciMainlinePermissive</c>
/// (4 Fälle) — Rochade, en passant, schwarze Promotion, Mehrdeutigkeit und Pfad-Blockaden waren offen.
/// FALLE: Fehlauflösungen hier degradieren Info-Linien still zu falschen Durchklick-Zügen statt zu
/// crashen (dieselbe Fehlerklasse wie die Hotfixes v0.316.1/v0.316.3) — die Tests prüfen deshalb
/// nicht nur den ersten Zug, sondern hängen einen Folgezug an, der NUR passt, wenn der Brettzustand
/// (mitgezogener Turm, geschlagener e.p.-Bauer, umgewandelte Figur) korrekt fortgeschrieben wurde.
/// </summary>
public class PermissiveSanTests
{
    // ---- Rochade -----------------------------------------------------------
    [Fact]
    public void Castle_MovesRookAlong()
    {
        // O-O: e1g1; der Folgezug Rf4 ist nur auflösbar, wenn der Turm h1→f1 mitgezogen wurde.
        var uci = PermissiveSan.TryResolve("4k3/8/8/8/8/8/8/4K2R w - - 0 1", ["O-O", "Ke7", "Rf4"]);
        Assert.Equal(new[] { "e1g1", "e8e7", "f1f4" }, uci);
    }

    [Fact]
    public void Castle_LongSide_MovesRookToD1()
    {
        // O-O-O: e1c1, Turm a1→d1 → Folgezug Rd4 belegt den Turm auf d1.
        var uci = PermissiveSan.TryResolve("4k3/8/8/8/8/8/8/R3K3 w - - 0 1", ["O-O-O", "Ke7", "Rd4"]);
        Assert.Equal(new[] { "e1c1", "e8e7", "d1d4" }, uci);
    }

    [Fact]
    public void Castle_OnKinglessDiagram_YieldsNominalKingMove()
    {
        // FALLE (bewusst so): Castle() unterstellt den König auf e1/e8, OHNE ihn zu prüfen. Auf den
        // königslosen Chessable-Musterdiagrammen — genau dem Zweck dieses Parsers — entsteht damit
        // ein Zug eines LEEREN Feldes (e1g1), der Turm bleibt auf h1. Das ist für die reine
        // Durchklick-Anzeige akzeptiert (siehe Doku-Kommentar am Parser); der Test hält das Verhalten
        // fest, damit eine spätere Änderung hier bewusst geschieht.
        var uci = PermissiveSan.TryResolve("7r/8/8/8/8/8/8/7R w - - 0 1", ["O-O"]);
        Assert.Equal(new[] { "e1g1" }, uci);
    }

    // ---- En passant --------------------------------------------------------
    [Fact]
    public void EnPassant_RemovesCapturedPawn()
    {
        // dxe6 schlägt diagonal auf ein LEERES Feld → der schwarze Bauer auf e5 muss verschwinden.
        // Nachweis: der schwarze Turm zieht danach a5→h5 quer über d5+e5 — blockiert, wenn e5 noch steht.
        var uci = PermissiveSan.TryResolve("8/8/8/r2Pp3/8/8/8/8 w - - 0 1", ["dxe6", "Rh5"]);
        Assert.Equal(new[] { "d5e6", "a5h5" }, uci);
    }

    // ---- Umwandlung --------------------------------------------------------
    [Fact]
    public void BlackPromotion_ProducesLowercasePieceOnBoard()
    {
        // Schwarze Promotion muss KLEIN geschrieben aufs Brett (sonst findet die nachfolgende
        // schwarze Figuren-SAN „Qb8" ihre eigene Dame nicht mehr).
        var uci = PermissiveSan.TryResolve("8/8/8/8/8/8/1p6/K7 b - - 0 1", ["b1=Q", "Ka2", "Qb8"]);
        Assert.Equal(new[] { "b2b1q", "a1a2", "b1b8" }, uci);
    }

    [Fact]
    public void Underpromotion_InPawnCapture_KeepsPromotionPiece()
    {
        // Bauernschlag mit Unterverwandlung: gxh8=N (weiß, g7xh8).
        var uci = PermissiveSan.TryResolve("7r/6P1/8/8/8/8/8/8 w - - 0 1", ["gxh8=N"]);
        Assert.Equal(new[] { "g7h8n" }, uci);
    }

    // ---- Mehrdeutigkeit ----------------------------------------------------
    [Fact]
    public void Disambiguation_ByRankAndFile_PicksTheNamedPiece()
    {
        // Zwei Türme auf a1/a8: „R1a5" (Reihe) muss den a1-Turm nehmen, „R8a5" den a8-Turm.
        Assert.Equal(new[] { "a1a5" }, PermissiveSan.TryResolve("R7/8/8/8/8/8/8/R7 w - - 0 1", ["R1a5"]));
        Assert.Equal(new[] { "a8a5" }, PermissiveSan.TryResolve("R7/8/8/8/8/8/8/R7 w - - 0 1", ["R8a5"]));
        // Datei-Disambiguierung auf einer Reihe: „Rad1" = a1-Turm.
        Assert.Equal(new[] { "a1d1" }, PermissiveSan.TryResolve("8/8/8/8/8/8/8/R6R w - - 0 1", ["Rad1"]));
    }

    [Fact]
    public void AmbiguousMove_WithoutDisambiguation_TakesLowestSquareIndex()
    {
        // FALLE: ohne Disambiguierung gewinnt schlicht der ERSTE Treffer im Feld-Scan (s = 0…63,
        // also von a1 aufwärts) — brett-orientierungsabhängig, aber deterministisch. Solches SAN ist
        // in gültigen PGNs nicht zulässig; der Test pinnt das Verhalten für den Fehlerfall.
        Assert.Equal(new[] { "a1a5" }, PermissiveSan.TryResolve("R7/8/8/8/8/8/8/R7 w - - 0 1", ["Ra5"]));
    }

    // ---- Pfad-Blockade / Abbruch-Semantik ----------------------------------
    [Fact]
    public void BlockedPath_IsNotResolved()
    {
        // Turm a1 nach a5, aber a3 ist besetzt → kein Kandidat → nichts auflösbar → null.
        Assert.Null(PermissiveSan.TryResolve("8/8/8/8/8/p7/8/R7 w - - 0 1", ["Ra5"]));
    }

    [Fact]
    public void UnresolvableMove_KeepsResolvedPrefix()
    {
        // Nicht auflösbarer Zug (keine weiße Dame auf dem Brett) beendet die Sequenz — das bereits
        // aufgelöste Präfix bleibt erhalten, statt die ganze Linie zu verwerfen.
        var uci = PermissiveSan.TryResolve("6k1/5pp1/6P1/8/8/8/8/7R w - - 0 1", ["Rh8+", "Kxh8", "Qd5"]);
        Assert.Equal(new[] { "h1h8", "g8h8" }, uci);
    }

    [Fact]
    public void ResultTokenEndsSequence()
    {
        var uci = PermissiveSan.TryResolve("6k1/5pp1/6P1/8/8/8/8/7R w - - 0 1", ["Rh8+", "*", "Kxh8"]);
        Assert.Equal(new[] { "h1h8" }, uci);
    }

    [Fact]
    public void BrokenFen_ReturnsNull()
    {
        // Nur 7 Reihen bzw. Müll im Brettfeld → LoadBoard scheitert → null (kein Crash).
        Assert.Null(PermissiveSan.TryResolve("8/8/8/8/8/8/8 w - - 0 1", ["e4"]));
        Assert.Null(PermissiveSan.TryResolve("8/8/8/8/8/8/8/XXXXXXXX w - - 0 1", ["e4"]));
    }
}
