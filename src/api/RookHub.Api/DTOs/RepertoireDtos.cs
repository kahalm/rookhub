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
}

public class UpdateRepertoireDto
{
    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }
    public bool? IsPublic { get; set; }
    public RepertoireKind? Kind { get; set; }
}

public class ExtensionRepertoireDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public RepertoireKind Kind { get; set; }
    /// <summary>Summe aller File-Groessen — Hinweis fuer den Client (Soft-Limit-Warning).</summary>
    public long TotalSizeBytes { get; set; }
}
