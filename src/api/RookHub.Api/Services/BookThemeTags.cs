namespace RookHub.Api.Services;

/// <summary>
/// EINE Quelle für die Buch-Themen-Semantik (<c>Book.Themes</c>-CSV): gültige Keys, Parsing mit
/// stabiler Reihenfolge/Dedupe und dem „Default Taktik"-Fallback. Vorher zweimal implementiert
/// (CourseService-Whitelist für UI/Setzen + TrainingGoalService-Switch fürs Zeit-Routing) — ein
/// neuer Themen-Key hätte synchron an beiden Stellen ergänzt werden müssen, sonst zeigte die
/// Kurs-UI den Tag als gültig, während der Trainingsziele-Tracker die Zeit still falsch bucketet.
/// </summary>
public static class BookThemeTags
{
    /// <summary>Gültige Keys (= <see cref="Models.ChessableTheme"/>-Namen, kleingeschrieben).</summary>
    public static readonly string[] ValidKeys = { "opening", "middlegame", "endgame", "tactics", "other" };

    public static bool IsValidKey(string key) => Array.IndexOf(ValidKeys, key) >= 0;

    /// <summary>Parst die CSV zu einer Key-Liste (Reihenfolge stabil, dedupliziert).
    /// Unset/leer/nur-ungültig → Default <c>["tactics"]</c> — jedes Buch ist standardmäßig Taktik.</summary>
    public static List<string> ParseKeys(string? csv)
    {
        var seen = new List<string>();
        if (!string.IsNullOrWhiteSpace(csv))
            foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var key = raw.ToLowerInvariant();
                if (IsValidKey(key) && !seen.Contains(key)) seen.Add(key);
            }
        return seen.Count > 0 ? seen : new List<string> { "tactics" };
    }
}
