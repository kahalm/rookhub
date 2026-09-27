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
