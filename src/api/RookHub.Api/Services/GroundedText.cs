using System.Text.Json;
using System.Text.Json.Nodes;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Der gemeinsame Teil der kurzen Modelltexte zur Partie (A6-018): Nacherzählung (<see cref="GameRecapService"/>), Roast
/// (<see cref="GameRoastService"/>) und Fehler-Erklärung (<see cref="GameMoveExplanationService"/>) lesen die Antwort
/// gleich aus (<see cref="TextOf"/>); Nacherzählung und Roast fragen auch gleich nach (<see cref="WriteAsync"/>). Die
/// Erklärung behält ihre eigene Schleife — der Meisterkommentar geht nur im ersten Versuch mit, und geprüft wird gegen die
/// Linien des einen Fehlers (<see cref="GameMoveExplanationService.IsGrounded"/>).
/// </summary>
internal static class GroundedText
{
    /// <summary>
    /// Zwei Versuche: der Auftrag mit den Fakten, und wenn die Antwort fehlt, zu lang ist oder einen Zug nennt, der nicht
    /// in <paramref name="allowed"/> steht (<see cref="GameMoveExplanationService.MentionsOnly"/>), noch einmal mit
    /// <paramref name="retryHint"/> hinter den Fakten. <c>null</c>, wenn auch das nichts taugt. Der Zweck ist zugleich der
    /// Schlüssel des Textes in der JSON-Antwort (<c>{"recap": "..."}</c>, <c>{"roast": "..."}</c>).
    /// </summary>
    /// <param name="foreignMove">Je verworfener Antwort mit fremdem Zug — der Aufrufer protokolliert mit seiner Vorlage.</param>
    public static async Task<string?> WriteAsync(IClaudeJsonClient llm, string purpose, string system, string facts,
        string retryHint, JsonNode schema, int maxTokens, int maxLength, IEnumerable<string> allowed, Action foreignMove,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var json = await llm.CompleteJsonAsync(purpose, system, attempt == 0 ? facts : facts + retryHint, schema, maxTokens, ct);
            var candidate = TextOf(json, purpose, maxLength);
            if (candidate != null && GameMoveExplanationService.MentionsOnly(candidate, allowed)) return candidate;
            if (candidate != null) foreignMove();
        }
        return null;
    }

    /// <summary>Der Text unter <paramref name="key"/>, getrimmt; <c>null</c>, wenn er fehlt, leer oder länger als
    /// <paramref name="maxLength"/> ist oder die Antwort kein JSON.</summary>
    public static string? TextOf(string? json, string key, int maxLength)
    {
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()?.Trim() : null;
            return string.IsNullOrWhiteSpace(text) || text.Length > maxLength ? null : text;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>Zeilen der Fakten, die Nacherzählung und Roast gleich schreiben (A6-018).</summary>
internal static class GameFacts
{
    /// <summary>„White: Ich (1500), Black: Gegner (1400), result 1-0, time control 300+3" — ohne Schluss: die Länge der
    /// Partie hängt jeder Dienst selbst an (Züge bzw. Halbzüge).</summary>
    public static string Players(SavedGameDetailDto game)
        => $"White: {game.White ?? "?"}{Elo(game.WhiteElo)}, Black: {game.Black ?? "?"}{Elo(game.BlackElo)}, result {game.Result ?? "*"}"
            + (game.TimeControl is { Length: > 0 } tc ? $", time control {tc}" : "");

    /// <summary>„Accuracy: White 92 %, Black 41 %."</summary>
    public static string Accuracy(GameAnalysis analysis)
        => $"Accuracy: White {Pct(analysis.AccuracyWhite)}, Black {Pct(analysis.AccuracyBlack)}.";

    private static string Elo(int? elo) => elo is int e ? $" ({e})" : "";
    private static string Pct(double? v) => v is double d ? $"{Math.Round(d)} %" : "unknown";
}
