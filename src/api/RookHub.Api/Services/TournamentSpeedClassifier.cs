using System.Globalization;
using System.Text.RegularExpressions;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// chess-results liefert die Bedenkzeit nur als Freitext ("90 min/40 moves + 30 sec",
/// "3min + 2sek/Zug", "1 Std."), keine Kategorie. Die Suche haette zwar einen Bedenkzeit-Filter,
/// aber den zu nutzen hiesse drei Abfragen statt einer pro Foederation - das dreifache an Last bei
/// chess-results fuer eine Angabe, die sich aus dem Text herleiten laesst.
///
/// Eingeordnet wird nach der FIDE-Formel: Gesamtzeit = Grundzeit + Inkrement (60 Zuege x n Sekunden
/// sind n Minuten). Unter 10 Minuten Blitz, bis 60 Schnellschach, darueber Standard.
/// </summary>
public static class TournamentSpeedClassifier
{
    private static readonly Regex HoursPattern = new(
        @"(\d+)\s*(?:h\b|std|stunden|stunde)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MinutesPattern = new(
        @"(\d+)\s*(?:min\b|min\.|minutes|minuten|minute)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IncrementPattern = new(
        @"(\d+)\s*(?:sec|sek)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TournamentSpeed Classify(string? timeControlText)
    {
        var total = TotalMinutes(timeControlText);
        if (total is null) return TournamentSpeed.Unknown;
        if (total < 10) return TournamentSpeed.Blitz;
        return total <= 60 ? TournamentSpeed.Rapid : TournamentSpeed.Standard;
    }

    /// <summary>
    /// Grundzeit + Inkrement in Minuten, oder null wenn im Text keine Grundzeit steht.
    /// Bei mehreren Zeitangaben zaehlt die ERSTE - "90 min/40 Zuege + 30 min Rest" ist ein
    /// 90-Minuten-Turnier mit Zusatzphase, kein 30-Minuten-Turnier.
    /// </summary>
    internal static int? TotalMinutes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        int? baseMinutes = null;
        var minuteMatch = MinutesPattern.Match(text);
        if (minuteMatch.Success && int.TryParse(minuteMatch.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var minutes))
        {
            baseMinutes = minutes;
        }
        else
        {
            var hourMatch = HoursPattern.Match(text);
            if (hourMatch.Success && int.TryParse(hourMatch.Groups[1].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var hours))
            {
                baseMinutes = hours * 60;
            }
        }

        if (baseMinutes is null) return null;

        var increment = 0;
        var incrementMatch = IncrementPattern.Match(text);
        if (incrementMatch.Success && int.TryParse(incrementMatch.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var seconds))
        {
            increment = seconds;
        }

        // Absurde Angaben (Tippfehler, "1000 min") nicht durchreichen - lieber Unknown.
        var total = baseMinutes.Value + increment;
        return total is > 0 and <= 600 ? total : null;
    }
}
