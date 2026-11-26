using System.Text.Json;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Lesen der Lichess-Übertragungs-API (Broadcasts) — gemeinsamer Baustein für LeagueHub (Partien in die Spielerkarten,
/// <c>League/LeagueBroadcastImport</c>) und die Live-Bretter der Turnierseite (<c>Tournaments/Live</c>, 1.014.0).
/// Rein: nur JSON/Text rein, Werte raus; abgerufen wird beim Aufrufer.
/// <list type="bullet">
/// <item><c>GET /api/broadcast/search?q=</c> → <see cref="ParseSearch"/> (die Suche verlangt ALLE Wörter, „AUT" findet
///   „Austria" nicht — gemessen 25.11.2026).</item>
/// <item><c>GET /api/broadcast/top</c> → <see cref="ParseTop"/> (laufende, kommende und die jüngsten fertigen offiziellen
///   Übertragungen mit <c>info.standings</c> — dort steht oft der chess-results-Link <c>tnr1404438</c>).</item>
/// <item><c>GET /api/broadcast/{tourId}</c> bzw. <c>/api/broadcast/-/-/{roundId}</c> → <see cref="ParseTourDetail"/>.</item>
/// </list>
/// </summary>
public static partial class LichessBroadcasts
{
    /// <summary>Ein Übertragungs-Turnier. <see cref="ChessResultsIds"/> = die <c>tnr</c>-Nummern aus <c>info.standings</c>
    /// und <c>info.website</c> (leer, wenn dort kein chess-results-Link steht).</summary>
    public sealed record Tour(string Id, string Name, string? Location, DateTime? Start, DateTime? End)
    {
        public IReadOnlyList<string> ChessResultsIds { get; init; } = [];
    }

    /// <summary>Eine Runde der Übertragung (<c>Finished</c>/<c>Ongoing</c> so, wie Lichess sie meldet).</summary>
    public sealed record Round(string Id, string Name, DateTime? StartsAt, bool Ongoing, bool Finished);

    /// <summary>Turnier + Runden in Lichess-Reihenfolge.</summary>
    public sealed record TourDetail(Tour Tour, IReadOnlyList<Round> Rounds)
    {
        public bool AllFinished => Rounds.Count > 0 && Rounds.All(r => r.Finished);
    }

    /// <summary><c>GET /api/broadcast/search</c> → die Turniere einer Seite und die nächste Seite (oder <c>null</c>).</summary>
    public static (List<Tour> Tours, int? NextPage) ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var list = ToursOf(r);
        int? next = r.TryGetProperty("nextPage", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : null;
        return (list, next);
    }

    /// <summary><c>GET /api/broadcast/top</c> → laufende + kommende + (erste Seite) fertige Turniere.</summary>
    public static List<Tour> ParseTop(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var list = new List<Tour>();
        foreach (var key in new[] { "active", "upcoming" })
            if (r.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var x in arr.EnumerateArray())
                    if (x.TryGetProperty("tour", out var t) && ParseTour(t) is { } tour) list.Add(tour);
        if (r.TryGetProperty("past", out var past) && past.ValueKind == JsonValueKind.Object) list.AddRange(ToursOf(past));
        return list;
    }

    private static List<Tour> ToursOf(JsonElement page)
    {
        var list = new List<Tour>();
        if (page.TryGetProperty("currentPageResults", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var x in arr.EnumerateArray())
                if (x.TryGetProperty("tour", out var t) && ParseTour(t) is { } tour) list.Add(tour);
        return list;
    }

    /// <summary><c>GET /api/broadcast/{id}</c> bzw. <c>/api/broadcast/-/-/{roundId}</c> → Turnier und Runden; <c>null</c> ohne Turnier.</summary>
    public static TourDetail? ParseTourDetail(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("tour", out var t) || ParseTour(t) is not { } tour) return null;
        var rounds = new List<Round>();
        if (r.TryGetProperty("rounds", out var rs) && rs.ValueKind == JsonValueKind.Array)
            foreach (var x in rs.EnumerateArray())
                if (ParseRound(x) is { } round) rounds.Add(round);
        // /api/broadcast/-/-/{roundId} antwortet mit {round, tour, games} — ohne „rounds“-Liste (2026-11-26: fremde Runde per
        // Partie-/Runden-Link galt deshalb immer als „nicht gefunden“). Die genannte Runde dann selbst aufnehmen.
        if (r.TryGetProperty("round", out var one) && one.ValueKind == JsonValueKind.Object && ParseRound(one) is { } single
            && rounds.All(x => x.Id != single.Id))
            rounds.Add(single);
        return new TourDetail(tour, rounds);
    }

    private static Round? ParseRound(JsonElement x)
    {
        var id = Str(x, "id");
        if (id is null) return null;
        DateTime? starts = x.TryGetProperty("startsAt", out var s) && s.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds(s.GetInt64()).UtcDateTime : null;
        return new Round(id, Str(x, "name") ?? id, starts, True(x, "ongoing"), True(x, "finished"));
    }

    private static bool True(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static Tour? ParseTour(JsonElement t)
    {
        var id = Str(t, "id");
        if (id is null) return null;
        DateTime? start = null, end = null;
        if (t.TryGetProperty("dates", out var d) && d.ValueKind == JsonValueKind.Array)
        {
            var ms = d.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt64()).ToList();
            if (ms.Count > 0) start = DateTimeOffset.FromUnixTimeMilliseconds(ms[0]).UtcDateTime;
            if (ms.Count > 1) end = DateTimeOffset.FromUnixTimeMilliseconds(ms[^1]).UtcDateTime;
        }
        string? loc = null;
        var tnrs = new List<string>();
        if (t.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
        {
            loc = Str(info, "location");
            foreach (var key in new[] { "standings", "website" })
                foreach (Match m in TnrRe().Matches(Str(info, key) ?? ""))
                    if (!tnrs.Contains(m.Groups[1].Value)) tnrs.Add(m.Groups[1].Value);
        }
        return new Tour(id, Str(t, "name") ?? id, loc, start, end ?? start) { ChessResultsIds = tnrs };
    }

    [GeneratedRegex(@"chess-results\.com/(?:[^\s""]*?/)?tnr(\d{3,9})\.aspx", RegexOptions.IgnoreCase)]
    private static partial Regex TnrRe();

    /// <summary>
    /// Aus einem Link die Kennung: Turnier-Link <c>/broadcast/{slug}/{tourId}</c> → (tourId, null), Runden-Link
    /// <c>/broadcast/{slug}/{runde}/{roundId}</c> → (null, roundId); eine nackte Kennung gilt als Turnier. <c>null</c> = kein Link.
    /// </summary>
    public static (string? TourId, string? RoundId)? IdsOf(string? input)
    {
        var s = (input ?? "").Trim();
        if (IdRe().IsMatch(s)) return (s, null);
        var m = LinkRe().Match(s);
        if (!m.Success) return null;
        var parts = m.Groups[1].Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            2 when IdRe().IsMatch(parts[1]) => (parts[1], null),
            3 when IdRe().IsMatch(parts[2]) => (null, parts[2]),
            // Partie-Link einer Runde (…/{runde}/{roundId}/{gameId}) — die Runde zählt.
            4 when IdRe().IsMatch(parts[2]) => (null, parts[2]),
            _ => null,
        };
    }

    /// <summary>
    /// Link einer FREMDEN Übertragung für „Brett hinzufügen“ der Live-Bretter (1.025.0): NUR <c>lichess.org/broadcast/…</c>
    /// (keine nackte Kennung) — Turnier-Link <c>/{slug}/{tourId}</c> → (tourId, null, null), Runden-Link
    /// <c>/{slug}/{runde}/{roundId}</c> → (null, roundId, null), Partie-Link <c>/{slug}/{runde}/{roundId}/{gameId}</c> →
    /// (null, roundId, gameId). Jede Kennung muss acht Buchstaben/Ziffern sein; sonst <c>null</c>.
    /// </summary>
    public static (string? TourId, string? RoundId, string? GameId)? BoardLinkOf(string? input)
    {
        var s = (input ?? "").Trim();
        if (s.Length > 400) return null;
        var m = LinkRe().Match(s);
        if (!m.Success) return null;
        // Nur der Wirt lichess.org (auch www./http), nicht „evil.com/?lichess.org/broadcast/…“.
        if (!Uri.TryCreate(s.Contains("://", StringComparison.Ordinal) ? s : "https://" + s, UriKind.Absolute, out var uri)
            || !(uri.Host.Equals("lichess.org", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("www.lichess.org", StringComparison.OrdinalIgnoreCase)))
            return null;
        var parts = m.Groups[1].Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            2 when IdRe().IsMatch(parts[1]) => (parts[1], null, null),
            3 when IdRe().IsMatch(parts[2]) => (null, parts[2], null),
            4 when IdRe().IsMatch(parts[2]) && IdRe().IsMatch(parts[3]) => (null, parts[2], parts[3]),
            _ => null,
        };
    }

    /// <summary>Ist das eine Lichess-Kennung (acht Buchstaben/Ziffern)?</summary>
    public static bool IsId(string? s) => s is not null && IdRe().IsMatch(s);

    [GeneratedRegex("^[A-Za-z0-9]{8}$")]
    private static partial Regex IdRe();
    [GeneratedRegex(@"lichess\.org/broadcast/([^?#\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRe();

    /// <summary>
    /// Die Rundennummer aus dem Lichess-Namen („Round 3", „Runde 3", „R3", „3. Runde", „Round 3.2" → 3); <c>null</c> ohne Zahl.
    /// </summary>
    public static int? RoundNumberOf(string? name)
    {
        var m = RoundNumberRe().Match(name ?? "");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
    }

    [GeneratedRegex(@"(\d{1,3})")]
    private static partial Regex RoundNumberRe();

    /// <summary>
    /// Die Lichess-Runde zur chess-results-Runde <paramref name="round"/>: über die Zahl im Namen, wenn jede Runde eine
    /// eigene trägt; sonst über die Reihenfolge (1 = erste Runde der Übertragung). <c>null</c> = gibt es nicht.
    /// </summary>
    public static Round? RoundFor(IReadOnlyList<Round> rounds, int round)
    {
        var numbers = rounds.Select(r => RoundNumberOf(r.Name)).ToList();
        if (numbers.All(n => n is not null) && numbers.Distinct().Count() == numbers.Count)
            return rounds.FirstOrDefault(r => RoundNumberOf(r.Name) == round);
        return round >= 1 && round <= rounds.Count ? rounds[round - 1] : null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
