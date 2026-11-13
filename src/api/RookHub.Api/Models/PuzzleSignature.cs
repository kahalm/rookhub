namespace RookHub.Api.Models;

/// <summary>Art einer <see cref="PuzzleSignature"/>.</summary>
public enum PuzzleSignatureKind : byte
{
    /// <summary>Mattbild der Schlussstellung (<see cref="Services.Puzzles.MatePattern"/>).</summary>
    Mate = 1,
    // Taktische Motive des ersten Lösungszugs (<see cref="Services.Puzzles.TacticMotifs"/>, 0.961.0) — je Thema eine Art, damit
    // eine Aufgabe mehrere tragen kann (Schlüssel PuzzleId + Kind).
    DoubleCheck = 2,
    Fork = 3,
    Skewer = 4,
    Pin = 5,
    DiscoveredAttack = 6,
    HangingPiece = 8,
    Promotion = 9,
}

/// <summary>
/// Einmal berechneter „Fingerabdruck“ einer Lichess-Aufgabe für die Empfohlenen Übungen der Partie-Zusammenfassung (0.960.0;
/// taktische Motive seit 0.961.0: streng = Thema + ziehende Figur + Zielfiguren, Familie = Thema).
/// Mattbild (<see cref="PuzzleSignatureKind.Mate"/>): die Schlussstellung relativ zum mattgesetzten König, spiegel- und
/// farbunabhängig, in zwei Stufen — <see cref="Strict"/> mit dem Typ der deckenden Figuren, <see cref="Family"/> ohne
/// („Bauer f4 oder Springer deckt die Dame“ = dieselbe Familie). Beide als 64-Bit-Hash der Signatur-Zeichenkette
/// (<see cref="Services.Puzzles.MatePattern.Hash"/>), damit der Index klein bleibt. Befüllt vom
/// <see cref="Services.Puzzles.PuzzleSignatureWorker"/>, nur für beliebte Aufgaben (<see cref="Services.Puzzles.PuzzleSignatureWorker.MinPopularity"/>).
/// </summary>
public class PuzzleSignature
{
    public int PuzzleId { get; set; }
    public Puzzle? Puzzle { get; set; }
    public PuzzleSignatureKind Kind { get; set; }
    /// <summary>Regelstand der Berechnung (<see cref="Services.Puzzles.MatePattern.Version"/>).</summary>
    public short Version { get; set; }
    public long Strict { get; set; }
    public long Family { get; set; }
    /// <summary>Aus dem Puzzle denormalisiert, damit „gleiche Signatur im Rating-Fenster“ ein reiner Index-Bereich ist.</summary>
    public int Rating { get; set; }
    /// <summary>Züge des Lösers bis zum Matt.</summary>
    public byte MateIn { get; set; }
}
