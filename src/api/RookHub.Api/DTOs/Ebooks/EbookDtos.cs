using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs.Ebooks;

/// <summary>Eine Datei (ein Format) eines E-Books.</summary>
public class EbookFileDto
{
    public int Id { get; set; }
    public string Format { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

/// <summary>Lesestand des Nutzers in einem Buch.</summary>
public class EbookProgressDto
{
    public int Page { get; set; }
    public int? PageCount { get; set; }
    /// <summary>EPUB: CFI der Lesestelle; PDF: leer.</summary>
    public string? Locator { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Ein E-Book in Übersicht und Leser.</summary>
public class EbookDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public int? Year { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? Description { get; set; }
    public int? TargetMinElo { get; set; }
    /// <summary>Startmodus des Bretts (0.781.0): view/free/calc/solve/maia — wirksam, Standard „solve“.</summary>
    public string DefaultBoardMode { get; set; } = "solve";
    /// <summary><c>upload</c> oder <c>library</c>.</summary>
    public string Source { get; set; } = "upload";
    public int? PageCount { get; set; }
    public bool HasCover { get; set; }
    /// <summary><c>pending</c> / <c>done</c> / <c>failed</c> / <c>unsupported</c> — Stand der Diagramm-Erkennung.</summary>
    public string DiagramState { get; set; } = "pending";
    public int DiagramCount { get; set; }
    /// <summary>Hintergrundschritte stehen noch aus (Titelbild, Umwandlung, Diagramme, Prüfung) — Karte zeigt „wird verarbeitet“.</summary>
    public bool Processing { get; set; }
    /// <summary><c>notneeded</c> / <c>pending</c> / <c>done</c> / <c>failed</c> — DJVU→PDF-Umwandlung.</summary>
    public string ConversionState { get; set; } = "notneeded";
    public int? OwnerUserId { get; set; }
    public string? OwnerName { get; set; }
    public bool IsOwner { get; set; }
    /// <summary>Darf der Aufrufer Metadaten, Freigabe und Löschen — Besitzer oder Verwalter.</summary>
    public bool CanManage { get; set; }
    /// <summary>Freigegebene Gruppen — nur gefüllt, wenn <see cref="CanManage"/>.</summary>
    public List<int>? GroupIds { get; set; }
    public List<EbookFileDto> Files { get; set; } = new();
    /// <summary>Format der Hauptdatei (PDF liest der Browser); <c>null</c> ohne Dateien.</summary>
    public string? PrimaryFormat { get; set; }
    public EbookProgressDto? Progress { get; set; }
    /// <summary>Sammlung des Buchs (0.787.0); <c>null</c> = keine.</summary>
    public EbookCollectionDto? Collection { get; set; }
    /// <summary>Dieser Eintrag steht in der Liste STELLVERTRETEND für seine Sammlung (Ordner-Karte, Titelbild von ihm).</summary>
    public bool CollectionStub { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class EbookTagFacetDto
{
    public string Tag { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>Antwort der Übersicht: eine Seite plus Rahmen (Schlagworte, Upload-Stand, Kontingent).</summary>
public class EbookListDto
{
    public List<EbookDto> Items { get; set; } = new();
    /// <summary>Die geöffnete Sammlung (bei <c>?collection=</c>), sonst <c>null</c>.</summary>
    public EbookCollectionDto? Collection { get; set; }
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    /// <summary>Schlagworte über ALLE sichtbaren Bücher (unabhängig vom Filter), häufigste zuerst.</summary>
    public List<EbookTagFacetDto> Tags { get; set; } = new();
    public bool UploadEnabled { get; set; }
    public long QuotaUsedBytes { get; set; }
    /// <summary><c>null</c> = unbegrenzt (Verwalter oder Kontingent 0).</summary>
    public long? QuotaLimitBytes { get; set; }
    public long MaxFileBytes { get; set; }
    /// <summary>Der Aufrufer darf alles verwalten (Admin / <c>ebooks.manage</c>).</summary>
    public bool CanManageAll { get; set; }
    /// <summary>Bibliothek auf dem Server konfiguriert — „Bibliothek einlesen" anbieten (nur Verwalter).</summary>
    public bool CanImportLibrary { get; set; }
}

/// <summary>Filter der Übersicht (Query-String).</summary>
public class EbookQuery
{
    public string? Search { get; set; }
    public string? Tag { get; set; }
    /// <summary>Nur eigene Uploads.</summary>
    public bool Mine { get; set; }
    /// <summary>Nur Bücher mit erkannten Diagrammen (Brett neben dem Buch).</summary>
    public bool WithDiagrams { get; set; }
    /// <summary>Nur die Bücher dieser Sammlung (0.787.0). Ohne: Sammlungen erscheinen als je EIN Eintrag (Ordner),
    /// außer bei einer Suche — dann stehen die Treffer einzeln da.</summary>
    public int? Collection { get; set; }
    /// <summary><c>title</c> (Vorgabe), <c>newest</c>, <c>recent</c> (zuletzt gelesen zuerst).</summary>
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 60;
}

/// <summary>Metadaten + Freigabe beim Anlegen/Ändern; beim PUT wirken nur gesetzte Felder.</summary>
public class EbookMetaDto
{
    [MaxLength(300)] public string? Title { get; set; }
    [MaxLength(300)] public string? Author { get; set; }
    public int? Year { get; set; }
    public List<string>? Tags { get; set; }
    [MaxLength(2000)] public string? Description { get; set; }
    public int? TargetMinElo { get; set; }
    /// <summary>Startmodus des Bretts (view/free/calc/solve/maia); <c>null</c> = unverändert, leer = Standard.</summary>
    [MaxLength(10)] public string? DefaultBoardMode { get; set; }
    /// <summary>Vollständige Liste der freigegebenen Gruppen; <c>null</c> = unverändert. Nicht-Verwalter können nur
    /// ihre eigenen Gruppen (und „Everyone") wählen, anderes wird still verworfen.</summary>
    public List<int>? GroupIds { get; set; }
}

/// <summary>Multipart-Form des Uploads (Datei + Metadaten; Listen als CSV, weil Formulare keine Arrays kennen).</summary>
public class EbookUploadForm
{
    public IFormFile? File { get; set; }
    [MaxLength(300)] public string? Title { get; set; }
    [MaxLength(300)] public string? Author { get; set; }
    public int? Year { get; set; }
    /// <summary>Schlagworte, kommagetrennt.</summary>
    [MaxLength(1000)] public string? Tags { get; set; }
    [MaxLength(2000)] public string? Description { get; set; }
    public int? TargetMinElo { get; set; }
    [MaxLength(10)] public string? DefaultBoardMode { get; set; }
    /// <summary>Gruppen-Ids, kommagetrennt.</summary>
    [MaxLength(500)] public string? GroupIds { get; set; }
}

/// <summary>Freigabe mehrerer Bücher auf einmal (Übersicht: auswählen → „Sichtbarkeit setzen").</summary>
public class SetEbookGroupsDto
{
    public List<int> EbookIds { get; set; } = new();
    public List<int> GroupIds { get; set; } = new();
}

/// <summary>Eine Gruppe, die der Aufrufer freigeben kann.</summary>
public class EbookGroupOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEveryone { get; set; }
}

public class EbookAccessTokenDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class EbookProgressUpdateDto
{
    [Range(1, 100000)] public int Page { get; set; }
    [Range(1, 100000)] public int? PageCount { get; set; }
    [MaxLength(300)] public string? Locator { get; set; }
}

/// <summary>Ergebnis von „Bibliothek einlesen".</summary>
public class EbookLibraryImportResultDto
{
    public bool Configured { get; set; }
    public int FilesSeen { get; set; }
    public int Added { get; set; }
    public int FilesAdded { get; set; }
    public int Skipped { get; set; }
    public int Unreadable { get; set; }
}

public class EbookLibraryStatusDto
{
    public bool Configured { get; set; }
    public bool UploadsEnabled { get; set; }
    public int Imported { get; set; }
    public int CoversPending { get; set; }
    public int DiagramsPending { get; set; }
}

/// <summary>Ein erkanntes Diagramm; Lage als Anteil der Seite (0–1, Ursprung links oben).</summary>
public class EbookDiagramDto
{
    public int Id { get; set; }
    public int Page { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public string Fen { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? Caption { get; set; }
    public bool SideKnown { get; set; }
    /// <summary>EPUB: Bildpfad im Buch (der Leser findet so das &lt;img&gt;); <see cref="Page"/> = Kapitel. PDF: <c>null</c>.</summary>
    public string? ImageRef { get; set; }
    /// <summary>Lösung aus dem Buch (UCI/SAN leerzeichengetrennt, Absatz, Seite) — <c>null</c>, wenn keine gefunden.</summary>
    public string? SolutionUci { get; set; }
    public string? SolutionSan { get; set; }
    public string? SolutionText { get; set; }
    public int? SolutionPage { get; set; }
    /// <summary><c>book</c> / <c>engine</c>.</summary>
    public string? SolutionSource { get; set; }
    /// <summary><c>unchecked</c> / <c>confirmed</c> / <c>doubtful</c> / <c>replaced</c> / <c>engine</c> / <c>nopuzzle</c>.</summary>
    public string SolutionCheck { get; set; } = "unchecked";
    public int? EngineEval { get; set; }
    /// <summary>Der Aufrufer hat dieses Diagramm als falsch gemeldet.</summary>
    public bool ReportedByMe { get; set; }
    /// <summary>Meldungen insgesamt — nur für Verwalter gefüllt, sonst 0.</summary>
    public int ReportCount { get; set; }
}

public class EbookDiagramReportRequest
{
    [MaxLength(300)] public string? Note { get; set; }
}

/// <summary>Eine Meldung in der Verwalter-Liste.</summary>
public class EbookDiagramReportDto
{
    public int Id { get; set; }
    public int EbookId { get; set; }
    public string EbookTitle { get; set; } = string.Empty;
    public int DiagramId { get; set; }
    /// <summary>Gibt es das Diagramm noch (nach einer Neuerkennung evtl. nicht)?</summary>
    public bool DiagramExists { get; set; }
    public int Page { get; set; }
    public string Fen { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? Username { get; set; }
    public string? FontFamily { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Antwort von <c>GET /api/ebooks/{id}/diagrams</c>.</summary>
public class EbookDiagramListDto
{
    public string State { get; set; } = "pending";
    public List<EbookDiagramDto> Items { get; set; } = new();
}

/// <summary>Anonyme Kennung (<c>X-Visitor-Id</c> der App), deren Uploads und Lesestände ins Konto wandern (0.778.0).</summary>
public class ClaimEbookSessionDto
{
    [MaxLength(36)] public string? SessionId { get; set; }
}

/// <summary>Sammlung (0.787.0): Kennung, Name und Zahl der (für den Aufrufer sichtbaren) Bücher darin.</summary>
public class EbookCollectionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BookCount { get; set; }
}

/// <summary>Bücher einer Sammlung zuordnen: <c>CollectionId</c> = bestehende, <c>NewName</c> = neue anlegen, beides leer = aus der
/// Sammlung nehmen.</summary>
public class EbookCollectionAssignDto
{
    public List<int> EbookIds { get; set; } = new();
    public int? CollectionId { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(200)] public string? NewName { get; set; }
}

public class EbookCollectionRenameDto
{
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(200)] public string Name { get; set; } = string.Empty;
}

/// <summary>„Nicht erkanntes Diagramm?“ (0.788.0): Stelle des Doppelklicks + Antwort.</summary>
public class EbookMissedDiagramDto
{
    [System.ComponentModel.DataAnnotations.Range(1, 100000)] public int Page { get; set; }
    [System.ComponentModel.DataAnnotations.Range(0.0, 1.0)] public double X { get; set; }
    [System.ComponentModel.DataAnnotations.Range(0.0, 1.0)] public double Y { get; set; }
    public bool IsDiagram { get; set; }
}

