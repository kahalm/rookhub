using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Sucht Online-Konten der Ligaspieler und legt sie als VORSCHLÄGE ab (0.607.0, Wunsch 2026-09-30: „Konto-Vorschläge" —
/// LeagueHub sucht selbst, ein Verwalter bestätigt oder verwirft). Portiert aus <c>online_accounts.py</c> im
/// league-analyzer, dort aber streng (nur Konten mit Klarnamen im Profil, 29 Konten bei 27 Spielern); hier genügt weniger,
/// weil ein Mensch entscheidet:
/// <list type="bullet">
/// <item>Kandidaten: Nutzernamen aus dem Namen (<see cref="Variants"/>) auf beiden Seiten, dazu auf Lichess die Nutzernamen,
///   die mit dem Nachnamen beginnen (Suchvorschläge, ab 5 Buchstaben).</item>
/// <item>Ausgeschlossen: gesperrte/geschlossene Konten, ein Profil mit einem ANDEREN Namen, ein Land, das weder Österreich
///   noch die Föderation des Spielers ist.</item>
/// <item>Hinweise (<see cref="Judge"/>): Klarname im Profil, FIDE-Wertung im Profil nahe der Liste, Tiroler Ort, Land. Ein
///   Nutzername aus dem Namen braucht mindestens EINEN Hinweis, einer aus der Suche einen starken.</item>
/// <item>Minderjährige (Jahrgang laut FIDE, über Lichess nachgeschlagen) werden NIE gesucht; ohne Jahrgang auch nicht.</item>
/// </list>
/// </summary>
public sealed partial class LeagueAccountFinder
{
    /// <summary>
    /// Fassung der Regeln. Wer an Kandidaten oder Urteil dreht, erhöht sie — dann sucht der Hintergrund jeden Spieler einmal neu,
    /// und offene Vorschläge, die die neue Regel nicht mehr trägt, fallen weg. 1 = 0.607.0, 2 = Online-Wertung gegen Elo (0.609.0).
    /// </summary>
    public const int CurrentVersion = 2;
    /// <summary>Jünger wird nicht gesucht.</summary>
    public const int AdultAge = 18;
    /// <summary>Nach so vielen Tagen wird ein Spieler erneut abgesucht (neue Konten, geänderte Profile).</summary>
    public const int RescanDays = 90;
    /// <summary>Lichess-Suchvorschläge erst ab so langen Nachnamen — „Mair" findet hunderte Fremde.</summary>
    public const int MinSearchLength = 5;
    public const int ScoreName = 3, ScoreFide = 2, ScoreTirol = 2, ScoreLastName = 1, ScoreCountry = 1;
    /// <summary>Nötige Hinweise: Nutzername aus dem Namen / aus der Suche.</summary>
    public const int NeedDerived = 1, NeedSearched = 3;
    /// <summary>So nah muss eine FIDE-Wertung im Profil an der Liste liegen.</summary>
    public const int FideTolerance = 250;
    /// <summary>
    /// Online-Wertung gegen Elo (Wunsch 2026-09-30: „ein Konto mit 500 auf einem FIDE-Spieler mit 2000 ergibt keinen Sinn —
    /// nur niedriger ist ein Problem, alles droppen, was 400 niedriger ist"): die BESTE belastbare Wertung des Kontos (ab
    /// <see cref="MinRatedGames"/> Partien, nicht vorläufig; Bullet zählt mit) darf höchstens <see cref="RatingBelow"/> UNTER der
    /// Elo liegen, sonst ist es nicht er. Nach oben gibt es keine Grenze (Online-Wertungen liegen meist darüber, Lichess
    /// deutlich). Liegt sie in <see cref="FitBelow"/>..<see cref="FitAbove"/> um die Elo, ist das ein Hinweis mehr.
    /// </summary>
    public const int RatingBelow = 400, FitBelow = 250, FitAbove = 450, ScoreRating = 1, MinRatedGames = 10;

    public sealed record Player(string Fide, string Name, string? Fed, int? Elo, string? Team);

    /// <summary>Ein Profil auf einer Seite, so weit es für die Entscheidung zählt.</summary>
    /// <param name="Rating">Beste belastbare Online-Wertung (<see cref="MinRatedGames"/>, nicht vorläufig) — <c>null</c> = keine.</param>
    /// <param name="RatingLabel">Wo sie herkommt („Lichess Blitz", „chess.com Schnell").</param>
    public sealed record Profile(string Site, string User, string Url, string? RealName, string? Flag, string? Location,
        string? Bio, int? FideRating, DateTime? LastActive, bool Closed, int? Rating = null, string? RatingLabel = null);

    public sealed record Verdict(int Score, List<string> Evidence);

    /// <summary>Ergebnis einer Suche für einen Spieler: neue Vorschläge und — wenn nicht gesucht wurde — warum.</summary>
    public sealed record ScanResult(int Found, string? Skipped);

    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly ILogger<LeagueAccountFinder> _logger;
    private readonly string _lichess;

    public LeagueAccountFinder(AppDbContext db, HttpClient http, ILogger<LeagueAccountFinder> logger, IConfiguration? config = null)
    {
        _db = db;
        _http = http;
        _logger = logger;
        _lichess = (config?["Lichess:SiteUrl"] ?? "https://lichess.org").TrimEnd('/');
    }

    /// <summary>Pause zwischen zwei chess.com-Abfragen (die Schnittstelle mag keine dichte Folge).</summary>
    public TimeSpan ChessComPause { get; init; } = TimeSpan.FromMilliseconds(300);
    /// <summary>Pause zwischen zwei Spielern im Hintergrundlauf.</summary>
    public TimeSpan PlayerPause { get; init; } = TimeSpan.FromSeconds(1);

    // ── Namen (rein, getestet) ──────────────────────────────────────────────────────────────────

    /// <summary>„Müller" → „Mueller", Akzente weg — so schreiben Nutzernamen Umlaute.</summary>
    public static string Plain(string? s)
    {
        var t = (s ?? "").Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("Ä", "Ae").Replace("Ö", "Oe")
            .Replace("Ü", "Ue").Replace("ß", "ss");
        var sb = new StringBuilder();
        foreach (var c in t.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c < 128) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Nachname und Vorname aus „Nachname, Vorname" (ohne Titel); ohne Komma „Nachname Vorname" wie auf chess-results.</summary>
    public static (string Last, string First) SplitName(string? name)
    {
        var key = LeagueNames.NameKey(LeagueNames.StripTitles(name));
        var comma = key.IndexOf(',');
        if (comma >= 0) return (key[..comma].Trim(), key[(comma + 1)..].Trim());
        var parts = key.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (key, "");
    }

    /// <summary>Naheliegende Nutzernamen: PatrikOberschmid, Patrik_Oberschmid, Patrik-Oberschmid, OberschmidPatrik,
    /// Oberschmid_Patrik, POberschmid, OberschmidP — nur, was als Nutzername auf beiden Seiten gültig ist.</summary>
    public static List<string> Variants(string? name)
    {
        var (last, first) = SplitName(name);
        var f = first.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var l = string.Concat(Plain(last).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Capital));
        var fp = Capital(Plain(f));
        if (l.Length == 0 || fp.Length == 0) return new();
        var list = new[] { fp + l, fp + "_" + l, fp + "-" + l, l + fp, l + "_" + fp, fp[0] + l, l + fp[0] };
        return list.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Length is >= 3 and <= 25 && ValidUser().IsMatch(x)).ToList();
    }

    private static string Capital(string s) =>
        string.Join("", Regex.Split(s, "(?<=[-])").Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));

    private static List<string> Tokens(string? s) =>
        Regex.Replace(Plain(s).ToLowerInvariant(), "[^a-z ]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static readonly Regex TirolRe = new(
        @"\b(tirol|tyrol|innsbruck|schwaz|kufstein|w(ö|oe)rgl|jenbach|absam|telfs|zirl|landeck|imst|reutte|lienz|kitzb\w*|zillertal|" +
        @"f(ü|ue)gen|rattenberg|v(ö|oe)ls|wattens|mils|kundl|steinach|pradl|hall in tirol)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>FIDE-Föderation → Landeskürzel der Profile.</summary>
    private static readonly Dictionary<string, string> Fed2 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AUT"] = "AT", ["GER"] = "DE", ["ITA"] = "IT", ["SUI"] = "CH", ["CZE"] = "CZ", ["UKR"] = "UA", ["HUN"] = "HU",
        ["SLO"] = "SI", ["CRO"] = "HR", ["BIH"] = "BA", ["TUR"] = "TR", ["FRA"] = "FR", ["BUL"] = "BG", ["MAR"] = "MA",
        ["POL"] = "PL", ["SVK"] = "SK", ["SRB"] = "RS", ["ROU"] = "RO", ["RUS"] = "RU", ["NED"] = "NL", ["ESP"] = "ES",
        ["ENG"] = "GB", ["USA"] = "US", ["IRI"] = "IR", ["SYR"] = "SY", ["AFG"] = "AF",
    };

    /// <summary>
    /// Passt das Profil zum Spieler? <c>null</c> = nein (fremder Name, fremdes Land, gesperrt oder zu wenig Hinweise).
    /// <paramref name="derived"/> = der Nutzername kam aus dem Namen (braucht einen Hinweis), sonst aus der Suche (braucht
    /// einen starken). <paramref name="fideFed"/> = Föderation laut FIDE (kann von der Meldeliste abweichen).
    /// </summary>
    public static Verdict? Judge(Player p, Profile prof, bool derived, string? fideFed = null)
    {
        if (prof.Closed) return null;
        var (last, first) = SplitName(p.Name);
        var lt = Tokens(last);
        var ft = Tokens(first).Take(1).ToList();
        if (lt.Count == 0) return null;
        var toks = Tokens(prof.RealName);
        var score = 0;
        var ev = new List<string>();
        if (toks.Count > 0)
        {
            if (!lt.All(toks.Contains)) return null;                 // das Profil nennt jemand anderen
            if (ft.Count > 0 && ft.All(toks.Contains)) { score += ScoreName; ev.Add($"Klarname im Profil („{Short(prof.RealName)}“)"); }
            else { score += ScoreLastName; ev.Add($"Nachname im Profil („{Short(prof.RealName)}“)"); }
        }
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AT" };
        if (p.Fed is { } f1 && Fed2.TryGetValue(f1, out var c1)) allowed.Add(c1);
        if (fideFed is { } f2 && Fed2.TryGetValue(f2, out var c2)) allowed.Add(c2);
        var flag = (prof.Flag ?? "").Trim();
        if (flag.Length >= 2)
        {
            var code = flag[..2].ToUpperInvariant();
            if (!allowed.Contains(code)) return null;                // anderes Land — vermutlich ein Namensvetter
            score += ScoreCountry;
            ev.Add(code == "AT" ? "Land Österreich" : $"Land {code}");
        }
        if (!RatingPlausible(prof, p.Elo)) return null;                   // 500 online bei 2000 Elo — ein anderer (höher ist ok)
        if (prof.Rating is { } rt && p.Elo is { } e0 && e0 > 0 && rt >= e0 - FitBelow && rt <= e0 + FitAbove)
        {
            score += ScoreRating;
            ev.Add($"{prof.RatingLabel ?? "Online-Wertung"} {rt} passt zu Elo {e0}");
        }
        if (prof.FideRating is { } fr && p.Elo is { } elo && elo > 0 && Math.Abs(fr - elo) <= FideTolerance)
        {
            score += ScoreFide;
            ev.Add($"FIDE-Wertung im Profil {fr} (Liste {elo})");
        }
        if (TirolRe.IsMatch($"{prof.Location} {prof.Bio}"))
        {
            score += ScoreTirol;
            ev.Add("Tiroler Ort im Profil");
        }
        if (score < (derived ? NeedDerived : NeedSearched)) return null;
        if (derived) { score += 1; ev.Insert(0, "Nutzername aus dem Namen"); }
        else ev.Insert(0, "Nutzername beginnt mit dem Nachnamen");
        return new Verdict(score, ev);
    }

    /// <summary>Liegt die Online-Wertung nicht zu weit UNTER der Elo? Ohne Wertung oder ohne Elo: kein Einwand.</summary>
    public static bool RatingPlausible(Profile prof, int? elo) =>
        prof.Rating is not { } r || elo is not { } e || e <= 0 || r >= e - RatingBelow;

    private static string Short(string? s) => (s ?? "").Length > 60 ? s![..60] + "…" : s ?? "";

    // ── Antworten der Seiten lesen (rein, getestet) ─────────────────────────────────────────────

    /// <summary><c>POST /api/users</c> (Lichess, viele Nutzer auf einmal).</summary>
    public static List<Profile> ParseLichessUsers(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<Profile>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var u in doc.RootElement.EnumerateArray())
        {
            var user = Str(u, "username") ?? Str(u, "id");
            if (user is null) continue;
            var closed = True(u, "disabled") || True(u, "closed") || True(u, "tosViolation");
            string? real = null, flag = null, loc = null, bio = null;
            int? fide = null;
            if (u.TryGetProperty("profile", out var pr) && pr.ValueKind == JsonValueKind.Object)
            {
                real = Str(pr, "realName");
                if (string.IsNullOrWhiteSpace(real))
                    real = string.Join(' ', new[] { Str(pr, "firstName"), Str(pr, "lastName") }.Where(x => !string.IsNullOrWhiteSpace(x)));
                flag = Str(pr, "flag") ?? Str(pr, "country");
                loc = Str(pr, "location");
                bio = Str(pr, "bio");
                fide = Int(pr, "fideRating");
            }
            DateTime? seen = u.TryGetProperty("seenAt", out var sa) && sa.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;
            var (rating, label) = BestLichess(u);
            list.Add(new Profile(LeagueOnlineSites.Lichess, user, LeagueOnlineSites.ProfileUrl(LeagueOnlineSites.Lichess, user),
                string.IsNullOrWhiteSpace(real) ? null : real, flag, loc, bio, fide, seen, closed, rating, label));
        }
        return list;
    }

    private static readonly (string Key, string Label)[] LichessPerfs =
        { ("bullet", "Bullet"), ("blitz", "Blitz"), ("rapid", "Schnell"), ("classical", "Klassisch"), ("correspondence", "Fernschach") };

    /// <summary>Beste Lichess-Wertung mit genug Partien und nicht vorläufig (unbespielte stehen auf 1500 und „prov").</summary>
    private static (int? Rating, string? Label) BestLichess(JsonElement u)
    {
        if (!u.TryGetProperty("perfs", out var perfs) || perfs.ValueKind != JsonValueKind.Object) return (null, null);
        (int? Rating, string? Label) best = (null, null);
        foreach (var (key, label) in LichessPerfs)
        {
            if (!perfs.TryGetProperty(key, out var pf) || pf.ValueKind != JsonValueKind.Object) continue;
            if (True(pf, "prov") || (Int(pf, "games") ?? 0) < MinRatedGames || Int(pf, "rating") is not { } r) continue;
            if (best.Rating is null || r > best.Rating) best = (r, $"Lichess {label}");
        }
        return best;
    }

    /// <summary><c>GET /pub/player/{name}/stats</c> (chess.com): beste Wertung mit genug Partien, dazu die FIDE-Wertung, die
    /// der Nutzer selbst angegeben hat.</summary>
    public static (int? Rating, string? Label, int? Fide) ParseChessComStats(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var d = doc.RootElement;
        (int? Rating, string? Label) best = (null, null);
        foreach (var (key, label) in new[] { ("chess_bullet", "Bullet"), ("chess_blitz", "Blitz"), ("chess_rapid", "Schnell"), ("chess_daily", "Täglich") })
        {
            if (!d.TryGetProperty(key, out var s) || s.ValueKind != JsonValueKind.Object) continue;
            var games = s.TryGetProperty("record", out var rec) && rec.ValueKind == JsonValueKind.Object
                ? (Int(rec, "win") ?? 0) + (Int(rec, "loss") ?? 0) + (Int(rec, "draw") ?? 0) : 0;
            if (games < MinRatedGames || !s.TryGetProperty("last", out var last) || Int(last, "rating") is not { } r) continue;
            if (best.Rating is null || r > best.Rating) best = (r, $"chess.com {label}");
        }
        return (best.Rating, best.Label, Int(d, "fide"));
    }

    /// <summary><c>GET /pub/player/{name}</c> (chess.com).</summary>
    public static Profile? ParseChessComPlayer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var d = doc.RootElement;
        var user = Str(d, "username");
        if (user is null) return null;
        var status = Str(d, "status") ?? "";
        var country = (Str(d, "country") ?? "").TrimEnd('/').Split('/')[^1];
        DateTime? seen = d.TryGetProperty("last_online", out var lo) && lo.TryGetInt64(out var s)
            ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime : null;
        // chess.com liefert den Namen klein; die Anzeige-Schreibweise steht in der Profiladresse.
        var url = Str(d, "url");
        var shown = url?.TrimEnd('/').Split('/')[^1] is { Length: > 0 } seg && seg.Equals(user, StringComparison.OrdinalIgnoreCase) ? seg : user;
        return new Profile(LeagueOnlineSites.ChessCom, shown, LeagueOnlineSites.ProfileUrl(LeagueOnlineSites.ChessCom, shown),
            Str(d, "name"), country.Length == 2 ? country : null, Str(d, "location"), null, Int(d, "fide"), seen,
            status.StartsWith("closed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary><c>GET /api/player/autocomplete?object=true</c> (Lichess).</summary>
    public static List<string> ParseAutocomplete(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var r) || r.ValueKind != JsonValueKind.Array) return new();
        return r.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : Str(x, "name") ?? Str(x, "id"))
            .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToList();
    }

    /// <summary><c>GET /api/fide/player/{id}</c> (Lichess): Jahrgang und Föderation.</summary>
    public static (int? Year, string? Federation) ParseFidePlayer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return (Int(doc.RootElement, "year"), Str(doc.RootElement, "federation"));
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static bool True(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]*$")]
    private static partial Regex ValidUser();

    // ── Suchen ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Den Spieler zu einer FIDE-ID aus der jüngsten Meldeliste (Name, Föderation, Elo, Mannschaft).</summary>
    public async Task<Player?> PlayerAsync(string fide, CancellationToken ct)
    {
        var row = await (from p in _db.LeaguePlayers.AsNoTracking()
                         join t in _db.LeagueTournaments.AsNoTracking() on p.Tnr equals t.Tnr
                         where p.FideId == fide
                         orderby t.Season descending, p.Id descending
                         select new { p.Name, p.Fed, p.EloI, p.EloN, p.Team }).FirstOrDefaultAsync(ct);
        if (row is not null) return new Player(fide, row.Name, row.Fed, row.EloI is > 0 ? row.EloI : row.EloN, row.Team);
        var name = await _db.LeaguePlayerProfiles.AsNoTracking().Where(p => p.FideId == fide).Select(p => p.Name).FirstOrDefaultAsync(ct);
        return name is null ? null : new Player(fide, name, null, null, null);
    }

    /// <summary>
    /// Sucht für einen Spieler und legt neue Vorschläge an. Schon übernommene (Konto da) und verworfene kommen nicht wieder.
    /// Drosselt eine Seite (429), wirft <see cref="LeagueOnlineSync.RateLimitedException"/> — der Stand bleibt unverändert.
    /// </summary>
    public async Task<ScanResult> ScanAsync(Player p, CancellationToken ct)
    {
        var scan = await _db.LeagueAccountScans.FirstOrDefaultAsync(s => s.FideId == p.Fide, ct);
        if (scan is null)
        {
            scan = new LeagueAccountScan { FideId = p.Fide };
            _db.LeagueAccountScans.Add(scan);
        }
        if (scan.BirthYear is null)
        {
            using var r = await _http.GetAsync($"{_lichess}/api/fide/player/{Uri.EscapeDataString(p.Fide)}", ct);
            if (r.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("Lichess");
            if (r.IsSuccessStatusCode)
            {
                var (born, fed) = ParseFidePlayer(await r.Content.ReadAsStringAsync(ct));
                scan.BirthYear = born;
                scan.Federation = Cut(fed, 8);
            }
        }
        var fideFed = scan.Federation;
        scan.ScannedAt = DateTime.UtcNow;
        string? skipped = scan.BirthYear is not { } year ? "Jahrgang unbekannt"
            : DateTime.UtcNow.Year - year < AdultAge ? "minderjährig" : null;
        if (skipped is not null)
        {
            // Nie Vorschläge für Minderjährige — auch keine von früher (Jahrgang nachgetragen).
            var stale = await _db.LeagueAccountSuggestions.Where(s => s.FideId == p.Fide && s.Status == LeagueSuggestionStatus.Open).ToListAsync(ct);
            _db.LeagueAccountSuggestions.RemoveRange(stale);
            scan.Note = skipped;
            scan.Found = 0;
            scan.Version = CurrentVersion;
            await _db.SaveChangesAsync(ct);
            return new ScanResult(0, skipped);
        }

        var derived = Variants(p.Name);
        var searched = new List<string>();
        var (last, _) = SplitName(p.Name);
        var plainLast = Plain(last).Replace(" ", "");
        if (plainLast.Length >= MinSearchLength && ValidUser().IsMatch(plainLast))
        {
            using var r = await _http.GetAsync($"{_lichess}/api/player/autocomplete?term={Uri.EscapeDataString(plainLast.ToLowerInvariant())}&object=true", ct);
            if (r.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("Lichess");
            if (r.IsSuccessStatusCode) searched = ParseAutocomplete(await r.Content.ReadAsStringAsync(ct));
        }
        var isDerived = new HashSet<string>(derived, StringComparer.OrdinalIgnoreCase);
        var profiles = new List<(Profile Profile, bool Derived)>();

        var lichessNames = derived.Concat(searched).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (lichessNames.Count > 0)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_lichess}/api/users")
            {
                Content = new StringContent(string.Join(',', lichessNames.Select(x => x.ToLowerInvariant())), Encoding.UTF8, "text/plain"),
            };
            using var r = await _http.SendAsync(req, ct);
            if (r.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("Lichess");
            r.EnsureSuccessStatusCode();
            profiles.AddRange(ParseLichessUsers(await r.Content.ReadAsStringAsync(ct)).Select(x => (x, isDerived.Contains(x.User))));
        }
        foreach (var name in derived)
        {
            if (ChessComPause > TimeSpan.Zero) await Task.Delay(ChessComPause, ct);
            using var r = await _http.GetAsync($"https://api.chess.com/pub/player/{Uri.EscapeDataString(name.ToLowerInvariant())}", ct);
            if (r.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("chess.com");
            if (r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) continue;
            r.EnsureSuccessStatusCode();
            if (ParseChessComPlayer(await r.Content.ReadAsStringAsync(ct)) is not { } prof || prof.Closed) continue;
            // Wertungen stehen nicht im Profil, sondern in /stats — ein Abruf mehr, nur für Konten, die es gibt.
            if (ChessComPause > TimeSpan.Zero) await Task.Delay(ChessComPause, ct);
            using var st = await _http.GetAsync($"https://api.chess.com/pub/player/{Uri.EscapeDataString(name.ToLowerInvariant())}/stats", ct);
            if (st.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("chess.com");
            if (st.IsSuccessStatusCode)
            {
                var (rating, label, fide) = ParseChessComStats(await st.Content.ReadAsStringAsync(ct));
                prof = prof with { Rating = rating, RatingLabel = label, FideRating = fide ?? prof.FideRating };
            }
            profiles.Add((prof, true));
        }

        var accounts = await _db.LeagueOnlineAccounts.AsNoTracking().Where(a => a.FideId == p.Fide)
            .Select(a => new { a.Site, a.UserName }).ToListAsync(ct);
        var known = await _db.LeagueAccountSuggestions.Where(s => s.FideId == p.Fide).ToListAsync(ct);
        static string Key(string site, string user) => site + "|" + user.ToLowerInvariant();
        var taken = accounts.Select(a => Key(a.Site, a.UserName)).Concat(known.Select(s => Key(s.Site, s.UserName))).ToHashSet();
        var found = 0;
        var confirmed = new HashSet<string>();
        foreach (var (prof, der) in profiles)
        {
            if (Judge(p, prof, der, fideFed) is not { } v) continue;
            confirmed.Add(Key(prof.Site, prof.User));
            if (!taken.Add(Key(prof.Site, prof.User))) continue;
            _db.LeagueAccountSuggestions.Add(new LeagueAccountSuggestion
            {
                FideId = p.Fide, Site = prof.Site, UserName = prof.User, Url = prof.Url, Score = v.Score,
                Evidence = Cut(string.Join("; ", v.Evidence), 500)!, ProfileName = Cut(prof.RealName, 120), Location = Cut(prof.Location, 120),
                LastActive = prof.LastActive, Status = LeagueSuggestionStatus.Open, CreatedAt = DateTime.UtcNow,
            });
            found++;
        }
        // Offene Vorschläge, die diese Suche nicht mehr trägt (geänderte Regel, geändertes Profil), fallen weg; verworfene bleiben.
        _db.LeagueAccountSuggestions.RemoveRange(known.Where(k => k.Status == LeagueSuggestionStatus.Open && !confirmed.Contains(Key(k.Site, k.UserName))));
        scan.Note = null;
        scan.Found = found;
        scan.Version = CurrentVersion;
        await _db.SaveChangesAsync(ct);
        return new ScanResult(found, null);
    }

    private static string? Cut(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    /// <summary>
    /// Ein Durchgang im Hintergrund: die Spieler der laufenden Saison mit FIDE-ID, die noch nie oder vor mehr als
    /// <see cref="RescanDays"/> Tagen abgesucht wurden — nie gesuchte zuerst —, bis <paramref name="budget"/> um ist. Drosselt
    /// eine Seite, endet der Durchgang. → noch Spieler offen?
    /// </summary>
    public async Task<bool> RunOnceAsync(TimeSpan budget, CancellationToken ct)
    {
        var season = await _db.LeagueTournaments.AsNoTracking().MaxAsync(t => (string?)t.Season, ct);
        if (season is null) return false;
        var rows = await (from p in _db.LeaguePlayers.AsNoTracking()
                          join t in _db.LeagueTournaments.AsNoTracking() on p.Tnr equals t.Tnr
                          where t.Season == season && p.FideId != null && p.FideId != ""
                          select new { p.FideId, p.Name, p.Fed, p.EloI, p.EloN, p.Team }).ToListAsync(ct);
        var players = rows.GroupBy(r => r.FideId!).Select(g => g.First())
            .Select(r => new Player(r.FideId!, r.Name, r.Fed, r.EloI is > 0 ? r.EloI : r.EloN, r.Team)).ToList();
        var due = DateTime.UtcNow.AddDays(-RescanDays);
        var scans = await _db.LeagueAccountScans.AsNoTracking()
            .ToDictionaryAsync(s => s.FideId, s => (s.ScannedAt, s.Version), ct);
        var queue = players.Where(p => !scans.TryGetValue(p.Fide, out var sc) || sc.ScannedAt < due || sc.Version < CurrentVersion)
            .OrderBy(p => scans.ContainsKey(p.Fide)).ThenBy(p => scans.TryGetValue(p.Fide, out var sc) ? sc.ScannedAt : DateTime.MinValue).ToList();
        var started = DateTime.UtcNow;
        var done = 0;
        foreach (var p in queue)
        {
            if (DateTime.UtcNow - started >= budget) break;
            try
            {
                await ScanAsync(p, ct);
            }
            catch (LeagueOnlineSync.RateLimitedException e)
            {
                _logger.LogWarning("LeagueHub: {Message} — Konto-Suche pausiert", e.Message);
                return true;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(e, "LeagueHub: Konto-Suche für {Fide} gescheitert", p.Fide);
                _db.ChangeTracker.Clear();
                var s = await _db.LeagueAccountScans.FirstOrDefaultAsync(x => x.FideId == p.Fide, ct);
                if (s is null) _db.LeagueAccountScans.Add(s = new LeagueAccountScan { FideId = p.Fide });
                s.ScannedAt = DateTime.UtcNow.AddDays(1 - RescanDays);    // nicht gleich wieder, aber morgen
                s.Note = Cut("Fehler: " + e.Message, 200);
                await _db.SaveChangesAsync(ct);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
            done++;
            if (PlayerPause > TimeSpan.Zero) await Task.Delay(PlayerPause, ct);
        }
        return done < queue.Count;
    }
}
