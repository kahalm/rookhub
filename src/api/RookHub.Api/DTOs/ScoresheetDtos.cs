using System.ComponentModel.DataAnnotations;
using RookHub.Api.Services;

namespace RookHub.Api.DTOs;

/// <summary>Kopfdaten einer vom Server angelegten oder korrigierten Partie.</summary>
public sealed record GameHeaderInput(string? Event, string? Site, string? Date, string? Round, string? White,
    string? Black, string? Result);

/// <summary>Ein Halbzug beim Korrigieren: SAN (englisch) + optionaler Kommentar dahinter.</summary>
public class GameMoveInputDto
{
    [MaxLength(20)]
    public string? San { get; set; }

    [MaxLength(2000)]
    public string? Comment { get; set; }
}

/// <summary><c>PUT /api/games/{id}</c> — die korrigierte Partie.</summary>
public class GameUpdateDto
{
    public List<GameMoveInputDto> Moves { get; set; } = new();

    [MaxLength(120)] public string? White { get; set; }
    [MaxLength(120)] public string? Black { get; set; }
    [MaxLength(12)] public string? Result { get; set; }
    [MaxLength(200)] public string? Event { get; set; }
    [MaxLength(200)] public string? Site { get; set; }
    [MaxLength(40)] public string? Round { get; set; }
    /// <summary><c>yyyy-MM-dd</c> (oder leer).</summary>
    [MaxLength(20)] public string? Date { get; set; }

    /// <summary>Meine Seite: <c>white</c>/<c>black</c>; leer = Festlegung zurücknehmen; <c>null</c> = unverändert.</summary>
    [MaxLength(8)] public string? OwnerSide { get; set; }

    /// <summary>Nur bei eingelesenen Partien: der Stand der Korrekturseite je Halbzug (Formular-Eintrag,
    /// bestätigt, unsicher, Lesarten) — damit die Seite beim nächsten Öffnen dort weitermacht. Für den Server
    /// Anzeige-Zustand; die Züge selbst kommen aus <see cref="Moves"/>.</summary>
    public List<ScoresheetPly>? ScoresheetPlies { get; set; }
}

/// <summary>Wie weit ist eine Formular-Einlesung?</summary>
public class ScoresheetScanDto
{
    public int Id { get; set; }
    /// <summary><c>pending</c>/<c>running</c>/<c>done</c>/<c>failed</c>.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Grund bei <c>failed</c>: <c>unreadable</c>, <c>noMoves</c>, <c>refused</c>, <c>notConfigured</c>, <c>failed</c>.</summary>
    public string? Error { get; set; }
    public int? SavedGameId { get; set; }
    public string NotationLanguage { get; set; } = "auto";
    public string OwnerSide { get; set; } = "auto";
    public string? FileName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    /// <summary>Lese-Durchgänge beim Modell (1 = auf Anhieb).</summary>
    public int Rounds { get; set; }
    public int MoveCount { get; set; }
    /// <summary>Halbzüge, die sich der Server nicht sicher ist (Korrekturseite markiert sie).</summary>
    public int UncertainCount { get; set; }
    /// <summary>Formular-Einträge am Ende, die sich keinem legalen Zug zuordnen ließen.</summary>
    public int UnresolvedCount { get; set; }
    /// <summary>Spielernamen, sobald gelesen (für die Liste der letzten Einlesungen).</summary>
    public string? White { get; set; }
    public string? Black { get; set; }
}

/// <summary><c>GET /api/scoresheets/status</c> — kann man gerade einlesen?</summary>
public class ScoresheetStatusDto
{
    public bool Available { get; set; }
    public int DailyLimit { get; set; }
    public int UsedToday { get; set; }
    /// <summary>Wie viel vom Kostenbudget verbraucht ist (das knappere von Tag und 30 Tagen), 0–100.</summary>
    public int BudgetUsedPercent { get; set; }
    /// <summary>Warum gerade nichts geht (<c>userDailyBudget</c>, <c>userMonthlyBudget</c>, <c>globalBudget</c>);
    /// <c>null</c> = es geht.</summary>
    public string? Blocked { get; set; }
    /// <summary>Admin: keine Nutzerbudgets (das Gesamtbudget gilt trotzdem).</summary>
    public bool Unlimited { get; set; }
    public List<ScoresheetLanguageDto> Languages { get; set; } = new();
}

public class ScoresheetLanguageDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Figurenbuchstaben K D T L S in dieser Sprache (Anzeige in der Auswahl).</summary>
    public string Pieces { get; set; } = string.Empty;
}

/// <summary><c>GET /api/games/{id}/scoresheet</c> — alles, was die Korrekturseite zum Formular braucht.</summary>
public class ScoresheetEditStateDto
{
    public int ScanId { get; set; }
    public string NotationLanguage { get; set; } = "auto";
    /// <summary>Alle Formular-Einträge in Reihenfolge (was dasteht).</summary>
    public List<string> Written { get; set; } = new();
    /// <summary>Stand je Halbzug der gespeicherten Partie.</summary>
    public List<ScoresheetPly> Plies { get; set; } = new();
    /// <summary>Einträge am Ende ohne legalen Zug.</summary>
    public List<string> Unresolved { get; set; } = new();
    /// <summary>Ab welchem Formular-Eintrag die unaufgelösten beginnen (<c>null</c> = keine).</summary>
    public int? UnresolvedFrom { get; set; }
}

/// <summary><c>POST /api/games/{id}/scoresheet/resolve</c> — den Rest ab einer festgelegten Stelle neu aufbereiten.</summary>
public class ScoresheetResolveRequestDto
{
    /// <summary>Die festgelegten Halbzüge (SAN) ab Partiebeginn.</summary>
    public List<string> Prefix { get; set; } = new();

    /// <summary>Welcher Formular-Eintrag gehört zum nächsten Halbzug? Nach einem Ersetzen der nächste,
    /// nach einem Einfügen derselbe, nach einem Löschen der übernächste.</summary>
    public int WrittenFrom { get; set; }
}

public class ScoresheetResolveResultDto
{
    public List<ScoresheetPly> Plies { get; set; } = new();
    public List<string> Unresolved { get; set; } = new();
    public int? UnresolvedFrom { get; set; }
}
