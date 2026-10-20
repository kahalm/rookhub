namespace RookHub.Api.Models;

/// <summary>
/// Eine anwendungsweite Einstellung, die ein Admin zur Laufzeit ändert und die einen Neustart übersteht
/// (Schlüssel → Wert). Erster Nutzer: der Schalter „Hintergrundjobs pausieren"
/// (<see cref="Services.BackgroundPause"/>, Schlüssel <see cref="Services.BackgroundPause.SettingKey"/>).
/// Bewusst schlicht: Wert als Text, dazu wann und von wem zuletzt geändert — das zeigt die Admin-Seite an.
/// </summary>
public class SystemSetting
{
    /// <summary>Schlüssel, z. B. <c>BackgroundJobs.Paused</c> (PK, höchstens 100 Zeichen).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Wert als Text (bool als <c>true</c>/<c>false</c>).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Zeitpunkt der letzten Änderung (UTC).</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Wer zuletzt geändert hat (Konto), <c>null</c> bei einer Änderung ohne Konto.</summary>
    public int? UpdatedByUserId { get; set; }

    /// <summary>Anzeigename des Ändernden zum Zeitpunkt der Änderung — bleibt auch nach Umbenennen/Löschen lesbar.</summary>
    public string? UpdatedByName { get; set; }
}
