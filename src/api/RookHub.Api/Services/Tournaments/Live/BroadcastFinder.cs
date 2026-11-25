using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>
/// Sucht zu einem geholten chess-results-Turnier die passende Lichess-Übertragung (Singleton, gecacht):
/// <list type="number">
/// <item><c>/api/broadcast/top</c> (laufende, kommende, jüngst fertige offizielle Übertragungen, 10 min gecacht für ALLE
///   Turniere): steht in <c>info.standings</c>/<c>info.website</c> der chess-results-Link <c>tnr{Id}</c>, ist es diese
///   (die Bundesliga 2026/27 trägt <c>tnr1404438</c>).</item>
/// <item>Sonst — nur für ein Turnier, das gerade läuft (Termin um heute, ohne Termin zuletzt vor ≤ 3 Tagen aktualisiert) —
///   die Lichess-Suche mit Wörtern des Namens (Länderkürzel ausgeschrieben, Jahreszahlen weg; die Suche verlangt ALLE
///   Wörter). Ein Treffer mit <c>tnr</c>-Link gewinnt; sonst prüft der Finder bei höchstens drei Kandidaten die zuletzt
///   begonnene Runde: mindestens <see cref="MinFideHits"/> FIDE-IDs der Partien stehen in der Startliste und das sind
///   mindestens die Hälfte der IDs der Runde.</item>
/// </list>
/// Kein Treffer → <see cref="MissTtl"/> Ruhe für dieses Turnier. Je Turnier läuft höchstens eine Suche gleichzeitig.
/// </summary>
public sealed partial class BroadcastFinder
{
    public const int MinFideHits = 4;
    public static readonly TimeSpan MissTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan TopTtl = TimeSpan.FromMinutes(10);
    private const int MaxCandidates = 3;

    private readonly ILichessLiveSource _source;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BroadcastFinder> _logger;
    private readonly SemaphoreSlim _one = new(1, 1);

    public BroadcastFinder(ILichessLiveSource source, IMemoryCache cache, ILogger<BroadcastFinder> logger)
    {
        _source = source;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Pause zwischen zwei Lichess-Abrufen einer Suche.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Die Übertragung oder <c>null</c>. Fehler bei Lichess zählen als „nichts gefunden" (ohne Ruhezeit).</summary>
    public async Task<LichessBroadcasts.Tour?> FindAsync(CrTournament t, IReadOnlyCollection<string> fideIds, DateTime now, CancellationToken ct)
    {
        var missKey = "live-find-miss:" + t.ChessResultsId;
        if (_cache.TryGetValue(missKey, out _)) return null;
        await _one.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(missKey, out _)) return null;
            var top = await TopAsync(ct);
            if (top.FirstOrDefault(x => x.ChessResultsIds.Contains(t.ChessResultsId)) is { } hit) return hit;
            if (!LooksCurrent(t, now)) { _cache.Set(missKey, true, MissTtl); return null; }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new List<LichessBroadcasts.Tour>();
            foreach (var q in Queries(t.Name))
            {
                var json = await _source.SearchAsync(q, ct);
                await Wait(ct);
                if (json is null) continue;
                foreach (var tour in LichessBroadcasts.ParseSearch(json).Tours)
                {
                    if (!seen.Add(tour.Id)) continue;
                    if (tour.ChessResultsIds.Contains(t.ChessResultsId)) return tour;
                    if (tour.ChessResultsIds.Count > 0) continue;                          // verweist auf ein anderes Turnier
                    if (tour.End is { } end && end < now.AddDays(-14) || tour.Start is { } s && s > now.AddDays(14)) continue;
                    candidates.Add(tour);
                }
            }
            var ids = fideIds.ToHashSet(StringComparer.Ordinal);
            foreach (var c in candidates.Take(MaxCandidates))
                if (ids.Count >= MinFideHits && await PlayersMatchAsync(c, ids, now, ct)) return c;
            _cache.Set(missKey, true, MissTtl);
            return null;
        }
        catch (Exception e) when (e is LichessRateLimitedException or HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogInformation("Live-Bretter: Suche nach der Übertragung zu {Tournament} fehlgeschlagen ({Error})", t.ChessResultsId, e.Message);
            return null;
        }
        finally
        {
            _one.Release();
        }
    }

    private Task Wait(CancellationToken ct) => Pause > TimeSpan.Zero ? Task.Delay(Pause, ct) : Task.CompletedTask;

    private async Task<List<LichessBroadcasts.Tour>> TopAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue("live-find-top", out List<LichessBroadcasts.Tour>? top) && top is not null) return top;
        var json = await _source.GetTopAsync(ct);
        top = json is null ? [] : LichessBroadcasts.ParseTop(json);
        _cache.Set("live-find-top", top, TopTtl);
        return top;
    }

    private async Task<bool> PlayersMatchAsync(LichessBroadcasts.Tour c, HashSet<string> ids, DateTime now, CancellationToken ct)
    {
        var detailJson = await _source.GetTourAsync(c.Id, ct);
        await Wait(ct);
        if (detailJson is null || LichessBroadcasts.ParseTourDetail(detailJson) is not { } detail) return false;
        var round = detail.Rounds.LastOrDefault(r => r.Ongoing || r.Finished || r.StartsAt is { } s && s <= now) ?? detail.Rounds.FirstOrDefault();
        if (round is null) return false;
        var pgn = await _source.GetRoundPgnAsync(round.Id, ct);
        await Wait(ct);
        if (string.IsNullOrEmpty(pgn)) return false;
        var inRound = FideRe().Matches(pgn).Select(m => m.Groups[1].Value).Where(x => x != "0").ToHashSet(StringComparer.Ordinal);
        var hits = inRound.Count(ids.Contains);
        return hits >= MinFideHits && hits * 2 >= inRound.Count;
    }

    [GeneratedRegex(@"\[(?:White|Black)FideId ""(\d+)""\]")]
    private static partial Regex FideRe();

    /// <summary>Läuft das Turnier gerade? Termin „22.08.2025-30.08.2025" (±2 Tage), ohne Termin: zuletzt vor ≤ 3 Tagen geholt.</summary>
    public static bool LooksCurrent(CrTournament t, DateTime now)
    {
        var dates = DateRe().Matches(t.Date ?? "")
            .Select(m => DateTime.TryParseExact(m.Value, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null)
            .Where(d => d is not null).Select(d => d!.Value).ToList();
        if (dates.Count > 0) return now.Date >= dates.Min().AddDays(-2) && now.Date <= dates.Max().AddDays(2);
        return t.UpdatedAt is { } u && u >= now.AddDays(-3);
    }

    [GeneratedRegex(@"\d{2}\.\d{2}\.\d{4}")]
    private static partial Regex DateRe();

    private static readonly Dictionary<string, string> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AUT"] = "Austria", ["GER"] = "German", ["SUI"] = "Swiss", ["ITA"] = "Italian", ["CRO"] = "Croatian",
        ["SLO"] = "Slovenian", ["SVK"] = "Slovak", ["CZE"] = "Czech", ["HUN"] = "Hungarian", ["FRA"] = "French",
        ["ESP"] = "Spanish", ["NED"] = "Dutch", ["POL"] = "Polish", ["ENG"] = "English", ["USA"] = "US",
    };

    /// <summary>Suchwörter aus dem chess-results-Namen: „1. BUNDESLIGA AUT 2026/2027" → „1. Bundesliga Austria", „Bundesliga Austria".</summary>
    public static List<string> Queries(string name)
    {
        var words = WordRe().Matches(name ?? "").Select(m => m.Value)
            .Where(w => !YearRe().IsMatch(w))
            .Select(w => Countries.TryGetValue(w, out var c) ? c : w.Length > 3 && w.All(char.IsUpper) ? Title(w) : w)
            .ToList();
        var full = string.Join(' ', words);
        var longWords = words.Where(w => w.Length >= 4).ToList();
        var list = new List<string>();
        if (full.Length >= 4) list.Add(full);
        if (longWords.Count >= 2) list.Add(string.Join(' ', longWords.Take(3)));
        if (longWords.Count >= 1) list.Add(longWords.OrderByDescending(w => w.Length).First());
        return list.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
    }

    private static string Title(string w) => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();

    [GeneratedRegex(@"[\p{L}\d][\p{L}\d.\-]*")]
    private static partial Regex WordRe();
    [GeneratedRegex(@"^\d{4}([/\-]\d{2,4})?$|^\d{2}/\d{2}$")]
    private static partial Regex YearRe();
}
