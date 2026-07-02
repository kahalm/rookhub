using System.ComponentModel.DataAnnotations;
using RookHub.Api.Models;

namespace RookHub.Api.DTOs;

public class RepertoireDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public RepertoireKind Kind { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int FileCount { get; set; }
    public bool UseForExtension { get; set; }
    public string? ChessableCourseId { get; set; }

    /// <summary><c>true</c> = dieses Repertoire wurde von einem anderen Nutzer mit mir geteilt
    /// (ich bin nicht der Besitzer). Steuert die Sektion „Mit mir geteilt" + das „von X"-Badge.</summary>
    public bool IsShared { get; set; }

    /// <summary>Benutzername des Teilenden, wenn <see cref="IsShared"/> — für das „von X"-Badge.</summary>
    public string? SharedByUsername { get; set; }
}

/// <summary>Eingabe: mit welchen Nutzern ein Repertoire geteilt werden soll (Batch, analog Kurs).</summary>
public class ShareRepertoireInputDto
{
    [System.ComponentModel.DataAnnotations.MaxLength(50)]
    public List<int> RecipientUserIds { get; set; } = new();
}

/// <summary>Ergebnis eines Teilen-Vorgangs: wie viele neu geteilt, welche Empfänger übersprungen (+Grund).</summary>
public class RepertoireShareResultDto
{
    public int Shared { get; set; }
    public List<RepertoireShareSkipDto> Skipped { get; set; } = new();
}

/// <summary>Ein übersprungener Empfänger. <see cref="Reason"/> ∈ self / not_found / not_friends / duplicate.</summary>
public class RepertoireShareSkipDto
{
    public int UserId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Ein Nutzer, mit dem ein Repertoire aktuell geteilt ist (für die „geteilt mit"-Liste im Dialog).</summary>
public class RepertoireShareRecipientDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public DateTime SharedAt { get; set; }
}

public class RepertoireDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public RepertoireKind Kind { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<RepertoireFileDto> Files { get; set; } = new();
    public bool UseForExtension { get; set; }
    public string? ChessableCourseId { get; set; }

    /// <summary><c>true</c> = der Aufrufer ist der Besitzer (darf bearbeiten/Dateien verwalten).
    /// <c>false</c> = das Repertoire ist ihm nur geteilt (nur ansehen/herunterladen/trainieren).</summary>
    public bool IsOwner { get; set; }
}

public class RepertoireFileDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; }
}

public class CreateRepertoireDto
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public RepertoireKind Kind { get; set; } = RepertoireKind.None;
    /// <summary>Von der Extension nutzbar? Default true (bestehendes Verhalten).</summary>
    public bool UseForExtension { get; set; } = true;
    [MaxLength(32)]
    public string? ChessableCourseId { get; set; }
}

public class UpdateRepertoireDto
{
    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }
    public bool? IsPublic { get; set; }
    public RepertoireKind? Kind { get; set; }
    public bool? UseForExtension { get; set; }
    /// <summary>Setzt die Chessable-Kurs-ID. Leerer String wird als null gespeichert (Verknüpfung löschen).</summary>
    [MaxLength(32)]
    public string? ChessableCourseId { get; set; }
    public bool UpdateChessableCourseId { get; set; }
}

public class ExtensionRepertoireDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public RepertoireKind Kind { get; set; }
    /// <summary>Summe aller File-Groessen — Hinweis fuer den Client (Soft-Limit-Warning).</summary>
    public long TotalSizeBytes { get; set; }
    public string? ChessableCourseId { get; set; }
}

/// <summary>
/// Request fuer <c>POST /api/extension/analyze-game</c>: SAN-Zugliste der Hauptlinie, optional
/// gefiltert auf einen <see cref="RepertoireKind"/>. Server baut/cacht das Positions-Set des
/// Users und vergleicht ply-weise.
/// </summary>
public class AnalyzeGameRequestDto
{
    [Required]
    public List<string> Moves { get; set; } = new();
    public RepertoireKind Kind { get; set; } = RepertoireKind.Opening;
    /// <summary>Invalidate the cached position-set before analyzing (manual refresh button).</summary>
    public bool Refresh { get; set; }
}

public class AnalyzeGameResponseDto
{
    /// <summary>Index des ersten dauerhaften Ausreissers (-1 = Partie komplett im Repertoire).</summary>
    public int Deviation { get; set; } = -1;
    /// <summary>Ply-Indices von Zugumstellungen (temporaer ausserhalb, Partie kehrt zurueck).</summary>
    public List<int> Gaps { get; set; } = new();
    /// <summary>Ply-Indices, deren resultierende Position im Repertoire steht.</summary>
    public List<int> InRepertoire { get; set; } = new();
    /// <summary>FEN VOR dem Out-of-Rep-Zug (fuer Chessable-Suche). Null wenn keine Abweichung.</summary>
    public string? FenBeforeDeviation { get; set; }
    /// <summary>Wie viele Repertoire-Dateien zur Position-Set-Berechnung beigetragen haben.</summary>
    public int RepertoireFileCount { get; set; }
    /// <summary>Ply, bei dem ein Zug nicht parsbar war (illegale SAN). Null = alle Zuege OK.</summary>
    public int? IllegalMoveAt { get; set; }
}

/// <summary>Eingabe fuer „Remember line": eine auf chessable.com gemerkte Stellung
/// (<c>POST /api/extension/remember-line</c>). Zeitstempel wird serverseitig gesetzt.</summary>
public class RememberLineInputDto
{
    [Required]
    [MaxLength(120)]
    public string Fen { get; set; } = string.Empty;

    [MaxLength(32)]
    public string? CourseId { get; set; }

    /// <summary>Optionaler, von der Extension über den Chessable-Bearer aufgelöster Kursname.
    /// Fehlt er, löst der Server ihn (falls möglich) aus dem gespeicherten Bearer des Users auf.</summary>
    [MaxLength(200)]
    public string? CourseName { get; set; }

    [MaxLength(1000)]
    public string? SourceUrl { get; set; }
}

/// <summary>Eine gemerkte Stellung (Ausgabe von <c>GET /api/extension/remembered-lines</c>).</summary>
public class RememberedPositionDto
{
    public int Id { get; set; }
    public string Fen { get; set; } = string.Empty;
    public string? CourseId { get; set; }
    public string? CourseName { get; set; }
    public string? SourceUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}
