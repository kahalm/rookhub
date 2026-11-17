using System.Text.Json;
using System.Text.RegularExpressions;
using RookHub.Api.DTOs.TrainingVideos;

namespace RookHub.Api.Services.TrainingVideos;

/// <summary>
/// Sprecherwechsel einer Trainerstunde (<see cref="Models.TrainingLesson.SpeakerTurns"/>): aus der Datei prüfen und
/// ordnen, als kompaktes JSON speichern, für die Stundenseite wieder lesen. Die Glättung kurzer Einwürfe macht schon die
/// Aufbereitung (<c>sprecher_turns.py</c>); hier wird nur aufgeräumt: Zeiten ≥ 0 und gerundet, nach Zeit, gleiche Zeit →
/// der letzte gewinnt, gleicher Sprecher hintereinander → ein Wechsel.
/// </summary>
public static partial class TrainingSpeakerTurns
{
    public const int MaxTurns = 5000;

    [GeneratedRegex("^[a-z0-9_-]{1,16}$")]
    private static partial Regex SpeakerId();

    /// <summary>Geordnete Wechsel; <c>null</c> = keine Angabe in der Datei.</summary>
    public static List<TrainingSpeakerTurnDto>? Normalize(List<TrainingSpeakerTurnFile>? turns, List<string> warnings)
    {
        if (turns == null) return null;
        var bad = 0;
        var byTime = new SortedDictionary<int, string>();
        foreach (var x in turns)
        {
            var sp = x.Sp?.Trim().ToLowerInvariant();
            if (x.T is not double t || double.IsNaN(t) || double.IsInfinity(t) || t < 0 || sp == null || !SpeakerId().IsMatch(sp))
            {
                bad++;
                continue;
            }
            byTime[(int)Math.Round(t)] = sp;
        }
        if (bad > 0) warnings.Add($"{bad} Sprecherwechsel unlesbar (Zeit oder Sprecher) — übersprungen.");
        var result = new List<TrainingSpeakerTurnDto>();
        foreach (var (t, sp) in byTime)
            if (result.Count == 0 || result[^1].Sp != sp) result.Add(new TrainingSpeakerTurnDto(t, sp));
        if (result.Count > MaxTurns)
        {
            warnings.Add($"{result.Count} Sprecherwechsel — nur die ersten {MaxTurns} eingelesen.");
            result = result.Take(MaxTurns).ToList();
        }
        return result;
    }

    /// <summary>Zum Speichern: leere Liste → <c>null</c> (keine Wechsel).</summary>
    public static string? Serialize(List<TrainingSpeakerTurnDto> turns) =>
        turns.Count == 0 ? null : JsonSerializer.Serialize(turns, TrainingLessonFile.Json);

    /// <summary>Gespeichertes JSON lesen; kaputt oder leer → leere Liste.</summary>
    public static List<TrainingSpeakerTurnDto> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<TrainingSpeakerTurnDto>>(json, TrainingLessonFile.Json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}
