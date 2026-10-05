namespace RookHub.Api.Services;

/// <summary>
/// Eigene Tags einer gespeicherten Partie (0.662.0): Freitext, höchstens <see cref="MaxTags"/> je Partie, jedes
/// höchstens <see cref="MaxTagLength"/> Zeichen. Gespeichert in EINER Spalte, durch Zeilenumbruch getrennt — Tags
/// enthalten selbst nie einen. Gleiche Tags (ohne Groß-/Kleinschreibung) zählen einmal; die erste Schreibweise gilt.
/// </summary>
public static class GameTags
{
    public const int MaxTags = 10;
    public const int MaxTagLength = 30;
    public const int MaxStoredLength = MaxTags * (MaxTagLength + 1);

    /// <summary>Bereinigt die Eingabe: getrimmt, Zeilenumbrüche und Kommas als Leerzeichen, gekürzt, ohne Doppelte, leere fallen weg.</summary>
    public static List<string> Clean(IEnumerable<string?>? raw)
    {
        var result = new List<string>();
        foreach (var item in raw ?? Array.Empty<string?>())
        {
            var t = string.Join(' ', (item ?? "").Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
            if (t.Length > MaxTagLength) t = t[..MaxTagLength].TrimEnd();
            if (t.Length == 0 || result.Contains(t, StringComparer.OrdinalIgnoreCase)) continue;
            result.Add(t);
            if (result.Count == MaxTags) break;
        }
        return result;
    }

    /// <summary>Spaltenwert aus der Liste; keine Tags = <c>null</c>.</summary>
    public static string? Join(IEnumerable<string?>? tags)
    {
        var clean = Clean(tags);
        return clean.Count == 0 ? null : string.Join('\n', clean);
    }

    public static List<string> Parse(string? stored)
        => string.IsNullOrEmpty(stored) ? new List<string>() : Clean(stored.Split('\n'));
}
