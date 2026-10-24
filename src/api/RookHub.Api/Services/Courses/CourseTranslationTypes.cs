namespace RookHub.Api.Services.Courses;

// Ergebnis- und Zwischentypen der Kurs-Uebersetzung (CourseTranslationService), aus dessen Datei herausgezogen
// (Zerlegung Z16).

/// <summary>Ein uebersetzbarer Text einer Kurs-Linie: Stelle (<see cref="CourseTextSlots"/>), der
/// normalisierte Text und sein Fingerabdruck (<see cref="CourseTextHash"/>).</summary>
public sealed record CourseLineSlot(int Slot, string Text, string Hash);

/// <summary>Was bei EINER Linie passiert ist.</summary>
public enum LineTranslationStatus
{
    /// <summary>Alles schon da und aktuell — kein Modellaufruf, nichts geschrieben.</summary>
    NothingToDo,
    /// <summary>Etwas geschrieben (uebersetzt, kopiert oder veraltete Reste entfernt).</summary>
    Written,
    /// <summary>Modell abgebrochen/verworfen oder Schreibkonflikt — fuer diese Linie wurde NICHTS geschrieben.</summary>
    Failed,
    NotFound,
    /// <summary>Es waere ein Modellaufruf noetig, aber kein Text-Modell konfiguriert.</summary>
    NotConfigured,
    /// <summary>Zielsprache = Quellsprache des Kurses.</summary>
    SameLanguage,
}

/// <param name="Kept">Stellen, deren Uebersetzung aktuell war.</param>
/// <param name="Reused">Stellen, deren Uebersetzung per Fingerabdruck kopiert wurde (anderer Kurs, andere
/// Stelle, Kapitel-Woerterbuch).</param>
/// <param name="Translated">Stellen, die ans Modell gingen (gleiche Texte zaehlen je Stelle).</param>
/// <param name="Removed">Uebersetzungen, deren Vorlage verschwunden ist.</param>
/// <param name="Pending">Kapitelname offen, weil das Kapitel-Woerterbuch ihn nicht hatte.</param>
/// <param name="Deferred">Stellen, die in diesem Durchgang einer ANDEREN Linie gehoerten (Phase 1 des Kurs-Laufs) und
/// deshalb offen blieben — sie kommen in Phase 2 per Wiederverwendung.</param>
public sealed record LineTranslationResult(LineTranslationStatus Status, int Kept = 0, int Reused = 0,
    int Translated = 0, int Removed = 0, int Pending = 0, int Deferred = 0)
{
    /// <summary>Stellen, die nach diesem Aufruf noch offen sind.</summary>
    public int Remaining => Pending + Deferred;
}

/// <summary>Eine Linie mit offener Arbeit und den Fingerabdruecken ihrer offenen Stellen (ohne Kapitel — das kommt
/// aus dem Kapitel-Woerterbuch des Laufs).</summary>
internal sealed record OpenLine(int Id, IReadOnlyList<string> OpenHashes);

/// <summary>Zwischenstand eines Kurs-Laufs — zu Beginn und alle <see cref="CourseTranslationService.ProgressEvery"/> Linien
/// (der Auftrag schreibt ihn mit, <see cref="CourseTranslationJobService.RunAsync"/>).</summary>
public sealed record CourseTranslationProgress(int LinesTotal, int LinesDone, int LinesFailed);

public enum CourseTranslationRunStatus
{
    /// <summary>Gelaufen (auch wenn einzelne Linien scheiterten — siehe <see cref="CourseTranslationRun.LinesFailed"/>).</summary>
    Done,
    /// <summary>Keine offene Arbeit — kein Modellaufruf.</summary>
    NothingToDo,
    SameLanguage,
    NotConfigured,
    NotFound,
    InvalidLanguage,
    /// <summary>Das Modell antwortete nicht (<see cref="IClaudeJsonClient.IsUnreachable"/>) — der Lauf hat angehalten,
    /// statt Linien als gescheitert zu verbuchen. Fertiges bleibt, der naechste Lauf macht weiter.</summary>
    Unreachable,
}

/// <param name="LinesTotal">Linien mit offener Arbeit zu Beginn des Laufs.</param>
/// <param name="ChaptersMissing">Kapitelnamen, fuer die es nach dem Lauf keine Uebersetzung gibt
/// (Kapitel-Fuhre gescheitert) — die Linien behalten dort einen offenen Kapitel-Slot.</param>
public sealed record CourseTranslationRun(CourseTranslationRunStatus Status, string? SourceLanguage,
    int LinesTotal = 0, int LinesDone = 0, int LinesFailed = 0, int ChaptersTotal = 0, int ChaptersMissing = 0);
