namespace RookHub.Api.Models;

/// <summary>
/// Ein Lichess-Puzzle, das als „besonders einfach" fuer die Kinderseite markiert ist — mit seinem
/// Platz in der Stufen-Leiter (<see cref="Level"/>, <see cref="Position"/>).
///
/// <para>Die Auswahl rechnet <see cref="Services.KidsCurriculum"/> aus dem Standard-Puzzle-Bestand
/// (niedrigstes Rating, Loesung 1–2 eigene Zuege, wenig Figuren); gefuellt wird die Tabelle vom
/// <see cref="Services.KidsPuzzleSeeder"/> beim Start bzw. per Admin-Neuaufbau. Eine eigene Tabelle
/// statt einer Spalte an <see cref="Puzzle"/>: die hat ueber fuenf Millionen Zeilen, und die
/// Markierung betrifft ein paar Hundert.</para>
/// </summary>
public class KidsPuzzle
{
    /// <summary>PK und FK auf <see cref="Puzzle"/> (Cascade: ein geleerter Bestand leert die Leiter mit).</summary>
    public int PuzzleId { get; set; }
    public Puzzle? Puzzle { get; set; }

    /// <summary>Stufe, 1-basiert. Die Reihenfolge der Stufen ist die Reihenfolge des Lehrplans.</summary>
    public int Level { get; set; }

    /// <summary>Platz innerhalb der Stufe, 0-basiert — leichteste zuerst.</summary>
    public int Position { get; set; }

    /// <summary>Thema der Stufe (<see cref="Services.KidsCurriculum.Themes"/>), z. B. <c>mate1</c> oder <c>fork</c>.</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>Figuren auf dem Brett in der Ausgangsstellung (inkl. Koenige).</summary>
    public int PieceCount { get; set; }

    /// <summary>Anzahl der Zuege, die das Kind selbst finden muss (1 oder 2).</summary>
    public int SolverMoves { get; set; }

    /// <summary>Stand des Lehrplans, mit dem die Zeile entstand — weicht er vom Code ab, baut der
    /// Seeder die Leiter neu (<see cref="Services.KidsCurriculum.Version"/>).</summary>
    public int CurriculumVersion { get; set; }
}
