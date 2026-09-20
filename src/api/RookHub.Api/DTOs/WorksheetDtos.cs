using System.ComponentModel.DataAnnotations;
using RookHub.Api.Models;

namespace RookHub.Api.DTOs;

/// <summary>Ein Aufgabenblatt in der Übersicht (ohne Stellungen).</summary>
public class WorksheetSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsClipboard { get; set; }
    public int PerPage { get; set; }
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Ein Aufgabenblatt mit seinen Stellungen (Bearbeiten + Drucken).</summary>
public class WorksheetDto : WorksheetSummaryDto
{
    public List<WorksheetItemDto> Items { get; set; } = new();
}

/// <summary>Eine Aufgabe des Blatts.</summary>
public class WorksheetItemDto
{
    public int Id { get; set; }
    public int SortOrder { get; set; }
    public string Fen { get; set; } = string.Empty;
    public string Orientation { get; set; } = "white";
    public string Heading { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Source { get; set; } = nameof(WorksheetItemSource.Manual);
    public int? SourceId { get; set; }
    public int? BookId { get; set; }
}

/// <summary>Eine zu ergänzende Stellung (Stellung wird ausgeschrieben, nicht verlinkt).</summary>
public class NewWorksheetItemDto
{
    [Required, MaxLength(120)]
    public string Fen { get; set; } = string.Empty;
    public string Orientation { get; set; } = "white";
    [MaxLength(200)]
    public string? Heading { get; set; }
    [MaxLength(2000)]
    public string? Text { get; set; }
    public WorksheetItemSource Source { get; set; } = WorksheetItemSource.Manual;
    public int? SourceId { get; set; }
    public int? BookId { get; set; }
}

/// <summary>„An Aufgabenblatt senden" — Ziel <c>null</c>/<c>0</c> ist die Zwischenablage.</summary>
public class AddWorksheetItemsDto
{
    public int? WorksheetId { get; set; }
    public List<NewWorksheetItemDto> Items { get; set; } = new();
}

/// <summary>Antwort des Sendens: wohin es ging und wie viel ankam (für die Snackbar).</summary>
public class AddWorksheetItemsResultDto
{
    public int WorksheetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsClipboard { get; set; }
    /// <summary>Tatsächlich hinzugefügte Stellungen (Duplikate/unbrauchbare FENs fallen weg).</summary>
    public int Added { get; set; }
    /// <summary>Übersprungen, weil schon auf dem Blatt (gleiche Stellung + Ausrichtung).</summary>
    public int Skipped { get; set; }
    /// <summary>Gesamtzahl der Stellungen auf dem Blatt nach dem Senden.</summary>
    public int Total { get; set; }
    /// <summary>Das Blatt ist voll (Obergrenze erreicht) — der Rest kam NICHT an.</summary>
    public bool Full { get; set; }
}

/// <summary>Blatt anlegen bzw. die Zwischenablage unter einem Namen sichern.</summary>
public class CreateWorksheetDto
{
    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;
    public int? PerPage { get; set; }
}

/// <summary>Umbenennen / Dichte ändern (beides optional).</summary>
public class UpdateWorksheetDto
{
    [MaxLength(120)]
    public string? Name { get; set; }
    public int? PerPage { get; set; }
}

/// <summary>Überschrift/Begleittext/Ausrichtung einer Aufgabe ändern (nur gesetzte Felder wirken).</summary>
public class UpdateWorksheetItemDto
{
    [MaxLength(200)]
    public string? Heading { get; set; }
    [MaxLength(2000)]
    public string? Text { get; set; }
    public string? Orientation { get; set; }
}

/// <summary>Neue Reihenfolge: die Item-IDs des Blatts in der gewünschten Abfolge.</summary>
public class ReorderWorksheetDto
{
    public List<int> ItemIds { get; set; } = new();
}
