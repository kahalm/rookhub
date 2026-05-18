using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RookHub.Api.Models;

public class RepertoireFile
{
    public int Id { get; set; }

    public int RepertoireId { get; set; }
    public Repertoire Repertoire { get; set; } = null!;

    [Required, MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Column(TypeName = "LONGTEXT")]
    public string PgnContent { get; set; } = string.Empty;

    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
