using RookHub.Api.Models;

namespace RookHub.Api.DTOs;

/// <summary>Konfiguration eines Menüeintrags (Admin-Sicht / PUT-Body).</summary>
public class MenuItemConfigDto
{
    public string Key { get; set; } = string.Empty;
    public MenuVisibilityLevel Level { get; set; }
    /// <summary>Nur bei Level=Groups relevant.</summary>
    public List<int> GroupIds { get; set; } = new();
}
