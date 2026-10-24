using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Vermerk „diese Discord-ID ist frei geworden" (Codereview A1-011, <see cref="Services.Social.DiscordLinkService"/>):
/// Bot-Tokens, die bis <see cref="ReleasedAt"/> ausgestellt wurden, löst nur noch das bisherige Konto ein. Eine Zeile
/// je Discord-ID; ein neueres Freiwerden überschreibt sie. Abgelaufen ist der Vermerk, sobald kein damals
/// ausgestelltes Token mehr gilt (<c>ReleasedAt + Discord:LinkTokenMaxAgeDays</c>) — dann räumt ihn das nächste
/// Freiwerden ab.
/// </summary>
public class DiscordLinkRelease
{
    /// <summary>Discord-User-ID (Snowflake), Schlüssel — Länge wie <see cref="UserProfile.DiscordId"/>.</summary>
    [MaxLength(32)]
    public string DiscordId { get; set; } = string.Empty;

    /// <summary>Konto, dem die ID bis zum Freiwerden gehörte. FK mit <c>SetNull</c>: verschwände das Konto hart,
    /// bleibt der Vermerk stehen und gilt dann für JEDES Konto (null = niemand darf die alten Tokens einlösen).</summary>
    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Zeitpunkt des Freiwerdens (UTC).</summary>
    public DateTime ReleasedAt { get; set; }
}
