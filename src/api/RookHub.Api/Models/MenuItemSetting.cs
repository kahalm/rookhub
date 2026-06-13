using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Sichtbarkeitsstufe eines Navigations-/Menüeintrags. Reihenfolge = absteigende Offenheit.
/// </summary>
public enum MenuVisibilityLevel
{
    /// <summary>Für jeden sichtbar, auch nicht eingeloggte Besucher.</summary>
    All = 0,
    /// <summary>Nur für eingeloggte (registrierte) Nutzer.</summary>
    Registered = 1,
    /// <summary>Nur für Mitglieder bestimmter Gruppen (Admins immer).</summary>
    Groups = 2,
    /// <summary>Nur für Admins.</summary>
    Admin = 3,
}

/// <summary>
/// Admin-konfigurierbare Sichtbarkeit eines einzelnen Menüeintrags (z. B. "repertoires").
/// Fehlt eine Zeile, gilt der Default aus <see cref="Services.MenuRegistry"/>.
/// </summary>
public class MenuItemSetting
{
    [MaxLength(50)]
    public string ItemKey { get; set; } = string.Empty;

    public MenuVisibilityLevel Level { get; set; }

    /// <summary>Nur relevant bei <see cref="MenuVisibilityLevel.Groups"/>: freigegebene Gruppen.</summary>
    public List<MenuItemGroupAccess> Groups { get; set; } = new();
}

/// <summary>Join: welche Gruppe darf einen gruppen-gegateten Menüeintrag sehen.</summary>
public class MenuItemGroupAccess
{
    [MaxLength(50)]
    public string ItemKey { get; set; } = string.Empty;
    public MenuItemSetting? Setting { get; set; }

    public int GroupId { get; set; }
    public Group? Group { get; set; }
}
