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
/// <item>Ausgeschlossen: gesperrte/geschlossene Konten, ein Profil mit einem ANDEREN Namen, ein Land, das weder das Land seiner Region (Österreich/Deutschland)
///   noch die Föderation des Spielers ist.</item>
/// <item>Hinweise (<see cref="Judge"/>): Klarname im Profil, FIDE-Wertung im Profil nahe der Liste, Ort der Region, Land. Ein
///   Nutzername aus dem Namen braucht mindestens EINEN Hinweis, einer aus der Suche einen starken.</item>
/// <item>Minderjährige (Jahrgang laut FIDE, über Lichess nachgeschlagen) werden seit 0.610.0 auch gesucht, ihre Konten bleiben
///   aber verborgen (<see cref="LeagueHiddenAccounts"/>).</item>
/// </list>
/// </summary>
public sealed partial class LeagueAccountFinder
{
    /// <summary>
    /// Fassung der Regeln. Wer an Kandidaten oder Urteil dreht, erhöht sie — dann sucht der Hintergrund jeden Spieler einmal neu,
    /// und offene Vorschläge, die die neue Regel nicht mehr trägt, fallen weg. 1 = 0.607.0, 2 = Online-Wertung gegen Elo (0.609.0),
    /// 3 = auch Minderjährige, verborgen (0.610.0), 4 = anderer Vorname im Profil = anderer Mensch (0.611.0),
    /// 5 = Online-Wertungsband 100–300 über der Elo (0.619.0), 6 = das Band ist der OPTIMALE Treffer, jede andere Wertung bis 400 unter
    /// der Elo ein schwächerer, dazu derselbe Nutzername auf der anderen Seite (0.621.0), 7 = Lichess-„vorläufig" mit genug Partien
    /// zählt (0.622.0), 8 = Land und Orte nach der Region des Spielers (Bayern: Deutschland, bayerische Orte; 0.712.0).
    /// </summary>
    public const int CurrentVersion = 8;
    /// <summary>Jünger = Konten verborgen (<see cref="LeagueHiddenAccounts"/>).</summary>
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
    /// deutlich). Liegt sie <see cref="FitMin"/> bis <see cref="FitMax"/> ÜBER der Elo, ist das der OPTIMALE Treffer
    /// (<see cref="ScoreRatingFit"/>), jede andere Wertung darüber oder bis 400 darunter ein SCHWÄCHERER (<see cref="ScoreRatingWeak"/>) —
    /// Wunsch 2026-09-30: „normal ist Elo online ca. 200 höher, 100–300 wäre passend" und „das Band zeigt die optimalen Treffer, 400
    /// unter FIDE schließt aus, alles andere ist halt Treffer, aber schwächer" (0.621.0; 0.619.0 gab außerhalb des Bands nichts, davor
    /// −250 bis +450 einen Punkt).
    /// </summary>
    public const int RatingBelow = 400, FitMin = 100, FitMax = 300, ScoreRatingFit = 2, ScoreRatingWeak = 1, MinRatedGames = 10;
    /// <summary>Derselbe Nutzername wie ein Treffer auf der anderen Seite (0.621.0) — ein Hinweis.</summary>
    public const int ScoreTwin = 1;

    /// <summary>Liegt die Online-Wertung im üblichen Abstand über der Elo (<see cref="FitMin"/>..<see cref="FitMax"/>)?</summary>
    public static bool RatingFits(int rating, int? elo) => elo is { } e && e > 0 && rating - e is >= FitMin and <= FitMax;

    /// <summary>
    /// Was die Online-Wertung für den Treffer bringt: im Band optimal, sonst (bis <see cref="RatingBelow"/> darunter) schwächer; <c>null</c>
    /// ohne Wertung, ohne Elo oder zu weit darunter (dann schließt <see cref="RatingPlausible"/> das Konto ohnehin aus).
    /// „Lichess Blitz 2100 liegt 200 über der Elo 1900 (optimal: 100–300 darüber)".
    /// </summary>
    public static (int Score, string Text)? RatingEvidence(string? label, int? rating, int? elo)
    {
        if (rating is not { } r || elo is not { } e || e <= 0 || r < e - RatingBelow) return null;
        var d = r - e;
        var text = $"{label ?? "Online-Wertung"} {r} liegt {(d >= 0 ? $"{d} über" : $"{-d} unter")} der Elo {e}";
        return RatingFits(r, e)
            ? (ScoreRatingFit, $"{text} (optimal: {FitMin}–{FitMax} darüber)")
            : (ScoreRatingWeak, $"{text} (Treffer, aber schwächer — optimal wären {FitMin}–{FitMax} darüber)");
    }

    /// <param name="Local">Spieler einer Liga (Vorgabe): das Land seiner Region ist als Land immer erlaubt, ein Ort der Region zählt.
    /// <c>false</c> = ein Spieler nur aus dem Partiebestand der Spielervorbereitung (0.637.0) — dann gilt nur seine Föderation, kein Ort.</param>
    /// <param name="Region">Liga-Region seiner jüngsten Meldeliste (0.712.0; <c>null</c> = Tirol wie bisher) — bestimmt Land (Österreich bzw.
    /// Deutschland) und Orte (<see cref="LeagueOnlineRegions"/>).</param>
    public sealed record Player(string Fide, string Name, string? Fed, int? Elo, string? Team, bool Local = true, string? Region = null)
    {
        /// <summary>Was die Region des Spielers für die Prüfung bedeutet.</summary>
        public LeagueOnlineRegion OnlineRegion => LeagueOnlineRegions.Of(Region);
    }

    /// <summary>Ein Profil auf einer Seite, so weit es für die Entscheidung zählt.</summary>
    /// <param name="Rating">Beste belastbare Online-Wertung (<see cref="MinRatedGames"/>, nicht vorläufig) — <c>null</c> = keine.</param>
    /// <param name="RatingLabel">Wo sie herkommt („Lichess Blitz", „chess.com Schnell").</param>
    /// <param name="Ratings">ALLE Wertungen je Kategorie (auch vorläufige) — für die Konto-Prüfung (i), 0.619.0.</param>
    public sealed record Profile(string Site, string User, string Url, string? RealName, string? Flag, string? Location,
        string? Bio, int? FideRating, DateTime? LastActive, bool Closed, int? Rating = null, string? RatingLabel = null,
        IReadOnlyList<Rating>? Ratings = null);

    /// <summary>Eine Wertung einer Kategorie: <paramref name="Reliable"/> = mindestens <see cref="MinRatedGames"/> Partien — nur solche
    /// zählen fürs Urteil. Lichess' „vorläufig" (prov) zählt seit 0.622.0 NICHT mehr dagegen: es kommt auch von langer Pause (hohe
    /// Wertungs-Abweichung) — gesehen an einem Konto mit 767 Bullet-Partien; unbespielte Kategorien haben 0 Partien und fallen ohnehin weg.</summary>
    public sealed record Rating(string Label, int Value, int Games, bool Reliable);

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

    /// <summary>Namensteile, klein, Umlaute als ae/oe/ue, ohne Satzzeichen.</summary>
    public static List<string> Tokens(string? s) =>
        Regex.Replace(Plain(s).ToLowerInvariant(), "[^a-z ]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static readonly Regex TirolRe = new(
        @"\b(tirol|tyrol|innsbruck|schwaz|kufstein|w(ö|oe)rgl|jenbach|absam|telfs|zirl|landeck|imst|reutte|lienz|kitzb\w*|zillertal|" +
        @"f(ü|ue)gen|rattenberg|v(ö|oe)ls|wattens|mils|kundl|steinach|pradl|hall in tirol)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Der Tiroler Ort im Text (Wortgrenzen — „Hallo" ist nicht Hall), sonst <c>null</c>.</summary>
    public static string? TirolPlace(string? text) => TirolRe.Match(text ?? "") is { Success: true } m ? m.Value : null;

    /// <summary>Die Länder, die ein Profil nennen darf: das Land der Region (Österreich bzw. Deutschland, 0.712.0), dazu die Föderation
    /// laut Meldeliste und laut FIDE.</summary>
    public static HashSet<string> AllowedCountries(string? fed, string? fideFed, bool local = true, string? region = null)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (local) allowed.Add(LeagueOnlineRegions.Of(region).Country);   // Liga der Region; sonst nur die Föderation
        IReadOnlyDictionary<string, string> map = local ? Fed2 : Prep.PrepFederations.Iso;   // ohne Liga alle Föderationen (0.637.0)
        if (fed is { } f1 && map.TryGetValue(f1, out var c1)) allowed.Add(c1);
        if (fideFed is { } f2 && map.TryGetValue(f2, out var c2)) allowed.Add(c2);
        return allowed;
    }

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
    /// <param name="lead">Statt „Nutzername aus dem Namen / beginnt mit dem Nachnamen": woher das Konto kommt (Team-Suche, 0.612.0).
    /// Dann gilt die Schwelle der Suche (ein starker Hinweis nötig) und es gibt keinen Punkt für den Nutzernamen.</param>
    public static Verdict? Judge(Player p, Profile prof, bool derived, string? fideFed = null, string? lead = null)
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
            switch (FirstNameMatch(toks, lt, Tokens(first)))
            {
                case NameFit.Full: score += ScoreName; ev.Add($"Klarname im Profil („{Short(prof.RealName)}“)"); break;
                case NameFit.Initial: score += ScoreLastName; ev.Add($"Nachname und Initiale im Profil („{Short(prof.RealName)}“)"); break;
                case NameFit.LastOnly: score += ScoreLastName; ev.Add($"Nachname im Profil („{Short(prof.RealName)}“)"); break;
                default: return null;                                  // anderer Vorname — ein anderer Mensch (0.611.0)
            }
        }
        var region = p.OnlineRegion;
        var allowed = AllowedCountries(p.Fed, fideFed, p.Local, p.Region);
        var flag = (prof.Flag ?? "").Trim();
        if (flag.Length >= 2)
        {
            var code = flag[..2].ToUpperInvariant();
            if (!allowed.Contains(code)) return null;                // anderes Land — vermutlich ein Namensvetter
            score += ScoreCountry;
            ev.Add(code == region.Country ? $"Land {region.CountryName}" : code == "AT" ? "Land Österreich" : $"Land {code}");
        }
        if (!RatingPlausible(prof, p.Elo)) return null;                   // 500 online bei 2000 Elo — ein anderer (höher ist ok)
        if (RatingEvidence(prof.RatingLabel, prof.Rating, p.Elo) is { } re)
        {
            score += re.Score;
            ev.Add(re.Text);
        }
        if (prof.FideRating is { } fr && p.Elo is { } elo && elo > 0 && Math.Abs(fr - elo) <= FideTolerance)
        {
            score += ScoreFide;
            ev.Add($"FIDE-Wertung im Profil {fr} (Liste {elo})");
        }
        if (p.Local && region.PlaceIn($"{prof.Location} {prof.Bio}") is not null)
        {
            score += ScoreTirol;
            ev.Add($"{region.PlaceLabel} im Profil");
        }
        if (score < (derived ? NeedDerived : NeedSearched)) return null;
        if (lead is not null) ev.Insert(0, lead);
        else if (derived) { score += 1; ev.Insert(0, "Nutzername aus dem Namen"); }
        else ev.Insert(0, "Nutzername beginnt mit dem Nachnamen");
        return new Verdict(score, ev);
    }

    /// <summary>Liegt die Online-Wertung nicht zu weit UNTER der Elo? Ohne Wertung oder ohne Elo: kein Einwand.</summary>
    public static bool RatingPlausible(Profile prof, int? elo) =>
        prof.Rating is not { } r || elo is not { } e || e <= 0 || r >= e - RatingBelow;

    public enum NameFit { Full, Initial, LastOnly, Other }

    /// <summary>Wörter in Profilnamen, die kein Vorname sind (Titel, „Schach").</summary>
    private static readonly HashSet<string> NameFiller = new(StringComparer.Ordinal)
    {
        "gm", "im", "fm", "cm", "nm", "wgm", "wim", "wfm", "wcm", "dr", "mag", "ing", "dipl", "prof", "msc", "bsc", "chess", "schach",
    };

    /// <summary>
    /// Passt der Vorname im Profil? (0.611.0 — gesehen in der ersten vollen Suche: „Andreas Berchtold" für Axel Berchtold,
    /// „Galin Georgiev" für Georgi.) Irgendein Vorname des Spielers steht da → <c>Full</c>; nur Initialen, eine davon passt →
    /// <c>Initial</c>; außer dem Nachnamen nichts (oder nur Titel) → <c>LastOnly</c>; ein ANDERER Vorname oder eine fremde
    /// Initiale → <c>Other</c> = ein anderer Mensch.
    /// </summary>
    public static NameFit FirstNameMatch(IReadOnlyList<string> profile, IReadOnlyList<string> last, IReadOnlyList<string> firsts)
    {
        var others = profile.Where(t => !last.Contains(t) && !NameFiller.Contains(t)).ToList();
        if (firsts.Count > 0 && others.Any(firsts.Contains)) return NameFit.Full;
        if (others.Count == 0 || firsts.Count == 0) return NameFit.LastOnly;
        if (others.All(o => o.Length == 1))
            return others.Any(o => firsts.Any(f => f[0] == o[0])) ? NameFit.Initial : NameFit.Other;
        return NameFit.Other;
    }

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
                fide = Fide(Int(pr, "fideRating"));
            }
            DateTime? seen = u.TryGetProperty("seenAt", out var sa) && sa.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;
            var ratings = LichessRatings(u);
            var (rating, label) = Best(ratings);
            list.Add(new Profile(LeagueOnlineSites.Lichess, user, LeagueOnlineSites.ProfileUrl(LeagueOnlineSites.Lichess, user),
                string.IsNullOrWhiteSpace(real) ? null : real, flag, loc, bio, fide, seen, closed, rating, label, ratings));
        }
        return list;
    }

    private static readonly (string Key, string Label)[] LichessPerfs =
        { ("bullet", "Bullet"), ("blitz", "Blitz"), ("rapid", "Schnell"), ("classical", "Klassisch"), ("correspondence", "Fernschach") };

    /// <summary>Die Lichess-Wertungen je Kategorie; belastbar = genug Partien (unbespielte stehen auf 1500 mit 0 Partien und fallen weg).</summary>
    private static List<Rating> LichessRatings(JsonElement u)
    {
        var list = new List<Rating>();
        if (!u.TryGetProperty("perfs", out var perfs) || perfs.ValueKind != JsonValueKind.Object) return list;
        foreach (var (key, label) in LichessPerfs)
        {
            if (!perfs.TryGetProperty(key, out var pf) || pf.ValueKind != JsonValueKind.Object || Int(pf, "rating") is not { } r) continue;
            var games = Int(pf, "games") ?? 0;
            if (games == 0) continue;                                             // nie gespielt — die 1500 sagen nichts
            list.Add(new Rating($"Lichess {label}", r, games, games >= MinRatedGames));
        }
        return list;
    }

    /// <summary>Die beste belastbare Wertung.</summary>
    private static (int? Rating, string? Label) Best(IEnumerable<Rating> ratings) =>
        ratings.Where(r => r.Reliable).MaxBy(r => r.Value) is { } b ? (b.Value, b.Label) : (null, null);

    /// <summary><c>GET /pub/player/{name}/stats</c> (chess.com): beste Wertung mit genug Partien, dazu die FIDE-Wertung, die
    /// der Nutzer selbst angegeben hat.</summary>
    public static (int? Rating, string? Label, int? Fide) ParseChessComStats(string json)
    {
        var (best, fide) = (Best(ChessComRatings(json)), ChessComFide(json));
        return (best.Rating, best.Label, fide);
    }

    /// <summary><c>GET /pub/player/{name}/stats</c> (chess.com) → die Wertungen je Kategorie.</summary>
    public static List<Rating> ChessComRatings(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var d = doc.RootElement;
        var list = new List<Rating>();
        foreach (var (key, label) in new[] { ("chess_bullet", "Bullet"), ("chess_blitz", "Blitz"), ("chess_rapid", "Schnell"), ("chess_daily", "Täglich") })
        {
            if (!d.TryGetProperty(key, out var s) || s.ValueKind != JsonValueKind.Object) continue;
            var games = s.TryGetProperty("record", out var rec) && rec.ValueKind == JsonValueKind.Object
                ? (Int(rec, "win") ?? 0) + (Int(rec, "loss") ?? 0) + (Int(rec, "draw") ?? 0) : 0;
            if (games == 0 || !s.TryGetProperty("last", out var last) || Int(last, "rating") is not { } r) continue;
            list.Add(new Rating($"chess.com {label}", r, games, games >= MinRatedGames));
        }
        return list;
    }

    private static int? ChessComFide(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Fide(Int(doc.RootElement, "fide"));
    }

    /// <summary>Das chess.com-Profil um die Wertungen aus <c>/stats</c> ergänzt (dort steht auch die selbst angegebene FIDE-Wertung).</summary>
    public static Profile WithChessComStats(Profile prof, string statsJson)
    {
        var ratings = ChessComRatings(statsJson);
        var (rating, label) = Best(ratings);
        return prof with { Rating = rating, RatingLabel = label, FideRating = ChessComFide(statsJson) ?? prof.FideRating, Ratings = ratings };
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
            Str(d, "name"), country.Length == 2 ? country : null, Str(d, "location"), null, Fide(Int(d, "fide")), seen,
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

    /// <summary>Eine FIDE-Wertung im Profil — chess.com schreibt „0", wenn keine angegeben ist (0.622.0; das (i) meldete „weicht um 2491 ab").</summary>
    private static int? Fide(int? rating) => rating is > 0 ? rating : null;

    private static bool True(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]*$")]
    private static partial Regex ValidUser();

    // ── Suchen ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Den Spieler zu einer FIDE-ID aus der jüngsten Meldeliste (Name, Föderation, Elo, Mannschaft).</summary>
    public Task<Player?> PlayerAsync(string fide, CancellationToken ct) => PlayerAsync(_db, fide, ct);

    /// <inheritdoc cref="PlayerAsync(string, CancellationToken)"/>
    public static async Task<Player?> PlayerAsync(AppDbContext db, string fide, CancellationToken ct)
    {
        if (LeagueNames.IsNoFideKey(fide)) return await LeagueNoFidePlayers.PlayerAsync(db, fide, ct);
        var row = await (from p in db.LeaguePlayers.AsNoTracking()
                         join t in db.LeagueTournaments.AsNoTracking() on p.Tnr equals t.Tnr
                         where p.FideId == fide
                         orderby t.Season descending, p.Id descending
                         select new { p.Name, p.Fed, p.EloI, p.EloN, p.Team, t.Source }).FirstOrDefaultAsync(ct);
        if (row is not null)
            return new Player(fide, row.Name, row.Fed, row.EloI is > 0 ? row.EloI : row.EloN, row.Team, Region: LeagueRegions.Of(row.Source));
        var name = await db.LeaguePlayerProfiles.AsNoTracking().Where(p => p.FideId == fide).Select(p => p.Name).FirstOrDefaultAsync(ct);
        return name is null ? null : new Player(fide, name, null, null, null);
    }

    /// <summary>
    /// Der Such-Eintrag eines Spielers samt Jahrgang und Föderation laut FIDE (über Lichess <c>/api/fide/player</c>) — angelegt
    /// (Fassung 0, also für die Namenssuche weiter fällig), wenn es noch keinen gibt; ohne Jahrgang wird erneut gefragt. Speichert
    /// NICHT. Jeder, der einen Vorschlag anlegt, holt ihn vorher: ohne Eintrag gälte ein Konto als sichtbar
    /// (<see cref="LeagueHiddenAccounts"/>), auch das eines Minderjährigen. Drosselt Lichess, wirft
    /// <see cref="LeagueOnlineSync.RateLimitedException"/>.
    /// </summary>
    public static async Task<LeagueAccountScan> ScanRowAsync(AppDbContext db, HttpClient http, string lichess, string fide, CancellationToken ct)
    {
        var scan = db.LeagueAccountScans.Local.FirstOrDefault(s => s.FideId == fide)
                   ?? await db.LeagueAccountScans.FirstOrDefaultAsync(s => s.FideId == fide, ct);
        if (scan is null)
        {
            scan = new LeagueAccountScan { FideId = fide };
            db.LeagueAccountScans.Add(scan);
        }
        if (scan.BirthYear is null)
        {
            using var r = await http.GetAsync($"{lichess}/api/fide/player/{Uri.EscapeDataString(fide)}", ct);
            if (r.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("Lichess");
            if (r.IsSuccessStatusCode)
            {
                var (born, fed) = ParseFidePlayer(await r.Content.ReadAsStringAsync(ct));
                scan.BirthYear = born;
                scan.Federation = Cut(fed, 8);
            }
        }
        return scan;
    }

    /// <summary>
    /// Sucht für einen Spieler und legt neue Vorschläge an. Schon übernommene (Konto da) und verworfene kommen nicht wieder.
    /// Drosselt eine Seite (429), wirft <see cref="LeagueOnlineSync.RateLimitedException"/> — der Stand bleibt unverändert.
    /// </summary>
    public async Task<ScanResult> ScanAsync(Player p, CancellationToken ct)
    {
        var scan = await ScanRowAsync(_db, _http, _lichess, p.Fide, ct);
        var fideFed = scan.Federation;
        scan.ScannedAt = DateTime.UtcNow;
        // Minderjährige werden seit 0.610.0 AUCH gesucht — ihre Konten bleiben aber verborgen (unbekannter Jahrgang seit 0.616.0 nicht)
        // (LeagueHiddenAccounts): niemand sieht Seite, Name oder Adresse, die Partien zählen nur im Eröffnungsbaum.
        var hiddenNote = LeagueHiddenAccounts.Hides(scan.BirthYear) ? "verborgen (minderjährig)" : null;

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
                prof = WithChessComStats(prof, await st.Content.ReadAsStringAsync(ct));
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
        var hits = new List<(Profile Prof, Verdict V)>();
        foreach (var (prof, der) in profiles)
            if (Judge(p, prof, der, fideFed) is { } v) hits.Add((prof, v));
        // Derselbe Nutzername auf der anderen Seite (0.621.0, Wunsch: „wenn du einen Treffer hast, prüfe, ob der gleiche Username auf
        // chess.com bzw. Lichess existiert und eventuell auch passt") — nur, wo dieser Name dort nicht ohnehin schon gefragt wurde.
        var asked = lichessNames.Select(n => Key(LeagueOnlineSites.Lichess, n)).Concat(derived.Select(n => Key(LeagueOnlineSites.ChessCom, n))).ToHashSet();
        foreach (var (prof, _) in hits.ToList())
        {
            if (!asked.Add(Key(OtherSite(prof.Site), prof.User))) continue;
            if (await TwinAsync(_http, _lichess, p, prof, fideFed, ct, ChessComPause) is { } twin) hits.Add(twin);
        }
        foreach (var (prof, v) in hits)
        {
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
        // Vorschläge der Team-Suche (Source „team") gehören ihr — die Namenssuche kann sie gar nicht bestätigen.
        _db.LeagueAccountSuggestions.RemoveRange(known.Where(k => k.Status == LeagueSuggestionStatus.Open && k.Source == null
                                                                && !confirmed.Contains(Key(k.Site, k.UserName))));
        scan.Note = hiddenNote;
        scan.Found = found;
        scan.Version = CurrentVersion;
        await _db.SaveChangesAsync(ct);
        return new ScanResult(found, null);
    }

    private static string? Cut(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    public static string OtherSite(string site) => site == LeagueOnlineSites.Lichess ? LeagueOnlineSites.ChessCom : LeagueOnlineSites.Lichess;

    /// <summary>
    /// Ein Profil frisch holen (Lichess <c>POST /api/users</c>; chess.com Profil + <c>/stats</c>). <c>null</c> = das Konto gibt es nicht
    /// (oder der Name ist auf dieser Seite gar nicht möglich). Drosselt die Seite, wirft <see cref="LeagueOnlineSync.RateLimitedException"/>,
    /// jeder andere Fehler eine <see cref="HttpRequestException"/>.
    /// </summary>
    public static async Task<Profile?> FetchProfileAsync(HttpClient http, string lichess, string site, string user, CancellationToken ct,
        TimeSpan pause = default)
    {
        if (LeagueOnlineSites.Parse(site, user) is null) return null;
        if (site == LeagueOnlineSites.Lichess)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{lichess}/api/users")
            {
                Content = new StringContent(user.ToLowerInvariant(), Encoding.UTF8, "text/plain"),
            };
            using var r = await http.SendAsync(req, ct);
            if (r.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("Lichess");
            r.EnsureSuccessStatusCode();
            return ParseLichessUsers(await r.Content.ReadAsStringAsync(ct)).FirstOrDefault(x => x.User.Equals(user, StringComparison.OrdinalIgnoreCase));
        }
        var name = Uri.EscapeDataString(user.ToLowerInvariant());
        if (pause > TimeSpan.Zero) await Task.Delay(pause, ct);
        using var pr = await http.GetAsync($"https://api.chess.com/pub/player/{name}", ct);
        if (pr.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("chess.com");
        if (pr.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return null;
        pr.EnsureSuccessStatusCode();
        if (ParseChessComPlayer(await pr.Content.ReadAsStringAsync(ct)) is not { } prof) return null;
        if (prof.Closed) return prof;
        if (pause > TimeSpan.Zero) await Task.Delay(pause, ct);
        using var st = await http.GetAsync($"https://api.chess.com/pub/player/{name}/stats", ct);
        if (st.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("chess.com");
        return st.IsSuccessStatusCode ? WithChessComStats(prof, await st.Content.ReadAsStringAsync(ct)) : prof;
    }

    /// <summary>
    /// Derselbe Nutzername wie der Treffer <paramref name="hit"/> auf der ANDEREN Seite (0.621.0): gibt es ihn, und passt er nach denselben
    /// Regeln zum Spieler (Name, Land, Wertung — ein Hinweis genügt, der gleiche Name ist selbst einer)? → (Profil, Urteil) oder <c>null</c>.
    /// </summary>
    public static async Task<(Profile Profile, Verdict Verdict)?> TwinAsync(HttpClient http, string lichess, Player p, Profile hit,
        string? fideFed, CancellationToken ct, TimeSpan pause = default)
    {
        Profile? twin;
        try
        {
            twin = await FetchProfileAsync(http, lichess, OtherSite(hit.Site), hit.User, ct, pause);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;                                              // die andere Seite hakt — der Treffer selbst bleibt
        }
        if (twin is not { Closed: false }) return null;
        var lead = $"gleicher Nutzername wie das {LeagueOnlineSites.Label(hit.Site)}-Konto „{hit.User}“";
        return Judge(p, twin, derived: true, fideFed, lead) is { } v ? (twin, v with { Score = v.Score + ScoreTwin }) : null;
    }

    /// <summary>
    /// Ein Durchgang im Hintergrund: die Spieler der laufenden Saison mit FIDE-ID, die noch nie oder vor mehr als
    /// <see cref="RescanDays"/> Tagen abgesucht wurden — nie gesuchte zuerst —, bis <paramref name="budget"/> um ist. Drosselt
    /// eine Seite, endet der Durchgang. → noch Spieler offen?
    /// </summary>
    public async Task<bool> RunOnceAsync(TimeSpan budget, CancellationToken ct)
    {
        // Je Region die laufende Saison (0.712.0) — die jüngste überhaupt ließ mit den bayerischen Ligen die Tiroler weg.
        var tnrs = await LeagueOnlineRegions.CurrentSeasonTnrsAsync(_db, ct);
        if (tnrs.Count == 0) return false;
        var rows = await (from p in _db.LeaguePlayers.AsNoTracking()
                          join t in _db.LeagueTournaments.AsNoTracking() on p.Tnr equals t.Tnr
                          where tnrs.Contains(t.Tnr) && p.FideId != null && p.FideId != ""
                          orderby t.Season descending, t.Tnr descending
                          select new { p.FideId, p.Name, p.Fed, p.EloI, p.EloN, p.Team, t.Source }).ToListAsync(ct);
        var players = rows.GroupBy(r => r.FideId!).Select(g => g.First())
            .Select(r => new Player(r.FideId!, r.Name, r.Fed, r.EloI is > 0 ? r.EloI : r.EloN, r.Team, Region: LeagueRegions.Of(r.Source))).ToList();
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
