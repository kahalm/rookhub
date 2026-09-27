using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// LeagueHub: fertig gerechnete Liga-Ansichten, Spielerkarten, PGN und Teilen-Links.
///
/// <para>Die Ansichten werden beim Aktualisieren EINMAL gerechnet und als JSON abgelegt
/// (<see cref="LeagueView"/>) — eine Liga zu rechnen braucht die ganze Historie im Speicher, das soll
/// nicht bei jedem Seitenaufruf passieren.</para>
/// </summary>
public sealed class LeagueService
{
    public const int ShareKeepDays = 7;
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    private readonly AppDbContext _db;
    private readonly LeagueModel _model;
    private readonly ILogger<LeagueService> _log;

    public LeagueService(AppDbContext db, LeagueModel model, ILogger<LeagueService> log)
    {
        _db = db; _model = model; _log = log;
    }

    public async Task<string?> CurrentSeasonAsync(CancellationToken ct) =>
        await _db.LeagueTournaments.MaxAsync(t => (string?)t.Season, ct);

    public async Task<LeagueWorld> LoadWorldAsync(CancellationToken ct) => new(
        await _db.LeagueTournaments.AsNoTracking().ToListAsync(ct),
        await _db.LeagueRounds.AsNoTracking().ToListAsync(ct),
        await _db.LeagueMatches.AsNoTracking().ToListAsync(ct),
        await _db.LeagueGames.AsNoTracking().ToListAsync(ct),
        await _db.LeaguePlayers.AsNoTracking().ToListAsync(ct));

    /// <summary>Alle Ligen der laufenden Saison neu rechnen und ablegen.</summary>
    public async Task<int> RebuildViewsAsync(CancellationToken ct)
    {
        var w = await LoadWorldAsync(ct);
        if (w.Seasons.Count == 0) return 0;
        var season = w.Seasons[^1];
        // Nur die Zählung: ohne Projektion käme jede Zeile samt Pgn/ProfileJson (bis ~2 MB je Spieler) mit —
        // die Selektoren von ToDictionaryAsync laufen erst im Client.
        var counts = await _db.LeaguePlayerProfiles.AsNoTracking().Select(p => new { p.FideId, p.GameCount })
            .ToDictionaryAsync(p => p.FideId, p => p.GameCount, ct);
        var accounts = (await _db.LeagueOnlineAccounts.AsNoTracking().ToListAsync(ct))
            .GroupBy(a => a.FideId).ToDictionary(g => g.Key, g => g.ToList());
        var builder = new LeagueViewBuilder(w, _model, counts, accounts);
        var now = DateTime.UtcNow;
        var n = 0;
        foreach (var t in w.T.Values.Where(t => t.Season == season && t.Stage == "Liga"))
        {
            var json = builder.Build(t.Tnr, w.Games).ToJsonString(Compact);
            var row = await _db.LeagueViews.FindAsync(new object[] { t.Tnr }, ct);
            if (row is null) _db.LeagueViews.Add(new LeagueView { Tnr = t.Tnr, Json = json, GeneratedAt = now });
            else { row.Json = json; row.GeneratedAt = now; }
            n++;
        }
        await _db.SaveChangesAsync(ct);
        await CleanupSharesAsync(ct);
        _log.LogInformation("LeagueHub: {Count} Ligen der Saison {Season} neu gerechnet", n, season);
        return n;
    }

    public async Task<JsonObject> IndexAsync(CancellationToken ct)
    {
        var season = await CurrentSeasonAsync(ct);
        var ts = await _db.LeagueTournaments.AsNoTracking().Where(t => t.Season == season && t.Stage == "Liga")
            .OrderBy(t => t.Level).ThenBy(t => t.Grp).ToListAsync(ct);
        var views = await _db.LeagueViews.AsNoTracking().Where(v => ts.Select(t => t.Tnr).Contains(v.Tnr))
            .Select(v => new { v.Tnr, v.GeneratedAt }).ToListAsync(ct);
        var generated = views.Count > 0 ? views.Max(v => v.GeneratedAt) : (DateTime?)null;
        return new JsonObject
        {
            ["season"] = season,
            ["generated"] = generated is null ? null : ToLocal(generated.Value).ToString("dd.MM.yyyy HH:mm"),
            ["leagues"] = new JsonArray(ts.Where(t => views.Any(v => v.Tnr == t.Tnr)).Select(t => (JsonNode)new JsonObject
            {
                ["tnr"] = t.Tnr, ["name"] = t.League + (string.IsNullOrEmpty(t.Grp) ? "" : $" {t.Grp}"),
            }).ToArray()),
        };
    }

    private static DateTime ToLocal(DateTime utc)
    {
        try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna")); }
        catch (TimeZoneNotFoundException) { return utc; }
    }

    public async Task<string?> LeagueJsonAsync(int tnr, CancellationToken ct) =>
        (await _db.LeagueViews.AsNoTracking().FirstOrDefaultAsync(v => v.Tnr == tnr, ct))?.Json;

    /// <summary>Spielerkarte: Eröffnungsprofil + Online-Konten (<paramref name="onlySure"/>: nur „sicher" — für Teilen-Links).</summary>
    public async Task<JsonObject?> CardAsync(string fide, bool onlySure, CancellationToken ct)
    {
        var p = await _db.LeaguePlayerProfiles.AsNoTracking().Where(x => x.FideId == fide)
            .Select(x => new { x.ProfileJson, x.Name, x.GameCount }).FirstOrDefaultAsync(ct);
        var acc = await _db.LeagueOnlineAccounts.AsNoTracking().Where(a => a.FideId == fide).ToListAsync(ct);
        if (p is null && acc.Count == 0) return null;
        var card = p is null ? new JsonObject { ["fide"] = fide, ["n"] = 0 } : JsonNode.Parse(p.ProfileJson)!.AsObject();
        card["accounts"] = new JsonArray(acc.Where(a => !onlySure || a.Confidence == "sicher")
            .Select(a => (JsonNode)new JsonObject { ["site"] = a.Site, ["user"] = a.UserName, ["url"] = a.Url, ["conf"] = a.Confidence })
            .ToArray());
        return card;
    }

    public async Task<(string Name, string Pgn)?> PgnAsync(string fide, CancellationToken ct)
    {
        var p = await _db.LeaguePlayerProfiles.AsNoTracking().Where(x => x.FideId == fide)
            .Select(x => new { x.Name, x.Pgn }).FirstOrDefaultAsync(ct);
        return p is null || string.IsNullOrEmpty(p.Pgn) ? null : (p.Name, p.Pgn);
    }

    // ---- Teilen-Links -------------------------------------------------------------------------------

    private async Task<(JsonObject League, JsonObject Fixture)?> FixtureAsync(int tnr, int round, string team, CancellationToken ct)
    {
        var json = await LeagueJsonAsync(tnr, ct);
        if (json is null) return null;
        var l = JsonNode.Parse(json)!.AsObject();
        var fx = l["fixtures"]?[team]?[round.ToString()]?.AsObject();
        return fx is null ? null : (l, fx);
    }

    private static bool Shareable(JsonObject fx) =>
        fx["bye"] is null && fx["status"]?.GetValue<string>() is "open" or "played" && fx["boards"] is JsonArray;

    public static string NewToken()
    {
        var buf = RandomNumberGenerator.GetBytes(18);   // 144 Bit
        return Convert.ToBase64String(buf).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>Ablauf: <see cref="ShareKeepDays"/> nach der Runde — bei einer schon gespielten Runde aber nie vor
    /// heute + <see cref="ShareKeepDays"/>, sonst wäre ein frisch angelegter Link zu einer älteren Runde sofort tot.</summary>
    public static DateOnly ExpiresFor(string? date, DateOnly today)
    {
        var d = (date ?? "").Split(' ').LastOrDefault();
        var day = LeagueDates.Parse(d) ?? today.AddDays(30 - ShareKeepDays);
        if (day < today) day = today;
        return day.AddDays(ShareKeepDays);
    }

    /// <summary>Link anlegen (gleiche Begegnung = gleicher Link, solange er gilt). null = nicht teilbar.</summary>
    public async Task<LeagueShare?> CreateShareAsync(int tnr, int round, string team, int? userId, CancellationToken ct)
    {
        var f = await FixtureAsync(tnr, round, team, ct);
        if (f is null || !Shareable(f.Value.Fixture)) return null;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var existing = await _db.LeagueShares.FirstOrDefaultAsync(s => s.Tnr == tnr && s.Round == round && s.Team == team, ct);
        if (existing is not null)
        {
            if (existing.Expires >= today) return existing;
            // Abgelaufen, aber noch nicht aufgeräumt (das passiert nur beim Aktualisieren): der eindeutige Index
            // ließe keinen zweiten Link zu, und den toten zurückzugeben hieße „Link ungültig" beim Empfänger.
            _db.LeagueShares.Remove(existing);
        }
        var share = new LeagueShare
        {
            Token = NewToken(), Tnr = tnr, Round = round, Team = team, CreatedByUserId = userId, CreatedAt = DateTime.UtcNow,
            Expires = ExpiresFor(f.Value.Fixture["date"]?.GetValue<string>(), today),
        };
        _db.LeagueShares.Add(share);
        await _db.SaveChangesAsync(ct);
        return share;
    }

    public Task<LeagueShare?> FindShareAsync(int tnr, int round, string team, CancellationToken ct) =>
        _db.LeagueShares.AsNoTracking().FirstOrDefaultAsync(s => s.Tnr == tnr && s.Round == round && s.Team == team, ct);

    public async Task<bool> DeleteShareAsync(string token, CancellationToken ct)
    {
        var s = await _db.LeagueShares.FindAsync(new object[] { token }, ct);
        if (s is null) return false;
        _db.LeagueShares.Remove(s);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<LeagueShare?> ValidShareAsync(string token, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await _db.LeagueShares.AsNoTracking().FirstOrDefaultAsync(s => s.Token == token && s.Expires >= today, ct);
    }

    /// <summary>Öffentliche Ansicht eines Links: genau diese Begegnung, Online-Konten nur „sicher".</summary>
    public async Task<JsonObject?> PublicShareAsync(string token, CancellationToken ct)
    {
        var s = await ValidShareAsync(token, ct);
        if (s is null) return null;
        var f = await FixtureAsync(s.Tnr, s.Round, s.Team, ct);
        if (f is null || !Shareable(f.Value.Fixture)) return null;
        var fx = f.Value.Fixture.DeepClone().AsObject();
        foreach (var r in fx["roster"]?.AsArray() ?? new JsonArray())
        {
            var acc = r?["acc"]?.AsArray();
            if (acc is null) continue;
            r!["acc"] = new JsonArray(acc.Where(a => a?["conf"]?.GetValue<string>() == "sicher").Select(a => a!.DeepClone()).ToArray());
        }
        var gen = await _db.LeagueViews.AsNoTracking().Where(v => v.Tnr == s.Tnr).Select(v => v.GeneratedAt).FirstOrDefaultAsync(ct);
        return new JsonObject
        {
            ["league"] = f.Value.League["name"]?.DeepClone(), ["season"] = f.Value.League["season"]?.DeepClone(),
            ["round"] = s.Round, ["team"] = s.Team, ["fixture"] = fx,
            ["generated"] = ToLocal(gen).ToString("dd.MM.yyyy HH:mm"), ["expires"] = s.Expires.ToString("yyyy-MM-dd"),
        };
    }

    /// <summary>Darf dieser Link die Karte/PGN dieses Spielers zeigen? (Nur Spieler der geteilten Meldeliste.)</summary>
    public async Task<bool> ShareCoversAsync(string token, string fide, CancellationToken ct)
    {
        var v = await PublicShareAsync(token, ct);
        return v?["fixture"]?["roster"]?.AsArray().Any(r => r?["fide"]?.GetValue<string>() == fide) == true;
    }

    private async Task CleanupSharesAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var old = await _db.LeagueShares.Where(s => s.Expires < today).ToListAsync(ct);
        if (old.Count == 0) return;
        _db.LeagueShares.RemoveRange(old);
        await _db.SaveChangesAsync(ct);
    }
}
