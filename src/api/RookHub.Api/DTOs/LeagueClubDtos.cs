namespace RookHub.Api.DTOs;

// LeagueHub — Vereins-Datenbank (/api/league/club): Partien von Vereinsmitgliedern, PGN-Massenimport oder ein
// eingelesenes Partieformular. Regeln in Services/League/LeagueClubService.cs.

/// <summary><c>POST /api/league/club/games/preview</c> — PGN-Text lesen und zuordnen, NICHTS speichern.</summary>
public class LeagueClubPreviewRequest
{
    public string Pgn { get; set; } = string.Empty;
}

/// <summary>Wer an einer Seite sitzt — so, wie der Nutzer es in der Übersicht festgelegt hat.</summary>
public record LeagueClubSideDecision
{
    /// <summary>Name (frei getippt oder aus der Meldeliste); leer = der Name aus dem PGN.</summary>
    public string? Name { get; set; }
    /// <summary>FIDE-ID eines ausgewählten Ligaspielers — schlägt den Namen.</summary>
    public string? Fide { get; set; }
    /// <summary>Durch „Schwaz" ersetzen (Spieler des eigenen Vereins, Vorgabe laut Übersicht).</summary>
    public bool Replace { get; set; }
}

public class LeagueClubImportGameDecision
{
    /// <summary>Nummer der Partie im PGN (1-basiert, wie in der Übersicht).</summary>
    public int Index { get; set; }
    public LeagueClubSideDecision White { get; set; } = new();
    public LeagueClubSideDecision Black { get; set; } = new();
}

/// <summary><c>POST /api/league/club/games/import</c> — derselbe PGN-Text wie bei der Übersicht und je Partie, die
/// übernommen werden soll, die Entscheidung. <see cref="Games"/> fehlt = alle übernehmbaren mit den Vorgaben der
/// Übersicht — auch die, die sie nicht vorwählt (Gegner nur in der Megabase).</summary>
public class LeagueClubImportRequest
{
    public string Pgn { get; set; } = string.Empty;
    public List<LeagueClubImportGameDecision>? Games { get; set; }
}

/// <summary><c>POST /api/league/club/games</c> — EINE Partie (aus der Korrektur eines Partieformulars).</summary>
public class LeagueClubGameRequest
{
    /// <summary>Die Züge als SAN ab der Grundstellung.</summary>
    public List<string> Moves { get; set; } = new();
    public string? White { get; set; }
    public string? Black { get; set; }
    public string? WhiteFide { get; set; }
    public string? BlackFide { get; set; }
    public int? WhiteElo { get; set; }
    public int? BlackElo { get; set; }
    /// <summary>Durch „Schwaz" ersetzen.</summary>
    public bool WhiteReplace { get; set; }
    public bool BlackReplace { get; set; }
    public string? Result { get; set; }
    public string? Event { get; set; }
    /// <summary>Nur das Jahr wird gespeichert.</summary>
    public int? Year { get; set; }
    /// <summary>Die Liga-Einlesung, aus der die Partie stammt — wird nach dem Übernehmen geschlossen (Foto weg).</summary>
    public int? ScanId { get; set; }
}

public class LeagueClubPreviewSideDto
{
    /// <summary>Der Name, wie er im PGN steht.</summary>
    public string? Raw { get; set; }
    public int? Elo { get; set; }
    public LeagueClubSideMatchDto Match { get; set; } = new();
    /// <summary>Laut Profil die Seite des Hochladenden.</summary>
    public bool Owner { get; set; }
    /// <summary>Vorgabe „durch Schwaz ersetzen" (Spieler von Schwaz oder der Hochladende).</summary>
    public bool Replace { get; set; }
}

public class LeagueClubPreviewGameDto
{
    public int Index { get; set; }
    public int? Year { get; set; }
    public string Result { get; set; } = "*";
    public string? Event { get; set; }
    public int Plies { get; set; }
    public string Opening { get; set; } = string.Empty;
    /// <summary>Nicht übernehmbar, egal wie man die Namen setzt: <c>illegal</c>, <c>noMoves</c>, <c>tooLong</c>,
    /// <c>fromPosition</c>.</summary>
    public string? Error { get; set; }
    /// <summary>Schon in der Vereins-Datenbank (oder weiter oben in derselben Datei).</summary>
    public bool Duplicate { get; set; }
    /// <summary>Die Partie als eigener PGN-Text (0.590.0, fehlt bei harten Fehlern) — die Seite importiert damit
    /// portionsweise (<c>games/import</c> mit diesem Text und Nummer 1…n je Portion), jede Portion sofort gespeichert.</summary>
    public string? Pgn { get; set; }
    public LeagueClubPreviewSideDto White { get; set; } = new();
    public LeagueClubPreviewSideDto Black { get; set; } = new();
}

public class LeagueClubPreviewDto
{
    public List<LeagueClubPreviewGameDto> Games { get; set; } = new();
    public bool Truncated { get; set; }
}

public class LeagueClubFailureDto
{
    public int Index { get; set; }
    public string? White { get; set; }
    public string? Black { get; set; }
    /// <summary><c>noLeaguePlayer</c> (keine Seite in der Liga oder der Megabase), <c>onlyOwnClub</c>, <c>fromPosition</c>, <c>illegal</c>, <c>noMoves</c>,
    /// <c>tooLong</c>, <c>notFound</c> (Nummer nicht im PGN).</summary>
    public string Reason { get; set; } = string.Empty;
}

public class LeagueClubImportResultDto
{
    public int Added { get; set; }
    public int Duplicates { get; set; }
    /// <summary>Davon mit „Schwaz" statt eines Namens.</summary>
    public int Anonymized { get; set; }
    /// <summary>Neu gemerkte (oder geänderte) Namens-Zuordnungen aus den Korrekturen — nur mit Konto.</summary>
    public int Remembered { get; set; }
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
    /// <summary>Die Züge als UCI mit Leerzeichen — für „Analyse" (RookHubs Analysebrett, <c>?moves=</c>).</summary>
    public string Uci { get; set; } = string.Empty;
    /// <summary>Die Partie als PGN, wie gespeichert (Hauptvariante, Kopfdaten; „Schwaz" statt des Namens) — „Analyse"
    /// gibt sie ans Analysebrett mit (<c>?pgn=</c>, 0.592.0), dort steht sie dann im PGN-Feld.</summary>
    public string Pgn { get; set; } = string.Empty;
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
    /// <summary>Spielt (jüngste Saison) für Schwaz.</summary>
    public bool Club { get; set; }
    /// <summary>Steht in einer Meldeliste der Liga (bei Treffern aus der Megabase nur, wenn die FIDE-ID passt).</summary>
    public bool League { get; set; } = true;
    /// <summary><c>liga</c> oder <c>mega</c> (Spielerverzeichnis der Megabase).</summary>
    public string Source { get; set; } = "liga";
    public int? Games { get; set; }
    public int? LastYear { get; set; }
    public int? MaxElo { get; set; }
}

/// <summary><c>POST …/games/lichess</c> — eine öffentliche Lichess-Studie (Adresse der Studie oder eines Kapitels).</summary>
public class LeagueClubLichessRequest
{
    public string Url { get; set; } = string.Empty;
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
    /// <summary>Spieler von Schwaz (bei Mehrdeutigkeit: alle Kandidaten).</summary>
    public bool Club { get; set; }
    /// <summary>Bei Mehrdeutigkeit die Kandidaten zur Auswahl.</summary>
    public List<LeagueRosterPersonDto> Candidates { get; set; } = new();
    /// <summary>Nur über den Nachnamen gefunden (die Partie nennt keinen Vornamen) — prüfen.</summary>
    public bool LastNameOnly { get; set; }
    /// <summary>Kein Ligaspieler, aber eindeutig im Spielerverzeichnis der Megabase — dann <see cref="Name"/> und
    /// <see cref="Fide"/> von dort. Die Partie ist übernehmbar, die Übersicht wählt sie nicht vor („nicht in Liga").</summary>
    public bool Mega { get; set; }
    /// <summary>Über eine gemerkte Zuordnung (jemand hat diesen Namen schon einmal so korrigiert).</summary>
    public bool Alias { get; set; }
}

public class LeagueClubMatchDto
{
    public LeagueClubSideMatchDto White { get; set; } = new();
    public LeagueClubSideMatchDto Black { get; set; } = new();
}

/// <summary><c>GET /api/league/club/admin/scans</c> — eine offene Liga-Einlesung für die Verwalter.</summary>
public class LeagueOpenScanDto
{
    public ScoresheetScanDto Scan { get; set; } = new();
    /// <summary>Ohne Konto über einen Teilen-Link hochgeladen.</summary>
    public bool ViaShareLink { get; set; }
    /// <summary>Die eigene (steht ohnehin in „Deine Formulare").</summary>
    public bool Mine { get; set; }
}

/// <summary><c>PUT /api/league/club/games/{id}</c> — Namen und Ergebnis einer gespeicherten Partie korrigieren. Eine Seite
/// ohne Angabe bleibt, wie sie ist; „Schwaz" lässt sich nicht ändern.</summary>
public class LeagueClubGameUpdateRequest
{
    public LeagueClubSideDecision? White { get; set; }
    public LeagueClubSideDecision? Black { get; set; }
    /// <summary><c>1-0</c>, <c>0-1</c>, <c>1/2-1/2</c>, <c>*</c>; fehlt = unverändert.</summary>
    public string? Result { get; set; }
}
