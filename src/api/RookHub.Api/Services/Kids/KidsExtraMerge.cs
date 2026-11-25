using System.Text.Json;
using RookHub.Api.DTOs.Kids;

namespace RookHub.Api.Services.Kids;

/// <summary>
/// Endlos-Läufe und Sternenjagd im Konto (1.020.0, <see cref="Models.KidsExtraProgress"/>). Zusammengeführt wird wie
/// zwischen zwei Geräten, ohne dass etwas Geschafftes verloren geht:
/// <list type="bullet">
/// <item><b>Läufe</b>: vereinigt (gleicher Zeitpunkt = derselbe Lauf), die jüngsten <see cref="KeepRuns"/>; Rekord = der höchste.</item>
/// <item><b>Sternenjagd</b>: geschaffte und freigeschaltete Stufen vereinigt.</item>
/// </list>
/// Der Browser führt hier NICHT selbst zusammen, er übernimmt das Ergebnis der Antwort (anders als bei Stufen und
/// Kursen, <see cref="KidsProgressMerge"/>).
/// </summary>
public static class KidsExtraMerge
{
    /// <summary>Wie <c>ENDLESS_KEEP_RUNS</c> im KidHub — die Kurve braucht die letzten zehn.</summary>
    public const int KeepRuns = 20;
    public const int MaxRunsPerRequest = 100;
    public const int MaxStages = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static KidsEndlessDto? ReadEndless(string? json) => Read<KidsEndlessDto>(json);
    public static KidsStarsDto? ReadStars(string? json) => Read<KidsStarsDto>(json);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static KidsEndlessDto? MergeEndless(KidsEndlessDto? a, KidsEndlessDto? b)
    {
        if (a is null) return b is null ? null : MergeEndless(new KidsEndlessDto(), b);
        if (b is null) return a;
        var runs = a.Runs.Concat(b.Runs)
            .GroupBy(r => r.At).Select(g => g.OrderByDescending(r => r.Solved).First())
            .OrderBy(r => r.At).ToList();
        var best = new[] { a.Best, b.Best }.Concat(runs.Select(r => r.Solved)).Max();
        return new KidsEndlessDto { Runs = runs.Skip(Math.Max(0, runs.Count - KeepRuns)).ToList(), Best = best };
    }

    public static KidsStarsDto? MergeStars(KidsStarsDto? a, KidsStarsDto? b)
    {
        if (a is null && b is null) return null;
        IEnumerable<int> D(KidsStarsDto? s) => s?.Done ?? [];
        IEnumerable<int> U(KidsStarsDto? s) => s?.Unlocked ?? [];
        return new KidsStarsDto
        {
            Done = D(a).Concat(D(b)).Distinct().Order().ToList(),
            Unlocked = U(a).Concat(U(b)).Distinct().Order().ToList(),
        };
    }

    /// <summary>Werte aus dem Browser in Grenzen; über den Deckeln: Fehler statt still kürzen.</summary>
    public static KidsEndlessDto? NormalizeEndless(KidsEndlessDto? e, long maxAt)
    {
        if (e is null) return null;
        if (e.Runs.Count > MaxRunsPerRequest) throw new Exceptions.DomainValidationException($"At most {MaxRunsPerRequest} endless runs.");
        return new KidsEndlessDto
        {
            Best = Math.Clamp(e.Best, 0, 100_000),
            Runs = e.Runs.Where(r => r.At > 0).Select(r => new KidsEndlessRunDto
            {
                At = Math.Min(r.At, maxAt),
                Solved = Math.Clamp(r.Solved, 0, 100_000),
                MaxRating = Math.Clamp(r.MaxRating, 0, 4000),
                FirstMistakeRating = r.FirstMistakeRating is int f ? Math.Clamp(f, 0, 4000) : null,
            }).ToList(),
        };
    }

    public static KidsStarsDto? NormalizeStars(KidsStarsDto? s)
    {
        if (s is null) return null;
        if (s.Done.Count > MaxStages || s.Unlocked.Count > MaxStages) throw new Exceptions.DomainValidationException($"At most {MaxStages} star stages.");
        return new KidsStarsDto
        {
            Done = s.Done.Where(x => x is > 0 and <= 10_000).Distinct().Order().ToList(),
            Unlocked = s.Unlocked.Where(x => x is > 0 and <= 10_000).Distinct().Order().ToList(),
        };
    }

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); } catch (JsonException) { return null; }
    }
}
