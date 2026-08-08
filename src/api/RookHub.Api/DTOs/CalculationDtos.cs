namespace RookHub.Api.DTOs;

/// <summary>
/// Eine Stellung eines Kalkulationsbuchs in der Übersichtsliste — bewusst LEICHT (ohne FEN,
/// Kommentar und vor allem ohne <c>Moves</c>): im Kalkulations-Modus gibt es keine Lösung, also
/// verlässt die gespeicherte Zugfolge den Server gar nicht erst.
/// </summary>
public class CalcPositionListItemDto
{
    public int Id { get; set; }
    public string Round { get; set; } = string.Empty;
    public string? Title { get; set; }
    /// <summary>Kapitel der Linie (<c>null</c> = ohne Kapitel) — fürs Gruppieren in der Sprungliste.</summary>
    public string? Chapter { get; set; }
    /// <summary>Der Nutzer hat zu dieser Stellung schon einen Analysebaum gespeichert (= nicht-leeres
    /// <c>TreeJson</c>). Eine Zeile, die nur Zeit/Festlegung/Bewertung trägt, zählt hier NICHT.</summary>
    public bool HasTree { get; set; }

    /// <summary>Zug, auf den sich der Nutzer festgelegt hat (SAN), <c>null</c> = keine Festlegung.</summary>
    public string? ChosenSan { get; set; }
    /// <summary>Derselbe Zug in UCI (zum Wiederfinden im Baum).</summary>
    public string? ChosenUci { get; set; }
    /// <summary>Aufsummierte aktive Rechenzeit an dieser Stellung (Sekunden).</summary>
    public int SecondsSpent { get; set; }
    /// <summary>Selbstbewertung als Stufe (<see cref="RookHub.Api.Models.CalculationGrade"/>, 0–4);
    /// <c>null</c> = noch nicht bewertet (≠ Stufe 0 „nicht gelöst").</summary>
    public int? Grade { get; set; }
    /// <summary>Aus <see cref="Grade"/> abgeleitete Punkte (<c>null</c>, solange unbewertet) —
    /// mitgeliefert, damit der Client die Gewichtung nicht nachbauen muss.</summary>
    public int? Points { get; set; }
}

/// <summary>
/// Serverseitig gerechnete Summen EINES Kapitels eines Kalkulationsbuchs. Bewusst hier und nicht
/// im Frontend: die Stellungsliste kann gefiltert/gekürzt dargestellt werden, die Summen sollen
/// aber immer das ganze Kapitel meinen.
/// </summary>
public class CalcChapterSummaryDto
{
    /// <summary>Kapitelname; <c>null</c> = Stellungen ohne Kapitel.</summary>
    public string? Chapter { get; set; }
    /// <summary>Stellungen im Kapitel (alle Linien).</summary>
    public int PositionCount { get; set; }
    /// <summary>Stellungen mit eigenem Analysebaum.</summary>
    public int TreeCount { get; set; }
    /// <summary>Stellungen mit Festlegung (<c>ChosenSan</c> gesetzt).</summary>
    public int ChosenCount { get; set; }
    /// <summary>Stellungen mit vergebener Selbstbewertung (<c>Grade != null</c>).</summary>
    public int RatedCount { get; set; }
    /// <summary>Summe der abgeleiteten Punkte (unbewertete Stellungen zählen nicht mit).</summary>
    public int Points { get; set; }
    /// <summary>Erreichbare Punkte des Kapitels = <see cref="PositionCount"/> × Punkte der besten
    /// Stufe. IMMER mitgeliefert: eine nackte Summe ist ohne die Zahl der Stellungen nicht lesbar
    /// („14 / 24" statt „14"). Zähler sind ALLE Stellungen, auch die unbewerteten.</summary>
    public int MaxPoints { get; set; }
    /// <summary>Summe der Rechenzeit in Sekunden.</summary>
    public int SecondsSum { get; set; }
}

/// <summary>Kopf + Stellungsliste eines Buchs für den Kalkulations-Modus.</summary>
public class CalcBookDto
{
    public int BookId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Ist das Buch als Kalkulationsbuch markiert (<c>Book.IsCalculation</c>)? Der Modus
    /// funktioniert auch ohne das Flag (Direkt-Link), die Kursübersicht bietet ihn aber nur dann an.</summary>
    public bool IsCalculation { get; set; }
    public List<CalcPositionListItemDto> Positions { get; set; } = new();
    /// <summary>Kapitelsummen in der Reihenfolge des ersten Auftretens in <see cref="Positions"/>.</summary>
    public List<CalcChapterSummaryDto> Chapters { get; set; } = new();
    /// <summary>Summe der abgeleiteten Punkte über das ganze Buch (= Summe über <see cref="Chapters"/>).</summary>
    public int Points { get; set; }
    /// <summary>Erreichbare Punkte des ganzen Buchs (alle Stellungen × beste Stufe).</summary>
    public int MaxPoints { get; set; }
    /// <summary>Summe der Rechenzeit über das ganze Buch (Sekunden).</summary>
    public int SecondsSum { get; set; }
}

/// <summary>
/// Eine einzelne Stellung inkl. eigenem Analysebaum. Enthält NIE die Lösungszüge: geliefert wird
/// nur die Ausgangs-FEN und — bei Buchlinien mit Trainingsstart mitten in der Partie — die Züge
/// BIS zum Trainingsstart (<see cref="SetupMoves"/>), damit das Frontend die Ausgangsstellung
/// nachstellen kann. Alles danach (die eigentliche Lösung) bleibt auf dem Server.
/// </summary>
public class CalcPositionDto
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string Round { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Chapter { get; set; }
    /// <summary>FEN aus dem PGN-Header (Ausgangspunkt, ggf. noch vor den <see cref="SetupMoves"/>).</summary>
    public string Fen { get; set; } = string.Empty;
    /// <summary>Züge (UCI, leerzeichengetrennt) von <see cref="Fen"/> bis zur Aufgabenstellung; leer
    /// bei reinen Stellungs-Linien (Kalkulationsbuch) und bei Puzzles mit <c>StartPly &lt; 0</c>.</summary>
    public string SetupMoves { get; set; } = string.Empty;
    /// <summary>Optionaler Erklär-/Aufgabentext zur Stellung (<c>BookPuzzle.Comment</c>).</summary>
    public string? Comment { get; set; }
    /// <summary>Eigener Analysebaum als JSON; <c>null</c> = noch keiner gespeichert.</summary>
    public string? TreeJson { get; set; }
    public DateTime? TreeUpdatedAt { get; set; }

    /// <summary>Zug, auf den sich der Nutzer festgelegt hat (SAN); <c>null</c> = keine Festlegung.</summary>
    public string? ChosenSan { get; set; }
    /// <summary>Derselbe Zug in UCI.</summary>
    public string? ChosenUci { get; set; }
    /// <summary>Aufsummierte aktive Rechenzeit an dieser Stellung (Sekunden).</summary>
    public int SecondsSpent { get; set; }
    /// <summary>Selbstbewertung als Stufe (<see cref="RookHub.Api.Models.CalculationGrade"/>, 0–4);
    /// <c>null</c> = noch nicht bewertet (≠ Stufe 0 „nicht gelöst").</summary>
    public int? Grade { get; set; }
    /// <summary>Aus <see cref="Grade"/> abgeleitete Punkte (<c>null</c>, solange unbewertet).</summary>
    public int? Points { get; set; }
}

/// <summary>
/// Die drei Trainings-Werte einer Stellung als EINGABE. Alle Felder sind optional — weggelassen
/// heißt „unverändert". Zwei Werte brauchen einen expliziten Schalter, weil <c>null</c> im JSON
/// nicht zwischen „nicht mitgeschickt" und „löschen" unterscheiden kann
/// (<see cref="ClearGrade"/>, <see cref="ClearChoice"/>).
/// </summary>
public interface ICalcMetaInput
{
    /// <summary>Zeit-DELTA seit dem letzten Speichern in Sekunden — wird AUFADDIERT, nie gesetzt
    /// (ein zweiter Besuch darf die Zeit des ersten nicht überschreiben). Je Übertragung auf
    /// <c>CalculationService.MaxSecondsPerFlush</c> gedeckelt, Negatives zählt als 0.
    /// <para>Gehört zusammen mit <see cref="SecondsToken"/>: ohne Marke ist ein addierendes Delta
    /// nicht wiederholungsfest.</para></summary>
    int? AddSeconds { get; }
    /// <summary>
    /// IDEMPOTENZ-MARKE für <see cref="AddSeconds"/> — vom Client vergeben, für den Server opak,
    /// eindeutig je gemessenem Delta. Wiederholt der Client denselben Patch (weil nur die ANTWORT
    /// verloren ging), MUSS er dieselbe Marke mitschicken: der Server erkennt sie wieder und
    /// verbucht die Zeit nicht ein zweites Mal. Wächst der wiederholte Patch um neu gemessene Zeit,
    /// bleibt die Marke ebenfalls gleich — angerechnet wird dann nur die Differenz.
    /// <para>Ohne Marke wird das Delta wie bisher bedingungslos addiert (Alt-Clients).</para>
    /// </summary>
    string? SecondsToken { get; }
    /// <summary>Selbstbewertung als Stufe 0–4 (<see cref="RookHub.Api.Models.CalculationGrade"/>).
    /// Weggelassen = unverändert; eine Stufe AUSSERHALB 0–4 ist ein Client-Fehler (400) und wird
    /// NICHT still auf 0 gesetzt — eine unbekannte Stufe ist nicht „nicht gelöst".</summary>
    int? Grade { get; }
    /// <summary><c>true</c> = Bewertung zurücknehmen (<c>Grade</c> → <c>null</c>, „noch nicht
    /// bewertet"). Schlägt <see cref="Grade"/>.</summary>
    bool ClearGrade { get; }
    /// <summary>Erster Zug, auf den sich der Nutzer festlegt (SAN). Gesetzt = TOGGLE: ein anderer
    /// Zug verschiebt die Festlegung, DERSELBE Zug nimmt sie zurück.</summary>
    string? ChosenSan { get; }
    /// <summary>Derselbe Zug in UCI; wird zusammen mit <see cref="ChosenSan"/> gesetzt/gelöscht.</summary>
    string? ChosenUci { get; }
    /// <summary><c>true</c> = Festlegung löschen, ohne den aktuellen Zug kennen zu müssen.
    /// Schlägt <see cref="ChosenSan"/>.</summary>
    bool ClearChoice { get; }
}

/// <summary>Eingabe: den eigenen Analysebaum zu einer Stellung speichern (Upsert). Die drei
/// Trainings-Werte dürfen im selben Aufruf mitkommen (spart beim Verlassen der Stellung eine
/// zweite Runde), sind aber auch ohne Baum über den PATCH-Endpoint setzbar.</summary>
public class SaveCalcTreeDto : ICalcMetaInput
{
    /// <summary>Serialisierter Baum. Muss gültiges JSON sein und darf
    /// <c>CalculationService.MaxTreeJsonLength</c> nicht überschreiten.</summary>
    public string TreeJson { get; set; } = string.Empty;

    public int? AddSeconds { get; set; }
    public string? SecondsToken { get; set; }
    public int? Grade { get; set; }
    public bool ClearGrade { get; set; }
    public string? ChosenSan { get; set; }
    public string? ChosenUci { get; set; }
    public bool ClearChoice { get; set; }
}

/// <summary>Eingabe: nur die drei Trainings-Werte einer Stellung ändern, OHNE den Baum erneut zu
/// schicken (festlegen, Zeit nachtragen, bewerten).</summary>
public class PatchCalcMetaDto : ICalcMetaInput
{
    public int? AddSeconds { get; set; }
    public string? SecondsToken { get; set; }
    public int? Grade { get; set; }
    public bool ClearGrade { get; set; }
    public string? ChosenSan { get; set; }
    public string? ChosenUci { get; set; }
    public bool ClearChoice { get; set; }
}

/// <summary>Antwort nach dem Speichern eines Baums bzw. dem Ändern der Trainings-Werte: der
/// Zustand der Stellung, wie er jetzt gespeichert ist (der Client muss nichts nachladen).</summary>
public class CalcPositionStateDto
{
    public int BookPuzzleId { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Es liegt ein nicht-leerer Analysebaum vor.</summary>
    public bool HasTree { get; set; }
    public string? ChosenSan { get; set; }
    public string? ChosenUci { get; set; }
    /// <summary>Gesamte (aufsummierte) Rechenzeit nach dieser Übertragung.</summary>
    public int SecondsSpent { get; set; }
    /// <summary>Gespeicherte Stufe (0–4) bzw. <c>null</c> = unbewertet.</summary>
    public int? Grade { get; set; }
    /// <summary>Die daraus abgeleiteten Punkte (<c>null</c>, solange unbewertet).</summary>
    public int? Points { get; set; }
}

/// <summary>
/// Eine Stellung eines ÖFFENTLICHEN Buchs für den anonymen Kalkulations-Modus. Bewusst ein EIGENES
/// DTO neben <see cref="CalcPositionDto"/>: hier gibt es keinen Nutzer, also auch keine
/// Trainings-Werte (Baum, Zeit, Stufe, Festlegung) — die liegen beim anonymen Besucher im Browser.
/// Was NICHT drinsteht, kann auch nicht versehentlich fremde Werte tragen.
/// <para>Wie überall im Kalkulations-Modus enthält das DTO <b>keine Lösung</b>: <c>BookPuzzle.Moves</c>
/// hat hier keine Entsprechung, höchstens der Vorlauf bis zum Trainingsstart
/// (<see cref="SetupMoves"/> = Halbzüge <c>0..StartPly</c>).</para>
/// </summary>
public class CalcPublicPositionDto
{
    public int Id { get; set; }
    public string Round { get; set; } = string.Empty;
    public string? Title { get; set; }
    /// <summary>Kapitel der Linie (<c>null</c> = ohne Kapitel) — Filter für <c>/{slug}/{kapitel}</c>.</summary>
    public string? Chapter { get; set; }
    /// <summary>FEN aus dem PGN-Header (Ausgangspunkt, ggf. noch vor den <see cref="SetupMoves"/>).</summary>
    public string Fen { get; set; } = string.Empty;
    /// <summary>Züge (UCI) von <see cref="Fen"/> BIS zur Aufgabenstellung — nie darüber hinaus
    /// (<c>CalculationService.SetupMoves</c>). Leer bei reinen Stellungs-Linien.</summary>
    public string SetupMoves { get; set; } = string.Empty;
    /// <summary>Optionaler Erklär-/Aufgabentext zur Stellung.</summary>
    public string? Comment { get; set; }
}

/// <summary>
/// Kopf + VOLLSTÄNDIGE Stellungen eines öffentlich freigegebenen Buchs für den anonymen
/// Kalkulations-Modus — die einzige Lese-Öffnung des Modus. Anders als der eingeloggte Pfad
/// (leichte Liste + Einzelabruf je Stellung) kommt hier alles in EINEM Abruf, weil es für einen
/// anonymen Besucher keinen zweiten, nutzerbezogenen Endpoint gibt und seine Arbeit ohnehin
/// vollständig lokal im Browser liegt.
/// </summary>
public class CalcPublicBookDto
{
    public int BookId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Ist das Buch als Kalkulationsbuch markiert (<c>Book.IsCalculation</c>)?</summary>
    public bool IsCalculation { get; set; }
    /// <summary>Stellungen in Lesereihenfolge (Round → Id).</summary>
    public List<CalcPublicPositionDto> Positions { get; set; } = new();
}
