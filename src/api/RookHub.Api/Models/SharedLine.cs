using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Eine einzelne, öffentlich geteilte Repertoire-Linie. Der Besitzer erzeugt aus einer Linie
/// seines Repertoires einen Nur-Ansehen-Link (<c>/l/{ShareToken}</c>); jeder kann ihn ohne Login
/// öffnen und die Linie (Züge + Kommentare) auf einem Brett durchspielen. Die Linie wird als
/// eigenständiges PGN gespeichert (Snapshot), damit der Link unabhängig vom Original-Repertoire
/// funktioniert. Analog zum öffentlichen Partie-Link (<see cref="SavedGame"/> / <c>/g/{token}</c>).
/// </summary>
public class SharedLine
{
    public int Id { get; set; }

    /// <summary>Wer den Link erzeugt hat (Besitzer/Teiler).</summary>
    public int OwnerUserId { get; set; }
    public AppUser? Owner { get; set; }

    /// <summary>Herkunfts-Repertoire (nur informativ, kein FK — das PGN ist ein eigenständiger Snapshot).</summary>
    public int? RepertoireId { get; set; }

    /// <summary>Anzeigetitel der Linie (Eröffnungs-/Kapitelname), optional.</summary>
    [MaxLength(200)]
    public string? Title { get; set; }

    /// <summary>Name des Herkunfts-Repertoires (nur zur Anzeige auf der öffentlichen Seite), optional.</summary>
    [MaxLength(200)]
    public string? RepertoireName { get; set; }

    /// <summary>Vollständiges PGN der Linie (SAN-Züge + Kommentare + ggf. FEN-Header). LONGTEXT.</summary>
    public string Pgn { get; set; } = string.Empty;

    /// <summary>SHA-256 (hex) des PGN — Dedup je Besitzer: dieselbe Linie erneut teilen ⇒ derselbe Link.</summary>
    [MaxLength(64)]
    public string LineHash { get; set; } = string.Empty;

    /// <summary>URL-sicheres Token für den öffentlichen Link (<c>/l/{token}</c>).</summary>
    [MaxLength(32)]
    public string ShareToken { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
