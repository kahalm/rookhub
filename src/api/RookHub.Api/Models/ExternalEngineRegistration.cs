using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Eine Engine, die sich DIREKT bei RookHub angemeldet hat („RookHub direkt", Kennung <c>rhe_…</c>) —
/// ohne Lichess dazwischen. Angelegt und aktualisiert vom offiziellen Lichess-Provider
/// (<c>engine-provider/</c>) über dieselben Endpunkte, die er bei Lichess benutzt
/// (<c>GET/POST/PUT /api/external-engine</c>, Bearer = RookHub-API-Token mit Scope <c>engine</c>).
///
/// <para><b>Der Name ist die Identität.</b> Der Provider sucht beim Start in der Liste nach seinem
/// Namen und aktualisiert diesen Eintrag (PUT), sonst legt er einen neuen an (POST) — deshalb
/// UNIQUE (UserId, Name). Zwei Rechner mit demselben Namen überschreiben sich gegenseitig.</para>
///
/// <para><b>Gespeichert wird nicht das <c>providerSecret</c></b>, sondern sein Selector
/// (<c>sha256("providerSecret:" + secret)</c> als Hex, wie lila-engine <c>ProviderSecret::selector</c>).
/// Der Provider wechselt das Secret bei jedem Start (sofern <c>PROVIDER_SECRET</c> nicht gesetzt ist);
/// über den Selector findet der Broker die Warteschlange, aus der er Arbeit holt.</para>
/// </summary>
public class ExternalEngineRegistration
{
    public const string IdPrefix = "rhe_";
    public const int IdRandomLength = 12;

    /// <summary><c>rhe_</c> + 12 Zeichen <c>[A-Za-z0-9]</c> — so lang wie Lichess' <c>eei_…</c>, damit die
    /// CSV-Spalte <see cref="LichessEngineCredential.BackgroundEngineIds"/> weiterhin 16 davon fasst.</summary>
    [Key, MaxLength(20)]
    public string Id { get; set; } = string.Empty;

    /// <summary>Besitzer; Cascade mit dem User (die Konto-Löschung räumt zusätzlich selbst ab, weil sie
    /// die AppUser-Zeile anonymisiert statt sie zu löschen).</summary>
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Zufallswert (32 Byte, base64url). Wird nur zum Besitzer ausgeliefert (Protokoll-Treue:
    /// Lichess liefert es mit); der Broker selbst braucht es nicht, weil der einzige Anfragende die
    /// RookHub-API ist (in-Prozess).</summary>
    [Required, MaxLength(64)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary><c>sha256("providerSecret:" + secret)</c>, 64 Hex-Zeichen (Index).</summary>
    [Required, MaxLength(64)]
    public string ProviderSelector { get; set; } = string.Empty;

    public int MaxThreads { get; set; }

    /// <summary>MiB.</summary>
    public int MaxHash { get; set; }

    /// <summary>Kommagetrennt, z. B. <c>chess</c>. Angeboten wird nur <c>chess</c>.</summary>
    [Required, MaxLength(200)]
    public string Variants { get; set; } = "chess";

    [MaxLength(500)]
    public string? ProviderData { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Letzter Abruf des Providers (Long-Poll) — im Speicher sekundengenau, hier höchstens
    /// minütlich nachgetragen. Daraus der Online-Punkt, auch nach einem API-Neustart.</summary>
    public DateTime? LastSeenAt { get; set; }

    public IReadOnlyList<string> VariantList =>
        Variants.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
