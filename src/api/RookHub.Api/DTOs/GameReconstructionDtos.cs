using System.ComponentModel.DataAnnotations;
using RookHub.Api.Models;

namespace RookHub.Api.DTOs;

/// <summary>Listeneintrag einer Rekonstruktion (ohne die Teile).</summary>
public class ReconstructionListItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Event { get; set; }
    public DateOnly? PlayedOn { get; set; }
    public string? Result { get; set; }
    public int PartCount { get; set; }
    /// <summary>Halbzüge, die ab der Grundstellung schon lückenlos stehen.</summary>
    public int KnownPlies { get; set; }
    /// <summary>Stellen, an denen die Kette abreißt — die offenen Lücken.</summary>
    public int Gaps { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Ein Bruchstück samt Auswertung (<see cref="Services.ReconstructionChain"/>).</summary>
public class ReconstructionPartDto
{
    public int Id { get; set; }
    public int Ordinal { get; set; }
    public ReconstructionPartKind Kind { get; set; }
    public string? Moves { get; set; }
    public string? Fen { get; set; }
    public int? FromPly { get; set; }
    public bool ContinuesPrevious { get; set; }
    public string? Note { get; set; }

    /// <summary>Ist bekannt, welche Stellung vor diesem Teil steht?</summary>
    public bool Anchored { get; set; }
    /// <summary>Nur wenn verankert: ließ sich das Teil spielen bzw. laden?</summary>
    public bool Valid { get; set; }
    public string? StartFen { get; set; }
    public string? EndFen { get; set; }
    public int PlyCount { get; set; }
    /// <summary>Halbzug-Nummer des Teils, solange die Kette ab der Grundstellung durchgeht.</summary>
    public int? StartPly { get; set; }
    /// <summary>Der erste Zug, der sich nicht spielen ließ.</summary>
    public string? FirstBadMove { get; set; }
    /// <summary>Das Teil behauptet den Anschluss, passt aber nicht zur Stellung davor.</summary>
    public bool Mismatch { get; set; }
}

/// <summary>Die ganze Rekonstruktion mit allen Teilen und dem Gesamtbild.</summary>
public class ReconstructionDetailDto : ReconstructionListItemDto
{
    public string? Note { get; set; }
    public List<ReconstructionPartDto> Parts { get; set; } = new();
    /// <summary>Die bereits gesicherten Züge ab der Grundstellung als SAN-Folge.</summary>
    public string PrefixSan { get; set; } = string.Empty;
}

/// <summary>Kopfdaten anlegen/ändern. Nur <see cref="Title"/> ist Pflicht.</summary>
public class ReconstructionHeadRequest
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? White { get; set; }

    [MaxLength(120)]
    public string? Black { get; set; }

    [MaxLength(200)]
    public string? Event { get; set; }

    public DateOnly? PlayedOn { get; set; }

    [MaxLength(12)]
    public string? Result { get; set; }

    [MaxLength(2000)]
    public string? Note { get; set; }
}

/// <summary>Ein Teil anlegen oder ändern.</summary>
public class ReconstructionPartRequest
{
    public ReconstructionPartKind Kind { get; set; }

    /// <summary>Zugfolge in SAN; Zugnummern, Kommentare und Ergebnis dürfen drinstehen.</summary>
    [MaxLength(4000)]
    public string? Moves { get; set; }

    [MaxLength(120)]
    public string? Fen { get; set; }

    [Range(0, 600)]
    public int? FromPly { get; set; }

    /// <summary>Schließt das Teil nahtlos an das vorige an? (Vorgabe: nein — Lücke.)</summary>
    public bool ContinuesPrevious { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}

/// <summary>Neue Reihenfolge der Teile (alle Ids der Rekonstruktion, in der gewünschten Folge).</summary>
public class ReconstructionOrderRequest
{
    [Required]
    public List<int> PartIds { get; set; } = new();
}
