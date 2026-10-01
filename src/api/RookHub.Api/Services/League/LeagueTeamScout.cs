using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Konto-Vorschläge aus dem Umfeld der Tiroler Vereine auf Lichess (0.612.0, Wunsch 2026-09-30: „auf Lichess gibt es Teams",
/// „schau, was Schach Tirol sonst noch organisiert hat"). Die Namenssuche (<see cref="LeagueAccountFinder"/>) findet nur Konten,
/// deren Nutzername aus dem Namen kommt — „Katzenpapa" oder „Trigonias" nie. Hier kommen die Konten aus zwei Quellen:
/// <list type="bullet">
/// <item><b>Mitglieder</b> der Tiroler Lichess-Teams (gefunden über die Team-Suche nach Tiroler Orten, <see cref="DefaultPlaces"/>,
///   nur Teams mit dem Ort im Namen);</item>
/// <item><b>Team-Battles</b> dieser Teams (Online-TMM 2021, Quarantäne-Liga …): wer für ein Tiroler Team gespielt hat, gehört zu
///   diesem Verein.</item>
/// </list>
/// Jedes Konto wird einmal geprüft (<see cref="RecheckDays"/>): steht ein Klarname im Profil, der zu einem Spieler der Saison
/// passt, gilt das Urteil der Namenssuche (Land, Wertung, Vorname …) mit der Team-Herkunft als Hinweis. Ohne Klarname, aber mit
/// Verein aus einem Team-Battle, entscheiden die Stellungen (<see cref="LeagueFingerprint"/>) unter den Spielern DES Vereins —
/// nur mit mindestens <see cref="ClubMargin"/>-fachem Abstand zum Zweiten (an der Meldeliste der Online-TMM 2021 geprüft: 11 von
/// 13 richtig). Vorschläge für Minderjährige bleiben verborgen wie überall (<see cref="LeagueHiddenAccounts"/>).
/// </summary>
public sealed partial class LeagueTeamScout
{
    public static readonly string[] DefaultPlaces =
    {
        "Tirol", "Innsbruck", "Schwaz", "Kufstein", "Wörgl", "Telfs", "Jenbach", "Absam", "Zirl", "Landeck", "Imst", "Lienz",
        "Rattenberg", "Zillertal", "Wattens", "Kitzbühel", "Reutte", "Hall", "Mils", "Fügen", "Kundl",
    };
    public const double ClubMargin = 1.3;
    /// <summary>Die gemeinsamen Stellungen müssen im Schnitt mindestens bis zu diesem Halbzug reichen (0.614.0). In offenen
    /// Team-Battles (Quarantäne-Liga) sind viele Teilnehmer keine Ligaspieler — „passt am besten unter den Vereinsspielern" traf dort
    /// schon mit dem ersten Zug allein (gemessen: 1,6 und 2,4 bei zwei falschen Treffern; die bestätigten der Online-TMM 2021 meist 4–11).</summary>
    public const double MinDepth = 4.0;
    /// <summary><see cref="LeagueAccountSuggestion.Source"/> der Vorschläge dieser Suche.</summary>
    public const string Source = "team";
    public const int MinGames = 20, MaxGames = 100, RecheckDays = 90;
    /// <summary>Teams, Mitglieder und Team-Battles werden so oft neu gelesen.</summary>
    public static readonly TimeSpan PoolEvery = TimeSpan.FromDays(30);
    /// <summary>So viele Profile fragt ein <c>POST /api/users</c> auf einmal.</summary>
    public const int ProfileChunk = 100;

    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly ILogger<LeagueTeamScout> _logger;
    private readonly string _lichess;
    private readonly string[] _places;

    public LeagueTeamScout(AppDbContext db, HttpClient http, ILogger<LeagueTeamScout> logger, IConfiguration? config = null)
    {
        _db = db;
        _http = http;
        _logger = logger;
        _lichess = (config?["Lichess:SiteUrl"] ?? "https://lichess.org").TrimEnd('/');
        var p = config?["LeagueOnline:TeamPlaces"];
        _places = string.IsNullOrWhiteSpace(p) ? DefaultPlaces : p.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Ein Wiederholversuch je Abruf (also hoechstens zwei Anfragen) — siehe <see cref="GetAsync"/>.</summary>
    private const int MaxAttempts = 2;

    /// <summary>Pause vor dem Wiederholversuch; im Test 0.</summary>
    public TimeSpan RetryPause { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Pause zwischen den Abrufen des BESTANDS-Aufbaus. Deutlich länger als <see cref="Pause"/>, weil der Aufbau
    /// hunderte Abrufe in Folge macht: am 01.10.2026 auf Prod trat Lichess' Drossel nach 54 Abrufen in 61 s zu
    /// (also schon bei einer Sekunde Abstand), und weil früher erst am ENDE gespeichert wurde, war jeder Anlauf
    /// vollständig verloren.
    /// </summary>
    public TimeSpan PoolPause { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Wie oft der Bestands-Aufbau eine Drossel aussitzt, bevor er den Durchgang doch beendet; im Test 0 oder 2.</summary>
    public int RateLimitWaits { get; init; } = 3;

    /// <summary>Wartezeit nach einer Drossel (Lichess empfiehlt eine Minute); im Test ~0.</summary>
    public TimeSpan RateLimitCooldown { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Nach so vielen abgearbeiteten Teams bzw. Team-Battles wird zwischengespeichert.</summary>
    private const int SaveEvery = 25;

    // ── Lesen (rein, getestet) ──────────────────────────────────────────────────────────────────

    /// <summary>Beide Schreibweisen eines Orts, klein: „Wörgl" → „woergl" und „worgl".</summary>
    private static IEnumerable<string> Forms(string place)
    {
        var a = LeagueAccountFinder.Plain(place).ToLowerInvariant();
        var b = new string(place.Normalize(NormalizationForm.FormD).Where(c => c < 128).ToArray()).ToLowerInvariant();
        return new[] { a, b }.Distinct();
    }

    private static string Norm(string? s) => Regex.Replace(LeagueAccountFinder.Plain(s).ToLowerInvariant(), "[^a-z0-9 ]", " ");

    /// <summary>Die Orte, die als Wort im Namen stehen („Spielgemeinschaft Kufstein / Wörgl" → kufstein, woergl).</summary>
    public static List<string> ClubKeys(string? name, IEnumerable<string> places)
    {
        var n = " " + Norm(name) + " ";
        var alt = " " + Regex.Replace(new string((name ?? "").Normalize(NormalizationForm.FormD).Where(c => c < 128).ToArray()).ToLowerInvariant(), "[^a-z0-9 ]", " ") + " ";
        return places.Where(p => Forms(p).Any(f => Regex.IsMatch(n, $@"\b{Regex.Escape(f)}\b") || Regex.IsMatch(alt, $@"\b{Regex.Escape(f)}\b")))
            .Select(p => Forms(p).First()).Distinct().ToList();
    }

    public static bool IsLocalTeam(string? name, IEnumerable<string> places) => ClubKeys(name, places).Count > 0;

    /// <summary>
    /// Nennt der Nutzername einen Vornamen aus den Meldelisten, der NICHT der des Spielers ist („Markus_Ragger" für Herbert,
    /// „Peter-Dorfen" für Giorgio)? → dieser Vorname, sonst <c>null</c>. Getrennt wird an Nicht-Buchstaben und an Binnen-Großbuchstaben.
    /// </summary>
    public static string? OtherFirstName(string user, string playerName, IReadOnlySet<string> firstNames)
    {
        var own = LeagueAccountFinder.Tokens(LeagueAccountFinder.SplitName(playerName).First).ToHashSet();
        var spaced = Regex.Replace(Regex.Replace(user, "([a-zäöüß])([A-ZÄÖÜ])", "$1 $2"), "[^A-Za-zÄÖÜäöüß]+", " ");
        return LeagueAccountFinder.Tokens(spaced).FirstOrDefault(t => t.Length >= 3 && firstNames.Contains(t) && !own.Contains(t));
    }

    /// <summary><c>GET /api/team/search</c> → (Kennung, Name).</summary>
    public static List<(string Id, string Name)> ParseTeamSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("currentPageResults", out var arr) || arr.ValueKind != JsonValueKind.Array) return new();
        return arr.EnumerateArray().Select(t => (Str(t, "id"), Str(t, "name"))).Where(t => t.Item1 is not null && t.Item2 is not null)
            .Select(t => (t.Item1!, t.Item2!)).ToList();
    }

    /// <summary>ndjson-Zeilen (Mitglieder, Turnierliste, Ergebnisse) als Elemente — kaputte Zeilen fallen weg.</summary>
    private static IEnumerable<JsonElement> Lines(string ndjson)
    {
        foreach (var raw in ndjson.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            JsonElement e;
            try { e = JsonDocument.Parse(line).RootElement.Clone(); }
            catch (JsonException) { continue; }
            yield return e;
        }
    }

    /// <summary><c>GET /api/team/{id}/users</c> → Nutzernamen.</summary>
    public static List<string> ParseTeamMembers(string ndjson) =>
        Lines(ndjson).Select(e => Str(e, "username") ?? Str(e, "id")).Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).ToList();

    /// <summary><c>GET /api/team/{id}/arena</c> → Kennungen der Team-Battles (Arenen mit <c>teamBattle</c>).</summary>
    public static List<string> ParseTeamBattles(string ndjson) =>
        Lines(ndjson).Where(e => e.TryGetProperty("teamBattle", out var tb) && tb.ValueKind == JsonValueKind.Object)
            .Select(e => Str(e, "id")).Where(id => id is not null).Select(id => id!).ToList();

    /// <summary><c>GET /api/tournament/{id}</c> → die Teams des Battles (Kennung → Name; Lichess liefert [Name, Flair]).</summary>
    public static Dictionary<string, string> ParseBattleTeams(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var res = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!doc.RootElement.TryGetProperty("teamBattle", out var tb) || !tb.TryGetProperty("teams", out var teams)
            || teams.ValueKind != JsonValueKind.Object) return res;
        foreach (var t in teams.EnumerateObject())
        {
            var name = t.Value.ValueKind switch
            {
                JsonValueKind.String => t.Value.GetString(),
                JsonValueKind.Array when t.Value.GetArrayLength() > 0 && t.Value[0].ValueKind == JsonValueKind.String => t.Value[0].GetString(),
                _ => null,
            };
            if (!string.IsNullOrEmpty(name)) res[t.Name] = name;
        }
        return res;
    }

    /// <summary><c>GET /api/tournament/{id}</c> → der Name des Turniers (<c>fullName</c>, sonst <c>name</c>).</summary>
    public static string? ParseTournamentName(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Str(doc.RootElement, "fullName") ?? Str(doc.RootElement, "name");
    }

    /// <summary>Die Serie eines Team-Battles ohne Runde: „Online TMM 2021 Runde 3 Team Battle" → „Online TMM 2021".</summary>
    public static string? EventSeries(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var s = Regex.Replace(name, @"\s+Team[- ]Battle\s*$", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+(Runde|Round|Rd\.?)\s*\d+\b", "", RegexOptions.IgnoreCase).Trim();
        return s.Length > 0 ? s : null;
    }

    /// <summary><c>GET /api/tournament/{id}/results</c> → (Nutzername, Team-Kennung).</summary>
    public static List<(string User, string? Team)> ParseResults(string ndjson) =>
        Lines(ndjson).Select(e => (Str(e, "username"), Str(e, "team"))).Where(x => x.Item1 is not null).Select(x => (x.Item1!, x.Item2)).ToList();

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ── Abrufen ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein Abruf, mit EINEM Wiederholversuch bei einem VORÜBERGEHENDEN Fehler (5xx oder keine Verbindung).
    /// Gemessen am 2026-10-01 auf Prod: von 155 Team-Battles in Folge beantwortete Lichess 37 mit <c>502</c>, alle
    /// binnen drei Sekunden — und dieselben Turnier-Kennungen antworten einzeln abgefragt mit 200. Ein 502 ist hier
    /// also die Last, keine Auskunft; ohne Wiederholung verloren diese 37 Battles ihr <c>PlayedFor</c>/<c>Events</c>
    /// bis zum nächsten Pool-Lauf (<see cref="PoolEvery"/>, 30 Tage).
    /// NICHT wiederholt wird, was eine ANTWORT ist: 404 (<c>null</c>), 401/403 (siehe <see cref="GetOpenAsync"/>)
    /// und 429 — das ist eine <see cref="LeagueOnlineSync.RateLimitedException"/> und beendet den Durchgang.
    /// </summary>
    private async Task<string?> GetAsync(string url, CancellationToken ct, string? accept = null, bool bulk = false)
    {
        var tries = 0;
        var waits = 0;
        while (true)
        {
            try
            {
                tries++;
                return await FetchOnceAsync(url, ct, accept, bulk);
            }
            catch (HttpRequestException e) when (tries < MaxAttempts && IsTransient(e))
            {
                _logger.LogInformation("LeagueHub: Team-Suche — {Url} voruebergehend nicht erreichbar ({Status}), neuer Versuch",
                    url, (int?)e.StatusCode);
                if (RetryPause > TimeSpan.Zero) await Task.Delay(RetryPause, ct);
            }
            catch (LeagueOnlineSync.RateLimitedException) when (bulk && waits < RateLimitWaits)
            {
                // Der BESTANDS-Aufbau sitzt eine Drossel aus, statt den ganzen Durchgang zu beenden: er macht hunderte
                // Abrufe in Folge und traf die Drossel am 01.10.2026 zweimal nach rund einer Minute — jedes Mal ohne
                // einen einzigen gespeicherten Treffer. Der Aufbau laeuft nur alle PoolEvery (30 Tage), die Minute
                // Warten ist also billig. Die Konto-Pruefung bleibt unberuehrt: dort beendet ein 429 den Durchgang.
                waits++;
                tries = 0;
                _logger.LogWarning("LeagueHub: Team-Suche — Lichess drosselt, warte {Seconds} s und mache weiter ({Wait}/{Max})",
                    (int)RateLimitCooldown.TotalSeconds, waits, RateLimitWaits);
                if (RateLimitCooldown > TimeSpan.Zero) await Task.Delay(RateLimitCooldown, ct);
            }
        }
    }

    /// <summary>5xx und „keine Verbindung" (<see cref="HttpRequestException.StatusCode"/> ist dann <c>null</c>) sind vorübergehend.</summary>
    private static bool IsTransient(HttpRequestException e) => e.StatusCode is null || (int)e.StatusCode >= 500;

    private async Task<string?> FetchOnceAsync(string url, CancellationToken ct, string? accept = null, bool bulk = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (accept is not null) req.Headers.Accept.ParseAdd(accept);
        using var r = await _http.SendAsync(req, ct);
        if (r.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("Lichess");
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadAsStringAsync(ct);
        var pause = bulk ? PoolPause : Pause;
        if (pause > TimeSpan.Zero) await Task.Delay(pause, ct);
        return body;
    }

    /// <summary>
    /// Wie <see cref="GetAsync"/>, aber ein 401/403 heisst „dieses Team gibt nichts her" statt „Durchgang zu Ende".
    /// Lichess antwortet 401, wenn ein Team seine Mitgliederliste verborgen hat (gesehen am 2026-09-30 an
    /// schachsport-union-innsbruck-team-2-mm-2021-osb-lv-tirol) — ohne diese Duldung flog die Ausnahme durch
    /// <see cref="RefreshPoolAsync"/> bis in den Takt, der GANZE Bestands-Aufbau brach an diesem einen Team ab,
    /// und kein Team danach wurde je gelesen (LeagueScoutAccounts blieb seit der Einfuehrung leer).
    /// Ein 429 (<see cref="LeagueOnlineSync.RateLimitedException"/>) fliegt weiter: dann endet der Durchgang wirklich.
    /// </summary>
    private async Task<string?> GetOpenAsync(string url, CancellationToken ct, string? accept = null, bool bulk = false)
    {
        try
        {
            return await GetAsync(url, ct, accept, bulk);
        }
        catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogInformation("LeagueHub: Team-Suche — {Url} nicht zugaenglich ({Status}), uebersprungen",
                url, (int?)e.StatusCode);
            return null;
        }
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];

    private static string Join(string? existing, string add, int max)
    {
        var parts = (existing ?? "").Split("; ", StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!parts.Contains(add)) parts.Add(add);
        var s = string.Join("; ", parts);
        if (s.Length <= max) return s;
        var cut = s.LastIndexOf("; ", max, StringComparison.Ordinal);
        return cut > 0 ? s[..cut] : s[..max];
    }

    /// <summary>
    /// Tiroler Teams finden, ihre Mitglieder und die Spieler ihrer Team-Battles in den Bestand nehmen. → neu aufgenommene Konten.
    /// </summary>
    public async Task<int> RefreshPoolAsync(CancellationToken ct)
    {
        var teams = new Dictionary<string, string>(StringComparer.Ordinal);
        var battles = new HashSet<string>(StringComparer.Ordinal);
        var added = 0;
        var skipped = 0;
        var done = 0;
        try
        {
            foreach (var place in _places)
            {
                var json = await GetAsync($"{_lichess}/api/team/search?text={Uri.EscapeDataString(place)}", ct, bulk: true);
                if (json is null) continue;
                foreach (var (id, name) in ParseTeamSearch(json))
                    if (IsLocalTeam(name, _places)) teams[id] = name;
            }
            var pool = await _db.LeagueScoutAccounts.ToDictionaryAsync(a => a.UserName, ct);
            LeagueScoutAccount Upsert(string user)
            {
                var key = user.ToLowerInvariant();
                if (!pool.TryGetValue(key, out var a))
                {
                    a = new LeagueScoutAccount { UserName = Cut(key, 30), DisplayName = Cut(user, 30), FoundAt = DateTime.UtcNow };
                    pool[key] = a;
                    _db.LeagueScoutAccounts.Add(a);
                    added++;
                }
                return a;
            }
            foreach (var (id, name) in teams)
            {
                // Ein Team, das gerade nichts hergibt, darf den Durchgang NICHT beenden: SaveChangesAsync steht erst am
                // Ende der Methode, eine Ausnahme hier verwarf also den ganzen Bestand (2026-09-30: LeagueScoutAccounts
                // blieb seit der Einfuehrung leer, weil ein Team seine Mitgliederliste verborgen hat → 401).
                // Ein 429 fliegt weiter (RateLimitedException erbt von Exception, nicht von HttpRequestException).
                try
                {
                    if (await GetOpenAsync($"{_lichess}/api/team/{Uri.EscapeDataString(id)}/users", ct, "application/x-ndjson", bulk: true) is { } members)
                        foreach (var u in ParseTeamMembers(members))
                        {
                            var a = Upsert(u);
                            a.Teams = Join(a.Teams, name, 500);
                        }
                    if (await GetOpenAsync($"{_lichess}/api/team/{Uri.EscapeDataString(id)}/arena?max=500", ct, "application/x-ndjson", bulk: true) is { } arenas)
                        foreach (var b in ParseTeamBattles(arenas)) battles.Add(b);
                }
                catch (HttpRequestException e)
                {
                    skipped++;
                    _logger.LogWarning(e, "LeagueHub: Team-Suche — Team {Team} uebersprungen ({Status})", id, (int?)e.StatusCode);
                }
                if (++done % SaveEvery == 0) await _db.SaveChangesAsync(ct);
            }
            foreach (var b in battles)
            {
                // Dieselbe Regel wie bei den Teams: ein Battle, das nicht zu lesen ist, kostet nur sich selbst.
                try
                {
                    if (await GetOpenAsync($"{_lichess}/api/tournament/{b}", ct, bulk: true) is not { } info) continue;
                    var names = ParseBattleTeams(info);
                    if (!names.Keys.Any(teams.ContainsKey)) continue;
                    var series = EventSeries(ParseTournamentName(info));
                    if (await GetOpenAsync($"{_lichess}/api/tournament/{b}/results?nb=1000", ct, "application/x-ndjson", bulk: true) is not { } results) continue;
                    foreach (var (user, team) in ParseResults(results))
                    {
                        if (team is null || !teams.TryGetValue(team, out var teamName)) continue;       // nur wer für ein Tiroler Team spielte
                        var a = Upsert(user);
                        a.PlayedFor = Join(a.PlayedFor, teamName, 200);
                        if (series is not null) a.Events = Join(a.Events, series, 500);
                    }
                }
                catch (HttpRequestException e)
                {
                    skipped++;
                    _logger.LogWarning(e, "LeagueHub: Team-Suche — Team-Battle {Battle} uebersprungen ({Status})", b, (int?)e.StatusCode);
                }
                if (++done % SaveEvery == 0) await _db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            // Gespeichert wird AUCH beim Verlassen durch eine Ausnahme — und zwar mit CancellationToken.None: ein
            // abgebrochener Token wuerde genau das Speichern verhindern, das die Arbeit retten soll (dieselbe Lehre
            // wie beim Rundenplan-Lauf des Turnierverzeichnisses). Ein Fehler beim Speichern darf die urspruengliche
            // Ausnahme nicht verdecken — der Takt muss eine Drossel als Drossel sehen.
            try
            {
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "LeagueHub: Team-Suche — Zwischenstand konnte nicht gespeichert werden");
            }
            _logger.LogInformation("LeagueHub: Team-Suche — {Teams} Tiroler Teams, {Battles} Team-Battles, {Added} neue Konten, {Skipped} uebersprungen",
                teams.Count, battles.Count, added, skipped);
        }
        return added;
    }

    /// <summary>
    /// Ein Durchgang: mit <paramref name="refreshPool"/> erst den Bestand erneuern (der Takt ruft das höchstens alle
    /// <see cref="PoolEvery"/>), dann fällige Konten prüfen, bis <paramref name="budget"/> um ist. → noch Konten offen?
    /// </summary>
    public async Task<bool> RunOnceAsync(TimeSpan budget, bool refreshPool, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        try
        {
            if (refreshPool) await RefreshPoolAsync(ct);
            var due = DateTime.UtcNow.AddDays(-RecheckDays);
            var queue = await _db.LeagueScoutAccounts.Where(a => a.CheckedAt == null || a.CheckedAt < due)
                .OrderBy(a => a.CheckedAt.HasValue).ThenBy(a => a.CheckedAt).ThenBy(a => a.UserName).Select(a => a.UserName).ToListAsync(ct);
            if (queue.Count == 0) return false;
            var ctx = await Context.LoadAsync(_db, ct);
            var done = 0;
            for (var i = 0; i < queue.Count && DateTime.UtcNow - started < budget; i += ProfileChunk)
            {
                var chunk = queue.Skip(i).Take(ProfileChunk).ToList();
                var profiles = await ProfilesAsync(chunk, ct);
                foreach (var user in chunk)
                {
                    if (DateTime.UtcNow - started >= budget) break;
                    var a = await _db.LeagueScoutAccounts.FirstAsync(x => x.UserName == user, ct);
                    try
                    {
                        a.Result = Cut(await CheckAsync(a, profiles.GetValueOrDefault(user), ctx, ct), 300);
                        a.CheckedAt = DateTime.UtcNow;
                    }
                    catch (HttpRequestException e)
                    {
                        // Ein Konto, dessen Partien gerade nicht zu holen sind, hält die übrigen nicht auf — morgen wieder.
                        _logger.LogWarning("LeagueHub: Team-Suche, Konto {User}: {Message}", user, e.Message);
                        a.Result = Cut("Abruf gescheitert: " + e.Message, 300);
                        a.CheckedAt = DateTime.UtcNow.AddDays(1 - RecheckDays);
                    }
                    await _db.SaveChangesAsync(ct);
                    done++;
                }
            }
            return done < queue.Count;
        }
        catch (LeagueOnlineSync.RateLimitedException e)
        {
            _logger.LogWarning("LeagueHub: {Message} — Team-Suche pausiert", e.Message);
            return true;
        }
    }

    private async Task<Dictionary<string, LeagueAccountFinder.Profile>> ProfilesAsync(List<string> users, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_lichess}/api/users")
        {
            Content = new StringContent(string.Join(',', users), Encoding.UTF8, "text/plain"),
        };
        using var r = await _http.SendAsync(req, ct);
        if (r.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("Lichess");
        r.EnsureSuccessStatusCode();
        var list = LeagueAccountFinder.ParseLichessUsers(await r.Content.ReadAsStringAsync(ct));
        if (Pause > TimeSpan.Zero) await Task.Delay(Pause, ct);
        return list.GroupBy(p => p.User.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Spieler der Saison (für Klarnamen) und aller Saisonen je Verein (für Stellungen), dazu zwischengespeicherte
    /// Repertoires — einmal je Durchgang geladen.</summary>
    private sealed class Context
    {
        public required List<LeagueAccountFinder.Player> Season { get; init; }
        public required Dictionary<string, LeagueAccountFinder.Player> ByFide { get; init; }
        public required List<(string Fide, string Team)> AllTeams { get; init; }
        /// <summary>Alle Vornamen der Meldelisten (klein, ab drei Buchstaben) — für <see cref="OtherFirstName"/>.</summary>
        public required HashSet<string> FirstNames { get; init; }
        public Dictionary<string, LeagueFingerprint.Repertoire> Repertoires { get; } = new(StringComparer.Ordinal);

        public static async Task<Context> LoadAsync(AppDbContext db, CancellationToken ct)
        {
            var rows = await (from p in db.LeaguePlayers.AsNoTracking()
                              join t in db.LeagueTournaments.AsNoTracking() on p.Tnr equals t.Tnr
                              where p.FideId != null && p.FideId != ""
                              orderby t.Season descending
                              select new { p.FideId, p.Name, p.Fed, p.EloI, p.EloN, p.Team, t.Season }).ToListAsync(ct);
            var season = rows.Count > 0 ? rows[0].Season : null;
            var byFide = rows.GroupBy(r => r.FideId!).ToDictionary(g => g.Key,
                g => new LeagueAccountFinder.Player(g.Key, g.First().Name, g.First().Fed, g.First().EloI is > 0 ? g.First().EloI : g.First().EloN, g.First().Team));
            return new Context
            {
                FirstNames = await FirstNamesAsync(db, ct),
                Season = rows.Where(r => r.Season == season).GroupBy(r => r.FideId!).Select(g => byFide[g.Key]).ToList(),
                ByFide = byFide,
                AllTeams = rows.Select(r => (r.FideId!, r.Team)).Distinct().ToList(),
            };
        }
    }

    /// <summary>Alle Vornamen der Meldelisten (klein, ab drei Buchstaben) — für <see cref="OtherFirstName"/>.</summary>
    public static async Task<HashSet<string>> FirstNamesAsync(AppDbContext db, CancellationToken ct)
    {
        var firsts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in await db.LeaguePlayers.AsNoTracking().Select(p => p.Name).Distinct().ToListAsync(ct))
            foreach (var t in LeagueAccountFinder.Tokens(LeagueAccountFinder.SplitName(n).First))
                if (t.Length >= 3) firsts.Add(t);
        return firsts;
    }

    /// <summary>Ein Konto prüfen: Klarname, sonst Stellungen unter den Spielern des Vereins. → Ergebnis in Worten.</summary>
    private async Task<string> CheckAsync(LeagueScoutAccount a, LeagueAccountFinder.Profile? prof, Context ctx, CancellationToken ct)
    {
        if (prof is null) return "Konto nicht mehr da";
        if (prof.Closed) return "Konto gesperrt/geschlossen";
        var origin = a.PlayedFor is { Length: > 0 } pf
            ? $"spielte für „{pf.Split("; ")[0]}“ (Lichess-Team-Battle)"
            : $"Mitglied im Lichess-Team „{(a.Teams ?? "").Split("; ")[0]}“";

        // 1) Klarname im Profil
        var toks = LeagueAccountFinder.Tokens(prof.RealName);
        if (toks.Count > 0)
        {
            var found = 0;
            foreach (var p in ctx.Season)
            {
                var (last, first) = LeagueAccountFinder.SplitName(p.Name);
                var lt = LeagueAccountFinder.Tokens(last);
                if (lt.Count == 0 || !lt.All(toks.Contains)
                    || LeagueAccountFinder.FirstNameMatch(toks, lt, LeagueAccountFinder.Tokens(first)) != LeagueAccountFinder.NameFit.Full) continue;
                var scan = await LeagueAccountFinder.ScanRowAsync(_db, _http, _lichess, p.Fide, ct);
                if (LeagueAccountFinder.Judge(p, prof, derived: false, scan.Federation, lead: origin) is not { } v) continue;
                if (await AddAsync(p, prof, v.Score + 1, v.Evidence, ct)) found++;
            }
            if (found > 0) return $"Klarname: {found} Vorschlag/Vorschläge";
        }

        // 2) Verein + Stellungen
        if (a.PlayedFor is not { Length: > 0 }) return toks.Count > 0 ? "Klarname passt zu keinem Spieler der Saison" : "kein Klarname, kein Verein";
        var keys = ClubKeys(a.PlayedFor, _places);
        var club = ctx.AllTeams.Where(t => keys.Any(k => Regex.IsMatch(" " + Norm(t.Team) + " ", $@"\b{Regex.Escape(k)}\b")))
            .Select(t => t.Fide).Distinct().ToList();
        if (club.Count < 2) return "zu wenige Vereinsspieler zum Vergleich";
        var ndjson = await GetAsync($"{_lichess}/api/games/user/{Uri.EscapeDataString(a.UserName)}?max={MaxGames}&moves=true&clocks=false"
                                   + "&evals=false&opening=false&pgnInJson=false", ct, "application/x-ndjson");
        var games = ndjson is null ? new List<LeagueOnlineSync.Game>() : LeagueOnlineSync.ParseLichess(ndjson, a.UserName).Games;
        var usable = LeagueFingerprint.Usable(games, g => g.Speed);
        if (usable.Count < MinGames) return $"kein Klarname, nur {usable.Count} Partien";
        var online = usable.Select(g => ((IReadOnlyList<string>)g.Moves, g.White)).ToList();
        var depths = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var fide in club) depths[fide] = LeagueFingerprint.Depth(online, await RepertoireAsync(fide, ctx, ct));
        if (LeagueFingerprint.Best(depths) is not { } best) return "keine gemeinsame Stellung mit dem Verein";
        if (best.Ratio < ClubMargin) return $"Stellungen nicht eindeutig (Abstand {best.Ratio:0.0})";
        var player = ctx.ByFide[best.Fide];
        if (best.Depth < MinDepth) return $"Stellungen → {player.Name}, aber gemeinsam nur bis Halbzug {best.Depth:0.0}";
        if (OtherFirstName(a.DisplayName, player.Name, ctx.FirstNames) is { } other)
            return $"Stellungen → {player.Name}, aber der Nutzername nennt einen anderen Vornamen („{other}“)";
        if (!LeagueAccountFinder.RatingPlausible(prof, player.Elo)) return $"Stellungen → {player.Name}, aber Wertung zu niedrig";
        var ev = new List<string> { origin, PositionsText(best, depths.Count, ctx) };
        if (LeagueAccountFinder.RatingEvidence(prof.RatingLabel, prof.Rating, player.Elo) is { } re) ev.Add(re.Text);
        return await AddAsync(player, prof, best.Ratio >= 2 ? 4 : 3, ev, ct) ? $"Stellungen → {player.Name}" : "schon bekannt oder bei einem anderen Spieler";
    }

    /// <summary>
    /// Der Stellungs-Hinweis in Worten (0.619.0, Wunsch „erklär das besser, 1,9× vor dem Zweiten sagt nichts"): wie weit die
    /// Online-Eröffnungen dem Brett-Repertoire dieses Spielers folgen — verglichen mit dem nächstbesten Vereinskollegen.
    /// </summary>
    private static string PositionsText((string Fide, double Depth, double Ratio, string? Second) best, int compared, Context ctx)
    {
        var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        if (best.Second is { } s && ctx.ByFide.TryGetValue(s, out var second))
            return $"seine Online-Eröffnungen folgen dem Brett-Repertoire dieses Spielers im Schnitt {best.Ratio.ToString("0.0", de)}-mal so weit "
                   + $"wie dem des nächstbesten Vereinskollegen ({second.Name}); verglichen mit {compared} Spielern des Vereins";
        var others = compared - 1;
        return "seine Online-Eröffnungen erreichen Stellungen aus dem Brett-Repertoire dieses Spielers, "
               + (others == 1 ? "aber nicht aus dem des anderen verglichenen Vereinsspielers"
                   : $"aber aus keinem der {others} übrigen verglichenen Vereinsspieler");
    }

    private async Task<LeagueFingerprint.Repertoire> RepertoireAsync(string fide, Context ctx, CancellationToken ct)
    {
        if (ctx.Repertoires.TryGetValue(fide, out var r)) return r;
        return ctx.Repertoires[fide] = await BoardRepertoireAsync(_db, fide, ct);
    }

    /// <summary>Die Stellungen aus allen Brettpartien eines Spielers (fremde + Vereinspartien, ohne eigene Ausgangsstellung).</summary>
    public static async Task<LeagueFingerprint.Repertoire> BoardRepertoireAsync(AppDbContext db, string fide, CancellationToken ct)
    {
        var r = new LeagueFingerprint.Repertoire();
        var (name, games) = await new LeagueProfileStore(db).GamesAsync(fide, ct);
        foreach (var g in games)
        {
            if (g.Headers.ContainsKey("FEN") || LeagueProfileBuilder.ColorOf(g, fide, name) is not { } color) continue;
            var moveText = PgnParser.SplitGames(g.Raw).Select(x => x.MoveText).FirstOrDefault() ?? "";
            r.Add(PgnParser.ExtractMainlineSans(moveText), color == "w");
        }
        return r;
    }

    /// <summary>Vorschlag anlegen, wenn es das Konto für den Spieler weder als Konto noch als Vorschlag (auch verworfen) gibt — und
    /// bei KEINEM anderen Spieler als Konto eingetragen ist (0.614.0: ein selbst gemeldetes Konto eines Vereinskollegen kam sonst
    /// über die Stellungen noch einmal als Vorschlag).</summary>
    /// <remarks>Vorher den Such-Eintrag samt Jahrgang holen — sonst bliebe das Konto eines Minderjährigen, den die Namenssuche
    /// noch nicht erfasst hat (andere Saison, Suche läuft noch), sichtbar.</remarks>
    /// <summary>Einen Vorschlag anlegen (nicht, wenn das Konto schon irgendwo steht) — und dann dasselbe für den gleichen Nutzernamen auf
    /// chess.com, wenn er dort existiert und passt (<see cref="LeagueAccountFinder.TwinAsync"/>, 0.621.0).</summary>
    private async Task<bool> AddAsync(LeagueAccountFinder.Player player, LeagueAccountFinder.Profile prof, int score, List<string> evidence,
        CancellationToken ct, bool twin = true)
    {
        var fide = player.Fide;
        var user = prof.User.ToLower();
        if (await _db.LeagueOnlineAccounts.AnyAsync(x => x.Site == prof.Site && x.UserName.ToLower() == user, ct)
            || await _db.LeagueAccountSuggestions.AnyAsync(x => x.FideId == fide && x.Site == prof.Site && x.UserName.ToLower() == user, ct))
            return false;
        await LeagueAccountFinder.ScanRowAsync(_db, _http, _lichess, fide, ct);
        _db.LeagueAccountSuggestions.Add(new LeagueAccountSuggestion
        {
            FideId = fide, Site = prof.Site, UserName = prof.User, Url = prof.Url, Score = score,
            Evidence = Cut(string.Join("; ", evidence), 500), ProfileName = prof.RealName is { } rn ? Cut(rn, 120) : null,
            Location = prof.Location is { } loc ? Cut(loc, 120) : null, LastActive = prof.LastActive,
            Status = LeagueSuggestionStatus.Open, CreatedAt = DateTime.UtcNow, Source = Source,
        });
        await _db.SaveChangesAsync(ct);
        if (twin)
        {
            var fed = (await LeagueAccountFinder.ScanRowAsync(_db, _http, _lichess, fide, ct)).Federation;
            if (await LeagueAccountFinder.TwinAsync(_http, _lichess, player, prof, fed, ct, Pause) is { } t)
                await AddAsync(player, t.Profile, t.Verdict.Score, t.Verdict.Evidence, ct, twin: false);
        }
        return true;
    }
}
