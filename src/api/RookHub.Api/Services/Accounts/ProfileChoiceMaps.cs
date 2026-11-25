using System.Text.Json;
using System.Text.RegularExpressions;
using RookHub.Api.DTOs.Accounts;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Services.Accounts;

/// <summary>
/// Die gemerkten Wahlen eines Kontos, die wie die übrigen Vorlieben über alle Geräte gelten sollen: Spielweise je
/// Puzzle-Bereich (<see cref="Models.UserProfile.SolveModesJson"/>) und Sprache der Kurs-Kommentare je Kurs
/// (<see cref="Models.UserProfile.CourseLanguagesJson"/>). Beide stehen als JSON-Abbildung Schlüssel → {Wert, at}
/// am Profil.
///
/// <para>Ein PUT ERSETZT die Abbildung nicht, er wird je Schlüssel zusammengeführt: der jüngere Zeitstempel gewinnt.
/// Sonst überschriebe ein Gerät mit altem Stand (oder das Hochladen der Gerätewerte beim ersten Laden) die neuere
/// Wahl eines anderen Geräts.</para>
///
/// <para>Deckel wie im Client (<c>MAX_EINTRAEGE</c> in <c>solve-mode.service.ts</c>): 200 Einträge, die ältesten
/// fliegen zuerst. Die Grundwahlen <c>*</c> und <c>course:*</c> zählen nicht mit und fliegen nie — sonst fragten
/// alle Bereiche wieder.</para>
/// </summary>
public static class ProfileChoiceMaps
{
    public const int MaxEntries = 200;

    /// <summary>Mehr Einträge in EINER Anfrage weist der Server ab (Deckel + Grundwahlen + Luft).</summary>
    public const int MaxRequestEntries = 250;

    public const int MaxKeyLength = 40;

    /// <summary>Bereiche: <c>*</c>, <c>puzzles</c>/<c>daily</c>/<c>book</c>/<c>endless</c>, <c>course:12</c>,
    /// <c>course:*</c>, <c>weekly:3</c>, <c>woodpecker:7</c>.</summary>
    private static readonly Regex SolveScope = new(@"^(\*|[a-z]{1,20}(:(\*|[0-9]{1,10}))?)$", RegexOptions.CultureInvariant);

    /// <summary>Kurs-Schlüssel des Clients (<c>b&lt;bookId&gt;</c>); die vorläufigen <c>f&lt;Dateiname&gt;</c> bleiben am Gerät.</summary>
    private static readonly Regex CourseKey = new(@"^b[0-9]{1,10}$", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Pinned = new(StringComparer.Ordinal) { "*", "course:*" };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Gespeicherte Abbildung lesen — kaputtes JSON gilt als leer (lieber neu fragen als 500).</summary>
    public static Dictionary<string, T> Read<T>(string? json) where T : class, IStampedChoice
    {
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.Ordinal);
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, T>>(json, Json);
            return map is null ? new(StringComparer.Ordinal) : new(map.Where(kv => kv.Value is not null), StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new(StringComparer.Ordinal);
        }
    }

    public static string MergeSolveModes(string? storedJson, IReadOnlyDictionary<string, SolveModeEntryDto> incoming, long nowMs)
        => Merge(storedJson, incoming, nowMs, (key, e) =>
        {
            if (!SolveScope.IsMatch(key)) throw new ArgumentException($"Invalid solve mode scope '{Trim(key)}'.");
            if (e.Mode is not ("training" or "easy")) throw new ArgumentException("Solve mode must be 'training' or 'easy'.");
            return new SolveModeEntryDto { Mode = e.Mode, At = e.At };
        });

    public static string MergeCourseLanguages(string? storedJson, IReadOnlyDictionary<string, CourseLanguageEntryDto> incoming, long nowMs)
        => Merge(storedJson, incoming, nowMs, (key, e) =>
        {
            if (!CourseKey.IsMatch(key)) throw new ArgumentException($"Invalid course key '{Trim(key)}'.");
            // Dieselbe Form, die der Server als ?lang= annimmt: eine der 25 Oberflächensprachen ODER die Quellsprache
            // eines Kurses („Original“, z. B. Dänisch) — die ist nur auf die Form geprüft (CourseTranslationLanguages).
            var lang = CourseTranslationLanguages.Normalize(e.Lang) ?? CourseCommentLocalizer.NormalizeLanguage(e.Lang);
            if (lang is null) throw new ArgumentException("Invalid course language.");
            return new CourseLanguageEntryDto { Lang = lang, At = e.At };
        });

    private static string Merge<T>(string? storedJson, IReadOnlyDictionary<string, T> incoming, long nowMs,
        Func<string, T, T> validate) where T : class, IStampedChoice
    {
        if (incoming.Count > MaxRequestEntries) throw new ArgumentException($"At most {MaxRequestEntries} entries per request.");
        var map = Read<T>(storedJson);
        foreach (var (key, raw) in incoming)
        {
            if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength) throw new ArgumentException("Invalid choice key.");
            if (raw is null) throw new ArgumentException("Choice entry must not be null.");
            var e = validate(key, raw);
            if (e.At <= 0) throw new ArgumentException("Choice timestamp must be positive.");
            // Eine Uhr in der Zukunft (falsch gestelltes Gerät) darf die Wahl nicht für immer festnageln.
            if (e.At > nowMs + 86_400_000) e.At = nowMs;
            if (map.TryGetValue(key, out var have) && have.At > e.At) continue;   // gespeicherte ist jünger
            map[key] = e;
        }

        var counted = map.Keys.Where(k => !Pinned.Contains(k)).ToList();
        if (counted.Count > MaxEntries)
            foreach (var k in counted.OrderBy(k => map[k].At).ThenBy(k => k, StringComparer.Ordinal).Take(counted.Count - MaxEntries))
                map.Remove(k);

        return JsonSerializer.Serialize(map, Json);
    }

    private static string Trim(string key) => key.Length > MaxKeyLength ? key[..MaxKeyLength] + "…" : key;
}
