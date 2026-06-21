namespace RookHub.Api.DTOs;

public class RecordBookAttemptDto
{
    public bool Solved { get; set; }
    public int TimeSeconds { get; set; }
    /// <summary>Höchste in diesem Versuch angesehene Tipp-Stufe (0–3).</summary>
    public int HintsUsed { get; set; }
}

public class RecordAnonymousBookAttemptDto : RecordBookAttemptDto
{
    /// <summary>Anonyme Geräte-/Sitzungs-ID (gleiche wie bei Standard-Puzzle/Endless).</summary>
    public string SessionId { get; set; } = string.Empty;
}

public class ClaimBookSessionDto
{
    public string SessionId { get; set; } = string.Empty;
}

public class BookSolverDto
{
    public string Name { get; set; } = string.Empty;
    public string? DiscordId { get; set; }
    public string? DiscordUsername { get; set; }
    public int TimeSeconds { get; set; }
}

public class BookPuzzleResultsDto
{
    /// <summary>Anzahl eingeloggter (namentlicher) Löser.</summary>
    public int SolvedCount { get; set; }
    /// <summary>Anzahl anonymer Löser (distinct Sessions), die gelöst haben.</summary>
    public int AnonymousSolvedCount { get; set; }
    public int AttemptCount { get; set; }
    public List<BookSolverDto> Solvers { get; set; } = new();
}

// --- Tagespuzzle-Leaderboards (Monats-Ladder + Hall of Fame) ------------------------
// Gemeinsame Wertungsregel: nur ERSTVERSUCH-Lösungen zählen (gaming-sicher, identisch zur
// Solver-Regel in GetResultsAsync). Punkte/Tag = 10 fürs Lösen + Tages-Rang-Bonus nach
// Erstversuch-Zeit (🥇 +5 / 🥈 +3 / 🥉 +1). Ein „Gold" = Tag als schnellster Erstversuch-Löser.

/// <summary>Ein Spieler in der Monats-Wertung des Tagespuzzles.</summary>
public class DailyLadderEntryDto
{
    public string Name { get; set; } = string.Empty;
    public string? DiscordId { get; set; }
    public string? DiscordUsername { get; set; }
    /// <summary>Gesamtpunkte im Zeitraum (10 je Erstversuch-Lösung + Rang-Bonus 5/3/1).</summary>
    public int Points { get; set; }
    /// <summary>Im Erstversuch gelöste Tagespuzzles im Zeitraum.</summary>
    public int Solved { get; set; }
    /// <summary>Tage als schnellster Erstversuch-Löser (🥇).</summary>
    public int Golds { get; set; }
}

/// <summary>Monats-Wertung des Tagespuzzles (absteigend nach Punkten).</summary>
public class DailyLadderDto
{
    /// <summary>Abgefragter Zeitraum als <c>yyyy-MM</c>.</summary>
    public string Period { get; set; } = string.Empty;
    public List<DailyLadderEntryDto> Entries { get; set; } = new();
}

/// <summary>Ein Eintrag in einer all-time Hall-of-Fame-Kategorie.</summary>
public class HallOfFameEntryDto
{
    public string Name { get; set; } = string.Empty;
    public string? DiscordId { get; set; }
    public string? DiscordUsername { get; set; }
    /// <summary>Wert der Kategorie (gelöste Dailies bzw. 🥇-Tage).</summary>
    public int Value { get; set; }
}

/// <summary>Schnellste je im Erstversuch gelöste Tagespuzzle-Lösung.</summary>
public class FastestSolveDto
{
    public string Name { get; set; } = string.Empty;
    public string? DiscordId { get; set; }
    public string? DiscordUsername { get; set; }
    public int TimeSeconds { get; set; }
    /// <summary>UTC-Datum, an dem dieses Puzzle das Tagespuzzle war (<c>yyyy-MM-dd</c>).</summary>
    public string Date { get; set; } = string.Empty;
}

/// <summary>All-time-Bestenlisten rund ums Tagespuzzle.</summary>
public class DailyHallOfFameDto
{
    public List<HallOfFameEntryDto> MostSolved { get; set; } = new();
    public List<HallOfFameEntryDto> MostGolds { get; set; } = new();
    public FastestSolveDto? Fastest { get; set; }
}

public class BookPuzzleDto
{
    public int Id { get; set; }
    public string LineId { get; set; } = string.Empty;
    public string BookFileName { get; set; } = string.Empty;
    public string Round { get; set; } = string.Empty;
    public string Fen { get; set; } = string.Empty;
    public string Moves { get; set; } = string.Empty;
    /// <summary>Halbzug-Index des Trainingsstarts; lösen ab moves[StartPly+1] (siehe BookPuzzle).</summary>
    public int StartPly { get; set; }
    public string? Title { get; set; }
    public string? Chapter { get; set; }
    public string? Comment { get; set; }
    /// <summary>
    /// Pro-Zug-Kommentare der Hauptlinie: Schlüssel = 0-basierter Halbzug-Index in <see cref="Moves"/>,
    /// NACH dessen Zug der Kommentar steht (<c>-1</c> = Einleitung vor dem ersten Zug). Null, wenn keine.
    /// Das Frontend zeigt sie beim Durchspielen/Review passend zum aktuellen Zug an.
    /// </summary>
    public Dictionary<int, string>? MoveComments { get; set; }
    public string? Difficulty { get; set; }
    public int? BookRating { get; set; }
    public string? Tags { get; set; }
    /// <summary>
    /// Vorberechnete, gestufte Lösungstipps, sprach-keyed (<c>{"de":[h1,h2,h3],"en":[…],"hr":[…]}</c>).
    /// Null/leer, wenn noch keine Tipps generiert wurden. Das Frontend wählt die aktive UI-Sprache
    /// (Fallback en→de) und deckt Stufe 1→3 progressiv auf.
    /// </summary>
    public Dictionary<string, List<string>>? Hints { get; set; }
}

public class BookPuzzleImportDto
{
    public string LineId { get; set; } = string.Empty;
    public string BookFileName { get; set; } = string.Empty;
    public string Round { get; set; } = string.Empty;
    public string Fen { get; set; } = string.Empty;
    public string Moves { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Chapter { get; set; }
    public string? Comment { get; set; }
    /// <summary>Optionale Pro-Zug-Kommentare (Schlüssel = Halbzug-Index, -1 = Einleitung).</summary>
    public Dictionary<int, string>? MoveComments { get; set; }
    public string? Difficulty { get; set; }
    public int? BookRating { get; set; }
    public string? Tags { get; set; }
}

public class BookInfoDto
{
    /// <summary>Stabile Buch-ID (für gezielte Abfragen, z. B. „Zufallspuzzle aus Buch X").</summary>
    public int? BookId { get; set; }
    public string BookFileName { get; set; } = string.Empty;
    public string? Difficulty { get; set; }
    public int? BookRating { get; set; }
    public string? Tags { get; set; }
    public int PuzzleCount { get; set; }
}
