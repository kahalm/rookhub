using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly IMemoryCache? _cache;

    /// <param name="cache">Der Cache der zerlegten Karten-Partien und geteilten Meldelisten
    /// (<see cref="LeagueProfileStore.CacheServiceKey"/>, Codereview N4-003); ohne (Tests) wird je Aufruf gerechnet.</param>
    public LeagueService(AppDbContext db, LeagueModel model, ILogger<LeagueService> log,
        [FromKeyedServices(LeagueProfileStore.CacheServiceKey)] IMemoryCache? cache = null)
    {
        _db = db; _model = model; _log = log; _cache = cache;
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
        var leagues = w.T.Values.Where(t => t.Season == season && t.Stage == "Liga").ToList();
        var tnrs = leagues.Select(t => t.Tnr).ToList();
        for (var attempt = 1; ; attempt++)
        {
            // Die Ansichten VOR Zählwerten und Konten laden (W5 N4-007): zieht ein Patch (hochgeladene Partie, Konto)
            // dazwischen nach, ist die Zeile beim Speichern eine andere (Json = Concurrency-Token) — dann neu lesen und
            // rechnen, statt seinen Stand mit den älteren Zählwerten zu überschreiben.
            var rows = await _db.LeagueViews.Where(v => tnrs.Contains(v.Tnr)).ToDictionaryAsync(v => v.Tnr, ct);
            // Nur die Zählung: ohne Projektion käme jede Zeile samt Pgn/ProfileJson (bis ~2 MB je Spieler) mit —
            // die Selektoren von ToDictionaryAsync laufen erst im Client.
            var counts = await _db.LeaguePlayerProfiles.AsNoTracking().Select(p => new { p.FideId, p.GameCount })
                .ToDictionaryAsync(p => p.FideId, p => p.GameCount, ct);
            // Konten Minderjähriger stehen nie in der Meldeliste (LeagueHiddenAccounts).
            var hidden = await LeagueHiddenAccounts.FidesAsync(_db, null, ct);
            var accounts = (await _db.LeagueOnlineAccounts.AsNoTracking().ToListAsync(ct))
                .Where(a => !hidden.Contains(a.FideId))
                .GroupBy(a => a.FideId).ToDictionary(g => g.Key, g => g.ToList());
            var builder = new LeagueViewBuilder(w, _model, counts, accounts);
            var now = DateTime.UtcNow;
            var n = 0;
            foreach (var t in leagues)
            {
                var json = builder.Build(t.Tnr, w.Games).ToJsonString(Compact);
                if (!rows.TryGetValue(t.Tnr, out var row)) _db.LeagueViews.Add(new LeagueView { Tnr = t.Tnr, Json = json, GeneratedAt = now });
                else { row.Json = json; row.GeneratedAt = now; }
                n++;
            }
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) when (attempt < ViewWriteAttempts)
            {
                DetachViews(_db);
                continue;
            }
            await CleanupSharesAsync(ct);
            _log.LogInformation("LeagueHub: {Count} Ligen der Saison {Season} neu gerechnet", n, season);
            return n;
        }
    }

    /// <summary>So oft versucht ein Schreiber der Ansichten es, wenn ihm ein anderer dazwischen geschrieben hat.</summary>
    internal const int ViewWriteAttempts = 5;

    /// <summary>
    /// Die fertigen Ansichten nachziehen, ohne die Ligen neu zu rechnen (Partienzahl, Online-Konten): <paramref name="patch"/>
    /// bekommt das JSON einer Ansicht und liefert das geänderte oder <c>null</c> (unverändert). Speichert selbst.
    /// <para>Konkurrenzschutz (W5 N4-007): <see cref="LeagueView.Json"/> ist Concurrency-Token. Hat „Daten aktualisieren"
    /// oder ein paralleler Patch die Zeile seit dem Laden geändert, wird neu geladen und erneut gepatcht — sonst schriebe
    /// dieser Patch sein geändertes ALTES JSON über die frisch gerechnete Prognose bzw. die Korrektur des anderen.</para>
    /// </summary>
    internal static async Task PatchViewsAsync(AppDbContext db, Func<string, string?> patch, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            foreach (var view in await db.LeagueViews.ToListAsync(ct))
                if (patch(view.Json) is { } json) view.Json = json;
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < ViewWriteAttempts)
            {
                DetachViews(db);
            }
        }
    }

    /// <summary>Geladene Ansichten vergessen, damit der nächste Versuch den Stand aus der Datenbank liest (ein noch
    /// verfolgter Eintrag käme sonst mit seinen alten Werten zurück).</summary>
    private static void DetachViews(AppDbContext db)
    {
        foreach (var e in db.ChangeTracker.Entries<LeagueView>().ToList()) e.State = EntityState.Detached;
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

    /// <summary>
    /// Wie oft die Prognose in den bisherigen Runden der laufenden Saison getroffen hat (0.650.0, Wunsch 2026-10-04: „Statistik
    /// auf Runden aufdröseln und auf Ligen und gesamt — nicht nur für Schwaz, sondern für alle Begegnungen"). Zählt jede
    /// gespielte Begegnung aus Sicht beider Teams (je eine Prognose für die Aufstellung des Gegners) aus dem Feld <c>eval</c>
    /// der fertig gerechneten Ansichten → <c>{ season, total, rounds[], leagues[{ tnr, name, rounds[], … }] }</c>, je Eintrag
    /// <c>fixtures, players, boards, of</c>. Ansichten aus der Zeit vor 0.650.0 haben kein <c>eval</c> — die zählen erst nach
    /// „Daten aktualisieren". Gemerkt, bis eine Ansicht neu gerechnet wird.
    /// </summary>
    public async Task<JsonObject> ForecastStatsAsync(CancellationToken ct)
    {
        var season = await CurrentSeasonAsync(ct);
        var ts = await _db.LeagueTournaments.AsNoTracking().Where(t => t.Season == season && t.Stage == "Liga")
            .OrderBy(t => t.Level).ThenBy(t => t.Grp).ToListAsync(ct);
        var tnrs = ts.Select(t => t.Tnr).ToList();
        var stamp = await _db.LeagueViews.AsNoTracking().Where(v => tnrs.Contains(v.Tnr)).Select(v => v.GeneratedAt).ToListAsync(ct);
        var key = $"league-forecast-stats:{season}:{stamp.Count}:{(stamp.Count > 0 ? stamp.Max().Ticks : 0)}";
        if (_cache?.TryGetValue(key, out JsonObject? hit) == true && hit is not null) return (JsonObject)hit.DeepClone();

        var views = await _db.LeagueViews.AsNoTracking().Where(v => tnrs.Contains(v.Tnr)).ToDictionaryAsync(v => v.Tnr, v => v.Json, ct);
        var total = new Tally();
        var byRound = new SortedDictionary<int, Tally>();
        var leagues = new JsonArray();
        foreach (var t in ts)
        {
            if (!views.TryGetValue(t.Tnr, out var json) || JsonNode.Parse(json)?["fixtures"] is not JsonObject teams) continue;
            var lt = new Tally();
            var lr = new SortedDictionary<int, Tally>();
            foreach (var (_, rounds) in teams)
                foreach (var (rnd, fx) in rounds?.AsObject() ?? new JsonObject())
                {
                    if (fx?["eval"] is not JsonObject ev || !int.TryParse(rnd, out var r)) continue;
                    int P(string k) => ev[k]?.GetValue<int>() ?? 0;
                    var (pl, bo, of) = (P("players"), P("boards"), P("of"));
                    foreach (var x in new[] { total, lt, Get(byRound, r), Get(lr, r) }) x.Add(pl, bo, of);
                }
            if (lt.Fixtures == 0) continue;
            var lo = lt.ToJson();
            lo["tnr"] = t.Tnr;
            lo["name"] = t.League + (string.IsNullOrEmpty(t.Grp) ? "" : $" {t.Grp}");
            lo["rounds"] = Rounds(lr);
            leagues.Add(lo);
        }
        var res = new JsonObject { ["season"] = season, ["total"] = total.ToJson(), ["rounds"] = Rounds(byRound), ["leagues"] = leagues };
        _cache?.Set(key, res, TimeSpan.FromHours(6));
        return (JsonObject)res.DeepClone();

        static Tally Get(SortedDictionary<int, Tally> d, int r) => d.TryGetValue(r, out var x) ? x : d[r] = new Tally();
        static JsonArray Rounds(SortedDictionary<int, Tally> d) => new(d.Select(kv =>
        {
            var o = kv.Value.ToJson();
            o["round"] = kv.Key;
            return (JsonNode)o;
        }).ToArray());
    }

    private sealed class Tally
    {
        public int Fixtures, Players, Boards, Of;
        public void Add(int players, int boards, int of) { Fixtures++; Players += players; Boards += boards; Of += of; }
        public JsonObject ToJson() => new() { ["fixtures"] = Fixtures, ["players"] = Players, ["boards"] = Boards, ["of"] = Of };
    }

    /// <summary>Spielerkarte: Eröffnungsprofil + Online-Konten (<paramref name="onlySure"/>: nur „sicher" — für Teilen-Links).
    /// <paramref name="reveal"/> = ein Admin fragt: Konten Minderjähriger vollständig (0.625.0) — nie zusammen mit <paramref name="onlySure"/>.
    /// <paramref name="prep"/>: die Spielervorbereitung fragt (0.638.0) — nur dann trägt ein Spieler ohne Meldeliste und Liga-Karte allein
    /// mit seinen Konten eine Karte; LeagueHub kennt ihn nicht, wie vor 0.637.0.</summary>
    public async Task<JsonObject?> CardAsync(string fide, bool onlySure, CancellationToken ct, bool reveal = false, bool prep = false)
    {
        var p = await _db.LeaguePlayerProfiles.AsNoTracking().Where(x => x.FideId == fide)
            .Select(x => new { x.ProfileJson, x.Name, x.GameCount }).FirstOrDefaultAsync(ct);
        var acc = await _db.LeagueOnlineAccounts.AsNoTracking().Where(a => a.FideId == fide).ToListAsync(ct);
        if (p is null && (acc.Count == 0 || !prep && !await LeagueOnlineAccountService.LeagueKnowsAsync(_db, fide, ct))) return null;
        var card = p is null ? new JsonObject { ["fide"] = fide, ["n"] = 0 } : JsonNode.Parse(p.ProfileJson)!.AsObject();
        var shown = acc.Where(a => !onlySure || a.Confidence == LeagueOnlineAccountService.Sure).OrderBy(a => a.Id).ToList();
        // Minderjährige (0.610.0): angemeldet steht nur DASS es ein Konto gibt, über einen Teilen-Link gar nichts.
        var hidden = (await LeagueHiddenAccounts.FidesAsync(_db, new[] { fide }, ct)).Contains(fide);
        card["accounts"] = new JsonArray((hidden && onlySure ? new List<LeagueOnlineAccount>() : shown)
            .Select(a => (JsonNode)LeagueOnlineAccountService.ToJson(a, full: !onlySure, hidden: hidden, reveal: reveal && !onlySure)).ToArray());
        // Online-Partien der gezeigten Konten — der Baum kann sie einbeziehen, auch ohne eine einzige Brettpartie. Die der
        // UNSICHEREN zählt der Baum seit 0.612.0 nur auf Wunsch mit (Schalter „auch unsichere Konten").
        card["online"] = shown.Sum(a => a.GameCount);
        card["onlineUnsure"] = shown.Where(a => a.Confidence != LeagueOnlineAccountService.Sure).Sum(a => a.GameCount);
        return card;
    }

    /// <summary>Alle Partien des Spielers — die fremden UND die der Vereins-Datenbank (auch auf Teilen-Links: „pgn sind
    /// nicht geschützt", Wunsch des Nutzers).</summary>
    public Task<(string Name, string Pgn)?> PgnAsync(string fide, CancellationToken ct) => new LeagueProfileStore(_db, _cache).PgnAsync(fide, ct);

    /// <summary>Fremde Partiesammlung einspielen (<see cref="LeagueProfileStore.ImportGamesAsync"/>).</summary>
    public Task<(int Games, int Players)> ImportGamesAsync(string pgn, string source, CancellationToken ct) =>
        new LeagueProfileStore(_db).ImportGamesAsync(pgn, source, ct);

    /// <summary>Die letzten Partien der Karte samt PGN (<see cref="LeagueProfileStore.RecentAsync"/>).</summary>
    public Task<JsonObject?> RecentAsync(string fide, CancellationToken ct, string? color = null) =>
        new LeagueProfileStore(_db, _cache).RecentAsync(fide, ct, color);

    /// <summary>Eröffnungsbaum des Spielers mit einer Farbe ab einer Zugfolge (<see cref="LeagueProfileStore.TreeAsync"/>).</summary>
    public Task<JsonObject?> TreeAsync(string fide, string color, string? line, CancellationToken ct,
        LeagueProfileStore.TreeFilter? filter = null) =>
        new LeagueProfileStore(_db, _cache).TreeAsync(fide, color is "s" or "b" ? "s" : "w", line, ct, filter);

    /// <summary>Eröffnungsprofil der Karte über gefilterte Partien (<see cref="LeagueProfileStore.ProfileAsync"/>, 0.617.0).</summary>
    public Task<JsonObject?> ProfileAsync(string fide, CancellationToken ct, LeagueProfileStore.TreeFilter? filter = null) =>
        new LeagueProfileStore(_db, _cache).ProfileAsync(fide, ct, filter);

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

    /// <summary>144 Bit statt der 128 der anderen Teilen-Links; ohne Eindeutigkeits-Abfrage, weil das Token hier der
    /// Primaerschluessel ist (eine Kollision scheitert laut am Insert).</summary>
    public static string NewToken() => ShareTokens.New(18);

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

    public async Task<bool> DeleteShareAsync(string token, CancellationToken ct)
    {
        var s = await _db.LeagueShares.FindAsync(new object[] { token }, ct);
        if (s is null) return false;
        _db.LeagueShares.Remove(s);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Gilt dieser Teilen-Link noch? (Die Upload-Wege ohne Anmeldung hängen daran.)</summary>
    public async Task<bool> ShareValidAsync(string token, CancellationToken ct) => await ValidShareAsync(token, ct) != null;

    /// <summary>Die Liga (Turnier-Nr.) eines gültigen Teilen-Links, sonst <c>null</c> (0.628.0, Spalte „Liga" der Quellen-Tabelle).</summary>
    public async Task<int?> ShareTnrAsync(string token, CancellationToken ct) => (await ValidShareAsync(token, ct))?.Tnr;

    /// <summary>Das Token des gültigen Links in der Schreibweise SEINER Zeile — <c>null</c> = kein gültiger Link. Die
    /// Spalte vergleicht in MariaDB groß/klein- und akzent-blind (Collation der Datenbank): „abc…" und „Ábc…" finden auch
    /// den Link „AbC…". Was am Link hängt (Vermerk an den Partien, Deckel je Link), hängt deshalb an diesem Token und
    /// nie am Wert aus der Route — sonst bekäme jede Schreibweise ihren eigenen Deckel, und der Rückbau per Original
    /// fände ihre Partien nicht (Codereview 2026-09-29, A2-009).</summary>
    public async Task<string?> ValidShareTokenAsync(string token, CancellationToken ct) => (await ValidShareAsync(token, ct))?.Token;

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

    /// <summary>Darf dieser Link die Karte/PGN dieses Spielers zeigen? (Nur Spieler der geteilten Meldeliste.) Jeder Klick
    /// auf einer geteilten Karte fragt das, auch jeder im Eröffnungsbaum — die Meldeliste kommt deshalb je Ansicht EINMAL
    /// aus dem Liga-JSON (<see cref="ShareRosterAsync"/>), statt je Anfrage die ganze Liga zu parsen und die Begegnung zu
    /// klonen (Codereview 2026-09-29, N4-003).</summary>
    public async Task<bool> ShareCoversAsync(string token, string fide, CancellationToken ct)
    {
        var s = await ValidShareAsync(token, ct);
        return s is not null && (await ShareRosterAsync(s, ct)).Contains(fide);
    }

    private sealed record ShareRosterKey(int Tnr, int Round, string Team, DateTime GeneratedAt);

    /// <summary>Die FIDE-IDs der Meldeliste einer geteilten Begegnung (leer, wenn sie nicht teilbar ist) — im Cache bis zum
    /// nächsten Rechnen der Ansicht (<see cref="LeagueView.GeneratedAt"/>). Was danach noch in das JSON geschrieben wird
    /// (Partienzahlen, Konten), ändert die Meldeliste nicht.</summary>
    private async Task<HashSet<string>> ShareRosterAsync(LeagueShare s, CancellationToken ct)
    {
        var generated = await _db.LeagueViews.AsNoTracking().Where(v => v.Tnr == s.Tnr)
            .Select(v => (DateTime?)v.GeneratedAt).FirstOrDefaultAsync(ct);
        if (generated is null) return new HashSet<string>(StringComparer.Ordinal);
        var key = new ShareRosterKey(s.Tnr, s.Round, s.Team, generated.Value);
        if (_cache != null && _cache.TryGetValue(key, out HashSet<string>? hit) && hit != null) return hit;
        var f = await FixtureAsync(s.Tnr, s.Round, s.Team, ct);
        var fides = f is null || !Shareable(f.Value.Fixture) ? new HashSet<string>(StringComparer.Ordinal)
            : (f.Value.Fixture["roster"]?.AsArray() ?? new JsonArray()).Select(r => r?["fide"]?.GetValue<string>())
                .OfType<string>().ToHashSet(StringComparer.Ordinal);
        _cache?.Set(key, fides, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = LeagueProfileStore.CacheIdle });
        return fides;
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
