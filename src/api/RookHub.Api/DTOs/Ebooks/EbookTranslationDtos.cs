namespace RookHub.Api.DTOs.Ebooks;

/// <summary>Übersicht der Buch-Übersetzung für Verwalter (<c>GET /api/admin/ebooks/translations</c>, 0.876.0).</summary>
public class EbookTranslationStatusDto
{
    /// <summary>Dienst eingeschaltet (<c>EbookTranslation:Languages</c> gesetzt und <c>Ebooks:Root</c> da).</summary>
    public bool Enabled { get; set; }
    /// <summary>Ein Modell auf eigener Hardware (Spark) ist eingestellt — übersetzt wird NUR darüber.</summary>
    public bool ModelAvailable { get; set; }
    public List<string> Languages { get; set; } = new();
    /// <summary>Gerade Sperrzeit der Spark (dann ruht die Übersetzung) und bis wann.</summary>
    public bool QuietNow { get; set; }
    public DateTime? QuietUntil { get; set; }
    /// <summary>Der gerade laufende Lauf, sonst <c>null</c>.</summary>
    public EbookTranslationRunDto? Running { get; set; }
    /// <summary>Bücher nach Quellsprache (erkannt) — „und“ = unklar/gemischt, „notext“ = ohne Text, „pending“ = noch nicht geprüft.</summary>
    public List<EbookLanguageCountDto> BookLanguages { get; set; } = new();
    /// <summary>Je Zielsprache: wie viele fertig, wartend, gescheitert, ohne Text — und wie viele Bücher insgesamt noch dran sind.</summary>
    public List<EbookTranslationCountsDto> Targets { get; set; } = new();
    /// <summary>Zuletzt gescheiterte (höchstens 20).</summary>
    public List<EbookTranslationItemDto> Failed { get; set; } = new();
    /// <summary>Die nächsten in der Reihe (höchstens 10).</summary>
    public List<EbookTranslationItemDto> Next { get; set; } = new();
}

public class EbookLanguageCountDto
{
    public string Language { get; set; } = "";
    public int Books { get; set; }
}

public class EbookTranslationCountsDto
{
    public string Language { get; set; } = "";
    public int Done { get; set; }
    public int Pending { get; set; }
    public int Failed { get; set; }
    public int NoText { get; set; }
    /// <summary>Bücher, die für diese Zielsprache noch übersetzt werden (mit Zeile oder ohne — Quellsprache bekannt, nicht Zielsprache).</summary>
    public int Remaining { get; set; }
    /// <summary>Davon in der ersten Runde (weder Englisch noch Deutsch) — Deutsch kommt erst danach.</summary>
    public int RemainingFirstRound { get; set; }
}

public class EbookTranslationRunDto
{
    public int EbookId { get; set; }
    public string Title { get; set; } = "";
    public string Language { get; set; } = "";
    public string? SourceLanguage { get; set; }
    public int SegmentsDone { get; set; }
    public int SegmentsTotal { get; set; }
    public DateTime StartedAt { get; set; }
    /// <summary>Geschätzte Restdauer in Minuten (aus dem Tempo dieses Laufs); <c>null</c>, solange zu wenig gemessen ist.</summary>
    public int? EtaMinutes { get; set; }
}

public class EbookTranslationItemDto
{
    public int EbookId { get; set; }
    public string Title { get; set; } = "";
    public string Language { get; set; } = "";
    public string? SourceLanguage { get; set; }
    /// <summary><c>pending</c> / <c>running</c> / <c>done</c> / <c>failed</c> / <c>notext</c>; <c>queued</c> = noch ohne Zeile.</summary>
    public string State { get; set; } = "";
    public bool Priority { get; set; }
    public int SegmentsDone { get; set; }
    public int SegmentsTotal { get; set; }
    public int SegmentsFailed { get; set; }
    public string? Error { get; set; }
    public DateTime? CompletedAt { get; set; }
}
