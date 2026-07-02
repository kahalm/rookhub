using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Ein Intervall-Eintrag der SR-Leiter: Zahlenwert + Einheit ("h" Stunden, "d" Tage,
/// "w" Wochen, "mo" Monate = 30 Tage). Wird serverseitig in Stunden umgerechnet.</summary>
public record SrLevelDto(double Value, string Unit);

/// <summary>SR-Zustand EINER Repertoire-Linie (an das Frontend geliefert). Level 0 = neu/nie geübt.</summary>
public record LineStateDto(
    string LineKey,
    int Level,
    int Reps,
    int Lapses,
    DateTime DueAt,
    DateTime? LastReviewedAt,
    bool InPool,
    bool Paused);

/// <summary>Nimmt einen Satz Linien in den Übungspool auf (Learn/manuell) — sofort fällig. Für
/// „ganzer Kurs"/„Kapitel" schickt das Frontend die jeweiligen Linien-Schlüssel.</summary>
public class PromoteLinesRequest
{
    [Required]
    public List<string> LineKeys { get; set; } = new();
}

/// <summary>Pausiert/aktiviert einen Satz Linien (Kapitel = alle seine Linien-Schlüssel).</summary>
public class SetPausedRequest
{
    [Required]
    public List<string> LineKeys { get; set; } = new();
    public bool Paused { get; set; }
}

/// <summary>Macht einen Satz Linien sofort fällig (und hebt eine Pause auf). Leere Liste = ganzer Kurs.</summary>
public class MakeDueRequest
{
    public List<string> LineKeys { get; set; } = new();
}

/// <summary>Ergebnis einer geübten Linie: korrekt (alle Züge richtig; geduldete zählen neutral) →
/// +1 Stufe, sonst zurück auf Stufe 1.</summary>
public class LineReviewRequest
{
    [Required, MaxLength(120)]
    public string LineKey { get; set; } = string.Empty;

    /// <summary>Optionales Anzeige-Label der Linie (White-Header).</summary>
    [MaxLength(120)]
    public string Label { get; set; } = string.Empty;

    public bool Correct { get; set; }
}

/// <summary>Effektive SR-Konfiguration für ein Repertoire: die tatsächlich wirksamen Intervalle
/// plus die globalen Nutzer-Defaults und der optionale pro-Repertoire-Override (null = keiner),
/// damit das Frontend beide bearbeiten kann.</summary>
public record SrConfigDto(
    List<SrLevelDto> Effective,
    List<SrLevelDto> User,
    List<SrLevelDto>? Repertoire,
    string Source);   // "repertoire" | "user" | "default"

/// <summary>Setzt Intervalle. Bei null wird der (pro-Repertoire) Override gelöscht bzw. für die
/// globale Route auf die Defaults zurückgesetzt.</summary>
public class SetSrConfigRequest
{
    public List<SrLevelDto>? Levels { get; set; }
}
