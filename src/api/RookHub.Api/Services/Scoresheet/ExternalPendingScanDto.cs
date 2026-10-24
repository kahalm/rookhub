namespace RookHub.Api.Services.Scoresheet;

/// <summary>Eine wartende Einlesung für den Leser von außen (<c>GET /api/admin/scoresheets/pending</c>).</summary>
public sealed class ExternalPendingScanDto
{
    public int Id { get; set; }
    /// <summary><c>own</c> (RookHub, legt eine Partie in „Meine Partien" an) oder <c>league</c> (LeagueHub).</summary>
    public string Purpose { get; set; } = "own";
    public int? UserId { get; set; }
    public bool Anonymous { get; set; }
    public int PageCount { get; set; }
    /// <summary>Weitere Fotos derselben Seiten zusammen (0.736.0); <c>GET …/{id}/photo?page=&amp;view=</c>.</summary>
    public int ViewCount { get; set; }
    public string NotationLanguage { get; set; } = "auto";
    public string? OwnerSide { get; set; }
    public DateTime CreatedAt { get; set; }
}
