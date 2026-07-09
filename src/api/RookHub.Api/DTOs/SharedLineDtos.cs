using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Anfrage zum Teilen einer Repertoire-Linie (der Client liefert das fertige Linien-PGN).</summary>
public class ShareLineInputDto
{
    /// <summary>Vollständiges PGN der Linie (SAN-Züge + Kommentare + ggf. FEN-Header).</summary>
    [Required]
    public string Pgn { get; set; } = string.Empty;

    /// <summary>Anzeigetitel (Eröffnungs-/Kapitelname), optional.</summary>
    [MaxLength(200)]
    public string? Title { get; set; }
}

/// <summary>Antwort nach dem Teilen: das Token für den öffentlichen Link <c>/l/{token}</c>.</summary>
public class ShareLineResultDto
{
    public string ShareToken { get; set; } = string.Empty;
}

/// <summary>Extension-Anfrage: teilt die aktuell gespielte Zugfolge (SAN, ab Grundstellung) als Line.</summary>
public class ShareExtensionLineInputDto
{
    public List<string> Moves { get; set; } = new();

    [MaxLength(200)]
    public string? Title { get; set; }
}

/// <summary>Öffentliche Sicht einer geteilten Linie (kein Login nötig).</summary>
public class SharedLineDto
{
    public string ShareToken { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? RepertoireName { get; set; }
    public string Pgn { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
