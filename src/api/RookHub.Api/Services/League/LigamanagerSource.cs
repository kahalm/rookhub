using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Zweite Liga-Quelle neben chess-results (2026-10-07, Schritt 1 von „LeagueHub für SK Weilheim in Bayern"): der
/// SBV-Ligamanager des Schachbunds Bayern, <see cref="SiteUrl"/>. Es gibt keine API — gelesen werden drei Seiten einer Liga:
/// <list type="bullet">
/// <item><c>spielplan</c> — Runden (Datum, Uhrzeit), Begegnungen (Heim/Gast, Mannschaftsergebnis, Spiellokal) und je
///   gespielter Begegnung die Brettergebnisse mit Melde-Nr. beider Spieler; Ergebnis aus HEIM-Sicht wie bei chess-results.</item>
/// <item><c>mannschaften</c> — Meldelisten (Nr., Name, Titel, DWZ, ELO und — nur in der laufenden Saison — der Link auf
///   <c>ratings.fide.com/profile/&lt;FIDE-ID&gt;</c>).</item>
/// <item><c>partien/download/alle.pgn</c> — alle Partien (Windows-1252; 404, solange keine Partie erfasst ist).</item>
/// </list>
/// Daraus entstehen dieselben Zwischenformen wie aus dem chess-results-Crawler (<see cref="LeagueRefresh.Pages"/>), geschrieben
/// über <see cref="LeagueRefresh.ReplaceAsync(AppDbContext, LeagueRefresh.Pages, DateTime, CancellationToken)"/> — Verknüpfung
/// Brett ↔ Meldeliste über (Team, NameKey), FIDE-ID/Meldebrett/Elo daraus. Die Partien gehen über
/// <see cref="LeagueProfileStore.ImportGamesAsync"/> in die Spielerkarten (Quelle <see cref="PgnSource"/>), die FIDE-ID schreibt
/// der Leser aus der Meldeliste in den Kopf.
/// <para><b>Farben:</b> der Spielplan nennt sie nicht. Am PGN geprüft (Landesliga Süd 2025/26, 360 Partien, keine Ausnahme):
/// in Bayern hat die HEIM-Mannschaft an den GERADEN Brettern Weiß. Die Farbe kommt aus dem PGN, wo es die Partie gibt, sonst
/// aus dieser Regel (<see cref="HomeWhiteByRule"/>).</para>
/// <para><b>FIDE-IDs älterer Saisonen:</b> nur die laufende Saison verlinkt die FIDE-Profile. Damit ein Spieler über die
/// Saisonen hinweg derselbe bleibt (Personen-Schlüssel = FIDE-ID, sonst Name — <see cref="LeagueNames.Pid"/>), ergänzt
/// <see cref="FillMissingFideAsync"/> fehlende IDs aus anderen Ligamanager-Ligen bei GLEICHEM Verein + Namen, wenn dort genau
/// eine ID dazu steht. Erst die laufende Saison einspielen, dann die älteren — sonst fehlen den älteren die Partien in den Karten.</para>
/// <para>Höflich: eigener User-Agent, <see cref="Pause"/> zwischen den Abrufen; abgerufen wird nur unter <see cref="SiteUrl"/>
/// mit einem aus geprüften Teilen gebauten Pfad (kein freier Abruf beliebiger Adressen).</para>
/// </summary>
public sealed partial class LigamanagerSource
{
    /// <summary>Wert von <see cref="LeagueTournament.Source"/>.</summary>
    public const string Source = "ligamanager";
    /// <summary>Kopfzeile <c>[LeagueSource]</c> der Partien in den Spielerkarten.</summary>
    public const string PgnSource = "Ligamanager";
    public const string ClientName = "Ligamanager";
    public const string SiteUrl = "https://ligamanager.schachbund-bayern.de";

    /// <summary>
    /// Versatz der Turniernummer (2026-10-07): eine Ligamanager-Liga liegt unter <c>Tnr = TnrOffset + Liga-Id</c>
    /// (<see cref="TnrOf"/>), nicht unter der nackten Id. <see cref="LeagueTournament.Tnr"/> ist der Primärschlüssel aller
    /// LeagueHub-Tabellen und bei chess-results die Turniernummer (heute 7-stellig, wächst weiter) — die 4-stelligen
    /// Ligamanager-Ids lägen im selben Zahlenraum und könnten mit einem (alten oder künftigen) chess-results-Turnier
    /// zusammenstoßen. Ab 900 000 000 liegt nichts von chess-results. Der Bereich endet bei 909 999 999 (die Adresse lässt
    /// höchstens 7-stellige Ids zu, <see cref="MaxLigamanagerId"/>) — ab 910 000 000 liegt der Schachkreis Zugspitze
    /// (<see cref="ZugspitzeSource.TnrOffset"/>).
    /// </summary>
    public const int TnrOffset = 900_000_000;
    /// <summary>Größte Ligamanager-Id (7-stellig wie in der Adresse, weit über den heutigen 4-stelligen Ids) — darüber beginnt
    /// der Bereich des Schachkreises Zugspitze.</summary>
    public const int MaxLigamanagerId = 9_999_999;

    /// <summary>Tnr einer Ligamanager-Liga: <see cref="TnrOffset"/> + Liga-Id.</summary>
    public static int TnrOf(int ligamanagerId) =>
        ligamanagerId is > 0 and <= MaxLigamanagerId ? TnrOffset + ligamanagerId
            : throw new ArgumentOutOfRangeException(nameof(ligamanagerId), ligamanagerId, "Ligamanager-Id außerhalb des Bereichs");

    /// <summary>Liegt die Tnr im Bereich der Ligamanager-Ligen (über <see cref="TnrOffset"/>, bis <see cref="MaxLigamanagerId"/>)?</summary>
    public static bool IsLigamanagerTnr(int tnr) => tnr > TnrOffset && tnr <= TnrOffset + MaxLigamanagerId;

    /// <summary>Liga-Id des Ligamanagers aus der Tnr; <c>null</c>, wenn die Tnr keine Ligamanager-Tnr ist.</summary>
    public static int? LigamanagerIdOf(int tnr) => IsLigamanagerTnr(tnr) ? tnr - TnrOffset : null;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly LeagueService _league;
    private readonly ILogger<LigamanagerSource> _log;
    private readonly Func<DateTime> _now;

    public LigamanagerSource(AppDbContext db, IHttpClientFactory http, LeagueService league, ILogger<LigamanagerSource> log,
        Func<DateTime>? now = null)
    {
        _db = db; _http = http; _league = league; _log = log; _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>Pause zwischen zwei Abrufen beim Ligamanager.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(1);

    // ── Liga-Adresse ────────────────────────────────────────────────────────────────────────────

    /// <summary>Eine Liga: Region (<c>bsb</c>, <c>obb</c>, <c>augsburg</c> …), Saison „2026-2027", Slug „landesliga-sued", Id 2573.</summary>
    public sealed record LeagueRef(string Region, string Season, string Slug, int Id)
    {
        /// <summary>„bsb/2026-2027/landesliga-sued-2573" — so steht es in <see cref="LeagueTournament.SourceRef"/>.</summary>
        public string Path => $"{Region}/{Season}/{Slug}-{Id}";
        /// <summary>„2026/27" — die Schreibweise der Saison in LeagueHub.</summary>
        public string SeasonLabel => $"{Season[..4]}/{Season[^2..]}";

        /// <summary>Aus einer Adresse (<c>https://ligamanager.schachbund-bayern.de/bsb/2026-2027/landesliga-sued-2573/spielplan</c>)
        /// oder einem Pfad („bsb/2026-2027/landesliga-sued-2573"); <c>null</c> = keine Ligamanager-Liga.</summary>
        public static LeagueRef? Parse(string? input)
        {
            var s = (input ?? "").Trim();
            if (s.Contains("://", StringComparison.Ordinal))
            {
                if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http")
                    || !u.Host.Equals(new Uri(SiteUrl).Host, StringComparison.OrdinalIgnoreCase)) return null;
                s = u.AbsolutePath;
            }
            var m = PathRe().Match(s.Trim('/') + "/");
            return m.Success ? Make(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value) : null;
        }

        /// <summary>Aus den Einzelteilen; die Saison darf „2026-2027", „2026/2027" oder „2026/27" heißen, der Slug muss die Id
        /// tragen („landesliga-sued-2573").</summary>
        public static LeagueRef? Of(string? region, string? season, string? slug)
        {
            var sm = SeasonRe().Match((season ?? "").Trim());
            var lm = SlugRe().Match((slug ?? "").Trim().ToLowerInvariant());
            var r = (region ?? "").Trim().ToLowerInvariant();
            if (!sm.Success || !lm.Success || !RegionRe().IsMatch(r)) return null;
            var y2 = sm.Groups[2].Value;
            var full = y2.Length == 2 ? sm.Groups[1].Value[..2] + y2 : y2;
            return Make(r, sm.Groups[1].Value, full, lm.Groups[1].Value, lm.Groups[2].Value);
        }

        private static LeagueRef? Make(string region, string y1, string y2, string slug, string id)
        {
            if (!int.TryParse(y1, out var a) || !int.TryParse(y2, out var b) || b != a + 1) return null;
            if (!int.TryParse(id, out var n) || n is <= 0 or > MaxLigamanagerId) return null;   // muss über TnrOffset in int passen
            return new LeagueRef(region.ToLowerInvariant(), $"{y1}-{y2}", slug.ToLowerInvariant(), n);
        }
    }

    [GeneratedRegex(@"^([a-z0-9-]{2,40})/(\d{4})-(\d{4})/([a-z0-9]+(?:-[a-z0-9]+)*?)-(\d{1,7})/", RegexOptions.IgnoreCase)]
    private static partial Regex PathRe();
    [GeneratedRegex(@"^(\d{4})[-/](\d{2}|\d{4})$")]
    private static partial Regex SeasonRe();
    [GeneratedRegex(@"^([a-z0-9]+(?:-[a-z0-9]+)*?)-(\d{1,7})$")]
    private static partial Regex SlugRe();
    [GeneratedRegex(@"^[a-z0-9-]{2,40}$")]
    private static partial Regex RegionRe();

    /// <summary>
    /// Ligastufe aus dem Slug (Wunsch: Oberliga 1, Regionalliga 2, Landesliga 3, Bezirksliga 4, Kreisliga 5 …). Die
    /// Bezirks-Spitzenligen heißen je Bezirk anders (Bezirksoberliga, Oberpfalzliga, Schwabenliga 1, Unterfrankenliga) und
    /// stehen auf 4 wie die Bezirksligen; darunter Kreisliga 5, Kreisklasse/A-Klasse 6, B-Klasse 7, C-Klasse 8.
    /// Jugendligen (U20-Bayernliga …) zählen wie ihr Namensteil. Unbekanntes → 6.
    /// </summary>
    public static int LevelOf(string slug)
    {
        var s = (slug ?? "").ToLowerInvariant();
        s = UPrefix().Replace(s, "");                                   // „u20-landesliga-sued" → „landesliga-sued"
        if (s.StartsWith("oberliga", StringComparison.Ordinal) || s.StartsWith("bayernliga", StringComparison.Ordinal)) return 1;
        if (s.StartsWith("regionalliga", StringComparison.Ordinal)) return 2;
        if (s.StartsWith("landesliga", StringComparison.Ordinal)) return 3;
        if (s.StartsWith("bezirk", StringComparison.Ordinal) || s.StartsWith("oberpfalzliga", StringComparison.Ordinal)
            || s.StartsWith("schwabenliga", StringComparison.Ordinal) || s.StartsWith("unterfrankenliga", StringComparison.Ordinal)
            || s.StartsWith("oberfrankenliga", StringComparison.Ordinal) || s.StartsWith("mittelfrankenliga", StringComparison.Ordinal)
            || s.StartsWith("niederbayernliga", StringComparison.Ordinal) || s.StartsWith("oberbayernliga", StringComparison.Ordinal))
            return 4;
        if (s.StartsWith("kreisoberliga", StringComparison.Ordinal) || s.StartsWith("kreisliga", StringComparison.Ordinal)) return 5;
        if (s.StartsWith("kreisklasse", StringComparison.Ordinal) || s.StartsWith("a-klasse", StringComparison.Ordinal)) return 6;
        if (s.StartsWith("b-klasse", StringComparison.Ordinal)) return 7;
        if (s.StartsWith("c-klasse", StringComparison.Ordinal)) return 8;
        return 6;
    }

    [GeneratedRegex(@"^u\d{2}-")]
    private static partial Regex UPrefix();

    // ── Lesen (rein, getestet) ──────────────────────────────────────────────────────────────────

    public sealed record BoardRow(int Board, int? HomeNr, string HomeName, string? HomeTitle, int? AwayNr, string AwayName,
        string? AwayTitle, string Result);
    public sealed record MatchRow(int Round, int MatchNo, string Home, string Away, double? HomePts, double? AwayPts, string? Venue,
        List<BoardRow> Boards);
    public sealed record RoundInfo(int Round, string? Date, string? Time);
    public sealed record Schedule(string Title, List<RoundInfo> Rounds, List<MatchRow> Matches);
    public sealed record RosterEntry(string Team, int Nr, string Name, string? Title, int? Dwz, int? Elo, string? Fide);
    public sealed record PgnGame(int Round, int Board, string White, string Black, string? WhiteTeam, string? BlackTeam, string Result,
        string? Date, int? WhiteElo, int? BlackElo, List<string> Sans);

    /// <summary>
    /// Name wie bei chess-results: „Pieper, Thomas, Dr." → „Pieper, Thomas Dr." (der Ligamanager trennt den akademischen
    /// Titel mit einem zweiten Komma ab; <see cref="LeagueNames.NameKey"/> ließe sonst „pieper, thomas," mit Komma stehen).
    /// </summary>
    public static string NormalizeName(string? raw)
    {
        var s = LeagueNames.Clean(WebUtility.HtmlDecode(raw ?? ""));
        var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 2 ? $"{parts[0]}, {string.Join(' ', parts[1..])}" : string.Join(", ", parts);
    }

    private static string Text(string html) => LeagueNames.Clean(WebUtility.HtmlDecode(TagRe().Replace(html, " ")));

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRe();

    /// <summary>„4½" → 4.5, „" → null.</summary>
    public static double? Points(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.Length == 0) return null;
        var half = t.EndsWith('½');
        if (half) t = t[..^1];
        if (t.Length == 0) return half ? 0.5 : null;
        return double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v + (half ? 0.5 : 0) : null;
    }

    private static int? Int(string? s) => int.TryParse((s ?? "").Trim(), out var v) && v > 0 ? v : null;

    /// <summary>Die Spielplan-Seite: Überschrift, Runden mit Termin, Begegnungen samt Brettergebnissen.</summary>
    public static Schedule ParseSchedule(string html)
    {
        var title = H1Re().Match(html) is { Success: true } h ? Text(h.Groups[1].Value) : "";
        title = Regex.Replace(title, @"\s*-\s*Spielplan$", "");
        var rounds = new List<RoundInfo>();
        var matches = new List<MatchRow>();
        var cards = RoundCardRe().Matches(html).ToList();
        for (var i = 0; i < cards.Count; i++)
        {
            var round = int.Parse(cards[i].Groups[1].Value, CultureInfo.InvariantCulture);
            var end = i + 1 < cards.Count ? cards[i + 1].Index : html.Length;
            var seg = html[cards[i].Index..end];
            string? date = null, time = null;
            if (RoundDateRe().Match(seg) is { Success: true } dm)
            {
                var txt = Text(dm.Groups[1].Value);
                if (DateRe().Match(txt) is { Success: true } d)
                {
                    date = d.Value;
                    var rest = txt[(d.Index + d.Length)..].Trim();
                    time = rest.Length == 0 ? null : rest.Length > 12 ? rest[..12] : rest;
                }
            }
            rounds.Add(new RoundInfo(round, date, time));
            var rows = MatchRowRe().Matches(seg).ToList();
            for (var j = 0; j < rows.Count; j++)
            {
                var mEnd = j + 1 < rows.Count ? rows[j + 1].Index : seg.Length;
                var ms = seg[rows[j].Index..mEnd];
                var home = HomeTeamRe().Match(ms) is { Success: true } hm ? NormalizeTeam(hm.Groups[1].Value) : "";
                var away = AwayTeamRe().Match(ms) is { Success: true } am ? NormalizeTeam(am.Groups[1].Value) : "spielfrei";
                if (home.Length == 0) continue;
                var hp = Points(ErgHeimRe().Match(ms) is { Success: true } eh ? Text(eh.Groups[1].Value) : null);
                var ap = Points(ErgGastRe().Match(ms) is { Success: true } eg ? Text(eg.Groups[1].Value) : null);
                string? venue = null;
                if (VenueRe().Match(ms) is { Success: true } vm)
                    venue = string.Join(", ", new[] { Text(vm.Groups[1].Value), Text(vm.Groups[2].Value) }.Where(x => x.Length > 0));
                var boards = new List<BoardRow>();
                foreach (Match br in BoardRowRe().Matches(ms))
                {
                    var cells = CellRe().Matches(br.Groups[2].Value).Select(c => (Cls: c.Groups[1].Value, Html: c.Groups[2].Value)).ToList();
                    var nrs = cells.Where(c => c.Cls.Contains("melde-nr")).Select(c => Int(Text(c.Html))).ToList();
                    var names = cells.Where(c => c.Cls.Contains("spieler-name")).Select(c => c.Html).ToList();
                    var res = cells.FirstOrDefault(c => c.Cls.Contains("ergebnis-zelle")).Html is { } rh ? Text(rh) : "";
                    if (names.Count < 2) continue;
                    var (hn, ht) = NameAndTitle(names[0]);
                    var (an, at) = NameAndTitle(names[1]);
                    boards.Add(new BoardRow(int.Parse(br.Groups[1].Value, CultureInfo.InvariantCulture), nrs.ElementAtOrDefault(0), hn, ht,
                        nrs.ElementAtOrDefault(1), an, at, NormalizeResult(res)));
                }
                matches.Add(new MatchRow(round, j + 1, home, away, hp, ap, venue is { Length: > 0 } ? venue : null, boards));
            }
        }
        return new Schedule(title, rounds, matches);
    }

    private static string NormalizeTeam(string s) => LeagueNames.Clean(WebUtility.HtmlDecode(s));

    private static (string Name, string? Title) NameAndTitle(string cellHtml)
    {
        var title = BadgeRe().Match(cellHtml) is { Success: true } b ? Text(b.Groups[1].Value) : null;
        var name = NormalizeName(Text(BadgeRe().Replace(cellHtml, "")));
        // „N.N." / „-" = Brett nicht besetzt
        if (name is "N.N." or "N. N." or "-" or "--") name = "";
        return (name, string.IsNullOrEmpty(title) ? null : title);
    }

    /// <summary>„1 - 0", „½ - ½", „+ - -" … — einheitlich mit „ - " wie bei chess-results.</summary>
    public static string NormalizeResult(string s)
    {
        var t = LeagueNames.Clean(s);
        if (t.Length == 0) return "";
        var m = ResultRe().Match(t);
        return m.Success ? $"{m.Groups[1].Value} - {m.Groups[2].Value}" : t;
    }

    /// <summary>(Heim, Gast, Kampflos) aus dem Ergebnis in Heim-Sicht — wie <see cref="LeagueGame.Forfeit"/>.</summary>
    public static (double? Home, double? Away, int Forfeit) Scores(string result)
    {
        var parts = result.Split(" - ");
        if (parts.Length != 2) return (null, null, 0);
        static double? V(string x) => x switch { "1" or "+" => 1, "0" or "-" => 0, "½" => 0.5, _ => null };
        var forfeit = parts[0] is "+" or "-" || parts[1] is "+" or "-" ? (parts[0] == "-" && parts[1] == "-" ? 2 : 1) : 0;
        return (V(parts[0]), V(parts[1]), forfeit);
    }

    /// <summary>Die Mannschaften-Seite: Meldeliste je Team (Nr., Name, Titel, DWZ, ELO, FIDE-ID aus dem Profil-Link).</summary>
    public static List<RosterEntry> ParseRoster(string html)
    {
        var list = new List<RosterEntry>();
        var sections = TeamSectionRe().Matches(html).ToList();
        for (var i = 0; i < sections.Count; i++)
        {
            var end = i + 1 < sections.Count ? sections[i + 1].Index : html.Length;
            var seg = html[sections[i].Index..end];
            var team = H2Re().Match(seg) is { Success: true } h ? Text(h.Groups[1].Value) : "";
            if (team.Length == 0) continue;
            foreach (Match row in RosterRowRe().Matches(seg))
            {
                var cells = CellRe().Matches(row.Groups[1].Value).Select(c => c.Groups[2].Value).ToList();
                if (cells.Count < 3 || Int(Text(cells[0])) is not { } nr) continue;
                var name = NameSpanRe().Match(cells[1]) is { Success: true } n ? NormalizeName(Text(n.Groups[1].Value)) : "";
                if (name.Length == 0) continue;
                var title = BadgeRe().Match(cells[1]) is { Success: true } b ? Text(b.Groups[1].Value) : null;
                var dwz = DwzRe().Match(cells[2]) is { Success: true } d ? Int(Text(d.Groups[1].Value)) : Int(Text(cells[2]));
                var elo = cells.Count > 3 ? Int(Text(cells[^1])) : null;
                var fide = FideRe().Match(row.Value) is { Success: true } f ? f.Groups[1].Value : null;
                list.Add(new RosterEntry(team, nr, name, string.IsNullOrEmpty(title) ? null : title, dwz, elo, fide));
            }
        }
        return list;
    }

    /// <summary>Das PGN als Text: UTF-8, wenn es gültiges UTF-8 ist, sonst Windows-1252 (so liefert es der Ligamanager,
    /// <c>charset=windows-1252</c>) — „Gröbenzell" statt „Gr�benzell".</summary>
    public static string DecodePgn(byte[] bytes, string? charset = null)
    {
        if (bytes.Length == 0) return "";
        if (charset is { Length: > 0 } cs && !cs.Contains("utf", StringComparison.OrdinalIgnoreCase))
        {
            try { return CodePagesEncodingProvider.Instance.GetEncoding(cs.Trim('"'))?.GetString(bytes) ?? Encoding.Latin1.GetString(bytes); }
            catch (ArgumentException) { /* unbekannter Name → unten raten */ }
        }
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException) { return (CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? Encoding.Latin1).GetString(bytes); }
    }

    /// <summary>Alle Partien des PGN mit Runde/Brett (<c>[Round "1.3"]</c> = Runde 1, <c>[Board "3"]</c>) und Hauptvariante.</summary>
    public static List<PgnGame> ParsePgn(string pgn)
    {
        var list = new List<PgnGame>();
        foreach (var (headers, raw) in PgnParser.SplitGameBlocks(pgn))
        {
            var h = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
            var round = Int((h.GetValueOrDefault("Round") ?? "").Split('.')[0]) ?? 0;
            var board = Int(h.GetValueOrDefault("Board")) ?? Int((h.GetValueOrDefault("Round") ?? "").Split('.').ElementAtOrDefault(1)) ?? 0;
            var moveText = PgnParser.SplitGames(raw).Select(x => x.MoveText).FirstOrDefault() ?? "";
            list.Add(new PgnGame(round, board, NormalizeName(h.GetValueOrDefault("White")), NormalizeName(h.GetValueOrDefault("Black")),
                h.TryGetValue("WhiteTeam", out var wt) ? NormalizeTeam(wt) : null, h.TryGetValue("BlackTeam", out var bt) ? NormalizeTeam(bt) : null,
                (h.GetValueOrDefault("Result") ?? "*").Trim(), LeagueBroadcastImport.DateOf(h.GetValueOrDefault("Date")),
                Int(h.GetValueOrDefault("WhiteElo")), Int(h.GetValueOrDefault("BlackElo")), PgnParser.ExtractMainlineSans(moveText)));
        }
        return list;
    }

    /// <summary>Die bayerische Farbregel: Heim hat an den GERADEN Brettern Weiß (am PGN 2025/26 geprüft, siehe Klassenkommentar).</summary>
    public static bool HomeWhiteByRule(int board) => board % 2 == 0;

    /// <summary>Alles, was eine Liga für LeagueHub braucht, plus Zähler für den Bericht.</summary>
    public sealed record Parsed(LeagueRefresh.Pages Pages, LeagueTournament Tournament, List<PgnGame> PgnGames, Counts Counts);

    public sealed record Counts
    {
        public int Rounds { get; init; }
        public int Matches { get; init; }
        public int BoardGames { get; init; }
        public int BoardGamesPlayed { get; init; }
        public int BoardPlayersUnmatched { get; init; }
        public int Players { get; init; }
        public int PlayersWithFide { get; init; }
        public int PgnGames { get; init; }
        public int PgnGamesWithMoves { get; init; }
        public int PgnGamesUnmatched { get; init; }
        public int ColorFromPgn { get; init; }
        public int ColorRuleMismatches { get; init; }
        public int? Boards { get; init; }
    }

    /// <summary>Die drei Seiten zu einer Liga zusammensetzen (rein).</summary>
    public static Parsed Build(LeagueRef lref, Schedule schedule, List<RosterEntry> roster, List<PgnGame> pgn)
    {
        var byNr = new Dictionary<(string, int), RosterEntry>();
        foreach (var r in roster) byNr.TryAdd((r.Team, r.Nr), r);
        var pgnByBoard = new Dictionary<(int, int, string, string), PgnGame>();
        foreach (var g in pgn)
        {
            if (g.WhiteTeam is null || g.BlackTeam is null) continue;
            pgnByBoard.TryAdd((g.Round, g.Board, g.WhiteTeam, g.BlackTeam), g);
        }
        var usedPgn = new HashSet<PgnGame>(ReferenceEqualityComparer.Instance);

        var matches = new List<LeagueRefresh.MatchRow>();
        var games = new List<LeagueRefresh.GameRow>();
        var stats = new Dictionary<(string Team, string Name), (int? Rb, double Pts, int N)>();
        var dates = schedule.Rounds.ToDictionary(r => r.Round, r => r);
        int colorPgn = 0, colorMismatch = 0, unmatched = 0, played = 0;

        string Player(string team, int? nr, string name)
        {
            if (nr is { } n && byNr.TryGetValue((team, n), out var e)) return e.Name;
            if (name.Length > 0) unmatched++;
            return name;
        }

        foreach (var m in schedule.Matches)
        {
            var rd = dates.GetValueOrDefault(m.Round);
            matches.Add(new LeagueRefresh.MatchRow(m.Round, m.MatchNo, m.Home, m.Away, m.HomePts, m.AwayPts, rd?.Date, rd?.Time,
                m.Venue is { Length: > 300 } v ? v[..300] : m.Venue));
            foreach (var b in m.Boards)
            {
                var hp = Player(m.Home, b.HomeNr, b.HomeName);
                var ap = Player(m.Away, b.AwayNr, b.AwayName);
                var rule = HomeWhiteByRule(b.Board);
                bool homeWhite;
                if (pgnByBoard.TryGetValue((m.Round, b.Board, m.Home, m.Away), out var pw)) { homeWhite = true; usedPgn.Add(pw); colorPgn++; }
                else if (pgnByBoard.TryGetValue((m.Round, b.Board, m.Away, m.Home), out var pb)) { homeWhite = false; usedPgn.Add(pb); colorPgn++; }
                else homeWhite = rule;
                if (homeWhite != rule) colorMismatch++;
                var (hs, @as, forfeit) = Scores(b.Result);
                if (hs is not null) played++;
                games.Add(new LeagueRefresh.GameRow(m.Round, m.MatchNo, b.Board, m.Home, m.Away, hp, ap, b.HomeTitle, b.AwayTitle,
                    homeWhite ? "w" : "s", b.Result, hs, @as, forfeit, null));
                void Stat(string team, int? nr, string name, double? s)
                {
                    if (name.Length == 0 || s is null) return;
                    var k = (team, name);
                    var cur = stats.GetValueOrDefault(k, (nr, 0, 0));
                    stats[k] = (cur.Rb ?? nr, cur.Pts + s.Value, cur.N + 1);
                }
                Stat(m.Home, b.HomeNr, hp, hs);
                Stat(m.Away, b.AwayNr, ap, @as);
            }
        }

        var rosterRows = roster.Select(r => new LeagueRefresh.RosterRow(null, r.Title, r.Name, r.Fide, r.Elo, r.Dwz, null, r.Team, r.Nr)).ToList();
        var statRows = stats.Select(kv => new LeagueRefresh.StatsRow(kv.Key.Team, kv.Value.Rb, kv.Key.Name, kv.Value.Pts, kv.Value.N, null)).ToList();
        var roundDates = schedule.Rounds.ToDictionary(r => r.Round, r => r.Date);
        var tnr = TnrOf(lref.Id);
        var pages = new LeagueRefresh.Pages(tnr, matches, games, roundDates, rosterRows, statRows);

        var dated = schedule.Rounds.Select(r => LeagueDates.Parse(r.Date)).Where(d => d is not null).Select(d => d!.Value).ToList();
        int? boards = games.Count > 0 ? games.Max(g => g.Board) : null;
        var title = schedule.Title.Length > 0 ? schedule.Title : lref.Slug;
        var tournament = new LeagueTournament
        {
            Tnr = tnr,
            Name = Cut($"{title} {lref.Season.Replace('-', '/')}", 200),
            Season = lref.SeasonLabel,
            Level = LevelOf(lref.Slug),
            League = Cut(title, 40),
            Grp = "",
            Stage = "Liga",
            Start = dated.Count > 0 ? dated.Min().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : null,
            End = dated.Count > 0 ? dated.Max().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : null,
            Rounds = schedule.Rounds.Count > 0 ? schedule.Rounds.Count : null,
            Source = Source,
            SourceRef = lref.Path,
            Boards = boards,
        };
        var counts = new Counts
        {
            Rounds = schedule.Rounds.Count,
            Matches = matches.Count,
            BoardGames = games.Count,
            BoardGamesPlayed = played,
            BoardPlayersUnmatched = unmatched,
            Players = roster.Count,
            PlayersWithFide = roster.Count(r => r.Fide is not null),
            PgnGames = pgn.Count,
            PgnGamesWithMoves = pgn.Count(IsReal),
            PgnGamesUnmatched = pgn.Count(g => !usedPgn.Contains(g)),
            ColorFromPgn = colorPgn,
            ColorRuleMismatches = colorMismatch,
            Boards = boards,
        };
        return new Parsed(pages, tournament, pgn, counts);
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];

    private static bool IsReal(PgnGame g) => g.Result is "1-0" or "0-1" or "1/2-1/2" && g.Sans.Count > 0;

    /// <summary>
    /// Die gespielten Partien als PGN für die Spielerkarten: nur echte (Ergebnis + Züge, keine kampflosen), Namen wie in der
    /// Meldeliste, FIDE-ID aus <paramref name="fideOf"/> (Team, NameKey) im Kopf — <see cref="LeagueProfileStore.ImportGamesAsync"/>
    /// ordnet allein darüber zu. → (PGN, Partien, Partien mit mindestens einer FIDE-ID).
    /// </summary>
    public static (string Pgn, int Games, int WithFide) ProfilePgn(IEnumerable<PgnGame> games, Func<string?, string, string?> fideOf)
    {
        var sb = new StringBuilder();
        int n = 0, withFide = 0;
        foreach (var g in games.Where(IsReal))
        {
            var wf = fideOf(g.WhiteTeam, LeagueNames.NameKey(g.White));
            var bf = fideOf(g.BlackTeam, LeagueNames.NameKey(g.Black));
            var ev = g.WhiteTeam is not null && g.BlackTeam is not null ? $"{g.WhiteTeam} - {g.BlackTeam}" : "Ligamanager";
            sb.Append(PgnWriter.Tag("Event", ev)).Append(PgnWriter.Tag("Site", "ligamanager.schachbund-bayern.de"))
                .Append(PgnWriter.Tag("Date", g.Date ?? "????.??.??")).Append(PgnWriter.Tag("Round", $"{g.Round}.{g.Board}"))
                .Append(PgnWriter.Tag("White", g.White)).Append(PgnWriter.Tag("Black", g.Black)).Append(PgnWriter.Tag("Result", g.Result));
            if (g.WhiteElo is { } we) sb.Append(PgnWriter.Tag("WhiteElo", we.ToString(CultureInfo.InvariantCulture)));
            if (g.BlackElo is { } be) sb.Append(PgnWriter.Tag("BlackElo", be.ToString(CultureInfo.InvariantCulture)));
            if (wf is not null) sb.Append(PgnWriter.Tag("WhiteFideId", wf));
            if (bf is not null) sb.Append(PgnWriter.Tag("BlackFideId", bf));
            if (g.WhiteTeam is not null) sb.Append(PgnWriter.Tag("WhiteTeam", g.WhiteTeam));
            if (g.BlackTeam is not null) sb.Append(PgnWriter.Tag("BlackTeam", g.BlackTeam));
            sb.Append('\n').Append(PgnWriter.MoveText(g.Sans, null, null, g.Result)).Append("\n\n");
            n++;
            if (wf is not null || bf is not null) withFide++;
        }
        return (sb.ToString(), n, withFide);
    }

    // ── Abrufen + Einspielen ────────────────────────────────────────────────────────────────────

    /// <summary>Die Liga gibt es beim Ligamanager nicht (404 auf dem Spielplan).</summary>
    public sealed class NotFoundException(string message) : Exception(message);
    /// <summary>Die Nummer gehört schon einer Liga aus einer anderen Quelle (chess-results). Seit dem <see cref="TnrOffset"/>
    /// nur noch ein Sicherheitsnetz — chess-results-Nummern reichen nicht bis dorthin.</summary>
    public sealed class ConflictException(string message) : Exception(message);

    /// <param name="forbiddenIsMissing">403 wie 404 behandeln — der PGN-Download älterer Saisonen (gemessen 07.10.2026: bis
    /// 2018/19) antwortet dauerhaft mit 403 „Fehler | Ligamanager"; Spielplan und Meldelisten sind dort weiter offen.</param>
    private async Task<(byte[] Body, string? Charset)?> GetAsync(HttpClient client, string path, CancellationToken ct, bool forbiddenIsMissing = false)
    {
        using var r = await client.GetAsync(path, ct);
        if (r.StatusCode == HttpStatusCode.NotFound || forbiddenIsMissing && r.StatusCode == HttpStatusCode.Forbidden) return null;
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadAsByteArrayAsync(ct), r.Content.Headers.ContentType?.CharSet);
    }

    private Task Wait(CancellationToken ct) => Pause > TimeSpan.Zero ? Task.Delay(Pause, ct) : Task.CompletedTask;

    /// <summary>Spielplan, Meldelisten und PGN holen und zusammensetzen (noch nichts schreiben).</summary>
    public async Task<Parsed> FetchAsync(LeagueRef lref, CancellationToken ct)
    {
        var client = _http.CreateClient(ClientName);
        var plan = await GetAsync(client, $"{lref.Path}/spielplan", ct)
                   ?? throw new NotFoundException($"Liga {lref.Path} gibt es beim Ligamanager nicht");
        await Wait(ct);
        var teams = await GetAsync(client, $"{lref.Path}/mannschaften", ct);
        await Wait(ct);
        // 404 = noch keine Partien; 403 = ältere Saison ohne Download → Farben nach der Regel, keine Partien für die Karten
        var pgn = await GetAsync(client, $"{lref.Path}/partien/download/alle.pgn", ct, forbiddenIsMissing: true);
        var schedule = ParseSchedule(Encoding.UTF8.GetString(plan.Body));
        var roster = teams is { } t ? ParseRoster(Encoding.UTF8.GetString(t.Body)) : new();
        // Eine 404-Seite kommt als HTML — nur echtes PGN zählt.
        var pgnText = pgn is { } p ? DecodePgn(p.Body, p.Charset) : "";
        var games = pgnText.TrimStart().StartsWith('[') ? ParsePgn(pgnText) : new();
        return Build(lref, schedule, roster, games);
    }

    /// <summary>Ergebnis eines Imports (bzw. eines Probelaufs mit <c>dryRun</c>).</summary>
    public sealed record ImportResult(int Tnr, string Name, string Season, int Level, bool DryRun, Counts Counts,
        int ProfileGames, int ProfileGamesWithFide, int ProfilesTouched, int FideFilled, int? Views);

    /// <summary>
    /// Eine Liga holen und — ohne <paramref name="dryRun"/> — ersetzen (Turnier-Zeile + Runden/Begegnungen/Bretter/Meldelisten
    /// in EINER Transaktion), fehlende FIDE-IDs ergänzen, die Partien in die Karten spielen und mit <paramref name="rebuildViews"/>
    /// die Ansichten neu rechnen. <paramref name="boards"/>: Bretter je Begegnung, solange noch keine Runde gespielt ist (sonst aus
    /// einer früheren Saison derselben Liga). <paramref name="importProfiles"/> = <c>false</c> (Wartungswerkzeug beim Laden der
    /// Trainings-Historie): die Partien NICHT in die Spielerkarten spielen.
    /// </summary>
    public async Task<ImportResult> ImportAsync(LeagueRef lref, bool dryRun, CancellationToken ct, int? boards = null, bool rebuildViews = true,
        bool importProfiles = true)
    {
        var parsed = await FetchAsync(lref, ct);
        var t = parsed.Tournament;
        if (dryRun)
            return new ImportResult(t.Tnr, t.Name, t.Season, t.Level, true, parsed.Counts, 0, 0, 0, 0, null);

        // Sicherheitsnetz: unter der (versetzten) Nummer steht eine Liga FREMDER Quelle — dann nichts überschreiben.
        // Dieselbe Liga (Source = ligamanager) wird ersetzt wie gehabt.
        var existing = await _db.LeagueTournaments.AsNoTracking().FirstOrDefaultAsync(x => x.Tnr == t.Tnr, ct);
        if (existing is not null && existing.Source != Source)
            throw new ConflictException($"Nummer {t.Tnr} gehört schon der Liga „{existing.Name}“ ({existing.Source ?? "chess-results"})");
        t.Boards ??= boards is > 0 and <= 16 ? boards : existing?.Boards ?? await PreviousBoardsAsync(lref, ct);

        var fideFilled = 0;
        await InTransactionAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            var row = await _db.LeagueTournaments.FirstOrDefaultAsync(x => x.Tnr == t.Tnr, ct);
            if (row is null) _db.LeagueTournaments.Add(row = new LeagueTournament { Tnr = t.Tnr });
            row.Name = t.Name; row.Season = t.Season; row.Level = t.Level; row.League = t.League; row.Grp = t.Grp; row.Stage = t.Stage;
            row.Start = t.Start; row.End = t.End; row.Rounds = t.Rounds; row.Source = Source; row.SourceRef = t.SourceRef; row.Boards = t.Boards;
            row.UpdatedAt = _now();
            await LeagueRefresh.ReplaceAsync(_db, parsed.Pages, _now(), ct);
            fideFilled = await FillMissingFideAsync(ct);
        }, ct);
        _db.ChangeTracker.Clear();

        // FIDE-IDs nach dem Ergänzen aus der Datenbank — dann tragen auch ältere Saisonen sie im Kopf.
        var fides = (await _db.LeaguePlayers.AsNoTracking().Where(p => p.Tnr == t.Tnr && p.FideId != null)
                .Select(p => new { p.Team, p.NameKey, p.FideId }).ToListAsync(ct))
            .GroupBy(p => (p.Team, p.NameKey)).ToDictionary(g => g.Key, g => g.First().FideId);
        var (pgn, n, withFide) = ProfilePgn(parsed.PgnGames, (team, key) => team is null ? null : fides.GetValueOrDefault((team, key)));
        var (imported, profiles) = withFide == 0 || !importProfiles ? (0, 0)
            : await new LeagueProfileStore(_db).ImportGamesAsync(pgn, PgnSource, ct, skipSameMoves: true);
        int? views = rebuildViews ? await _league.RebuildViewsAsync(ct) : null;
        _log.LogInformation("LeagueHub: Ligamanager-Liga {Path} eingespielt — {Matches} Begegnungen, {Boards} Bretter, {Games} Partien in {Profiles} Karten",
            lref.Path, parsed.Counts.Matches, parsed.Counts.BoardGames, imported, profiles);
        return new ImportResult(t.Tnr, t.Name, t.Season, t.Level, false, parsed.Counts, n, withFide, profiles, fideFilled, views);
    }

    /// <summary>
    /// Ligamanager-Ligen unter einer UNversetzten Nummer (<c>Source = ligamanager</c>, <c>Tnr ≤ TnrOffset</c>) — so hätte sie der
    /// Import bis 0.697.0 angelegt. <b>Bewusst keine Umschreibe-Migration</b>: der Endpunkt lief bis zum Versatz gegen keine
    /// Datenbank (Dev und Prod ohne Ligamanager-Ligen), und ein Umschlüsseln über sieben Tabellen (Turnier, Runden, Begegnungen,
    /// Bretter, Meldelisten, Ansichten, Teilen-Links; die Vereinspartien hängen an <c>LeagueGames.Id</c>, nicht an der Tnr) wäre
    /// viel Code für einen Fall, den es nicht gibt. Stattdessen meldet der Start solche Zeilen als Warnung, und das Aktualisieren
    /// lässt sie aus (sonst entstünde dieselbe Liga ein zweites Mal unter der neuen Nummer). Abhilfe, falls doch eine auftaucht:
    /// die Liga neu einspielen (landet unter der versetzten Nummer) und die alte Zeile samt Abhängigen löschen.
    /// </summary>
    public static async Task<List<(int Tnr, string? SourceRef)>> LegacyTnrsAsync(AppDbContext db, CancellationToken ct = default) =>
        (await db.LeagueTournaments.AsNoTracking().Where(t => t.Source == Source && t.Tnr <= TnrOffset)
            .Select(t => new { t.Tnr, t.SourceRef }).ToListAsync(ct))
        .Select(t => (t.Tnr, t.SourceRef)).ToList();

    /// <summary>Beim Start: <see cref="LegacyTnrsAsync"/> als Warnung ins Log. Wirft nie — ein Prüffehler darf den Start nicht kippen.</summary>
    public static async Task WarnLegacyTnrsAsync(AppDbContext db, ILogger log, CancellationToken ct = default)
    {
        try
        {
            foreach (var (tnr, sourceRef) in await LegacyTnrsAsync(db, ct))
                log.LogWarning("LeagueHub: Ligamanager-Liga {Tnr} ({SourceRef}) liegt unter einer Nummer ohne Versatz {Offset} — "
                    + "wird nicht aktualisiert; neu einspielen und die alte Zeile löschen", tnr, sourceRef, TnrOffset);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "LeagueHub: Prüfung auf Ligamanager-Ligen ohne Versatz gescheitert");
        }
    }

    /// <summary>Bretter derselben Liga einer früheren Saison (gleiche Region + Slug) — vor der ersten Runde steht die Zahl
    /// nirgends auf den Seiten.</summary>
    private async Task<int?> PreviousBoardsAsync(LeagueRef lref, CancellationToken ct)
    {
        var rows = await _db.LeagueTournaments.AsNoTracking().Where(x => x.Source == Source && x.SourceRef != null)
            .Select(x => new { x.Tnr, x.SourceRef, x.Season, x.Boards }).ToListAsync(ct);
        foreach (var r in rows.OrderByDescending(r => r.Season, StringComparer.Ordinal))
        {
            if (LeagueRef.Parse(r.SourceRef) is not { } o || o.Region != lref.Region || o.Slug != lref.Slug || o.Id == lref.Id) continue;
            if (r.Boards is > 0) return r.Boards;
            var max = await _db.LeagueGames.AsNoTracking().Where(g => g.Tnr == r.Tnr).MaxAsync(g => (int?)g.Board, ct);
            if (max is > 0) return max;
        }
        return null;
    }

    /// <summary>
    /// Fehlende FIDE-IDs ergänzen — seit der Zugspitze-Quelle (2026-10-07) über die ganze Region Bayern
    /// (<see cref="LeagueRegions.FillMissingFideAsync"/>: Ligamanager + Schachkreis Zugspitze, gleicher Verein + NameKey, genau
    /// eine ID). Speichert selbst. → ergänzte Meldelisten-Zeilen.
    /// </summary>
    public Task<int> FillMissingFideAsync(CancellationToken ct) => LeagueRegions.FillMissingFideAsync(_db, LeagueRegions.Bayern, ct);

    /// <summary>Selbst geöffnete Transaktion IN der Execution-Strategy (CLAUDE.md); InMemory kennt keine Transaktionen.</summary>
    private async Task InTransactionAsync(Func<Task> work, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            await work();
            await _db.SaveChangesAsync(ct);
            return;
        }
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await work();
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    // ── Muster der Seiten ───────────────────────────────────────────────────────────────────────

    [GeneratedRegex(@"<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline)]
    private static partial Regex H1Re();
    [GeneratedRegex(@"<h2[^>]*>(.*?)</h2>", RegexOptions.Singleline)]
    private static partial Regex H2Re();
    [GeneratedRegex(@"<div\s+class=""[^""]*\bspielplan-runde-card\b[^""]*""\s+data-round=""(\d+)""")]
    private static partial Regex RoundCardRe();
    [GeneratedRegex(@"class=""[^""]*\bspielplan-runde-datum\b[^""]*""[^>]*>(.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex RoundDateRe();
    [GeneratedRegex(@"\b\d{2}\.\d{2}\.\d{4}\b")]
    private static partial Regex DateRe();
    [GeneratedRegex(@"<div\s+class=""match-row\b")]
    private static partial Regex MatchRowRe();
    [GeneratedRegex(@"<span\s+class=""fw-bold team-name text-end""\s+title=""([^""]*)""")]
    private static partial Regex HomeTeamRe();
    [GeneratedRegex(@"<span\s+class=""fw-bold team-name""\s+title=""([^""]*)""")]
    private static partial Regex AwayTeamRe();
    [GeneratedRegex(@"class=""erg-heim"">(.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex ErgHeimRe();
    [GeneratedRegex(@"class=""erg-gast"">(.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex ErgGastRe();
    [GeneratedRegex(@"bi-geo-alt-fill[^>]*></i>\s*<span>(.*?)</span>\s*</div>\s*<div>(.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex VenueRe();
    [GeneratedRegex(@"<tr[^>]*>\s*<th\s+class=""brett-nr[^""]*""[^>]*>\s*(\d+)\s*</th>(.*?)</tr>", RegexOptions.Singleline)]
    private static partial Regex BoardRowRe();
    [GeneratedRegex(@"<td(?:\s+class=""([^""]*)"")?[^>]*>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex CellRe();
    [GeneratedRegex(@"<span\s+class=""badge[^""]*"">(.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex BadgeRe();
    [GeneratedRegex(@"^(\+|-|1|0|½)\s*-\s*(\+|-|1|0|½)$")]
    private static partial Regex ResultRe();
    [GeneratedRegex(@"<div\s+class=""mannschaft-section\b")]
    private static partial Regex TeamSectionRe();
    [GeneratedRegex(@"<tr\s+class=""mannschaft-aufstellung-brett""[^>]*>(.*?)</tr>", RegexOptions.Singleline)]
    private static partial Regex RosterRowRe();
    [GeneratedRegex(@"<span\s+class=""fw-semibold[^""]*"">(.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex NameSpanRe();
    [GeneratedRegex(@"<div\s+class=""tabular-nums"">(.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex DwzRe();
    [GeneratedRegex(@"ratings\.fide\.com/profile/(\d{3,10})")]
    private static partial Regex FideRe();
}
