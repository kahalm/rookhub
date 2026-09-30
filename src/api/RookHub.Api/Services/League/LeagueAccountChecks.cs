using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Konto-Prüfung (i) (0.619.0, Wunsch 2026-09-30: „mach bei den Konten immer ein (i) und zeig an, was alles geprüft wurde:
/// Matching über Selbstmeldung, über TMM 2021, Name, Land, % Übereinstimmung Repertoire, Elo passend — normal ist online ca. 200
/// höher, 100–300 wäre passend"). Je eingetragenem Konto bzw. Vorschlag eine Liste von Prüfungen, jede mit Ergebnis
/// (<see cref="Ok"/> spricht dafür, <see cref="Warn"/> macht stutzig, <see cref="Fail"/> spricht dagegen, <see cref="None"/> = nichts
/// zu prüfen, <see cref="Info"/> = zur Kenntnis) und einem Satz dazu.
/// <para>Das Profil wird dafür frisch von Lichess bzw. chess.com geholt (dieselben Abrufe wie die Konto-Suche); ein Vorschlag
/// bekommt zum Repertoire-Vergleich auch seine letzten Partien geholt, ein eingetragenes Konto nimmt die schon gespeicherten. Das
/// Ergebnis liegt <see cref="CacheFor"/> im Arbeitsspeicher — wer die Liste zweimal aufklappt, fragt die Seiten nicht zweimal.</para>
/// <para>Konten Minderjähriger (<see cref="LeagueHiddenAccounts"/>) prüft niemand: die Prüfung nennt Profilangaben.</para>
/// </summary>
public sealed class LeagueAccountChecks
{
    public const string Ok = "ok", Warn = "warn", Fail = "fail", None = "none", Info = "info";
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    /// <summary>Ein Ergebnis ohne Profil (Seite gerade nicht erreichbar) nur kurz merken.</summary>
    public static readonly TimeSpan CacheForFailed = TimeSpan.FromMinutes(1);
    /// <summary>Repertoire: so viele EIGENE Züge weit muss eine Online-Partie einer Brett-Stellung folgen (nach einem ist fast jede
    /// Partie „im Repertoire", 1.e4).</summary>
    public const int RepertoireOwnMoves = 3;
    public const int MinBoardGames = 5, MinOnlineGames = 10, MaxOnlineGames = 100;
    /// <summary>Anteil der Online-Partien, der für (ab <see cref="RepertoireGood"/>) bzw. kaum (unter <see cref="RepertoireLow"/>) für
    /// ihn spricht. Online spielt man oft anderes als am Brett — deshalb ist wenig Übereinstimmung nur ein Stutzen, kein Nein.</summary>
    public const double RepertoireGood = 0.35, RepertoireLow = 0.10;
    /// <summary>Wer so lange nicht mehr online war, ist wohl ein altes Konto.</summary>
    public const int InactiveYears = 2;
    /// <summary>Selbstmeldungen und Turnierserien der Online-TMM 2021 („Online TMM 2021", „TOMM 2021").</summary>
    private static readonly Regex Tmm2021 = new(@"\bT\s*O?\s*M\s*M\b.*2021|2021.*\bT\s*O?\s*M\s*M\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public sealed record Item(string Key, string Label, string Status, string Text);

    /// <param name="ProfileLoaded">Konnte das Profil geholt werden? Ohne sind die Profil-Prüfungen „nicht geprüft".</param>
    public sealed record Result(string Site, string User, string Url, string Player, int? Elo, DateTime CheckedAt, bool ProfileLoaded,
        List<Item> Items);

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly IMemoryCache? _cache;
    private readonly string _lichess;
    private readonly string[] _places;

    public LeagueAccountChecks(AppDbContext db, HttpClient http, IMemoryCache? cache = null, IConfiguration? config = null)
    {
        _db = db;
        _http = http;
        _cache = cache;
        _lichess = (config?["Lichess:SiteUrl"] ?? "https://lichess.org").TrimEnd('/');
        var p = config?["LeagueOnline:TeamPlaces"];
        _places = string.IsNullOrWhiteSpace(p) ? LeagueTeamScout.DefaultPlaces
            : p.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Prüfung eines eingetragenen Kontos; <c>null</c> = unbekannt oder verborgen.</summary>
    public async Task<Result?> ForAccountAsync(int id, CancellationToken ct)
    {
        var a = await _db.LeagueOnlineAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null || await HiddenAsync(a.FideId, ct)) return null;
        return await CachedAsync($"league-checks:a:{id}", () => BuildAsync(a.FideId, a.Site, a.UserName, a, null, ct));
    }

    /// <summary>Prüfung eines Vorschlags; <c>null</c> = unbekannt oder verborgen.</summary>
    public async Task<Result?> ForSuggestionAsync(int id, CancellationToken ct)
    {
        var s = await _db.LeagueAccountSuggestions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null || await HiddenAsync(s.FideId, ct)) return null;
        return await CachedAsync($"league-checks:s:{id}", () => BuildAsync(s.FideId, s.Site, s.UserName, null, s, ct));
    }

    private async Task<bool> HiddenAsync(string fide, CancellationToken ct) =>
        LeagueHiddenAccounts.Hides(await _db.LeagueAccountScans.AsNoTracking().Where(x => x.FideId == fide).Select(x => x.BirthYear)
            .FirstOrDefaultAsync(ct));

    private async Task<Result> CachedAsync(string key, Func<Task<Result>> build)
    {
        if (_cache?.TryGetValue(key, out Result? hit) == true && hit is not null) return hit;
        var r = await build();
        _cache?.Set(key, r, r.ProfileLoaded ? CacheFor : CacheForFailed);
        return r;
    }

    private async Task<Result> BuildAsync(string fide, string site, string user, LeagueOnlineAccount? account, LeagueAccountSuggestion? sugg,
        CancellationToken ct)
    {
        var player = await LeagueAccountFinder.PlayerAsync(_db, fide, ct) ?? new LeagueAccountFinder.Player(fide, fide, null, null, null);
        var fideFed = await _db.LeagueAccountScans.AsNoTracking().Where(x => x.FideId == fide).Select(x => x.Federation).FirstOrDefaultAsync(ct);
        var (prof, profError) = await ProfileAsync(site, user, ct);
        var items = new List<Item>
        {
            await SelfReportAsync(fide, site, user, ct),
        };
        var scout = site == LeagueOnlineSites.Lichess
            ? await _db.LeagueScoutAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.UserName == user.ToLower(), ct)
            : null;
        var playerTeams = await _db.LeaguePlayers.AsNoTracking().Where(p => p.FideId == fide).Select(p => p.Team).Distinct().ToListAsync(ct);
        items.Add(Tmm2021Check(site, scout, playerTeams));
        items.Add(prof is null ? NotLoaded("name", "Name im Profil", profError) : NameCheck(player, prof));
        items.Add(UserNameCheck(player, user, await LeagueTeamScout.FirstNamesAsync(_db, ct)));
        items.Add(prof is null ? NotLoaded("country", "Land", profError) : CountryCheck(player, prof, fideFed));
        items.Add(await RepertoireCheckAsync(fide, site, user, account, ct));
        if (prof is null) items.Add(NotLoaded("rating", "Online-Wertung", profError));
        else items.AddRange(RatingChecks(prof, player.Elo));
        if (prof is not null)
        {
            items.Add(FideRatingCheck(prof, player.Elo));
            items.Add(TirolCheck(prof));
        }
        items.Add(TeamsCheck(scout, playerTeams));
        if (prof is not null)
        {
            items.Add(ActivityCheck(prof));
            items.Add(new Item("closed", "Konto", prof.Closed ? Fail : Ok, prof.Closed ? "gesperrt oder geschlossen" : "offen, nicht gesperrt"));
        }
        items.Add(await ElsewhereCheckAsync(fide, site, user, ct));
        if (scout?.Result is { Length: > 0 } res) items.Add(new Item("scout", "Team-Suche", Info, res));
        if (sugg is not null) items.Add(new Item("evidence", "Hinweise der Suche", Info, sugg.Evidence));
        return new Result(site, user, LeagueOnlineSites.ProfileUrl(site, user), player.Name, player.Elo, DateTime.UtcNow, prof is not null, items);
    }

    private static Item NotLoaded(string key, string label, string? why) => new(key, label, None, $"nicht geprüft — {why ?? "Profil nicht abrufbar"}");

    // ── Profil holen ────────────────────────────────────────────────────────────────────────────

    /// <summary>Das Profil frisch von der Seite — <c>(null, Grund)</c>, wenn es nicht geht.</summary>
    private async Task<(LeagueAccountFinder.Profile? Profile, string? Error)> ProfileAsync(string site, string user, CancellationToken ct)
    {
        try
        {
            if (site == LeagueOnlineSites.Lichess)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{_lichess}/api/users")
                {
                    Content = new StringContent(user.ToLowerInvariant(), Encoding.UTF8, "text/plain"),
                };
                using var r = await _http.SendAsync(req, ct);
                if (r.StatusCode == HttpStatusCode.TooManyRequests) return (null, "Lichess bremst gerade");
                if (!r.IsSuccessStatusCode) return (null, "Lichess nicht erreichbar");
                var prof = LeagueAccountFinder.ParseLichessUsers(await r.Content.ReadAsStringAsync(ct))
                    .FirstOrDefault(p => p.User.Equals(user, StringComparison.OrdinalIgnoreCase));
                return prof is null ? (null, "Konto auf Lichess nicht gefunden") : (prof, null);
            }
            var name = Uri.EscapeDataString(user.ToLowerInvariant());
            using var pr = await _http.GetAsync($"https://api.chess.com/pub/player/{name}", ct);
            if (pr.StatusCode == HttpStatusCode.TooManyRequests) return (null, "chess.com bremst gerade");
            if (pr.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return (null, "Konto auf chess.com nicht gefunden");
            if (!pr.IsSuccessStatusCode) return (null, "chess.com nicht erreichbar");
            if (LeagueAccountFinder.ParseChessComPlayer(await pr.Content.ReadAsStringAsync(ct)) is not { } p) return (null, "Profil unlesbar");
            using var st = await _http.GetAsync($"https://api.chess.com/pub/player/{name}/stats", ct);
            return st.IsSuccessStatusCode ? (LeagueAccountFinder.WithChessComStats(p, await st.Content.ReadAsStringAsync(ct)), null) : (p, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            return (null, site == LeagueOnlineSites.Lichess ? "Lichess nicht erreichbar" : "chess.com nicht erreichbar");
        }
    }

    // ── Die Prüfungen (rein bis auf die Datenbank-Abfragen, getestet) ──────────────────────────

    private async Task<Item> SelfReportAsync(string fide, string site, string user, CancellationToken ct)
    {
        const string key = "self", label = "Selbstmeldung";
        var lower = user.ToLower();
        var reports = await _db.LeagueSelfReports.AsNoTracking()
            .Where(r => (r.Site == site && r.UserName.ToLower() == lower) || r.FideId == fide).ToListAsync(ct);
        static string From(LeagueSelfReport r) => r.Source + (string.IsNullOrWhiteSpace(r.Team) ? "" : $", für „{r.Team}“");
        bool Same(LeagueSelfReport r) => r.Site == site && r.UserName.Equals(user, StringComparison.OrdinalIgnoreCase);
        if (reports.FirstOrDefault(r => Same(r) && r.FideId == fide) is { } own)
            return new Item(key, label, Ok, $"selbst gemeldet ({From(own)})");
        if (reports.FirstOrDefault(r => Same(r) && r.FideId != fide) is { } other)
            return new Item(key, label, Fail, $"{other.Source}: gemeldet von {await NameOrAnonymousAsync(other.FideId, ct)} — nicht von ihm");
        if (reports.FirstOrDefault(r => r.FideId == fide && r.Site == site) is { } sameSite)
            return new Item(key, label, Warn, $"selbst gemeldet hat er ein anderes {LeagueOnlineSites.Label(site)}-Konto („{sameSite.UserName}“, {sameSite.Source})");
        if (reports.FirstOrDefault(r => r.FideId == fide) is { } otherSite)
            return new Item(key, label, None, $"selbst gemeldet nur {LeagueOnlineSites.Label(otherSite.Site)}: {otherSite.UserName} ({otherSite.Source})");
        return new Item(key, label, None, "keine Selbstmeldung bekannt");
    }

    /// <summary>Der Name eines anderen Spielers — oder „einem anderen Spieler", wenn dessen Konten verborgen sind (minderjährig).</summary>
    private async Task<string> NameOrAnonymousAsync(string fide, CancellationToken ct) =>
        await HiddenAsync(fide, ct) ? "einem anderen Spieler"
            : (await LeagueAccountFinder.PlayerAsync(_db, fide, ct))?.Name ?? "einem anderen Spieler";

    /// <summary>Hat das Konto in der Online-TMM 2021 (Lichess-Team-Battles, Liste der Team-Suche) gespielt — und für seinen Verein?</summary>
    public Item Tmm2021Check(string site, LeagueScoutAccount? scout, IReadOnlyList<string> playerTeams)
    {
        const string key = "tmm2021", label = "Online-TMM 2021";
        if (site != LeagueOnlineSites.Lichess) return new Item(key, label, None, "wurde auf Lichess gespielt — hier nicht zu prüfen");
        if (scout is null) return new Item(key, label, None, "nicht unter den Konten der Tiroler Lichess-Teams");
        var playedFor = Split(scout.PlayedFor);
        if (scout.Events is null)
            return playedFor.Count > 0
                ? new Item(key, label, Info, $"spielte in Tiroler Team-Battles (für „{playedFor[0]}“) — welche Turniere, trägt die Team-Suche beim nächsten Durchlauf nach")
                : new Item(key, label, None, "nur Mitglied eines Tiroler Lichess-Teams, in keinem Team-Battle gefunden");
        if (!Split(scout.Events).Any(e => Tmm2021.IsMatch(e))) return new Item(key, label, None, "nicht in der Online-TMM 2021");
        var own = playedFor.FirstOrDefault(t => SameClub(t, playerTeams));
        if (own is not null) return new Item(key, label, Ok, $"spielte in der Online-TMM 2021, für „{own}“ — seinen Verein");
        return new Item(key, label, Warn, playedFor.Count > 0
            ? $"spielte in der Online-TMM 2021, aber für „{playedFor[0]}“ — nicht sein Verein"
            : "spielte in der Online-TMM 2021");
    }

    /// <summary>Tiroler Lichess-Teams und Team-Battles außer der TMM 2021.</summary>
    public Item TeamsCheck(LeagueScoutAccount? scout, IReadOnlyList<string> playerTeams)
    {
        const string key = "teams", label = "Tiroler Lichess-Teams";
        if (scout is null) return new Item(key, label, None, "in keinem Tiroler Lichess-Team gefunden");
        var teams = Split(scout.Teams).Concat(Split(scout.PlayedFor)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var battles = Split(scout.Events).Where(e => !Tmm2021.IsMatch(e)).ToList();
        var parts = new List<string>();
        if (teams.Count > 0) parts.Add("Teams: " + string.Join(", ", teams.Take(5)) + (teams.Count > 5 ? " …" : ""));
        if (battles.Count > 0) parts.Add("Team-Battles: " + string.Join(", ", battles.Take(5)) + (battles.Count > 5 ? " …" : ""));
        if (parts.Count == 0) return new Item(key, label, None, "in keinem Tiroler Lichess-Team gefunden");
        return new Item(key, label, teams.Any(t => SameClub(t, playerTeams)) ? Ok : Info, string.Join("; ", parts));
    }

    /// <summary>Steht im Lichess-Team derselbe Ort wie in einer seiner Mannschaften („SK Schwaz 2" ↔ „Schach Schwaz")?</summary>
    private bool SameClub(string team, IReadOnlyList<string> playerTeams)
    {
        var keys = LeagueTeamScout.ClubKeys(team, _places);
        return keys.Count > 0 && playerTeams.Any(pt => LeagueTeamScout.ClubKeys(pt, _places).Any(keys.Contains));
    }

    private static List<string> Split(string? s) =>
        (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Klarname im Profil gegen den Namen des Spielers.</summary>
    public static Item NameCheck(LeagueAccountFinder.Player player, LeagueAccountFinder.Profile prof)
    {
        const string key = "name", label = "Name im Profil";
        var toks = LeagueAccountFinder.Tokens(prof.RealName);
        if (toks.Count == 0) return new Item(key, label, None, "kein Klarname im Profil");
        var (last, first) = LeagueAccountFinder.SplitName(player.Name);
        var lt = LeagueAccountFinder.Tokens(last);
        var shown = $"„{prof.RealName}“";
        if (lt.Count == 0 || !lt.All(toks.Contains)) return new Item(key, label, Fail, $"{shown} — anderer Nachname");
        return LeagueAccountFinder.FirstNameMatch(toks, lt, LeagueAccountFinder.Tokens(first)) switch
        {
            LeagueAccountFinder.NameFit.Full => new Item(key, label, Ok, $"{shown} — Vor- und Nachname passen"),
            LeagueAccountFinder.NameFit.Initial => new Item(key, label, Ok, $"{shown} — Nachname und Initiale passen"),
            LeagueAccountFinder.NameFit.LastOnly => new Item(key, label, Info, $"{shown} — nur der Nachname steht da"),
            _ => new Item(key, label, Fail, $"{shown} — anderer Vorname"),
        };
    }

    /// <summary>Wie hängt der Nutzername mit dem Namen zusammen?</summary>
    public static Item UserNameCheck(LeagueAccountFinder.Player player, string user, IReadOnlySet<string> firstNames)
    {
        const string key = "username", label = "Nutzername";
        if (LeagueAccountFinder.Variants(player.Name).Contains(user, StringComparer.OrdinalIgnoreCase))
            return new Item(key, label, Ok, $"„{user}“ ist aus Vor- und Nachnamen gebildet");
        if (LeagueTeamScout.OtherFirstName(user, player.Name, firstNames) is { } other)
            return new Item(key, label, Fail, $"„{user}“ nennt einen anderen Vornamen („{other}“)");
        var plain = LeagueAccountFinder.Plain(user).ToLowerInvariant();
        var last = LeagueAccountFinder.Tokens(LeagueAccountFinder.SplitName(player.Name).Last).Where(t => t.Length >= 4).ToList();
        if (last.Any(plain.Contains)) return new Item(key, label, Info, $"„{user}“ enthält den Nachnamen");
        return new Item(key, label, None, $"„{user}“ — kein Bezug zum Namen erkennbar");
    }

    /// <summary>Land im Profil: Österreich oder seine Föderation (Meldeliste bzw. FIDE).</summary>
    public static Item CountryCheck(LeagueAccountFinder.Player player, LeagueAccountFinder.Profile prof, string? fideFed)
    {
        const string key = "country", label = "Land";
        var flag = (prof.Flag ?? "").Trim();
        if (flag.Length < 2) return new Item(key, label, None, "kein Land im Profil");
        var code = flag[..2].ToUpperInvariant();
        if (code == "AT") return new Item(key, label, Ok, "Österreich");
        if (LeagueAccountFinder.AllowedCountries(player.Fed, fideFed).Contains(code))
            return new Item(key, label, Ok, $"{code} — seine Föderation ({fideFed ?? player.Fed})");
        var fed = fideFed ?? player.Fed;
        return new Item(key, label, Fail, $"{code} — weder Österreich noch seine Föderation" + (fed is null ? "" : $" ({fed})"));
    }

    /// <summary>
    /// Online-Wertungen gegen die Elo, je Kategorie (Wunsch: „normal ist online ca. 200 höher, 100–300 wäre passend"). Belastbar
    /// heißt: genug Partien und nicht vorläufig — nur solche entscheiden. Bewertung: <see cref="LeagueAccountFinder.FitMin"/> bis
    /// <see cref="LeagueAccountFinder.FitMax"/> darüber passt; darüber höher als üblich; knapp darüber oder darunter stutzig; mehr
    /// als <see cref="LeagueAccountFinder.RatingBelow"/> darunter ist er es nicht (dieselbe Grenze wie die Konto-Suche).
    /// </summary>
    public static List<Item> RatingChecks(LeagueAccountFinder.Profile prof, int? elo)
    {
        var ratings = prof.Ratings ?? (prof.Rating is { } r ? new[] { new LeagueAccountFinder.Rating(prof.RatingLabel ?? "Online", r, LeagueAccountFinder.MinRatedGames, true) } : Array.Empty<LeagueAccountFinder.Rating>());
        if (ratings.Count == 0) return new() { new Item("rating", "Online-Wertung", None, "keine Wertung (keine gewerteten Partien)") };
        return ratings.Select(x => RatingCheck(x, elo)).ToList();
    }

    public static Item RatingCheck(LeagueAccountFinder.Rating x, int? elo)
    {
        var key = "rating:" + x.Label;
        var label = x.Label;
        var games = $"{x.Games.ToString("#,0", De)} {(x.Games == 1 ? "Partie" : "Partien")}";
        if (!x.Reliable) return new Item(key, label, None, $"{x.Value} — nur {games} bzw. vorläufig, zählt nicht");
        if (elo is not { } e || e <= 0) return new Item(key, label, Info, $"{x.Value} ({games}) — keine Elo zum Vergleich");
        var d = x.Value - e;
        var diff = d >= 0 ? $"{d} über der Elo {e}" : $"{-d} unter der Elo {e}";
        var (status, why) = d switch
        {
            _ when d < -LeagueAccountFinder.RatingBelow => (Fail, "viel zu niedrig — vermutlich ein anderer"),
            < 0 => (Warn, "unter der Elo — ungewöhnlich"),
            < LeagueAccountFinder.FitMin => (Warn, $"knapp — üblich sind {LeagueAccountFinder.FitMin}–{LeagueAccountFinder.FitMax} darüber"),
            <= LeagueAccountFinder.FitMax => (Ok, $"passt (üblich {LeagueAccountFinder.FitMin}–{LeagueAccountFinder.FitMax} darüber)"),
            _ => (Warn, $"höher als üblich ({LeagueAccountFinder.FitMin}–{LeagueAccountFinder.FitMax} darüber)"),
        };
        return new Item(key, label, status, $"{x.Value} ({games}) — {diff}: {why}");
    }

    public static Item FideRatingCheck(LeagueAccountFinder.Profile prof, int? elo)
    {
        const string key = "fide", label = "FIDE-Wertung im Profil";
        if (prof.FideRating is not { } fr) return new Item(key, label, None, "nicht angegeben");
        if (elo is not { } e || e <= 0) return new Item(key, label, Info, $"{fr} — keine Elo zum Vergleich");
        return Math.Abs(fr - e) <= LeagueAccountFinder.FideTolerance
            ? new Item(key, label, Ok, $"{fr} — passt zur Elo {e}")
            : new Item(key, label, Warn, $"{fr} — weicht von der Elo {e} um {Math.Abs(fr - e)} ab");
    }

    public static Item TirolCheck(LeagueAccountFinder.Profile prof)
    {
        const string key = "tirol", label = "Tiroler Ort im Profil";
        var place = LeagueAccountFinder.TirolPlace($"{prof.Location} {prof.Bio}");
        if (place is not null) return new Item(key, label, Ok, $"„{place}“");
        return new Item(key, label, None, string.IsNullOrWhiteSpace(prof.Location) ? "kein Ort im Profil" : $"„{prof.Location}“ — kein Tiroler Ort");
    }

    public static Item ActivityCheck(LeagueAccountFinder.Profile prof, DateTime? now = null)
    {
        const string key = "active", label = "Zuletzt aktiv";
        if (prof.LastActive is not { } seen) return new Item(key, label, None, "unbekannt");
        var text = seen.ToString("MM/yyyy", De);
        return (now ?? DateTime.UtcNow) - seen > TimeSpan.FromDays(365.25 * InactiveYears)
            ? new Item(key, label, Info, $"{text} — lange nicht mehr online")
            : new Item(key, label, Info, text);
    }

    /// <summary>Ist dasselbe Konto schon bei einem anderen Spieler eingetragen?</summary>
    private async Task<Item> ElsewhereCheckAsync(string fide, string site, string user, CancellationToken ct)
    {
        const string key = "elsewhere", label = "Bei anderen Spielern";
        var lower = user.ToLower();
        var others = await _db.LeagueOnlineAccounts.AsNoTracking()
            .Where(a => a.Site == site && a.UserName.ToLower() == lower && a.FideId != fide).Select(a => a.FideId).Distinct().ToListAsync(ct);
        if (others.Count == 0) return new Item(key, label, Ok, "bei keinem anderen Spieler eingetragen");
        var names = new List<string>();
        foreach (var o in others) names.Add(await NameOrAnonymousAsync(o, ct));
        return new Item(key, label, Fail, "auch eingetragen bei " + string.Join(", ", names.Distinct()));
    }

    // ── Repertoire ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// „% Übereinstimmung Repertoire": wie viele seiner letzten Online-Partien folgen mindestens <see cref="RepertoireOwnMoves"/>
    /// eigene Züge weit einer Stellung aus seinen Brettpartien (<see cref="LeagueFingerprint.Coverage"/>)?
    /// </summary>
    private async Task<Item> RepertoireCheckAsync(string fide, string site, string user, LeagueOnlineAccount? account, CancellationToken ct)
    {
        const string key = "repertoire", label = "Übereinstimmung Repertoire";
        var board = await LeagueTeamScout.BoardRepertoireAsync(_db, fide, ct);
        if (board.Games < MinBoardGames)
            return new Item(key, label, None, $"nur {board.Games} Brettpartien — zu wenige zum Vergleich");
        List<(IReadOnlyList<string> Sans, bool White)> online;
        if (account is not null)
        {
            var rows = await _db.LeagueOnlineGames.AsNoTracking().Where(g => g.AccountId == account.Id)
                .OrderByDescending(g => g.PlayedAt).Take(MaxOnlineGames * 3).Select(g => new { g.Line, g.White, g.Speed }).ToListAsync(ct);
            online = LeagueFingerprint.Usable(rows, r => r.Speed).Take(MaxOnlineGames)
                .Select(r => ((IReadOnlyList<string>)r.Line.Split(' ', StringSplitOptions.RemoveEmptyEntries), r.White)).ToList();
            if (online.Count == 0)
                return new Item(key, label, None, account.SyncedAt is null ? "noch keine Partien geholt" : "keine Online-Partien");
        }
        else
        {
            var games = await RecentGamesAsync(site, user, ct);
            if (games is null) return new Item(key, label, None, "Partien gerade nicht abrufbar");
            online = LeagueFingerprint.Usable(games, g => g.Speed).Take(MaxOnlineGames)
                .Select(g => ((IReadOnlyList<string>)g.Moves.ToList(), g.White)).ToList();
        }
        return RepertoireItem(LeagueFingerprint.Coverage(online, board, RepertoireOwnMoves), board.Games);
    }

    public static Item RepertoireItem((int Games, int Reached, double Depth) c, int boardGames)
    {
        const string key = "repertoire", label = "Übereinstimmung Repertoire";
        if (c.Games < MinOnlineGames) return new Item(key, label, None, $"nur {c.Games} Online-Partien — zu wenige zum Vergleich");
        var share = (double)c.Reached / c.Games;
        var text = $"{(share * 100).ToString("0", De)} % seiner letzten {c.Games} Online-Partien folgen mindestens {RepertoireOwnMoves} eigene "
                   + $"Züge weit einer Stellung aus seinen {boardGames} Brettpartien (gemeinsam im Schnitt bis Halbzug {c.Depth.ToString("0.0", De)})";
        return new Item(key, label, share >= RepertoireGood ? Ok : share >= RepertoireLow ? Info : Warn, text);
    }

    /// <summary>Die letzten Partien eines Vorschlags (Lichess: ein Abruf; chess.com: die jüngsten Monatsarchive). <c>null</c> = nicht
    /// zu holen.</summary>
    private async Task<List<LeagueOnlineSync.Game>?> RecentGamesAsync(string site, string user, CancellationToken ct)
    {
        try
        {
            if (site == LeagueOnlineSites.Lichess)
            {
                using var r = await _http.GetAsync($"{_lichess}/api/games/user/{Uri.EscapeDataString(user)}?max={MaxOnlineGames * 2}&moves=true"
                                                   + "&clocks=false&evals=false&opening=false&pgnInJson=false", ct);
                if (!r.IsSuccessStatusCode) return null;
                return LeagueOnlineSync.ParseLichess(await r.Content.ReadAsStringAsync(ct), user).Games;
            }
            var name = Uri.EscapeDataString(user.ToLowerInvariant());
            using var list = await _http.GetAsync($"https://api.chess.com/pub/player/{name}/games/archives", ct);
            if (!list.IsSuccessStatusCode) return null;
            var games = new List<LeagueOnlineSync.Game>();
            foreach (var url in LeagueOnlineSync.ArchivesFrom(await list.Content.ReadAsStringAsync(ct), 0, 0).AsEnumerable().Reverse().Take(3))
            {
                using var m = await _http.GetAsync(url, ct);
                if (!m.IsSuccessStatusCode) break;
                games.AddRange(LeagueOnlineSync.ParseChessCom(await m.Content.ReadAsStringAsync(ct), user).Games);
                if (games.Count >= MaxOnlineGames * 2) break;
            }
            return games.OrderByDescending(g => g.PlayedAt).ToList();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}

/// <summary>
/// Selbstmeldungen einspielen (0.619.0): eine Liste je QUELLE (z. B. „Meldeliste Online-TMM 2021") ersetzt die bisherigen Einträge
/// dieser Quelle — was fehlt, fällt weg, was sich geändert hat (Spieler, Team), wird nachgezogen. Unbekannte Spieler (FIDE-ID in
/// keiner Meldeliste und ohne Karte) und unlesbare Konten bleiben draußen und werden genannt. <c>dryRun</c> zählt nur.
/// </summary>
public static class LeagueSelfReportImport
{
    public const int MaxSourceLength = 120, MaxTeamLength = 200, MaxItems = 5000;

    public sealed record Entry(string? Fide, string? Site, string? User, string? Team);
    public sealed record Request(string? Source, List<Entry>? Items);
    public sealed record Problem(int Index, string Reason);
    public sealed record Outcome(int Added, int Updated, int Unchanged, int Removed, List<Problem> Skipped, bool DryRun);

    /// <summary>→ <c>(null, Grund)</c> bei unbrauchbarer Anfrage (<c>noSource</c>, <c>tooMany</c>).</summary>
    public static async Task<(Outcome? Outcome, string? Reason)> ImportAsync(AppDbContext db, Request req, bool dryRun, CancellationToken ct)
    {
        var source = (req.Source ?? "").Trim();
        if (source.Length is 0 or > MaxSourceLength) return (null, "noSource");
        var items = req.Items ?? new();
        if (items.Count > MaxItems) return (null, "tooMany");
        var known = (await db.LeaguePlayers.AsNoTracking().Where(p => p.FideId != null && p.FideId != "").Select(p => p.FideId!).Distinct().ToListAsync(ct))
            .Concat(await db.LeaguePlayerProfiles.AsNoTracking().Select(p => p.FideId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var skipped = new List<Problem>();
        var wanted = new Dictionary<string, (string Fide, string Site, string User, string? Team)>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            var e = items[i];
            var fide = (e.Fide ?? "").Trim();
            if (fide.Length is 0 or > 16 || !fide.All(char.IsAsciiDigit)) { skipped.Add(new(i, "invalidFide")); continue; }
            if (!known.Contains(fide)) { skipped.Add(new(i, "unknownPlayer")); continue; }
            if (LeagueOnlineSites.Parse(e.Site, e.User) is not { } acc) { skipped.Add(new(i, "invalidUser")); continue; }
            var key = acc.Site + "|" + acc.User.ToLowerInvariant();
            if (wanted.ContainsKey(key)) { skipped.Add(new(i, "duplicate")); continue; }
            var team = string.IsNullOrWhiteSpace(e.Team) ? null : e.Team.Trim();
            wanted[key] = (fide, acc.Site, acc.User, team is { Length: > MaxTeamLength } ? team[..MaxTeamLength] : team);
        }
        var existing = await db.LeagueSelfReports.Where(r => r.Source == source).ToListAsync(ct);
        int added = 0, updated = 0, unchanged = 0, removed = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in existing)
        {
            var key = r.Site + "|" + r.UserName.ToLowerInvariant();
            if (!wanted.TryGetValue(key, out var w) || !seen.Add(key))
            {
                removed++;
                if (!dryRun) db.LeagueSelfReports.Remove(r);
                continue;
            }
            if (r.FideId == w.Fide && r.Team == w.Team && r.UserName == w.User) { unchanged++; continue; }
            updated++;
            if (!dryRun) { r.FideId = w.Fide; r.Team = w.Team; r.UserName = w.User; }
        }
        foreach (var (key, w) in wanted)
        {
            if (seen.Contains(key)) continue;
            added++;
            if (!dryRun)
                db.LeagueSelfReports.Add(new LeagueSelfReport
                {
                    FideId = w.Fide, Site = w.Site, UserName = w.User, Source = source, Team = w.Team, CreatedAt = DateTime.UtcNow,
                });
        }
        if (!dryRun) await db.SaveChangesAsync(ct);
        return (new Outcome(added, updated, unchanged, removed, skipped, dryRun), null);
    }
}
