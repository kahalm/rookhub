namespace RookHub.Api.DTOs.Ebooks;

// Externe Übersetzer (Claude-Agenten …): /api/admin/ebooks/translations/external — siehe docs/api/ebooks.md.

/// <summary><c>POST …/external/claim</c>: Buch + Zielsprache beanspruchen.</summary>
public class ExternalTranslationClaimDto
{
    public int EbookId { get; set; }
    public string Language { get; set; } = "en";
    /// <summary>Kennung des Übersetzers (Buchstaben, Ziffern, <c>. _ - @</c>, höchstens 80) — landet als <c>external:&lt;name&gt;</c> in <c>Model</c>.</summary>
    public string? Translator { get; set; }
}

/// <summary>Stand einer extern beanspruchten Übersetzung (Antwort von claim und <c>GET …/external/{id}</c>).</summary>
public class ExternalTranslationInfoDto
{
    public int TranslationId { get; set; }
    public int EbookId { get; set; }
    public string Title { get; set; } = "";
    public string Language { get; set; } = "";
    /// <summary>Quellsprache des Buchs (<c>null</c> = nicht bestimmt).</summary>
    public string? SourceLanguage { get; set; }
    public string State { get; set; } = "";
    /// <summary>Name hinter <c>external:</c>, <c>null</c> wenn nicht (mehr) extern.</summary>
    public string? Translator { get; set; }
    public int SegmentsTotal { get; set; }
    public int SegmentsDone { get; set; }
    public int SegmentsOpen { get; set; }
    public int SegmentsFailed { get; set; }
    public string? Error { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>Hinweise (Figurenbuchstaben, aus dem Zwischenspeicher übernommene Texte, unbekannte Quellsprache …).</summary>
    public List<string> Notes { get; set; } = new();
    /// <summary>Der Auftrag, den die Spark für dieses Buch bekommt (wörtlich) — nur bei <c>GET …/external/{id}</c>.</summary>
    public string? SystemPrompt { get; set; }
    /// <summary>Wie Texte geholt und zurückgegeben werden (Format, Prüfungen) — nur bei <c>GET …/external/{id}</c>.</summary>
    public string? Instructions { get; set; }
}

/// <summary>Ein Text: <see cref="Source"/> genau so, wie er an die Spark ginge.</summary>
public class ExternalTranslationSegmentDto
{
    public int Index { get; set; }
    public string Source { get; set; } = "";
    /// <summary>Gespeicherte Übersetzung (nur, wenn nicht <c>onlyOpen</c>).</summary>
    public string? Target { get; set; }
    /// <summary>EPUB-Dokument des ersten Vorkommens (Orientierung).</summary>
    public string Document { get; set; } = "";
}

public class ExternalTranslationSegmentPageDto
{
    /// <summary>Texte insgesamt unter dem Filter (<c>onlyOpen</c>).</summary>
    public int Total { get; set; }
    public int Offset { get; set; }
    public List<ExternalTranslationSegmentDto> Items { get; set; } = new();
}

/// <summary>Eine Übersetzung für <c>PUT …/external/{id}/segments</c>.</summary>
public class ExternalTranslationTargetDto
{
    public int Index { get; set; }
    public string? Target { get; set; }
}

public class ExternalTranslationRejectDto
{
    public int Index { get; set; }
    /// <summary><c>unknown_index</c>, <c>empty</c>, <c>placeholders</c>, <c>nesting</c>, <c>open_quote</c>, <c>too_short</c>, <c>wrong_language</c>, <c>duplicate</c>.</summary>
    public string Reason { get; set; } = "";
    public string Message { get; set; } = "";
}

public class ExternalTranslationPutResultDto
{
    public int Accepted { get; set; }
    public List<ExternalTranslationRejectDto> Rejected { get; set; } = new();
    public int SegmentsDone { get; set; }
    public int SegmentsTotal { get; set; }
}

/// <summary><c>POST …/external/{id}/assemble</c>.</summary>
public class ExternalTranslationAssembleDto
{
    /// <summary>Fehlende/unpassende Texte im Original lassen (zählen in <c>SegmentsFailed</c>) statt 409.</summary>
    public bool AllowMissing { get; set; }
    /// <summary>Optional: Buchtitel in der Zielsprache (sonst holt ihn der Spark-Worker im Leerlauf nach).</summary>
    public string? Title { get; set; }
}

public class ExternalTranslationAssembleResultDto
{
    public int TranslationId { get; set; }
    public string State { get; set; } = "";
    public int SegmentsTotal { get; set; }
    public int SegmentsDone { get; set; }
    public int SegmentsFailed { get; set; }
    public string? RelativePath { get; set; }
    public long SizeBytes { get; set; }
}
