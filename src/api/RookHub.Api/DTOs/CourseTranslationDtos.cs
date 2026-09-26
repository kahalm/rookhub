using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary><c>GET /api/courses/{id}/translations</c> — welche Sprachen es fuer den Kurs gibt, was wartet oder laeuft,
/// und ob der Aufrufer gerade selbst einen Auftrag offen hat (Plan „Kurs-Kommentare mehrsprachig", Abschnitt 5).</summary>
public class CourseTranslationsDto
{
    /// <summary>Quellsprache der Kommentare (<c>und</c> = nicht bestimmbar). Wird beim ersten Abruf bestimmt.</summary>
    public string? SourceLanguage { get; set; }

    /// <summary>Sprachen, in denen es fuer den Kurs Saetze gibt (je Sprache: Linien mit Satz / Linien mit Text).</summary>
    public List<CourseTranslationLanguageDto> Languages { get; set; } = new();

    /// <summary>Offene Auftraege des Kurses, dahinter die juengsten erledigten.</summary>
    public List<CourseTranslationJobDto> Jobs { get; set; } = new();

    /// <summary>Bis wann die Spark gerade anderen gehoert (<c>null</c> = frei) — ein Auftrag wird trotzdem angenommen
    /// und wartet.</summary>
    public DateTimeOffset? QuietUntil { get; set; }

    /// <summary>Der offene ANGEFORDERTE Auftrag des Aufrufers (irgendein Kurs) — fuer den Hinweis beim Nutzer-Limit.
    /// <c>null</c> ohne Anmeldung oder ohne offenen Auftrag.</summary>
    public CourseTranslationMyJobDto? MyOpenJob { get; set; }

    /// <summary>Es ist ein Text-Modell konfiguriert (sonst <c>not-configured</c> beim Anfordern).</summary>
    public bool Available { get; set; }

    /// <summary>Der Aufrufer darf JETZT einen neuen Auftrag anfordern: angemeldet, Modell da, und (Admin oder) kein
    /// eigener offener Auftrag. Ein schon offener Auftrag fuer (Kurs, Sprache) geht trotzdem immer.</summary>
    public bool CanRequest { get; set; }
}

public class CourseTranslationLanguageDto
{
    public string Language { get; set; } = string.Empty;

    /// <summary>Linien mit einem Satz in dieser Sprache (auch wenn einzelne Stellen inzwischen veraltet sind).</summary>
    public int LinesTranslated { get; set; }

    /// <summary>Linien mit uebersetzbarem Text.</summary>
    public int LinesTotal { get; set; }
}

public class CourseTranslationJobDto
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string Language { get; set; } = string.Empty;

    /// <summary><c>queued</c>/<c>running</c>/<c>done</c>/<c>failed</c>/<c>cancelled</c> (klein, wie bei den
    /// Analyseauftraegen).</summary>
    public string Status { get; set; } = string.Empty;

    public int LinesTotal { get; set; }
    public int LinesDone { get; set; }
    public int LinesFailed { get; set; }

    /// <summary>Angelegt von der Automatik (de/en fuer alle Kurse, Nachziehen nach dem Aktualisieren).</summary>
    public bool Automatic { get; set; }

    public bool RequestedByMe { get; set; }

    /// <summary>Platz in der Warteschlange (1 = der naechste), nur bei <c>queued</c>. Angeforderte stehen vor der
    /// Automatik, je Gruppe die aeltesten zuerst.</summary>
    public int? QueuePosition { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? LastError { get; set; }
}

public class CourseTranslationMyJobDto
{
    public int JobId { get; set; }
    public int BookId { get; set; }
    public string BookName { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary><c>POST /api/courses/{id}/translations</c>.</summary>
public class RequestCourseTranslationDto
{
    [Required, MaxLength(16)]
    public string Language { get; set; } = string.Empty;
}

/// <summary><c>PUT /api/courses/{id}/comment-language</c> — Quellsprache korrigieren (<c>und</c> = nicht bestimmbar).</summary>
public class SetCourseCommentLanguageDto
{
    [Required, MaxLength(16)]
    public string Language { get; set; } = string.Empty;
}

/// <summary><c>GET /api/admin/course-translations</c> — Warteschlange + juengste erledigte Auftraege.</summary>
public class AdminCourseTranslationsDto
{
    /// <summary>Offene Auftraege in der Reihenfolge, in der der Dienst sie nimmt (der laufende zuerst).</summary>
    public List<AdminCourseTranslationJobDto> Queue { get; set; } = new();
    public List<AdminCourseTranslationJobDto> Recent { get; set; } = new();
    public DateTimeOffset? QuietUntil { get; set; }
    public bool Available { get; set; }
    /// <summary>Sprachen der Automatik (<c>CourseTranslation:AutoLanguages</c>) — leer = aus.</summary>
    public List<string> AutoLanguages { get; set; } = new();
}

public class AdminCourseTranslationJobDto : CourseTranslationJobDto
{
    public string BookName { get; set; } = string.Empty;
    public int? RequestedByUserId { get; set; }
    public string? RequestedByUsername { get; set; }
}
