using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Listen-Repraesentation eines API-Tokens — ohne den Raw-Token (gibt es nur einmal beim Anlegen).</summary>
public class ApiTokenDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Erste 12 Zeichen inkl. <c>rkh_</c>-Prefix — z. B. <c>rkh_abcdefgh</c>.</summary>
    public string Prefix { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Response beim Anlegen — enthaelt einmalig den Raw-Token.</summary>
public class ApiTokenCreatedDto : ApiTokenDto
{
    /// <summary>Der vollstaendige Raw-Token. Wird NUR beim Anlegen geliefert; danach ist nur der Hash gespeichert.</summary>
    public string RawToken { get; set; } = string.Empty;
}

public class CreateApiTokenDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    /// <summary>Optionaler Ablauf in Tagen ab jetzt (NULL = laeuft nie ab).</summary>
    [Range(1, 36500)]
    public int? ExpiresInDays { get; set; }
    /// <summary>Aktuell unterstuetzt: nur <c>extension</c> (read-only auf /api/extension/*).</summary>
    [MaxLength(50)]
    public string? Scope { get; set; }
}
