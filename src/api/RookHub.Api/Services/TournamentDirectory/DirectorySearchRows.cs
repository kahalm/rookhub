using System.Globalization;
using System.Text.Json;

namespace RookHub.Api.Services.TournamentDirectory;

/// <summary>
/// Die Trefferliste der chess-results-Turniersuche (Crawler <c>/api/tournament-search</c>) als
/// Zeilen. Ein Nicht-Array ist eine leere Liste, eine Zeile ohne Nummer oder Namen faellt heraus.
///
/// <para>Herausgezogen aus <see cref="TournamentDirectoryService"/> (Codereview 2026-09-29, Z8).</para>
/// </summary>
internal static class DirectorySearchRows
{
    /// <summary>Eine Zeile der Trefferliste, wie der Crawler sie liefert.</summary>
    internal sealed record CrawlerDirectoryRow(
        string ChessResultsId, string Name, string? Federation, string? State,
        DateOnly? StartDate, DateOnly? EndDate, string? Location, string? TimeControl,
        string? Director, string? Organizer, string? ChiefArbiter,
        int? Rounds, int? PlayerCount, DateTime? LastUpdatedApproxUtc);

    internal static List<CrawlerDirectoryRow> ParseRows(JsonElement json)
    {
        var rows = new List<CrawlerDirectoryRow>();
        if (json.ValueKind != JsonValueKind.Array) return rows;

        foreach (var element in json.EnumerateArray())
        {
            var id = Str(element, "chessResultsId");
            var name = Str(element, "name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;

            rows.Add(new CrawlerDirectoryRow(
                id, name,
                Str(element, "federation"), Str(element, "state"),
                Date(element, "startDate"), Date(element, "endDate"),
                Str(element, "location"), Str(element, "timeControl"),
                Str(element, "director"), Str(element, "organizer"), Str(element, "chiefArbiter"),
                Int(element, "rounds"), Int(element, "playerCount"),
                Timestamp(element, "lastUpdatedApproxUtc")));
        }
        return rows;
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static DateOnly? Date(JsonElement e, string prop) =>
        DateOnly.TryParseExact(Str(e, prop), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;

    private static DateTime? Timestamp(JsonElement e, string prop) =>
        DateTime.TryParse(Str(e, prop), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
}
