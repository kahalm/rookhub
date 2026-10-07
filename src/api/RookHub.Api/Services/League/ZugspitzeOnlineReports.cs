using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Meldungen Dritter aus dem Online-Bereich des Schachkreises Zugspitze / Bezirks Oberbayern (0.716.0, Wunsch 2026-10-07
/// „Online-Konten-Zuordnung für die Region Bayern"). Der Kreis hat 2020/Q4–2022/Q1 rund 150 Turniere auf Lichess ausgerichtet
/// (ZugLiga, ObbLiga, Online-KEM, Kreis-Vergleichskämpfe, Blitz/Rapid-Serien) und je Turnier eine Ergebnisseite mit
/// „Nachname,Vorname", Verein und Lichess-Wertung veröffentlicht — aber OHNE den Nutzernamen. Den liefert Lichess
/// (<c>/api/swiss/{id}/results</c> bzw. <c>/api/tournament/{id}/results</c>, ndjson mit Wertung und Punkten).
/// <para><b>Zuordnung</b> Seite ↔ Lichess über Wertung + Punkte (bei Gleichstand dazu Performance bzw. Sonneborn-Berger), NIE über
/// den Rang (nicht angemeldete Lichess-Spieler fehlen auf der Seite, die Ränge verschieben sich). Mehrdeutige fallen weg, ebenso ein
/// Lichess-Konto, das zwei Zeilen erklären würde. Dann „Nachname,Vorname" + Verein gegen die bayerischen Meldelisten
/// (<see cref="LeagueRosterIndex"/>, voller Vorname nötig; mehrere Kandidaten → der mit demselben Verein; ein Verein auf der Seite,
/// der zu keinem seiner Vereine passt → weg). Ergebnis: <see cref="LeagueSelfReport"/> der Quelle <see cref="SourceOf"/> mit
/// <see cref="Reporter"/> (im (i) „Gemeldet von Schachkreis Zugspitze") und je Meldung ein Vorschlag der Konto-Suche
/// (<see cref="SuggestionSource"/>) — Selbstmeldungen flossen bis dahin nicht in die Vorschläge.</para>
/// <para><b>Minderjährige</b>: Jugend-Turniere/-Serien werden übersprungen; sonst greift <see cref="LeagueHiddenAccounts"/> (vor jedem
/// Vorschlag wird der Jahrgang geholt wie bei der Team-Suche).</para>
/// <para><b>Höflich</b>: je Saison eine Turnierliste, je Turnier eine Ergebnisseite und ein Lichess-Abruf, <see cref="Pause"/>
/// dazwischen; ein 429 von Lichess beendet den Lauf (<see cref="LeagueOnlineSync.RateLimitedException"/>), geschrieben wird nur am Ende.</para>
/// </summary>
public sealed partial class ZugspitzeOnlineReports
{
    public const string Reporter = "Schachkreis Zugspitze";
    /// <summary><see cref="LeagueAccountSuggestion.Source"/> der Vorschläge aus Meldungen.</summary>
    public const string SuggestionSource = "report";
    /// <summary>Punkte eines Vorschlags aus einer Meldung — über den meisten der Suche, ein Mensch hat zugeordnet.</summary>
    public const int SuggestionScore = 5;
    /// <summary>Die Saisonen des Online-Bereichs (Jahr + Quartal).</summary>
    public static readonly string[] Seasons = { "20204", "20211", "20212", "20213", "20221" };

    public static string SourceOf(string season) => $"Online-Schach Oberbayern {season}";
    public static bool ValidSeason(string? season) => season is not null && SeasonRe().IsMatch(season);

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<ZugspitzeOnlineReports> _log;
    private readonly string _lichess;

    public ZugspitzeOnlineReports(AppDbContext db, IHttpClientFactory http, ILogger<ZugspitzeOnlineReports> log, IConfiguration? config = null)
    {
        _db = db; _http = http; _log = log;
        _lichess = (config?["Lichess:SiteUrl"] ?? "https://lichess.org").TrimEnd('/');
    }

    /// <summary>Pause nach jedem Abruf (Kreis und Lichess).</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(1);

    // ── Lesen (rein, getestet) ──────────────────────────────────────────────────────────────────

    /// <summary>Ein Turnier der Liste: Name, Serie (<c>serie=</c>), Lichess-Kennung, Art (<c>swiss</c>/<c>tournament</c>).</summary>
    public sealed record Tournament(string Name, string Serie, string Id, string Kind)
    {
        public bool Youth => LeagueOnlineRegions.IsYouth(Name) || LeagueOnlineRegions.IsYouth(Serie);
    }

    /// <summary><c>/onlineturniere/?saison=…</c> → die Turniere mit Ergebnisseite (<c>onlineergebnis</c>); 4er-MM (anderer Ausrichter,
    /// <c>onlinemm</c>) fallen weg. Die Art steht im Lichess-Link, sonst in der Dauer („8 Runden" = Schweizer System).</summary>
    public static List<Tournament> ParseTournamentList(string html)
    {
        var list = new List<Tournament>();
        foreach (Match m in ListRowRe().Matches(html))
        {
            var name = Text(m.Groups[1].Value);
            var links = m.Groups[2].Value;
            var res = ResultLinkRe().Match(links);
            if (!res.Success) continue;
            var serie = WebUtility.HtmlDecode(res.Groups[1].Value);
            var id = res.Groups[2].Value;
            var lichess = LichessLinkRe().Match(links);
            var kind = lichess.Success && lichess.Groups[2].Value == id ? lichess.Groups[1].Value
                : m.Groups[3].Value.Contains("Runde", StringComparison.OrdinalIgnoreCase) ? "swiss" : "tournament";
            if (list.All(t => t.Id != id)) list.Add(new Tournament(name, serie, id, kind));
        }
        return list;
    }

    /// <summary>Eine Zeile der Einzelwertung: Rang, „Nachname,Vorname", Verein, Lichess-Wertung, Punkte und — zur Unterscheidung bei
    /// Gleichstand — Performance (Arena, Spalte „Perf") bzw. Sonneborn-Berger (Schweizer System, Spalte „S-B").</summary>
    public sealed record PageRow(int Rank, string Name, string Team, int Rating, double Points, double? Extra);

    /// <summary>Was <see cref="PageRow.Extra"/> ist.</summary>
    public enum ExtraKind { None, Performance, TieBreak }

    /// <summary>
    /// <c>/onlineergebnis/?…</c> → die Einzelwertung. Die Seite stellt sie als drei Tabellen nebeneinander (links Rang/Name/Team/Rating,
    /// rechts Punkte/Perf bzw. Punkte/S-B/Gesw, dazwischen die Runden) — die Zeilen gehören über ihre Reihenfolge zusammen.
    /// Bei einem Teamkampf steht davor die Mannschaftswertung; gelesen wird ab „Einzelwertung".
    /// </summary>
    public static (List<PageRow> Rows, ExtraKind Extra) ParseResultPage(string html)
    {
        var from = html.IndexOf("Einzelwertung", StringComparison.Ordinal);
        var left = html.IndexOf("class='lefttable'", Math.Max(0, from), StringComparison.Ordinal);
        if (left < 0) return (new(), ExtraKind.None);
        var right = html.IndexOf("class='righttable", left, StringComparison.Ordinal);
        if (right < 0) return (new(), ExtraKind.None);
        var mid = html.IndexOf("class='midtable", right, StringComparison.Ordinal);
        var rightEnd = mid > 0 ? mid : html.IndexOf("</table>", right, StringComparison.Ordinal) is var e and > 0 ? e : html.Length;
        var leftRows = Rows(html[left..right]).Where(c => c.Count >= 4).ToList();
        var rightHtml = html[right..rightEnd];
        var headers = ThRe().Matches(rightHtml).Select(x => Text(x.Groups[1].Value)).ToList();
        var extra = headers.Count > 1 ? headers[1] switch
        {
            "Perf" => ExtraKind.Performance,
            "S-B" => ExtraKind.TieBreak,
            _ => ExtraKind.None,
        } : ExtraKind.None;
        var rightRows = Rows(rightHtml).Where(c => c.Count >= 1).ToList();
        var rows = new List<PageRow>();
        for (var i = 0; i < leftRows.Count && i < rightRows.Count; i++)
        {
            var l = leftRows[i];
            var r = rightRows[i];
            if (!int.TryParse(l[0].TrimEnd('.'), out var rank) || !int.TryParse(l[3], out var rating) || Number(r[0]) is not { } points) continue;
            var name = l[1].Trim();
            if (name.Length == 0) continue;
            rows.Add(new PageRow(rank, name, l[2].Trim(), rating, points, extra != ExtraKind.None && r.Count > 1 ? Number(r[1]) : null));
        }
        return (rows, extra);
    }

    /// <summary>Lichess-Ergebnisse (ndjson): Arena <c>score</c>/<c>performance</c>, Schweizer System <c>points</c>/<c>tieBreak</c>/<c>performance</c>.</summary>
    public sealed record LichessRow(string User, int Rating, double Points, int? Performance, double? TieBreak);

    public static List<LichessRow> ParseLichessResults(string ndjson)
    {
        var list = new List<LichessRow>();
        foreach (var raw in ndjson.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var e = doc.RootElement;
                if (!e.TryGetProperty("username", out var u) || u.ValueKind != JsonValueKind.String) continue;
                if (!e.TryGetProperty("rating", out var r) || !r.TryGetInt32(out var rating)) continue;
                double? points = e.TryGetProperty("points", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble()
                    : e.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : null;
                if (points is null) continue;
                int? perf = e.TryGetProperty("performance", out var pf) && pf.TryGetInt32(out var pv) ? pv : null;
                double? tb = e.TryGetProperty("tieBreak", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDouble() : null;
                list.Add(new LichessRow(u.GetString()!, rating, points.Value, perf, tb));
            }
            catch (JsonException) { }
        }
        return list;
    }

    /// <summary>
    /// Seite ↔ Lichess: je Zeile die Lichess-Zeilen mit derselben Wertung und denselben Punkten; mehrere → zusätzlich Performance bzw.
    /// Sonneborn-Berger. Genau eine bleibt → zugeordnet; ein Konto, das zwei Zeilen erklären würde, fällt für beide weg.
    /// → (Seiten-Zeile → Nutzername, mehrdeutige Zeilen, Zeilen ohne passendes Konto).
    /// </summary>
    public static (Dictionary<int, string> Matched, int Ambiguous, int Missing) Match(IReadOnlyList<PageRow> page, IReadOnlyList<LichessRow> lichess,
        ExtraKind extra)
    {
        var tentative = new Dictionary<int, string>();
        var ambiguous = 0;
        var missing = 0;
        for (var i = 0; i < page.Count; i++)
        {
            var row = page[i];
            var c = lichess.Where(l => l.Rating == row.Rating && Math.Abs(l.Points - row.Points) < 0.01).ToList();
            if (c.Count > 1 && row.Extra is { } x)
                c = c.Where(l => extra switch
                {
                    ExtraKind.Performance => l.Performance is { } pf && Math.Abs(pf - x) < 0.5,
                    ExtraKind.TieBreak => l.TieBreak is { } tb && Math.Abs(tb - x) < 0.01,
                    _ => true,
                }).ToList();
            if (c.Count == 0) missing++;
            else if (c.Count > 1) ambiguous++;
            else tentative[i] = c[0].User;
        }
        var twice = tentative.GroupBy(t => t.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).SelectMany(g => g.Select(t => t.Key)).ToList();
        foreach (var i in twice) tentative.Remove(i);
        return (tentative, ambiguous + twice.Count, missing);
    }

    private static IEnumerable<List<string>> Rows(string html) =>
        Regex.Split(html, "<tr", RegexOptions.IgnoreCase).Skip(1)
            .Select(tr => TdRe().Matches(tr).Select(m => Text(m.Groups[1].Value)).ToList());

    private static string Text(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", "")).Replace(' ', ' '), @"\s+", " ").Trim();

    /// <summary>„8½" → 8,5; „½" → 0,5; „12" → 12; „42.25" → 42,25.</summary>
    internal static double? Number(string s)
    {
        var t = s.Trim().Replace(',', '.');
        var half = t.EndsWith('½');
        if (half) t = t[..^1];
        if (t.Length == 0) return half ? 0.5 : null;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v + (half ? 0.5 : 0) : null;
    }

    [GeneratedRegex(@"^20\d\d[1-4]$")]
    private static partial Regex SeasonRe();
    [GeneratedRegex(@"<tr><td>(.*?)</td><td>(.*?)</td><td>(.*?)</td><td>.*?</td><td>.*?</td></tr>", RegexOptions.Singleline)]
    private static partial Regex ListRowRe();
    [GeneratedRegex(@"onlineergebnis/\?saison=\d+&(?:amp;)?serie=([^&']*)&(?:amp;)?turnier=([A-Za-z0-9]{8})'")]
    private static partial Regex ResultLinkRe();
    [GeneratedRegex(@"lichess\.org/(swiss|tournament)/([A-Za-z0-9]{8})\b")]
    private static partial Regex LichessLinkRe();
    [GeneratedRegex(@"<th>(.*?)</th>", RegexOptions.Singleline)]
    private static partial Regex ThRe();
    [GeneratedRegex(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex TdRe();

    // ── Abrufen + Einspielen ────────────────────────────────────────────────────────────────────

    public sealed class NotFoundException(string message) : Exception(message);

    /// <summary>Zähler eines Laufs.</summary>
    public sealed class Counts
    {
        /// <summary>Turniere mit Ergebnisseite (ohne Jugend); <see cref="SkippedYouth"/> Jugend-Turniere/-Serien.</summary>
        public int Tournaments { get; set; }
        public int SkippedYouth { get; set; }
        /// <summary>Turniere, deren Seite oder Lichess-Ergebnis nicht kam bzw. leer war.</summary>
        public int Unreadable { get; set; }
        /// <summary>Zeilen der Einzelwertungen.</summary>
        public int Rows { get; set; }
        /// <summary>Zeilen mit eindeutigem Lichess-Konto.</summary>
        public int Matched { get; set; }
        /// <summary>Zeilen, zu denen Wertung + Punkte mehrere Lichess-Konten passen (oder ein Konto zwei Zeilen) — weggelassen.</summary>
        public int Ambiguous { get; set; }
        /// <summary>Zeilen ohne passendes Lichess-Konto (Konto geschlossen, Daten verschieden).</summary>
        public int NotOnLichess { get; set; }
        /// <summary>Name in keiner bayerischen Meldeliste.</summary>
        public int NoRoster { get; set; }
        /// <summary>Mehrere gleichnamige Ligaspieler, auch der Verein entscheidet nicht.</summary>
        public int RosterAmbiguous { get; set; }
        /// <summary>Ligaspieler gefunden, aber der Verein der Seite passt zu keinem seiner Vereine.</summary>
        public int OtherClub { get; set; }
        /// <summary>Ligaspieler ohne FIDE-ID (Meldungen hängen an der FIDE-ID).</summary>
        public int NoFide { get; set; }
        /// <summary>Zeilen, die einem Ligaspieler mit FIDE-ID zugeordnet sind.</summary>
        public int Assigned { get; set; }
        /// <summary>Konten, die in verschiedenen Turnieren verschiedenen Spielern zugeordnet würden — weggelassen.</summary>
        public int Conflicting { get; set; }
        /// <summary>Meldungen (je Konto eine).</summary>
        public int Reports { get; set; }
    }

    /// <summary>Eine Meldung: Konto ↔ Spieler, mit den Turnieren als Beleg.</summary>
    public sealed record Item(string Fide, string Player, string User, string Team, string PageName, List<string> Tournaments);

    public sealed record Result(string Season, string Source, bool DryRun, Counts Counts, LeagueSelfReportImport.Outcome? Reports,
        int Suggestions, List<Item>? Items);

    private async Task<string?> GetSiteAsync(string path, CancellationToken ct)
    {
        var client = _http.CreateClient(ZugspitzeSource.ClientName);
        using var r = await client.GetAsync(path, ct);
        await WaitAsync(ct);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsStringAsync(ct);
    }

    private async Task<string?> GetLichessAsync(string kind, string id, CancellationToken ct)
    {
        var client = _http.CreateClient(LeagueOnlineSync.ClientName);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_lichess}/api/{kind}/{id}/results?nb=1000");
        req.Headers.Accept.ParseAdd("application/x-ndjson");
        using var r = await client.SendAsync(req, ct);
        await WaitAsync(ct);
        if (r.StatusCode == HttpStatusCode.TooManyRequests) throw new LeagueOnlineSync.RateLimitedException("Lichess");
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsStringAsync(ct);
    }

    private Task WaitAsync(CancellationToken ct) => Pause > TimeSpan.Zero ? Task.Delay(Pause, ct) : Task.CompletedTask;

    /// <summary>
    /// Eine Saison einspielen (<paramref name="dryRun"/>: nur zählen und die Zuordnung zeigen). Ersetzt die Meldungen der Quelle
    /// <see cref="SourceOf"/> (wie <c>POST admin/self-reports</c>) und legt je neuer Meldung einen Vorschlag an.
    /// Wirft <see cref="NotFoundException"/> (keine Turnierliste), <see cref="LeagueOnlineSync.RateLimitedException"/> (Lichess 429)
    /// und <see cref="HttpRequestException"/> (Kreis/Lichess nicht erreichbar).
    /// </summary>
    public async Task<Result> ImportAsync(string season, bool dryRun, CancellationToken ct)
    {
        if (!ValidSeason(season)) throw new ArgumentException("season", nameof(season));
        var counts = new Counts();
        var html = await GetSiteAsync($"onlineturniere/?saison={season}", ct);
        var tournaments = html is null ? new() : ParseTournamentList(html);
        if (tournaments.Count == 0) throw new NotFoundException($"keine Turniere für {season}");

        var roster = await RosterAsync(ct);
        var region = LeagueOnlineRegions.Bayern;
        // Konto (klein) → (FIDE, Spieler, Konto wie geschrieben, Team der Seite, Name der Seite, Turniere)
        var byUser = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        var conflicting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tournaments)
        {
            if (t.Youth) { counts.SkippedYouth++; continue; }
            counts.Tournaments++;
            var page = await GetSiteAsync($"onlineergebnis/?saison={season}&serie={Uri.EscapeDataString(t.Serie)}&turnier={t.Id}", ct);
            var (rows, extra) = page is null ? (new List<PageRow>(), ExtraKind.None) : ParseResultPage(page);
            if (rows.Count == 0) { counts.Unreadable++; continue; }
            var nd = await GetLichessAsync(t.Kind, t.Id, ct)
                     ?? await GetLichessAsync(t.Kind == "swiss" ? "tournament" : "swiss", t.Id, ct);   // Art nur geraten (kein Link)
            var lichess = nd is null ? new List<LichessRow>() : ParseLichessResults(nd);
            if (lichess.Count == 0) { counts.Unreadable++; continue; }
            counts.Rows += rows.Count;
            var (matched, ambiguous, missing) = Match(rows, lichess, extra);
            counts.Matched += matched.Count;
            counts.Ambiguous += ambiguous;
            counts.NotOnLichess += missing;
            foreach (var (i, user) in matched)
            {
                var row = rows[i];
                if (Resolve(roster, region, row, counts) is not { } person) continue;
                counts.Assigned++;
                var label = $"{t.Name} (Rang {row.Rank}, Lichess-Wertung {row.Rating})";
                if (byUser.TryGetValue(user, out var known))
                {
                    if (known.Fide != person.Fide) conflicting.Add(user);
                    else if (!known.Tournaments.Contains(label)) known.Tournaments.Add(label);
                }
                else byUser[user] = new Item(person.Fide!, person.Name, user, row.Team, row.Name, new() { label });
            }
        }
        foreach (var u in conflicting) byUser.Remove(u);
        counts.Conflicting = conflicting.Count;
        var items = byUser.Values.OrderBy(x => x.Player, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.User).ToList();
        counts.Reports = items.Count;

        var source = SourceOf(season);
        var request = new LeagueSelfReportImport.Request(source,
            items.Select(x => new LeagueSelfReportImport.Entry(x.Fide, LeagueOnlineSites.Lichess, x.User, x.Team, Note(x))).ToList(), Reporter);
        var (outcome, _) = await LeagueSelfReportImport.ImportAsync(_db, request, dryRun, ct);
        var suggestions = dryRun ? 0 : await SuggestAsync(items, source, ct);
        _log.LogInformation("LeagueHub: Online-Schach Oberbayern {Season}{Dry} — {Tournaments} Turniere, {Rows} Zeilen, {Matched} Lichess-Konten, "
                            + "{Assigned} zugeordnet, {Reports} Meldungen, {Suggestions} neue Vorschläge",
            season, dryRun ? " (Probelauf)" : "", counts.Tournaments, counts.Rows, counts.Matched, counts.Assigned, counts.Reports, suggestions);
        return new Result(season, source, dryRun, counts, outcome, suggestions, dryRun ? items : null);
    }

    /// <summary>„Online-KEM 2022 M I (Rang 1, Lichess-Wertung 2365) + 2 weitere" — höchstens <see cref="LeagueSelfReportImport.MaxNoteLength"/>.</summary>
    internal static string Note(Item x)
    {
        var first = x.Tournaments[0];
        var more = x.Tournaments.Count > 1 ? $" + {x.Tournaments.Count - 1} weitere" : "";
        var s = first + more;
        return s.Length <= LeagueSelfReportImport.MaxNoteLength ? s : s[..LeagueSelfReportImport.MaxNoteLength];
    }

    /// <summary>Die Meldelisten der Region Bayern (alle Saisonen).</summary>
    private async Task<LeagueRosterIndex> RosterAsync(CancellationToken ct)
    {
        var rows = await (from p in _db.LeaguePlayers.AsNoTracking()
                          join t in _db.LeagueTournaments.AsNoTracking().InRegion(LeagueRegions.Bayern) on p.Tnr equals t.Tnr
                          select new { p.Tnr, p.Team, p.Name, p.NameKey, p.FideId, t.Season }).ToListAsync(ct);
        return new LeagueRosterIndex(rows.Select(r => new LeagueRosterIndex.Row(r.Tnr, r.Team, r.Name, r.NameKey, r.FideId, r.Season)));
    }

    /// <summary>
    /// „Nachname,Vorname" + Verein der Seite → Ligaspieler mit FIDE-ID, sonst <c>null</c> (und der Grund in <paramref name="counts"/>).
    /// Verlangt den vollen Vornamen (die dritte Stufe des Index — Nachname + Initiale — träfe sonst „Claudia" für „Christian").
    /// </summary>
    internal static LeagueRosterIndex.Person? Resolve(LeagueRosterIndex roster, LeagueOnlineRegion region, PageRow row, Counts counts)
    {
        var name = row.Name.Replace(",", ", ");
        var hit = roster.Match(name, null);
        var (last, first) = LeagueAccountFinder.SplitName(name);
        var lt = LeagueAccountFinder.Tokens(last);
        var ft = LeagueAccountFinder.Tokens(first).Take(1).ToList();
        bool FullName(LeagueRosterIndex.Person p)
        {
            var (pl, pf) = LeagueAccountFinder.SplitName(p.Name);
            return LeagueAccountFinder.Tokens(pl).SequenceEqual(lt) && ft.Count > 0 && ft.All(LeagueAccountFinder.Tokens(pf).Contains);
        }
        var candidates = hit.Candidates.Where(FullName).ToList();
        if (candidates.Count == 0) { counts.NoRoster++; return null; }
        var pageKeys = region.ClubKeys(row.Team);
        bool SameClub(LeagueRosterIndex.Person p) => p.Teams.SelectMany(region.ClubKeys).Any(pageKeys.Contains);
        bool OtherClub(LeagueRosterIndex.Person p) => pageKeys.Count > 0 && p.Teams.SelectMany(region.ClubKeys).Any() && !SameClub(p);
        LeagueRosterIndex.Person? person;
        if (candidates.Count == 1) person = candidates[0];
        else
        {
            var same = pageKeys.Count > 0 ? candidates.Where(SameClub).ToList() : new();
            if (same.Count != 1) { counts.RosterAmbiguous++; return null; }
            person = same[0];
        }
        if (OtherClub(person)) { counts.OtherClub++; return null; }
        if (person.Fide is null) { counts.NoFide++; return null; }
        return person;
    }

    /// <summary>Je Meldung ein Vorschlag (<see cref="SuggestionSource"/>), wenn es das Konto weder als Konto (bei irgendwem) noch als
    /// Vorschlag für ihn (auch verworfen) gibt. Vorher der Jahrgang (Minderjährige verborgen). Speichert.</summary>
    private async Task<int> SuggestAsync(List<Item> items, string source, CancellationToken ct)
    {
        var lichess = _http.CreateClient(LeagueOnlineSync.ClientName);
        var added = 0;
        foreach (var x in items)
        {
            var lower = x.User.ToLower();
            if (await _db.LeagueOnlineAccounts.AnyAsync(a => a.Site == LeagueOnlineSites.Lichess && a.UserName.ToLower() == lower, ct)
                || await _db.LeagueAccountSuggestions.AnyAsync(s => s.FideId == x.Fide && s.Site == LeagueOnlineSites.Lichess && s.UserName.ToLower() == lower, ct))
                continue;
            var asked = (_db.LeagueAccountScans.Local.FirstOrDefault(s => s.FideId == x.Fide)
                         ?? await _db.LeagueAccountScans.AsNoTracking().FirstOrDefaultAsync(s => s.FideId == x.Fide, ct))?.BirthYear is null;
            await LeagueAccountFinder.ScanRowAsync(_db, lichess, _lichess, x.Fide, ct);
            if (asked) await WaitAsync(ct);                                       // ein Lichess-Abruf (Jahrgang) — höflich
            var evidence = $"Gemeldet von {Reporter} ({source}): {x.PageName}, {x.Team} — {string.Join("; ", x.Tournaments)}";
            _db.LeagueAccountSuggestions.Add(new LeagueAccountSuggestion
            {
                FideId = x.Fide, Site = LeagueOnlineSites.Lichess, UserName = x.User, Url = LeagueOnlineSites.ProfileUrl(LeagueOnlineSites.Lichess, x.User),
                Score = SuggestionScore, Evidence = evidence.Length <= 500 ? evidence : evidence[..500], Status = LeagueSuggestionStatus.Open,
                CreatedAt = DateTime.UtcNow, Source = SuggestionSource,
            });
            await _db.SaveChangesAsync(ct);
            added++;
        }
        return added;
    }
}
