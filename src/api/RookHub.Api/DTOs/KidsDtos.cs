namespace RookHub.Api.DTOs;

/// <summary>Eine Stufe der Kinder-Leiter (Uebersicht).</summary>
public class KidsLevelDto
{
    public int Level { get; set; }
    /// <summary>Thema der Stufe (<c>mate1</c>, <c>capture</c>, …) — die Kinderseite uebersetzt es.</summary>
    public string Theme { get; set; } = string.Empty;
    public int PuzzleCount { get; set; }
}

/// <summary>Eine Stufe mit ihren Aufgaben — am Stueck, damit die Seite ohne weitere Abfragen spielt.</summary>
public class KidsLevelDetailDto
{
    public int Level { get; set; }
    public string Theme { get; set; } = string.Empty;
    public List<KidsPuzzleDto> Puzzles { get; set; } = new();
}

/// <summary>Eine Aufgabe in Lichess-Form: <c>moves[0]</c> stellt die Aufgabe, danach ist das Kind am Zug.</summary>
public class KidsPuzzleDto
{
    public int Id { get; set; }
    public string Fen { get; set; } = string.Empty;
    public string Moves { get; set; } = string.Empty;
}

/// <summary>Ein fuer Kinder freigegebener Kurs (<see cref="Models.Book.ForKids"/>).</summary>
public class KidsCourseDto
{
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Aufgaben ohne reine Info-Linien — nur die zaehlen auf der Kinderseite.</summary>
    public int PuzzleCount { get; set; }
}

/// <summary>Ergebnis eines Neuaufbaus der Kinder-Leiter.</summary>
public class KidsRebuildResultDto
{
    public int Levels { get; set; }
    public int Puzzles { get; set; }
}

/// <summary>Sprach-Hinweis aus dem Land der Besucher-IP (<c>GET /api/kids/language-hint</c>).</summary>
public class KidsLanguageHintDto
{
    /// <summary>ISO-Land der IP, <c>null</c> bei LAN-/privater Adresse oder ohne Länderliste.</summary>
    public string? Country { get; set; }
    /// <summary>Passende Kindersprache (de/en/hr/hu; ein bekanntes Land ohne eigene: en) oder <c>null</c>
    /// bei unbekanntem Land — dann nimmt KidHub Deutsch.</summary>
    public string? Language { get; set; }
}

/// <summary>
/// Der Fortschritt eines Kindes auf KidHub (<c>GET/PUT /api/kids/progress</c>) — dieselbe Form im Konto
/// wie im Browser. Zeiten sind Millisekunden seit 1970 (UTC), so wie JavaScript sie zaehlt.
/// </summary>
public class KidsProgressDto
{
    public List<KidsLevelProgressDto> Levels { get; set; } = new();
    public List<KidsCourseProgressDto> Courses { get; set; } = new();
}

public class KidsLevelProgressDto
{
    public int Level { get; set; }
    /// <summary>Beste Sternzahl, 0–3.</summary>
    public int Stars { get; set; }
    public int RunIndex { get; set; }
    public int RunMistakes { get; set; }
    /// <summary>Letzte Aenderung des laufenden Durchgangs — der juengere gewinnt.</summary>
    public long RunAt { get; set; }
}

public class KidsCourseProgressDto
{
    public int BookId { get; set; }
    /// <summary>Zuletzt „von vorn" (0 = nie); Linien davor zaehlen nicht mehr.</summary>
    public long ResetAt { get; set; }
    public List<KidsSolvedLineDto> Solved { get; set; } = new();
}

public class KidsSolvedLineDto
{
    /// <summary>Die Linie (<c>BookPuzzle.Id</c>).</summary>
    public int Id { get; set; }
    /// <summary>Wann sie geloest wurde.</summary>
    public long At { get; set; }
}

/// <summary>Endlos-Modus: ein Rating-Fenster je gewuenschtem Puzzle (<c>POST /api/kids/endless/batch</c>).</summary>
public class KidsEndlessBatchRequest
{
    public List<KidsEndlessWindowDto> Windows { get; set; } = new();
    /// <summary>Schon gespielte Puzzles dieses Laufs — kommen nicht noch einmal.</summary>
    public List<int> Exclude { get; set; } = new();
}

public class KidsEndlessWindowDto
{
    public int MinRating { get; set; }
    public int MaxRating { get; set; }
}

/// <summary>Ein Puzzle des Endlos-Modus (Lichess-Form: <c>moves[0]</c> stellt die Aufgabe).</summary>
public class KidsEndlessPuzzleDto
{
    public int Id { get; set; }
    public string Fen { get; set; } = string.Empty;
    public string Moves { get; set; } = string.Empty;
    public int Rating { get; set; }
}
