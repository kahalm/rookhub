using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Lichess-Übertragungen (Broadcasts) von Turnieren am Brett in die Spielerkarten (0.608.0, Wunsch 2026-09-30). Viele
/// österreichische Turniere werden dort live übertragen (Tirol Open, Kufsteiner Open, Bundesliga, Meisterschaften), und jede
/// Partie trägt die FIDE-ID beider Spieler — zugeordnet wird über sie wie bei der Megabase
/// (<see cref="LeagueProfileStore.ImportGamesAsync"/>), Quelle <see cref="Source"/>. Gemessen am 2026-09-30: von 406 Partien
/// mit Ligaspielern in neun Übertragungen fehlten 43 im Bestand (vor allem Bundesliga); der Rest steht schon über
/// chess-results da — der Gewinn ist vor allem, dass die Partien schon WÄHREND des Turniers da sind.
/// <list type="bullet">
/// <item>Finden: Lichess-Suche nach <c>LeagueBroadcasts:Queries</c> (Vorgabe <see cref="DefaultQueries"/>), höchstens
///   <see cref="MaxYears"/> Jahre zurück, einmal je <see cref="DiscoverEvery"/>. Weitere per Link (<see cref="AddAsync"/>).</item>
/// <item>Einspielen: nur fertige Partien (kein „*"), Standardschach ab der Grundstellung, Datum mit Punkten; Kommentare,
///   Bewertungen und Uhrzeiten der Übertragung fallen weg (<see cref="CleanPgn"/>). Dieselbe Partie aus einer anderen Quelle
///   wird auch bei abweichendem Datum erkannt (<c>skipSameMoves</c>).</item>
/// <item>Laufende Turniere alle <see cref="RefreshOngoing"/> neu, fertige (alle Runden vorbei) nie wieder.</item>
/// </list>
/// </summary>
public sealed partial class LeagueBroadcastImport
{
    public const string Source = "Lichess-Übertragung";
    /// <summary>Tirol/Österreich, seit 0.712.0 auch Bayern (Bayerische Einzelmeisterschaften, Bavarian Open, Tegernsee Masters,
    /// Munich Chess Festival — Recherche 07.10.2026; „Bayern", „Oberbayern", „Landesliga Süd" bringen nichts).</summary>
    public static readonly string[] DefaultQueries =
        { "Austria", "Österreich", "Tirol", "Tyrol", "Südtirol", "Innsbruck", "Bavarian", "Bayerische", "Tegernsee", "Munich", "München" };
    public const int MaxPages = 10;
    public static readonly TimeSpan DiscoverEvery = TimeSpan.FromHours(20);
    public static readonly TimeSpan RefreshOngoing = TimeSpan.FromHours(6);
    /// <summary>So lange nach dem letzten Tag gilt ein Turnier als fertig, wenn die Runden-Angaben fehlen.</summary>
    public static readonly TimeSpan FinishedAfter = TimeSpan.FromDays(3);

    /// <summary>Die Kopfzeilen, die aus der Übertragung übernommen werden.</summary>
    private static readonly string[] KeepHeaders =
    {
        "Event", "Site", "Date", "Round", "White", "Black", "Result", "WhiteElo", "BlackElo", "WhiteTitle", "BlackTitle",
        "WhiteFideId", "BlackFideId", "TimeControl", "ECO", "Opening", "GameURL",
    };

    public sealed record Tour(string Id, string Name, string? Location, DateTime? Start, DateTime? End);

    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly ILogger<LeagueBroadcastImport> _logger;
    private readonly string _lichess;
    private readonly string[] _queries;

    public LeagueBroadcastImport(AppDbContext db, HttpClient http, ILogger<LeagueBroadcastImport> logger, IConfiguration? config = null)
    {
        _db = db;
        _http = http;
        _logger = logger;
        _lichess = (config?["Lichess:SiteUrl"] ?? "https://lichess.org").TrimEnd('/');
        var q = config?["LeagueBroadcasts:Queries"];
        _queries = string.IsNullOrWhiteSpace(q) ? DefaultQueries
            : q.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        MaxYears = Math.Clamp(config?.GetValue<int?>("LeagueBroadcasts:MaxYears") ?? 5, 1, 30);
    }

    public int MaxYears { get; }
    /// <summary>Pause zwischen zwei Abrufen bei Lichess.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(1);

    // ── Lesen (rein, getestet) ──────────────────────────────────────────────────────────────────

    /// <summary><c>GET /api/broadcast/search</c> → die Turniere einer Seite und die nächste Seite (oder <c>null</c>).</summary>
    public static (List<Tour> Tours, int? NextPage) ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var list = new List<Tour>();
        if (r.TryGetProperty("currentPageResults", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var x in arr.EnumerateArray())
                if (x.TryGetProperty("tour", out var t) && ParseTour(t) is { } tour) list.Add(tour);
        int? next = r.TryGetProperty("nextPage", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : null;
        return (list, next);
    }

    /// <summary><c>GET /api/broadcast/{id}</c> bzw. <c>/api/broadcast/-/-/{roundId}</c> → das Turnier und ob alle Runden vorbei sind.</summary>
    public static (Tour? Tour, bool AllFinished) ParseTourDetail(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var tour = r.TryGetProperty("tour", out var t) ? ParseTour(t) : null;
        var rounds = r.TryGetProperty("rounds", out var rs) && rs.ValueKind == JsonValueKind.Array ? rs.EnumerateArray().ToList() : new();
        var all = rounds.Count > 0 && rounds.All(x => x.TryGetProperty("finished", out var f) && f.ValueKind == JsonValueKind.True);
        return (tour, all);
    }

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
        var loc = t.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object ? Str(info, "location") : null;
        return new Tour(id, Str(t, "name") ?? id, loc, start, end ?? start);
    }

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
            _ => null,
        };
    }

    [GeneratedRegex("^[A-Za-z0-9]{8}$")]
    private static partial Regex IdRe();
    [GeneratedRegex(@"lichess\.org/broadcast/([^?#\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRe();

    /// <summary>
    /// Die Übertragung als sauberes PGN: nur fertige Partien (1-0, 0-1, ½) im Standardschach ab der Grundstellung mit
    /// mindestens einer FIDE-ID; Datum mit Punkten (ältere Übertragungen schreiben „2024-08-24" — sonst erkennte der Abgleich
    /// mit chess-results die Partie nicht), fehlt es, gilt <c>UTCDate</c>; nur die Hauptvariante, ohne Bewertungen und Uhren.
    /// </summary>
    public static (string Pgn, int Games) CleanPgn(string pgn)
    {
        var sb = new StringBuilder();
        var n = 0;
        foreach (var b in PgnParser.SplitGameBlocks(pgn))
        {
            var h = new Dictionary<string, string>(b.Headers, StringComparer.OrdinalIgnoreCase);
            var result = h.GetValueOrDefault("Result") ?? "";
            if (result is not ("1-0" or "0-1" or "1/2-1/2")) continue;
            if (h.TryGetValue("Variant", out var v) && !v.Equals("Standard", StringComparison.OrdinalIgnoreCase)) continue;
            if (h.ContainsKey("FEN") || h.GetValueOrDefault("SetUp") == "1") continue;
            if (!h.ContainsKey("WhiteFideId") && !h.ContainsKey("BlackFideId")) continue;
            var moveText = PgnParser.SplitGames(b.Raw).Select(x => x.MoveText).FirstOrDefault() ?? "";
            var sans = PgnParser.ExtractMainlineSans(moveText);
            if (sans.Count == 0) continue;
            h["Date"] = DateOf(h.GetValueOrDefault("Date")) ?? DateOf(h.GetValueOrDefault("UTCDate")) ?? "????.??.??";
            foreach (var k in KeepHeaders)
                if (h.TryGetValue(k, out var val) && !string.IsNullOrWhiteSpace(val)) sb.Append(PgnWriter.Tag(k, val.Trim()));
            sb.Append('\n').Append(PgnWriter.MoveText(sans, null, null, result)).Append("\n\n");
            n++;
        }
        return (sb.ToString(), n);
    }

    /// <summary>„2024-08-24" / „2024.08.24" → „2024.08.24"; Unvollständiges (mit „?") → <c>null</c>.</summary>
    public static string? DateOf(string? s)
    {
        var d = (s ?? "").Trim().Replace('-', '.').Replace('/', '.');
        return DateRe().IsMatch(d) ? d : null;
    }

    [GeneratedRegex(@"^\d{4}\.\d{2}\.\d{2}$")]
    private static partial Regex DateRe();

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ── Abrufen ─────────────────────────────────────────────────────────────────────────────────

    private async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        using var r = await _http.GetAsync(url, ct);
        if (r.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("Lichess");
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsStringAsync(ct);
    }

    private Task Wait(CancellationToken ct) => Pause > TimeSpan.Zero ? Task.Delay(Pause, ct) : Task.CompletedTask;

    /// <summary>Neue Übertragungen über die Suche finden und vormerken. → wie viele neu.</summary>
    public async Task<int> DiscoverAsync(CancellationToken ct)
    {
        var horizon = DateTime.UtcNow.AddYears(-MaxYears);
        var known = (await _db.LeagueBroadcasts.Select(b => b.TourId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var q in _queries)
        {
            int? page = 1;
            for (var i = 0; page is { } p && i < MaxPages; i++)
            {
                var json = await GetAsync($"{_lichess}/api/broadcast/search?q={Uri.EscapeDataString(q)}&page={p}", ct);
                await Wait(ct);
                if (json is null) break;
                var (tours, next) = ParseSearch(json);
                foreach (var t in tours)
                {
                    if ((t.End ?? t.Start) is { } last && last < horizon || !known.Add(t.Id)) continue;
                    _db.LeagueBroadcasts.Add(Row(t, manual: false));
                    added++;
                }
                page = next;
            }
        }
        await _db.SaveChangesAsync(ct);
        return added;
    }

    private static LeagueBroadcast Row(Tour t, bool manual) => new()
    {
        TourId = t.Id, Name = Cut(t.Name, 200)!, Location = Cut(t.Location, 200), StartsAt = t.Start, EndsAt = t.End, Manual = manual,
        FoundAt = DateTime.UtcNow,
    };

    /// <summary>Eine Übertragung (neu) einspielen: Stand, PGN, Partien mit Ligaspielern in die Karten.</summary>
    public async Task ImportAsync(LeagueBroadcast b, CancellationToken ct)
    {
        var detail = await GetAsync($"{_lichess}/api/broadcast/{Uri.EscapeDataString(b.TourId)}", ct);
        await Wait(ct);
        if (detail is null)
        {
            b.Error = "Übertragung nicht gefunden";
            b.Finished = true;                                                // gelöscht — nicht jeden Tag wieder fragen
            b.ImportedAt = DateTime.UtcNow;
            await SaveAsync(b, ct);
            return;
        }
        var (tour, allFinished) = ParseTourDetail(detail);
        if (tour is not null)
        {
            b.Name = Cut(tour.Name, 200)!;
            b.Location = Cut(tour.Location, 200) ?? b.Location;
            b.StartsAt = tour.Start ?? b.StartsAt;
            b.EndsAt = tour.End ?? b.EndsAt;
        }
        var pgn = await GetAsync($"{_lichess}/api/broadcast/{Uri.EscapeDataString(b.TourId)}.pgn", ct) ?? "";
        await Wait(ct);
        var (clean, _) = CleanPgn(pgn);
        var (games, _) = clean.Length == 0 ? (0, 0)
            : await new LeagueProfileStore(_db).ImportGamesAsync(clean, Source, ct, skipSameMoves: true);
        b.Games = games;
        b.Error = null;
        b.ImportedAt = DateTime.UtcNow;
        b.Finished = allFinished || b.EndsAt is { } end && end < DateTime.UtcNow - FinishedAfter;
        await SaveAsync(b, ct);
    }

    /// <summary>Den Stand der Übertragung schreiben — ausdrücklich: das Einspielen in die Karten leert den ChangeTracker
    /// (<see cref="LeagueProfileStore.ImportGamesAsync"/>), danach hängt die Zeile nicht mehr daran.</summary>
    private async Task SaveAsync(LeagueBroadcast b, CancellationToken ct)
    {
        _db.LeagueBroadcasts.Update(b);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Eine Übertragung per Link (Turnier oder Runde) hinzufügen und gleich einspielen — für Turniere, die die Suche nicht
    /// findet (Opens im Ausland mit Tiroler Spielern). Reason: <c>invalidUrl</c>, <c>notFound</c>.
    /// </summary>
    public async Task<(LeagueBroadcast? Broadcast, string? Reason)> AddAsync(string? input, CancellationToken ct)
    {
        if (IdsOf(input) is not { } ids) return (null, "invalidUrl");
        var detail = await GetAsync(ids.TourId is { } t ? $"{_lichess}/api/broadcast/{t}" : $"{_lichess}/api/broadcast/-/-/{ids.RoundId}", ct);
        if (detail is null || ParseTourDetail(detail).Tour is not { } tour) return (null, "notFound");
        var b = await _db.LeagueBroadcasts.FirstOrDefaultAsync(x => x.TourId == tour.Id, ct);
        if (b is null) _db.LeagueBroadcasts.Add(b = Row(tour, manual: true));
        b.Finished = false;                                                   // ausdrücklich gewünscht: neu holen
        await _db.SaveChangesAsync(ct);
        await ImportAsync(b, ct);
        return (b, null);
    }

    private static string? Cut(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    /// <summary>
    /// Ein Durchgang: mit <paramref name="discover"/> erst suchen (der Takt ruft das höchstens alle <see cref="DiscoverEvery"/>),
    /// dann fällige Übertragungen einspielen — nie eingespielte, und laufende alle <see cref="RefreshOngoing"/>; noch nicht
    /// begonnene warten. → noch etwas offen?
    /// </summary>
    public async Task<bool> RunOnceAsync(TimeSpan budget, bool discover, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        try
        {
            if (discover) await DiscoverAsync(ct);
            var now = DateTime.UtcNow;
            var refresh = now - RefreshOngoing;
            var due = await _db.LeagueBroadcasts
                .Where(b => !b.Finished && (b.StartsAt == null || b.StartsAt <= now) && (b.ImportedAt == null || b.ImportedAt < refresh))
                .OrderBy(b => b.ImportedAt.HasValue).ThenByDescending(b => b.StartsAt).ToListAsync(ct);
            var done = 0;
            foreach (var b in due)
            {
                if (DateTime.UtcNow - started >= budget) break;
                try
                {
                    await ImportAsync(b, ct);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
                {
                    _logger.LogWarning(e, "LeagueHub: Übertragung {Tour} nicht eingespielt", b.TourId);
                    b.Error = Cut(e.Message, 300);
                    b.ImportedAt = DateTime.UtcNow;                           // nicht gleich wieder, beim nächsten Takt
                    await SaveAsync(b, ct);
                }
                done++;
            }
            return done < due.Count;
        }
        catch (LeagueOnlineSync.RateLimitedException e)
        {
            _logger.LogWarning("LeagueHub: {Message} — Übertragungen pausiert", e.Message);
            return true;
        }
    }

    /// <summary>Alle vorgemerkten Übertragungen (jüngste zuerst) als JSON für die Verwaltung.</summary>
    public async Task<List<object>> ListAsync(CancellationToken ct) =>
        (await _db.LeagueBroadcasts.AsNoTracking().OrderByDescending(b => b.StartsAt).ToListAsync(ct))
        .Select(b => (object)new
        {
            tourId = b.TourId, name = b.Name, location = b.Location, url = $"https://lichess.org/broadcast/-/{b.TourId}",
            startsAt = b.StartsAt?.ToString("yyyy-MM-dd"), endsAt = b.EndsAt?.ToString("yyyy-MM-dd"), manual = b.Manual,
            importedAt = b.ImportedAt is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("O") : null,
            finished = b.Finished, games = b.Games, error = b.Error,
        }).ToList();
}
