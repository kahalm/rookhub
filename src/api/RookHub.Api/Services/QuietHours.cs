using System.Globalization;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Sperrzeiten der Spark (0.546.0): in diesen Fenstern schickt RookHub dem Sprachmodell auf eigener Hardware KEINE
/// Hintergrundarbeit — die Maschine gehört dann anderen (Wunsch des Nutzers, 2026-09-26: „übersetzungen auf der spark
/// sollen nicht laufen: MO-DO von 8:00 - 17:00 und freitag von 8:00- 14:00", und für die Texte zur Partie „gleiches
/// zeitfenster"). Gilt für Nacherzählung, Fehler-Erklärungen und Roasts (<see cref="GameReviewTextScheduler"/>, die Knöpfe
/// sagen ab) und für die Kurs-Übersetzung (Stufe B). Der Bibliothekslauf hält dieselben Fenster außerhalb der App ein
/// (Schaltuhr <c>.jobs/spark-uebersetzung.sh</c>).
///
/// <para><b>Angabe</b> <c>TextLlm:QuietHours</c>: Fenster durch <c>;</c> getrennt, je Fenster Tage und Uhrzeiten —
/// <c>"Mon-Thu 08:00-17:00; Fri 08:00-14:00"</c> (Vorgabe). Tage als Bereich (<c>Mon-Thu</c>), Liste (<c>Sat,Sun</c>) oder
/// einzeln; das Ende ist ausschließlich (17:00 ist frei). Ein Fenster endet am selben Tag — über Mitternacht geht keines.
/// LEER heißt „nie gesperrt". Die Uhrzeit gilt in <c>TextLlm:TimeZone</c> (Vorgabe <c>Europe/Vienna</c>): der Server
/// läuft in UTC, und die Sommerzeit verschöbe die Fenster sonst zweimal im Jahr um eine Stunde.</para>
/// </summary>
public sealed class QuietHours
{
    public const string DefaultSpec = "Mon-Thu 08:00-17:00; Fri 08:00-14:00";
    public const string DefaultTimeZone = "Europe/Vienna";

    public sealed record Window(DayOfWeek Day, TimeSpan From, TimeSpan To);

    private readonly IReadOnlyList<Window> _windows;
    private readonly TimeZoneInfo _zone;
    private readonly TimeProvider _time;

    /// <summary>Aus der Konfiguration; eine unlesbare Angabe ist ein Konfigurationsfehler und bricht beim ersten Gebrauch
    /// mit Klartext ab — still „nie gesperrt" hieße, die Spark läuft genau dann, wenn sie es nicht soll.</summary>
    public QuietHours(IConfiguration config, TimeProvider? time = null)
        : this(config["TextLlm:QuietHours"] ?? DefaultSpec, config["TextLlm:TimeZone"], time) { }

    public QuietHours(string? spec, string? timeZone = null, TimeProvider? time = null)
    {
        _windows = Parse(spec);
        _zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZone) ? DefaultTimeZone : timeZone.Trim());
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Es gibt überhaupt ein Fenster.</summary>
    public bool Enabled => _windows.Count > 0;

    public IReadOnlyList<Window> Windows => _windows;

    /// <summary>Die Uhr, nach der gesperrt wird (Tests setzen eine eigene).</summary>
    public DateTimeOffset Now => _time.GetUtcNow();

    public bool IsQuietNow() => IsQuiet(_time.GetUtcNow());

    /// <summary>Wann die Sperre, die JETZT gilt, endet — <c>null</c>, wenn gerade keine gilt.</summary>
    public DateTimeOffset? QuietUntil()
    {
        var now = _time.GetUtcNow();
        return IsQuiet(now) ? EndOf(now) : null;
    }

    public bool IsQuiet(DateTimeOffset at) => Containing(TimeZoneInfo.ConvertTime(at, _zone)) != null;

    /// <summary>Das Ende des Fensters, in dem <paramref name="at"/> liegt — schließen Fenster lückenlos aneinander an, das
    /// Ende des letzten; <paramref name="at"/> selbst, wenn es nicht gesperrt ist.</summary>
    public DateTimeOffset EndOf(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, _zone);
        // Höchstens eine Woche weit — mehr Fenster als Wochentage kann keine Kette haben.
        for (var guard = 0; guard < 8 && Containing(local) is { } window; guard++)
        {
            var end = local.Date + window.To;
            local = new DateTimeOffset(end, _zone.GetUtcOffset(end));
        }
        return local.ToOffset(at.Offset);
    }

    private Window? Containing(DateTimeOffset local)
    {
        var time = local.TimeOfDay;
        foreach (var w in _windows)
            if (w.Day == local.DayOfWeek && time >= w.From && time < w.To) return w;
        return null;
    }

    // ── Angabe lesen ──────────────────────────────────────────────────────────────────────────────

    private static readonly string[] DayNames = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };
    private static readonly Regex Item = new(@"^\s*(?<days>[A-Za-z,\-\s]+?)\s+(?<from>\d{1,2}:\d{2})\s*-\s*(?<to>\d{1,2}:\d{2})\s*$",
        RegexOptions.Compiled);

    /// <summary>Die Fenster aus der Angabe; leer = keine. <see cref="FormatException"/> bei allem, was nicht eindeutig ist.</summary>
    public static IReadOnlyList<Window> Parse(string? spec)
    {
        var windows = new List<Window>();
        if (string.IsNullOrWhiteSpace(spec)) return windows;
        foreach (var raw in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = Item.Match(raw);
            if (!m.Success) throw new FormatException($"TextLlm:QuietHours: „{raw}\" ist kein Fenster wie „Mon-Thu 08:00-17:00\".");
            var from = Time(m.Groups["from"].Value, raw);
            var to = Time(m.Groups["to"].Value, raw);
            if (to <= from) throw new FormatException($"TextLlm:QuietHours: „{raw}\" endet nicht nach dem Beginn (über Mitternacht geht kein Fenster).");
            foreach (var day in Days(m.Groups["days"].Value, raw)) windows.Add(new Window(day, from, to));
        }
        return windows;
    }

    private static TimeSpan Time(string text, string raw)
    {
        if (TimeSpan.TryParseExact(text, @"h\:mm", CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1)) return t;
        throw new FormatException($"TextLlm:QuietHours: „{text}\" in „{raw}\" ist keine Uhrzeit.");
    }

    private static IEnumerable<DayOfWeek> Days(string text, string raw)
    {
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-', StringSplitOptions.TrimEntries);
            if (range.Length > 2) throw new FormatException($"TextLlm:QuietHours: „{part}\" in „{raw}\" ist kein Tagesbereich.");
            var first = Day(range[0], raw);
            var last = range.Length == 2 ? Day(range[1], raw) : first;
            // Mo-Do, auch über das Wochenende hinweg (Fri-Mon).
            for (var d = (int)first; ; d = (d + 1) % 7)
            {
                yield return (DayOfWeek)d;
                if (d == (int)last) break;
            }
        }
    }

    private static DayOfWeek Day(string text, string raw)
    {
        var key = text.Trim().ToLowerInvariant();
        var i = key.Length >= 3 ? Array.IndexOf(DayNames, key[..3]) : -1;
        if (i < 0) throw new FormatException($"TextLlm:QuietHours: „{text}\" in „{raw}\" ist kein Wochentag (Mon, Tue, …).");
        return (DayOfWeek)i;
    }
}
