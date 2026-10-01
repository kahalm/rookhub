using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace RookHub.Api.Services;

/// <summary>
/// Die Kleinhelfer fuer Crawler-Antworten, die vorher je Datei kopiert waren (Codereview A5-013) —
/// und nicht gleich: zwei der acht Kuerzungs-Kopien machten aus <c>null</c> einen Leerstring.
/// chess-results-Eintraege speicherten fehlende Veranstalter/Turnierleiter/Bundeslaender deshalb als
/// <c>''</c>, Verbandseintraege als <c>NULL</c>.
/// </summary>
public static class DirectoryText
{
    /// <summary>
    /// Auf <paramref name="max"/> Zeichen kuerzen. <c>null</c> BLEIBT <c>null</c> — „fehlt" ist eine
    /// andere Auskunft als „leer", und beide Quellen-Arten speichern so dasselbe.
    /// </summary>
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];

    /// <summary>
    /// Ein Datum aus einer Kalender-Antwort des Crawlers (dort ISO <c>yyyy-MM-dd</c>), nachsichtig
    /// mit invarianter Kultur gelesen; unlesbar oder leer → <c>null</c>.
    ///
    /// <para>Nicht fuer chess-results-Rohtexte (<c>yyyy/MM/dd</c>, <c>dd.MM.yyyy</c>): dafuer haben
    /// Turnierverlauf und Turniersuche ihre festen Formate, damit kein Tag als Monat gelesen wird.</para>
    /// </summary>
    public static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var d) ? d : null;
}
