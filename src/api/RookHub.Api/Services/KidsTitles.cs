using System.Text.Json;

namespace RookHub.Api.Services;

/// <summary>
/// Eigene Kurstitel fuer KidHub (<see cref="Models.Book.KidsTitles"/>) — kindgerecht und je Sprache
/// der Kinderseite, ohne den Buchnamen in RookHub anzufassen („Learn Chess the Right Way – Book 1:
/// Must-know Checkmates" heisst dort „Matt in einem Zug"). Gespeichert als JSON <c>{"de":"…","en":"…"}</c>.
/// </summary>
public static class KidsTitles
{
    /// <summary>Die Sprachen der Kinderseite (Spiegel von <c>KIDS_LANGUAGES</c> in <c>src-kidhub/app/app.component.ts</c>).</summary>
    public static readonly string[] Languages = { "de", "en", "hr", "hu" };
    public const int MaxLength = 120;

    public static Dictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    /// <summary>Titel in der Sprache, sonst Englisch, sonst Deutsch, sonst der Buchname — wer eine
    /// Sprache ohne eigenen Titel spricht, versteht Englisch eher (dieselbe Regel wie die Startsprache).</summary>
    public static string Pick(string? json, string? lang, string fallback)
    {
        var titles = Parse(json);
        foreach (var key in new[] { lang?.Trim().ToLowerInvariant(), "en", "de" })
            if (key is not null && titles.TryGetValue(key, out var t) && !string.IsNullOrWhiteSpace(t)) return t;
        return fallback;
    }

    /// <summary>Eingabe aus der Bücherverwaltung → gespeichertes JSON (<c>null</c> = keine eigenen Titel).
    /// Leere Einträge fallen weg; eine fremde Sprache oder ein zu langer Titel ist ein Fehler.</summary>
    public static string? Normalize(IDictionary<string, string?> input)
    {
        var clean = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (rawKey, rawValue) in input)
        {
            var key = rawKey.Trim().ToLowerInvariant();
            if (Array.IndexOf(Languages, key) < 0)
                throw new ArgumentException($"Unknown KidHub language '{rawKey}' (allowed: {string.Join(", ", Languages)}).");
            var value = rawValue?.Trim();
            if (string.IsNullOrEmpty(value)) continue;
            if (value.Length > MaxLength) throw new ArgumentException($"KidHub title longer than {MaxLength} characters.");
            clean[key] = value;
        }
        return clean.Count == 0 ? null : JsonSerializer.Serialize(clean);
    }
}
