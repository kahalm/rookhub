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
    /// <summary>Bin ich mir bei diesem Teil sicher? (Vorgabe ja; „nein" ist die Auskunft.)</summary>
    public bool Certain { get; set; }
    /// <summary>Zugfolge ohne Anschluss: beginnt sie mit einem Zug von Schwarz?</summary>
    public bool BlackToMove { get; set; }
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

    /// <summary>Bin ich mir sicher? Fehlt das Feld, bleibt es bei „ja" — ein Client, der die Frage
    /// nicht kennt, darf nicht für den Nutzer „unsicher" behaupten.</summary>
    public bool? Certain { get; set; }

    /// <summary>Beginnt die Zugfolge mit einem Zug von Schwarz? (Nur ohne Anschluss; Vorgabe nein.)</summary>
    public bool? BlackToMove { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}

/// <summary>Neue Reihenfolge der Teile (alle Ids der Rekonstruktion, in der gewünschten Folge).</summary>
public class ReconstructionOrderRequest
{
    [Required]
    public List<int> PartIds { get; set; } = new();
}

/// <summary>Wie weit darf die Suche nach den fehlenden Zügen gehen?</summary>
public class ReconstructionGapRequest
{
    /// <summary>Halbzüge; ohne Angabe <see cref="Services.GapSolver.DefaultMaxPlies"/>, gedeckelt auf
    /// <see cref="Services.GapSolver.MaxSearchPlies"/> (der Baum wächst exponentiell).</summary>
    [Range(1, Services.GapSolver.MaxSearchPlies)]
    public int? MaxPlies { get; set; }
}

/// <summary>Ein gefundener Weg durch die Lücke.</summary>
public class ReconstructionGapSolutionDto
{
    public string San { get; set; } = string.Empty;
    public int Plies { get; set; }
}

/// <summary>Das Ergebnis der Lückensuche zu EINEM Teil.</summary>
public class ReconstructionGapResultDto
{
    /// <summary>Das Teil, VOR dem die Lücke liegt.</summary>
    public int PartId { get; set; }
    /// <summary>Stellung am Ende des vorigen Teils (null, wenn sie unbekannt ist).</summary>
    public string? FromFen { get; set; }
    /// <summary>Die gesuchte Stellung — die des Teils.</summary>
    public string? ToFen { get; set; }
    public int MaxPlies { get; set; }
    /// <summary>Besuchte Stellungen — sagt, wie teuer die Antwort war.</summary>
    public int Nodes { get; set; }
    /// <summary>Die Suche brach am Budget ab: „nicht gefunden" ist dann NICHT „gibt es nicht".</summary>
    public bool BudgetExhausted { get; set; }
    /// <summary>Warum die Liste leer ist (<c>no-previous</c>/<c>no-gap</c>/<c>target-not-a-position</c>/
    /// <c>no-anchor</c> bzw. die Gründe des <see cref="Services.GapSolver"/>).</summary>
    public string? Reason { get; set; }
    public List<ReconstructionGapSolutionDto> Solutions { get; set; } = new();
}

/// <summary>Einen gefundenen Weg übernehmen: die Züge werden VOR das Teil gesetzt.</summary>
public class ReconstructionGapApplyRequest
{
    [Required]
    [MaxLength(4000)]
    public string Moves { get; set; } = string.Empty;
}
