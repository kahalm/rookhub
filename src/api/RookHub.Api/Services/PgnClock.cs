using System.Globalization;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Restzeit auf der Uhr als PGN-Auszeichnung <c>[%clk h:mm:ss]</c> (Lichess-/ChessBase-Schreibweise): schreiben, lesen und
/// je Halbzug der Hauptvariante einsammeln. Querschnitt: die Formular-Einlesung schreibt sie (handschriftlich notierte
/// Restzeiten, 1.015.0), die Vereins-Datenbank behält sie beim Neuschreiben des PGN, die Live-Bretter lesen sie.
/// </summary>
public static partial class PgnClock
{
    /// <summary>Sekunden → <c>h:mm:ss</c> (Stunden ohne führende Null, wie Lichess).</summary>
    public static string Format(int seconds)
    {
        var s = Math.Max(0, seconds);
        return string.Create(CultureInfo.InvariantCulture, $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}");
    }

    /// <summary>Die Auszeichnung selbst: <c>[%clk 1:12:00]</c>.</summary>
    public static string Tag(int seconds) => $"[%clk {Format(seconds)}]";

    /// <summary><c>[%clk 1:30:59]</c> (auch „1:30:59.5") in einem Kommentar → Sekunden; <c>null</c> = keine.</summary>
    public static int? SecondsOf(string? comment)
    {
        if (string.IsNullOrEmpty(comment)) return null;
        var m = ClockRe().Match(comment);
        if (!m.Success) return null;
        return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
               + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 60
               + int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Ein Kommentar ohne seine <c>[%clk]</c>-Auszeichnungen (Leerraum geglättet).</summary>
    public static string Strip(string? comment)
        => string.Join(' ', ClockRe().Replace(comment ?? string.Empty, " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Kommentar und Uhr zusammen, die Uhr vorn: <c>[%clk 1:12:00] sheet: Sf3</c>. Eine Uhr, die schon im Kommentar
    /// steht, wird ersetzt; ohne Uhr bleibt der Kommentar wie er ist.</summary>
    public static string? Combine(int? seconds, string? comment)
    {
        if (seconds is not int s) return string.IsNullOrWhiteSpace(comment) ? null : comment;
        var rest = Strip(comment);
        return rest.Length == 0 ? Tag(s) : $"{Tag(s)} {rest}";
    }

    /// <summary>Je Halbzug der Hauptvariante die Uhr aus dem Kommentar direkt dahinter (<c>null</c> = keine) — Varianten,
    /// NAGs und Zugnummern fallen weg (derselbe Leser wie bei den Live-Brettern).</summary>
    public static List<int?> Mainline(string? moveText)
        => Tournaments.Live.LiveGameParser.Scan(moveText ?? string.Empty).Clocks;

    /// <summary>Wie <see cref="Mainline"/>, aber nur, wenn der Leser genau <paramref name="plyCount"/> Halbzüge fand — sonst
    /// könnten die Uhren gegen die Züge verrutscht sein, und es gibt keine (<c>null</c>).</summary>
    public static List<int?>? Aligned(string? moveText, int plyCount)
    {
        var (sans, clocks, _) = Tournaments.Live.LiveGameParser.Scan(moveText ?? string.Empty);
        return sans.Count == plyCount ? clocks : null;
    }

    [GeneratedRegex(@"\[%clk\s+(\d+):(\d{1,2}):(\d{1,2})(?:\.\d+)?\]")]
    private static partial Regex ClockRe();
}
