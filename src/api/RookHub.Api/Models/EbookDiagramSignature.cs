namespace RookHub.Api.Models;

/// <summary>
/// „Fingerabdruck“ eines Buchdiagramms für die Empfohlenen Übungen (0.962.0, Wunsch 2026-11-13: „wenn du in büchern das motiv
/// findest dann sind beispiele aus dem kapitel &gt; allgemeine probleme“): dieselben Signaturen wie <see cref="PuzzleSignature"/>
/// (Mattbild der Schlussstellung bzw. Motiv des ersten Lösungszugs, je streng und Familie), gerechnet aus Diagrammstellung und
/// Lösung. Befüllt vom <see cref="Services.Puzzles.PuzzleSignatureWorker"/> (<see cref="Services.Puzzles.BookExercises"/>).
///
/// <para><see cref="Solvable"/>: Buchlösung, von Stockfish bestätigt (<see cref="EbookSolutionCheck.Confirmed"/> oder
/// <see cref="EbookSolutionCheck.Replaced"/> mit <c>SolutionSource = book</c>) — sonst (Engine-Lösung) nur „Beispiel im Buch“.
/// <see cref="LineHash"/> erkennt veraltete Zeilen (Stellung oder Lösung später korrigiert): die Auswahl nimmt nur Zeilen, deren
/// Hash zum aktuellen Diagramm passt.</para>
/// </summary>
public class EbookDiagramSignature
{
    public int DiagramId { get; set; }
    public EbookDiagram? Diagram { get; set; }
    public PuzzleSignatureKind Kind { get; set; }
    /// <summary>Regelstand (<see cref="Services.Puzzles.MatePattern.Version"/> bzw. <see cref="Services.Puzzles.TacticMotifs.Version"/>).</summary>
    public short Version { get; set; }
    public long Strict { get; set; }
    public long Family { get; set; }
    /// <summary>Aus dem Diagramm denormalisiert: Rechte (Bücher des Betrachters) als reiner Filter, ohne Join.</summary>
    public int EbookId { get; set; }
    public bool Solvable { get; set; }
    /// <summary><see cref="Services.Puzzles.MatePattern.Hash"/> von „FEN|Lösung“ zum Zeitpunkt der Berechnung.</summary>
    public long LineHash { get; set; }
    /// <summary>Fester Mischwert (Hash der Diagramm-Id): die Auswahl liest ab einem je Partie gesäten Wert, so kommen nicht immer
    /// dieselben (ältesten) Bücher zuerst.</summary>
    public int Shuffle { get; set; }
    /// <summary>Züge des Lösers bis zum Matt (0 bei Motiven).</summary>
    public byte MateIn { get; set; }
    /// <summary>Kapitel aus dem Inhaltsverzeichnis des Buchs (<see cref="Services.Ebooks.EbookChapterLookup"/>), <c>null</c> = keins.</summary>
    public string? Chapter { get; set; }
}
