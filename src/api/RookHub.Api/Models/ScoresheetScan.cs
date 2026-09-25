namespace RookHub.Api.Models;

/// <summary>Stand einer Formular-Einlesung.</summary>
public enum ScoresheetScanStatus
{
    /// <summary>Hochgeladen, wartet auf den Worker.</summary>
    Pending = 0,
    /// <summary>Claude liest gerade (oder der Worker ist mittendrin abgestürzt — siehe <see cref="ScoresheetScan.StartedAt"/>).</summary>
    Running = 1,
    /// <summary>Fertig; die Partie liegt unter <see cref="ScoresheetScan.SavedGameId"/>.</summary>
    Done = 2,
    /// <summary>Gescheitert; <see cref="ScoresheetScan.Error"/> nennt den Grund als Code.</summary>
    Failed = 3,
}

/// <summary>
/// Ein hochgeladenes Foto eines Partieformulars und was daraus wurde (Menüpunkt „Partieformular
/// einlesen", 0.529.0). Claude liest das Bild, der Server prüft jeden Zug auf Legalität
/// (<see cref="Services.ScoresheetResolver"/>), und das Ergebnis landet als normale gespeicherte
/// Partie (<see cref="SavedGame"/>, Quelle <c>scoresheet</c>) in „Meine Partien".
///
/// <para>Das FOTO bleibt hier liegen, auch nach dem Einlesen: die Partie verweist über
/// <see cref="SavedGameId"/> darauf, und ihr ⋮-Menü zeigt es an bzw. lädt es herunter — beim Korrigieren
/// ist das Original die einzige Quelle. In der Datenbank statt auf der Platte, damit es mit der Partie
/// gesichert, gelöscht und umgezogen wird und der Stack kein zusätzliches Volume braucht.</para>
/// </summary>
public class ScoresheetScan
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Die daraus entstandene Partie; <c>null</c>, solange gelesen wird oder wenn es scheiterte.
    /// Wird die Partie gelöscht, geht das Foto mit (Cascade).</summary>
    public int? SavedGameId { get; set; }
    public SavedGame? Game { get; set; }

    /// <summary>Das Foto (JPEG/PNG/WebP). Sehr große Uploads werden verkleinert abgelegt
    /// (<see cref="Services.ScoresheetScanService.MaxStoredBytes"/>).</summary>
    public byte[] Photo { get; set; } = Array.Empty<byte>();

    /// <summary>MIME-Typ von <see cref="Photo"/>.</summary>
    public string ContentType { get; set; } = "image/jpeg";

    /// <summary>Originaler Dateiname (für den Download), ohne Pfad.</summary>
    public string? FileName { get; set; }

    /// <summary>Gewählte Notationssprache (<c>de</c>, <c>en</c>, …) oder <c>auto</c>.</summary>
    public string NotationLanguage { get; set; } = "auto";

    public ScoresheetScanStatus Status { get; set; } = ScoresheetScanStatus.Pending;

    /// <summary>Grund des Scheiterns als Code (<c>notConfigured</c>, <c>unreadable</c>, <c>noMoves</c>,
    /// <c>refused</c>, <c>failed</c>) — die Seite formuliert ihn in der Sprache des Nutzers.</summary>
    public string? Error { get; set; }

    /// <summary>Letzte Antwort des Modells (JSON), zur Nachvollziehbarkeit.</summary>
    public string? TranscriptionJson { get; set; }

    /// <summary>Ergebnis der Zugprüfung je Halbzug (JSON): was auf dem Formular stand, welcher Zug
    /// daraus wurde und wie sicher — die Korrekturseite markiert damit die unsicheren Züge.</summary>
    public string? ResolutionJson { get; set; }

    /// <summary>Welches Modell gelesen hat.</summary>
    public string? Model { get; set; }

    /// <summary>Wie oft der Worker angesetzt hat (ein Absturz mitten im Lesen zählt mit; ab drei ist Schluss).</summary>
    public int Attempts { get; set; }

    /// <summary>Wie viele Lese-Durchgänge beim Modell nötig waren (1 = auf Anhieb, mehr = Nachfragen).</summary>
    public int Rounds { get; set; }

    /// <summary>Beim Modell verbrauchte Eingabe-Tokens (alle Durchgänge, auch abgebrochene).</summary>
    public int InputTokens { get; set; }

    /// <summary>Beim Modell verbrauchte Ausgabe-Tokens (inklusive Nachdenken — das kostet wie Ausgabe).</summary>
    public int OutputTokens { get; set; }

    /// <summary>Kosten dieser Einlesung in Millionstel Dollar, nach den Preisen zum Zeitpunkt des Aufrufs
    /// (<see cref="Services.ScoresheetBudget"/>). Daran hängen die Budgets je Nutzer und insgesamt.</summary>
    public long CostMicroUsd { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
