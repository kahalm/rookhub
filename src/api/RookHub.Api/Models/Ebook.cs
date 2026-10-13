using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>Woher ein E-Book stammt — entscheidet, unter welchem Wurzelverzeichnis seine Dateien liegen.</summary>
public enum EbookSource
{
    /// <summary>Von einem Nutzer über die Oberfläche hochgeladen; liegt unter <c>Ebooks:Root</c>.</summary>
    Upload = 0,
    /// <summary>Aus der Bibliothek auf dem Server eingelesen (<c>Ebooks:LibraryRoot</c>, nur lesend) — die Datei
    /// bleibt an Ort und Stelle, RookHub merkt sich nur den Pfad.</summary>
    Library = 1,
}

/// <summary>Stand des Titelbilds (<c>&lt;Root&gt;/covers/&lt;Id&gt;.jpg</c>), das der <c>EbookCoverWorker</c> nachträglich rendert.</summary>
public enum EbookCoverState
{
    /// <summary>Noch nicht versucht — der Worker nimmt sich das Buch beim nächsten Durchlauf.</summary>
    Pending = 0,
    Done = 1,
    /// <summary>Werkzeug fehlt, Datei kaputt, Zeitüberschreitung — kein weiterer Versuch (sonst liefe der Worker im Kreis).</summary>
    Failed = 2,
    /// <summary>Format ohne Titelbild-Weg (mobi, azw3, cbr).</summary>
    Unsupported = 3,
}

/// <summary>
/// Ein E-BOOK der Bibliothek (<c>/ebooks</c>): ein Titel mit einer oder mehreren Dateien (dasselbe Buch als PDF
/// und EPUB), Metadaten und einer Gruppen-Freigabe je Buch (<see cref="EbookGroupAccess"/>) — dasselbe Muster
/// wie <see cref="BookGroupAccess"/> bei den Kursen, nur dass hier JEDER angemeldete Nutzer hochladen darf und die
/// Sichtbarkeit seines Buchs selbst steuert (seine eigenen Gruppen; Admins alle).
///
/// <para>Sichtbar ist ein Buch für den Besitzer, für Admins und für Mitglieder einer freigegebenen Gruppe
/// (die System-Gruppe „Everyone" öffnet es für alle Angemeldeten) — Regel in <c>Services/Ebooks/EbookAccess</c>.</para>
/// </summary>
public class Ebook
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Autor(en) als Anzeigetext („Jeremy Silman“, „Kotov, Yudovich“); leer = unbekannt.</summary>
    [MaxLength(300)]
    public string Author { get; set; } = string.Empty;

    public int? Year { get; set; }

    /// <summary>Schlagworte als CSV (<c>Endspiel,Strategie</c>) — die Filterzeile der Übersicht. Max. 20 à 40 Zeichen.</summary>
    [MaxLength(600)]
    public string Tags { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Ab welcher Spielstärke das Buch gedacht ist (aus den Beiblättern der Bibliothek); <c>null</c> = keine Angabe.</summary>
    public int? TargetMinElo { get; set; }

    /// <summary>Wer es hochgeladen bzw. eingelesen hat — darf es bearbeiten und löschen. <c>null</c>, wenn das Konto
    /// gelöscht wurde (dann nur noch Admins).</summary>
    public int? OwnerUserId { get; set; }
    public AppUser? Owner { get; set; }

    public EbookSource Source { get; set; } = EbookSource.Upload;

    /// <summary>Seitenzahl der PDF-Datei (vom Cover-Worker ermittelt); <c>null</c> = unbekannt/kein PDF.</summary>
    public int? PageCount { get; set; }

    public EbookCoverState CoverState { get; set; } = EbookCoverState.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<EbookFile> Files { get; set; } = new();
    public List<EbookGroupAccess> GroupAccess { get; set; } = new();
}

/// <summary>Eine Datei eines E-Books (ein Format). Ein Buch hat mindestens eine; die Bibliothek bringt oft
/// PDF + DJVU + EPUB desselben Titels mit.</summary>
public class EbookFile
{
    public int Id { get; set; }

    public int EbookId { get; set; }
    public Ebook Ebook { get; set; } = null!;

    /// <summary>Kleingeschriebene Endung ohne Punkt (<c>pdf</c>, <c>epub</c>, <c>djvu</c>, <c>cbz</c> …).</summary>
    [Required, MaxLength(10)]
    public string Format { get; set; } = string.Empty;

    /// <summary>Pfad RELATIV zum Wurzelverzeichnis der Quelle (<see cref="Ebook.Source"/>), immer mit „/“. Nie absolut,
    /// nie mit „..“ — <c>EbookStorage</c> prüft das beim Auflösen.</summary>
    [Required, MaxLength(1000)]
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Ursprünglicher Dateiname (für den Download); bei Bibliotheksdateien der Name auf der Platte.</summary>
    [Required, MaxLength(300)]
    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>SHA-256 (Hex, klein) — nur bei Uploads berechnet; erkennt eine doppelt hochgeladene Datei.</summary>
    [MaxLength(64)]
    public string? Sha256 { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Gruppe, die ein E-Book sehen darf (n:m, zusammengesetzter Schlüssel) — wie <see cref="BookGroupAccess"/>.</summary>
public class EbookGroupAccess
{
    public int EbookId { get; set; }
    public Ebook? Ebook { get; set; }

    public int GroupId { get; set; }
    public Group? Group { get; set; }
}

/// <summary>Lesestand eines Nutzers in einem E-Book: die zuletzt gesehene Seite (PDF) — „Weiterlesen ab Seite 23“.</summary>
public class EbookProgress
{
    public int EbookId { get; set; }
    public Ebook? Ebook { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>1-basierte Seite.</summary>
    public int Page { get; set; }

    /// <summary>Seitenzahl, wie der Leser sie beim Speichern kannte (für den Prozentbalken, auch wenn der Server keine hat).</summary>
    public int? PageCount { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
