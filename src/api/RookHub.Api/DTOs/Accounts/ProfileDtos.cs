using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs.Accounts;

public class ProfileDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? FideId { get; set; }
    public string? ChessResultsId { get; set; }
    public string? ChessComUsername { get; set; }
    public string? LichessUsername { get; set; }

    // Discord-Verknüpfung (Username zur Anzeige; ID ist die eindeutige Identität)
    public string? DiscordId { get; set; }
    public string? DiscordUsername { get; set; }

    // User Preferences
    public string? BoardTheme { get; set; }
    public string? PieceSet { get; set; }
    public int? StockfishDepth { get; set; }
    public string? PuzzleDifficulty { get; set; }
    public int? BookStockfishDepth { get; set; }

    /// <summary>Spielweise je Puzzle-Bereich (Grundwahl <c>*</c>, Kurs-Grundwahl <c>course:*</c>, <c>course:12</c> …).</summary>
    public Dictionary<string, SolveModeEntryDto>? SolveModes { get; set; }

    /// <summary>Sprachwahl der Kurs-Kommentare je Kurs (<c>b12</c> → Sprachkürzel).</summary>
    public Dictionary<string, CourseLanguageEntryDto>? CourseLanguages { get; set; }

    /// <summary>Anzeige-Vorlieben (Speicher-Schlüssel der Oberfläche → roher Wert), nur Schlüssel aus <c>UiPrefKeys</c>.</summary>
    public Dictionary<string, UiPrefEntryDto>? UiPrefs { get; set; }
}

public class UiPrefEntryDto : IStampedChoice
{
    /// <summary>Der Wert, wie er im localStorage steht (Zeichenkette, oft JSON), höchstens 2 000 Zeichen.</summary>
    public string V { get; set; } = string.Empty;
    public long At { get; set; }
}

/// <summary>Eine gemerkte Wahl mit Zeitstempel (ms seit 1970, vom Gerät) — bei Konflikt gewinnt der jüngere.</summary>
public interface IStampedChoice
{
    long At { get; set; }
}

public class SolveModeEntryDto : IStampedChoice
{
    /// <summary><c>training</c> oder <c>easy</c>.</summary>
    public string Mode { get; set; } = string.Empty;
    public long At { get; set; }
}

public class CourseLanguageEntryDto : IStampedChoice
{
    /// <summary>Sprachkürzel (auch die Quellsprache eines Kurses = „Original“).</summary>
    public string Lang { get; set; } = string.Empty;
    public long At { get; set; }
}

public class UpdateProfileDto
{
    /// <summary>
    /// E-Mail-Adresse. <c>null</c> = unverändert lassen (z. B. reine Einstellungs-Saves).
    /// Leerer String = hinterlegte E-Mail entfernen. Format-/Dublettenprüfung im Service.
    /// </summary>
    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>
    /// Aktuelles Passwort — Pflicht, sobald sich die E-Mail TATSÄCHLICH ändert (auch Erst-Setzen
    /// und Entfernen): sie ist der Reset-Anker, ohne Nachfrage wurde jede kurz erbeutete Sitzung
    /// per „Passwort vergessen" zur dauerhaften Kontoübernahme. Bei unveränderter E-Mail ignoriert.
    /// </summary>
    [MaxLength(1024)]   // wie die Passwort-Obergrenze der Registrierung
    public string? CurrentPassword { get; set; }

    [MaxLength(50)]
    public string? FirstName { get; set; }

    [MaxLength(50)]
    public string? LastName { get; set; }

    [MaxLength(50)]
    public string? DisplayName { get; set; }

    [MaxLength(20), RegularExpression(@"^\d*$", ErrorMessage = "FideId must be numeric.")]
    public string? FideId { get; set; }

    [MaxLength(20), RegularExpression(@"^\d*$", ErrorMessage = "ChessResultsId must be numeric.")]
    public string? ChessResultsId { get; set; }

    [MaxLength(50), RegularExpression(@"^[a-zA-Z0-9_-]*$", ErrorMessage = "ChessComUsername may only contain letters, digits, hyphens and underscores.")]
    public string? ChessComUsername { get; set; }

    [MaxLength(50), RegularExpression(@"^[a-zA-Z0-9_-]*$", ErrorMessage = "LichessUsername may only contain letters, digits, hyphens and underscores.")]
    public string? LichessUsername { get; set; }

    // User Preferences
    [MaxLength(20)]
    public string? BoardTheme { get; set; }

    [MaxLength(20)]
    public string? PieceSet { get; set; }

    [Range(1, 24)]
    public int? StockfishDepth { get; set; }

    [MaxLength(20)]
    public string? PuzzleDifficulty { get; set; }

    [Range(1, 24)]
    public int? BookStockfishDepth { get; set; }

    /// <summary>Geänderte Spielweisen (null = unverändert). Wird je Bereich mit dem Gespeicherten ZUSAMMENGEFÜHRT,
    /// der jüngere Eintrag gewinnt — ein Gerät überschreibt so nie die neuere Wahl eines anderen.</summary>
    public Dictionary<string, SolveModeEntryDto>? SolveModes { get; set; }

    /// <summary>Geänderte Kurs-Sprachen (null = unverändert), zusammengeführt wie <see cref="SolveModes"/>.</summary>
    public Dictionary<string, CourseLanguageEntryDto>? CourseLanguages { get; set; }

    /// <summary>Geänderte Anzeige-Vorlieben (null = unverändert), zusammengeführt wie <see cref="SolveModes"/>.</summary>
    public Dictionary<string, UiPrefEntryDto>? UiPrefs { get; set; }
}

public class LinkDiscordDto
{
    [Required]
    public string Token { get; set; } = string.Empty;
}

/// <summary>Bestätigung für das Löschen des eigenen Accounts — verlangt das aktuelle Passwort.</summary>
public class DeleteAccountDto
{
    [Required]
    public string Password { get; set; } = string.Empty;
}
