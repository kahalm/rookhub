namespace RookHub.Api.DTOs;

// LeagueHub — Vereins-Datenbank (/api/league/club): Partien von Vereinsmitgliedern, PGN-Massenimport oder ein
// eingelesenes Partieformular. Regeln in Services/League/LeagueClubService.cs.

/// <summary><c>POST /api/league/club/games/import</c> — PGN-Text (eine oder viele Partien).</summary>
public class LeagueClubImportRequest
{
    public string Pgn { get; set; } = string.Empty;
    /// <summary>„Meinen Namen durch Schwaz ersetzen" — Vorgabe AN (Wunsch des Nutzers).</summary>
    public bool Anonymize { get; set; } = true;
}

/// <summary><c>POST /api/league/club/games</c> — EINE Partie (aus der Korrektur eines Partieformulars).</summary>
public class LeagueClubGameRequest
{
    /// <summary>Die Züge als SAN ab der Grundstellung.</summary>
    public List<string> Moves { get; set; } = new();
    public string? White { get; set; }
    public string? Black { get; set; }
    public int? WhiteElo { get; set; }
    public int? BlackElo { get; set; }
    public string? Result { get; set; }
    public string? Event { get; set; }
    /// <summary>Nur das Jahr wird gespeichert.</summary>
    public int? Year { get; set; }
    /// <summary>„white"/„black" — die Seite des Hochladenden; Pflicht, wenn anonymisiert wird.</summary>
    public string? OwnerSide { get; set; }
    public bool Anonymize { get; set; } = true;
    /// <summary>Die Liga-Einlesung, aus der die Partie stammt — wird nach dem Übernehmen geschlossen (Foto weg).</summary>
    public int? ScanId { get; set; }
}

public class LeagueClubFailureDto
{
    public int Index { get; set; }
    public string? White { get; set; }
    public string? Black { get; set; }
    /// <summary><c>noLeaguePlayer</c>, <c>ownerNotFound</c>, <c>fromPosition</c>, <c>illegal</c>, <c>noMoves</c>,
    /// <c>tooLong</c>.</summary>
    public string Reason { get; set; } = string.Empty;
}

public class LeagueClubImportResultDto
{
    public int Added { get; set; }
    public int Duplicates { get; set; }
    /// <summary>Davon mit „Schwaz" statt des eigenen Namens.</summary>
    public int Anonymized { get; set; }
    public bool Truncated { get; set; }
    public List<int> Ids { get; set; } = new();
    public List<LeagueClubFailureDto> Failed { get; set; } = new();
}

public class LeagueClubGameDto
{
    public int Id { get; set; }
    public int? Year { get; set; }
    public string White { get; set; } = string.Empty;
    public string Black { get; set; } = string.Empty;
    public string? WhiteFide { get; set; }
    public string? BlackFide { get; set; }
    public int? WhiteElo { get; set; }
    public int? BlackElo { get; set; }
    public string Result { get; set; } = "*";
    public string? Event { get; set; }
    public int Plies { get; set; }
    /// <summary>Die ersten Züge („1.e4 c5 2.Nf3 d6").</summary>
    public string Opening { get; set; } = string.Empty;
    public bool Anonymized { get; set; }
    /// <summary>Darf der Aufrufer sie löschen (Verwalter, oder eigene NICHT anonymisierte).</summary>
    public bool CanDelete { get; set; }
}

public class LeagueClubListDto
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<LeagueClubGameDto> Items { get; set; } = new();
}

/// <summary>Ein Spieler aus den Meldelisten (Vorschläge beim Eintippen der Namen).</summary>
public class LeagueRosterPersonDto
{
    public string Name { get; set; } = string.Empty;
    public string? Fide { get; set; }
    public List<string> Teams { get; set; } = new();
}

public class LeagueClubMatchRequest
{
    public string? White { get; set; }
    public string? Black { get; set; }
}

public class LeagueClubSideMatchDto
{
    /// <summary>Steht in einer Meldeliste der Liga.</summary>
    public bool League { get; set; }
    /// <summary>Mehrere Ligaspieler dieses Namens — dann ohne FIDE-ID.</summary>
    public bool Ambiguous { get; set; }
    public string? Name { get; set; }
    public string? Fide { get; set; }
}

public class LeagueClubMatchDto
{
    public LeagueClubSideMatchDto White { get; set; } = new();
    public LeagueClubSideMatchDto Black { get; set; } = new();
}
