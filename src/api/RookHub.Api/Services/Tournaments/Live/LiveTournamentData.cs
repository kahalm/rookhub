using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Exceptions;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>Kopf eines geholten Turniers (Crawler <c>GET /api/tournaments/{id}</c>).</summary>
public sealed record CrTournament(string ChessResultsId, string Name, string? Date, DateTime? UpdatedAt, int TotalRounds);

/// <summary>Was die Live-Bretter vom Crawler brauchen (Schnittstelle für Tests).</summary>
public interface ILiveTournamentData
{
    /// <summary><c>null</c> = Turnier nicht geholt (404).</summary>
    Task<CrTournament?> GetTournamentAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<CrPlayer>> GetPlayersAsync(string id, CancellationToken ct);
    Task<CrRound> GetRoundAsync(string id, int round, CancellationToken ct);
}

/// <summary>
/// Über <see cref="CrawlerProxyService"/> (dieselben Routen wie die Turnierseite, Felder wie in
/// <c>Fixtures/CrawlerContract</c>), kurz gecacht: ein Turniertag mit hundert Zuschauern soll den Crawler nicht hundertmal
/// fragen. Paarungen 60 s (Ergebnisse trägt chess-results nach), Startliste und Kopf 5 min.
/// </summary>
public sealed class CrawlerLiveTournamentData : ILiveTournamentData
{
    private readonly CrawlerProxyService _proxy;
    private readonly IMemoryCache _cache;

    public CrawlerLiveTournamentData(CrawlerProxyService proxy, IMemoryCache cache)
    {
        _proxy = proxy;
        _cache = cache;
    }

    public async Task<CrTournament?> GetTournamentAsync(string id, CancellationToken ct)
    {
        if (_cache.TryGetValue("live-cr-t:" + id, out CrTournament? t)) return t;
        try
        {
            t = ParseTournament(await _proxy.GetAsync($"/api/tournaments/{id}", ct));
        }
        catch (CrawlerRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            t = null;
        }
        _cache.Set("live-cr-t:" + id, t, TimeSpan.FromMinutes(5));
        return t;
    }

    public async Task<IReadOnlyList<CrPlayer>> GetPlayersAsync(string id, CancellationToken ct)
    {
        if (_cache.TryGetValue("live-cr-p:" + id, out IReadOnlyList<CrPlayer>? p) && p is not null) return p;
        p = ParsePlayers(await _proxy.GetAsync($"/api/tournaments/{id}/players", ct));
        _cache.Set("live-cr-p:" + id, p, TimeSpan.FromMinutes(5));
        return p;
    }

    public async Task<CrRound> GetRoundAsync(string id, int round, CancellationToken ct)
    {
        var key = $"live-cr-r:{id}:{round}";
        if (_cache.TryGetValue(key, out CrRound? r) && r is not null) return r;
        var players = await GetPlayersAsync(id, ct);
        r = ParseRound(await _proxy.GetAsync($"/api/tournaments/{id}/pairings?round={round}", ct), players);
        _cache.Set(key, r, TimeSpan.FromSeconds(60));
        return r;
    }

    // ── Lesen (rein, getestet) ──────────────────────────────────────────────────────────────────

    public static CrTournament? ParseTournament(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        DateTime? updated = Str(e, "updatedAt") is { } u && DateTime.TryParse(u, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;
        var rounds = e.TryGetProperty("totalRounds", out var tr) && tr.ValueKind == JsonValueKind.Number ? tr.GetInt32() : 0;
        return new CrTournament(Str(e, "chessResultsId") ?? "", Str(e, "name") ?? "", Str(e, "date"), updated, rounds);
    }

    public static IReadOnlyList<CrPlayer> ParsePlayers(JsonElement e)
    {
        var list = new List<CrPlayer>();
        if (e.ValueKind != JsonValueKind.Array) return list;
        foreach (var p in e.EnumerateArray())
            list.Add(new CrPlayer(Str(p, "name") ?? "", Str(p, "fideId") is { Length: > 0 } f && f != "0" ? f : null, Int(p, "elo"),
                Str(p, "teamName"), Int(p, "boardNumber")));
        return list;
    }

    /// <summary>Paarungen einer Runde: Mannschafts-Paarungen (Feld <c>homeTeam</c>) oder Einzel-Paarungen.</summary>
    public static CrRound ParseRound(JsonElement e, IReadOnlyList<CrPlayer> players)
    {
        var pairings = new List<CrPairing>();
        var matches = new List<CrMatch>();
        var team = false;
        if (e.ValueKind == JsonValueKind.Array)
            foreach (var x in e.EnumerateArray())
            {
                if (x.TryGetProperty("homeTeam", out _))
                {
                    team = true;
                    matches.Add(new CrMatch(Int(x, "matchNumber") ?? matches.Count + 1, Str(x, "homeTeam") ?? "", Str(x, "awayTeam") ?? "",
                        Dec(x, "homeScore"), Dec(x, "awayScore")));
                }
                else pairings.Add(new CrPairing(Int(x, "boardNumber") ?? pairings.Count + 1, Str(x, "white") ?? "", Str(x, "black") ?? "", Str(x, "result")));
            }
        return new CrRound(team, players, pairings, matches);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
    private static decimal? Dec(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
}
