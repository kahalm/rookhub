using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Dritte Liga-Quelle (2026-10-07, „LeagueHub für SK Weilheim", Schritt 2): der Schachkreis Zugspitze, <see cref="SiteUrl"/> —
/// eine WordPress-Seite mit eigenem Liga-Programm, kein Ligamanager, kein chess-results. Dort spielen die unteren Mannschaften
/// von SK Weilheim (II Zugspitzliga, III A-Klasse, IV B-Klasse); die erste spielt im Ligamanager (<see cref="LigamanagerSource"/>,
/// gleiche Region <see cref="LeagueRegions.Bayern"/>). Keine API — gelesen werden je Liga + Saison:
/// <list type="bullet">
/// <item><c>/ergebnisse/?Saison=Y&amp;Liga=N</c> — Kopf (Liga-Name „Zugspitzliga 2026/27"), Runden mit Termin („1.Runde am
///   Sonntag, 11.10.2026, 10:00 Uhr"), Begegnungen je Runde mit Mannschaftsergebnis („4½:1½", „6:0 kl" = kampflos,
///   „spielfrei").</item>
/// <item><c>/ergebnisse/?Saison=Y&amp;Liga=N&amp;Runde=r</c> — nur für gespielte Runden: je Begegnung die Bretter mit
///   Ranglisten-Nr. („Pos") beider Spieler, Name „Nachname,Vorname", DWZ und Ergebnis aus HEIM-Sicht („1:0", „½", „+:-",
///   „0:0kl"). Die Farbe steht als Feldfarbe am Namen: dunkles Feld (<c>#d47844</c>) = Schwarz, helles = Weiß.</item>
/// <item><c>/ligadaten/?Liga=N</c> — NUR für die laufende Saison (der Parameter Saison wirkt dort nicht): je Mannschaft
///   Spiellokal und Aufstellung („1. IM Bayer,Bernhard 2347"). Kontaktdaten sieht nur ein angemeldeter Nutzer — gelesen wird
///   nichts davon (keine Anmeldung). Ältere Saisonen bekommen ihre Meldeliste aus den Brettern (wer gespielt hat, mit Pos).</item>
/// </list>
/// <para><b>Keine FIDE-IDs, kein PGN.</b> Die Spieler-Identität ist der Name (<see cref="LeagueNames.Pid"/>); die FIDE-ID kommt
/// aus den Ligamanager-Meldelisten derselben Vereine (<see cref="LeagueRegions.FillMissingFideAsync"/>, Region Bayern) — gleicher
/// Verein + NameKey, genau eine ID. Deshalb ZUERST die Ligamanager-Ligen der Vereine einspielen.</para>
/// <para><b>Farben:</b> am echten Bestand (2025/26 Zugspitzliga + Kreisklasse, 2026/27 A-/B-Klasse; 406 Bretter) hat die
/// HEIM-Mannschaft an den GERADEN Brettern Weiß — dieselbe Regel wie im Ligamanager (<see cref="LigamanagerSource.HomeWhiteByRule"/>);
/// gelesen wird die Feldfarbe, die Regel gilt nur, wenn sie fehlt.</para>
/// <para><b>Welche Ligen:</b> die Erwachsenen-Ligen des Kreises (<see cref="LevelOf"/>: Zugspitzliga 5, Kreisklasse 6, A-Klasse 7,
/// B-Klasse 8, C-Klasse 9 — samt Vor-/Endrunden). Senioren-, Jugend- und Pokal-Wettbewerbe und die vom Kreis nur gespiegelten
/// Ligen des Verbands (Oberliga … Bezirksliga — die kommen aus dem Ligamanager) lehnt der Leser ab
/// (<see cref="UnsupportedException"/>): sie haben keine Stufe in der Liga-Leiter, ihre Einsätze verfälschten QHigher/QLower,
/// und das Prognose-Modell ist an Erwachsenen-Ligen gerechnet.</para>
/// <para>Höflich: eigener User-Agent, <see cref="Pause"/> zwischen den Abrufen; abgerufen wird nur unter <see cref="SiteUrl"/>
/// mit Pfaden aus Zahlen (kein freier Abruf beliebiger Adressen).</para>
/// </summary>
public sealed partial class ZugspitzeSource
{
    /// <summary>Wert von <see cref="LeagueTournament.Source"/>.</summary>
    public const string Source = "zugspitze";
    public const string ClientName = "Zugspitze";
    public const string SiteUrl = "https://schachkreis-zugspitze.de";

    /// <summary>
    /// Turniernummer: <c>TnrOffset + Saisonjahr · 1000 + Liga-Id</c> (<see cref="TnrOf"/>) — z. B. Zugspitzliga 2026/27 =
    /// 912 026 001. Die Liga-Ids des Kreises (<c>?Liga=1</c>) gelten über die Saisonen hinweg (Liga 1 ist jedes Jahr die
    /// Zugspitzliga), eine Saison braucht also die Jahreszahl im Schlüssel. Der Bereich liegt über dem Ligamanager
    /// (900 000 001 … 909 999 999, <see cref="LigamanagerSource.MaxLigamanagerId"/>) und weit unter <see cref="int.MaxValue"/>:
    /// Saisons <see cref="MinSeason"/>…<see cref="MaxSeason"/>, Liga-Ids 1…<see cref="MaxLigaId"/> → 911 990 001 … 912 199 999.
    /// </summary>
    public const int TnrOffset = 910_000_000;
    public const int MinSeason = 1990;
    public const int MaxSeason = 2199;
    public const int MaxLigaId = 999;

    public static int TnrOf(int season, int ligaId) =>
        season is >= MinSeason and <= MaxSeason && ligaId is > 0 and <= MaxLigaId ? TnrOffset + season * 1000 + ligaId
            : throw new ArgumentOutOfRangeException(nameof(season), $"{season}/{ligaId}", "Saison oder Liga-Id außerhalb des Bereichs");

    /// <summary>Liegt die Tnr im Bereich der Zugspitze-Ligen?</summary>
    public static bool IsZugspitzeTnr(int tnr) =>
        tnr > TnrOffset + MinSeason * 1000 && tnr <= TnrOffset + MaxSeason * 1000 + MaxLigaId && (tnr - TnrOffset) % 1000 != 0;

    /// <summary>(Saison, Liga-Id) aus der Tnr; <c>null</c>, wenn sie keine Zugspitze-Tnr ist.</summary>
    public static (int Season, int LigaId)? RefOf(int tnr) =>
        IsZugspitzeTnr(tnr) ? ((tnr - TnrOffset) / 1000, (tnr - TnrOffset) % 1000) : null;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly LeagueService _league;
    private readonly ILogger<ZugspitzeSource> _log;
    private readonly Func<DateTime> _now;

    public ZugspitzeSource(AppDbContext db, IHttpClientFactory http, LeagueService league, ILogger<ZugspitzeSource> log,
        Func<DateTime>? now = null)
    {
        _db = db; _http = http; _league = league; _log = log; _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>Pause zwischen zwei Abrufen beim Schachkreis.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromSeconds(1);

    // ── Liga-Adresse ────────────────────────────────────────────────────────────────────────────

    /// <summary>Eine Liga des Kreises: Liga-Id (<c>?Liga=1</c>) und Saison als Anfangsjahr (2026 = „2026/27"); ohne Saison =
    /// die laufende (beim Abruf aufgelöst).</summary>
    public sealed record LeagueRef(int LigaId, int? Season)
    {
        /// <summary>„zugspitze/2026-27/1" — so steht es in <see cref="LeagueTournament.SourceRef"/>.</summary>
        public string Path => Season is { } y ? $"{Source}/{y}-{(y + 1) % 100:00}/{LigaId}" : $"{Source}/{LigaId}";
        /// <summary>„2026/27".</summary>
        public string SeasonLabel => Season is { } y ? $"{y}/{(y + 1) % 100:00}" : "";
        /// <summary>Die Ergebnis-Seite beim Kreis (Link der Ansicht).</summary>
        public string Url => Season is { } y ? $"{SiteUrl}/ergebnisse/?Saison={y}&Liga={LigaId}" : $"{SiteUrl}/ergebnisse/?Liga={LigaId}";

        /// <summary>Aus einer Adresse des Kreises (<c>https://schachkreis-zugspitze.de/ergebnisse/?Saison=2026&amp;Liga=1</c>,
        /// auch <c>/ligadaten/?Liga=1</c>) oder dem gespeicherten Pfad („zugspitze/2026-27/1"); <c>null</c> = keine Zugspitze-Liga.</summary>
        public static LeagueRef? Parse(string? input)
        {
            var s = (input ?? "").Trim();
            if (s.Length == 0) return null;
            if (s.Contains("://", StringComparison.Ordinal))
            {
                if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http")
                    || !(u.Host.Equals(new Uri(SiteUrl).Host, StringComparison.OrdinalIgnoreCase)
                         || u.Host.Equals("www." + new Uri(SiteUrl).Host, StringComparison.OrdinalIgnoreCase))) return null;
                var q = QueryRe().Matches(u.Query).ToDictionary(m => m.Groups[1].Value.ToLowerInvariant(), m => m.Groups[2].Value);
                if (!q.TryGetValue("liga", out var l)) return null;
                return Of(int.TryParse(l, out var li) ? li : null, q.GetValueOrDefault("saison"));
            }
            var m2 = PathRe().Match(s);
            if (!m2.Success) return null;
            var r = Of(int.Parse(m2.Groups[3].Value, CultureInfo.InvariantCulture), m2.Groups[1].Value);
            // „2026-27" muss zusammenpassen
            return r is { Season: { } y } && (y + 1) % 100 == int.Parse(m2.Groups[2].Value, CultureInfo.InvariantCulture) ? r : null;
        }

        /// <summary>Aus Liga-Id und Saison („2026", „2026/27", „2026-27", „2026/2027"; leer = laufende).</summary>
        public static LeagueRef? Of(int? ligaId, string? season)
        {
            if (ligaId is not (> 0 and <= MaxLigaId)) return null;
            var t = (season ?? "").Trim();
            if (t.Length == 0) return new LeagueRef(ligaId.Value, null);
            var m = SeasonRe().Match(t);
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out var y) || y is < MinSeason or > MaxSeason) return null;
            if (m.Groups[2].Success)
            {
                var y2 = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if ((m.Groups[2].Value.Length == 2 ? (y + 1) % 100 : y + 1) != y2) return null;
            }
            return new LeagueRef(ligaId.Value, y);
        }
    }

    [GeneratedRegex(@"[?&]([A-Za-z]+)=([^&#]*)")]
    private static partial Regex QueryRe();
    [GeneratedRegex(@"^zugspitze/(\d{4})-(\d{2})/(\d{1,3})$")]
    private static partial Regex PathRe();
    [GeneratedRegex(@"^(\d{4})(?:[-/](\d{2}|\d{4}))?$")]
    private static partial Regex SeasonRe();

    /// <summary>
    /// Stufe einer Kreisliga nach ihrem Namen — unter der Bezirksliga Oberbayern (Ligamanager, Stufe 4): Zugspitzliga (früher
    /// auch „Kreisliga") 5, Kreisklasse 6, A-Klasse 7, B-Klasse 8, C-Klasse 9 (samt „Vorrunde Nord/Süd", „Endrunde A/B").
    /// Anders als im Ligamanager (Kreisklasse = A-Klasse = 6) sind Kreisklasse und A-Klasse hier ZWEI Stufen: ein Verein spielt
    /// mit der einen Mannschaft in der Kreisklasse, mit der nächsten in der A-Klasse. <c>null</c> = kein Erwachsenen-Ligabetrieb
    /// des Kreises (Senioren, U12/U16, Pokal, gespiegelte Verbandsligen).
    /// </summary>
    public static int? LevelOf(string? title)
    {
        var s = LeagueNames.Clean(title).ToLowerInvariant();
        if (s.StartsWith("zugspitzliga", StringComparison.Ordinal) || s.StartsWith("kreisliga", StringComparison.Ordinal)) return 5;
        if (s.StartsWith("kreisklasse", StringComparison.Ordinal)) return 6;
        if (s.StartsWith("a-klasse", StringComparison.Ordinal)) return 7;
        if (s.StartsWith("b-klasse", StringComparison.Ordinal)) return 8;
        if (s.StartsWith("c-klasse", StringComparison.Ordinal)) return 9;
        return null;
    }

    // ── Lesen (rein, getestet) ──────────────────────────────────────────────────────────────────

    public sealed record BoardRow(int Board, int? HomeNr, string HomeName, string? HomeTitle, int? HomeDwz, bool? HomeWhite,
        int? AwayNr, string AwayName, string? AwayTitle, int? AwayDwz, string Result);
    public sealed record MatchRow(int Round, int MatchNo, string Home, string Away, double? HomePts, double? AwayPts, bool Forfeit,
        List<BoardRow> Boards);
    public sealed record RoundInfo(int Round, string? Date, string? Time);
    /// <summary>Eine Ergebnis-Seite: Liga-Name („Zugspitzliga", „C-Klasse, Vorrunde Nord"), Saison der Seite (Anfangsjahr), die
    /// neueste Saison der Auswahl, Runden, Begegnungen (mit Brettern nur auf einer Runden-Seite).</summary>
    public sealed record Results(string Title, int? Season, int? NewestSeason, List<RoundInfo> Rounds, List<MatchRow> Matches);
    public sealed record RosterEntry(string Team, int Nr, string Name, string? Title, int? Dwz);
    /// <summary>Die Ligadaten: Saison der Seite („2026/27" → 2026), Meldelisten, Spiellokal je Mannschaft.</summary>
    public sealed record LigaData(int? Season, List<RosterEntry> Roster, Dictionary<string, string> Venues);

    private static string Text(string html) => LeagueNames.Clean(WebUtility.HtmlDecode(TagRe().Replace(html, " ")));

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRe();

    private static int? Int(string? s) => int.TryParse((s ?? "").Trim().TrimEnd('.'), out var v) && v > 0 ? v : null;

    /// <summary>„IM Bayer,Bernhard" → („Bayer, Bernhard", „IM"); „Pappenheim,Rainer,Dr." → „Pappenheim, Rainer Dr." (wie
    /// <see cref="LigamanagerSource.NormalizeName"/>); „N.N."/leer → „".</summary>
    public static (string Name, string? Title) NameAndTitle(string? raw)
    {
        var t = LeagueNames.Clean(WebUtility.HtmlDecode(raw ?? ""));
        string? title = null;
        if (TitleRe().Match(t) is { Success: true } m) { title = m.Groups[1].Value; t = t[m.Length..]; }
        var name = LigamanagerSource.NormalizeName(t);
        if (name is "N.N." or "N. N." or "NN" or "-" or "--") name = "";
        return (name, title);
    }

    [GeneratedRegex(@"^(GM|IM|FM|CM|WGM|WIM|WFM|WCM)\s+")]
    private static partial Regex TitleRe();

    /// <summary>Brett-Ergebnis in Heim-Sicht → Schreibweise wie bei chess-results („1 - 0", „½ - ½", „+ - -", „- - -").</summary>
    public static string NormalizeResult(string? s)
    {
        var t = LeagueNames.Clean(WebUtility.HtmlDecode(s ?? "")).Replace(" ", "").ToLowerInvariant();
        return t switch
        {
            "" => "",
            "1:0" => "1 - 0",
            "0:1" => "0 - 1",
            "½" or "½:½" or "0,5:0,5" => "½ - ½",
            "+:-" or "1:0kl" => "+ - -",
            "-:+" or "0:1kl" => "- - +",
            "-:-" or "0:0kl" or "0:0" => "- - -",
            _ => t,
        };
    }

    /// <summary>„4½:1½" → (4,5; 1,5; false), „6:0 kl" → (6; 0; true), „8:0 kl *" ebenso; „" → (null, null, false).</summary>
    public static (double? Home, double? Away, bool Forfeit) MatchScore(string? s)
    {
        var t = LeagueNames.Clean(WebUtility.HtmlDecode(s ?? ""));
        var forfeit = t.Contains("kl", StringComparison.OrdinalIgnoreCase);
        t = t.Replace("kl", "", StringComparison.OrdinalIgnoreCase).Replace("*", "").Trim();
        var parts = t.Split(':');
        if (parts.Length != 2) return (null, null, false);
        var h = LigamanagerSource.Points(parts[0]);
        var a = LigamanagerSource.Points(parts[1]);
        return h is null || a is null ? (null, null, false) : (h, a, forfeit);
    }

    /// <summary>Feldfarbe am Namen → Weiß? (dunkel = Schwarz; unbekannt = <c>null</c>).</summary>
    private static bool? WhiteOf(string? attrs)
    {
        var c = BgRe().Match(attrs ?? "") is { Success: true } m ? m.Groups[1].Value.ToLowerInvariant() : "";
        return c switch { "#f3d2a6" => true, "#d47844" => false, _ => null };
    }

    [GeneratedRegex(@"bgcolor=['""]?(#[0-9a-fA-F]{6})")]
    private static partial Regex BgRe();

    /// <summary>Eine Ergebnis-Seite (Übersicht oder Runde) lesen.</summary>
    public static Results ParseResults(string html)
    {
        var title = "";
        int? season = null;
        foreach (Match t in TitleTagRe().Matches(html))
        {
            title = LeagueNames.Clean(WebUtility.HtmlDecode(t.Groups[1].Value));
            season = int.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        int? newest = null;
        foreach (Match o in SeasonOptionRe().Matches(html))
        {
            var y = int.Parse(o.Groups[1].Value, CultureInfo.InvariantCulture);
            if (newest is null || y > newest) newest = y;
        }
        var rounds = new List<RoundInfo>();
        var matches = new List<MatchRow>();
        var heads = RoundHeadRe().Matches(html).ToList();
        for (var i = 0; i < heads.Count; i++)
        {
            var round = int.Parse(heads[i].Groups[1].Value, CultureInfo.InvariantCulture);
            var time = heads[i].Groups[3].Success ? heads[i].Groups[3].Value + " Uhr" : null;
            rounds.Add(new RoundInfo(round, heads[i].Groups[2].Value, time));
            var end = i + 1 < heads.Count ? heads[i + 1].Index : html.Length;
            var seg = html[heads[i].Index..end];
            var ms = MatchHeadRe().Matches(seg).ToList();
            for (var j = 0; j < ms.Count; j++)
            {
                var mEnd = j + 1 < ms.Count ? ms[j + 1].Index : seg.Length;
                var body = seg[(ms[j].Index + ms[j].Length)..mEnd];
                var no = int.Parse(ms[j].Groups[1].Value, CultureInfo.InvariantCulture);
                var home = Text(ms[j].Groups[2].Value);
                var away = Text(ms[j].Groups[3].Value);
                var (hp, ap, forfeit) = MatchScore(Text(ms[j].Groups[4].Value));
                // „spielfrei - SK Penzberg II": der Rest der Kette (LeagueWorld, Ansicht) kennt das Freilos nur als GAST.
                if (home.Equals("spielfrei", StringComparison.OrdinalIgnoreCase)) (home, away, hp, ap) = (away, "spielfrei", null, null);
                if (away.Equals("spielfrei", StringComparison.OrdinalIgnoreCase)) { away = "spielfrei"; hp = ap = null; }
                if (home.Length == 0 || away.Length == 0) continue;
                var boards = new List<BoardRow>();
                foreach (Match br in BoardRowRe().Matches(body))
                {
                    var cells = CellRe().Matches(br.Value).Select(c => (Attrs: c.Groups[1].Value, Html: c.Groups[2].Value)).ToList();
                    if (cells.Count < 9 || Int(Text(cells[0].Html)) is not { } board) continue;
                    var (hn, ht) = NameAndTitle(Text(cells[2].Html));
                    var (an, at) = NameAndTitle(Text(cells[6].Html));
                    var hw = WhiteOf(cells[2].Attrs);
                    var aw = WhiteOf(cells[6].Attrs);
                    boards.Add(new BoardRow(board, Int(Text(cells[1].Html)), hn, ht, Int(Text(cells[3].Html)), hw ?? (aw is { } x ? !x : null),
                        Int(Text(cells[5].Html)), an, at, Int(Text(cells[7].Html)), NormalizeResult(Text(cells[8].Html))));
                }
                matches.Add(new MatchRow(round, no, home, away, hp, ap, forfeit, boards));
            }
        }
        return new Results(title, season, newest, rounds, matches);
    }

    /// <summary>Die Ligadaten-Seite: Saison, Aufstellungen und Spiellokal (Name + Anschrift, ohne Telefon/Kontakt) je Mannschaft.</summary>
    public static LigaData ParseLigaData(string html)
    {
        int? season = LigaDataHeadRe().Match(html) is { Success: true } h ? int.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        var roster = new List<RosterEntry>();
        var venues = new Dictionary<string, string>(StringComparer.Ordinal);
        var teams = TeamHeadRe().Matches(html).ToList();
        for (var i = 0; i < teams.Count; i++)
        {
            var team = Text(teams[i].Groups[1].Value);
            if (team.Length == 0) continue;
            var end = i + 1 < teams.Count ? teams[i + 1].Index : html.Length;
            var seg = html[teams[i].Index..end];
            if (VenueRe().Match(seg) is { Success: true } v)
            {
                // „TuS Vereinsheim<br>Jahnstraße 4, 82538 Geretsried (<a …>Karte</a>)<br>nur Getränke …" → die ersten zwei Zeilen
                var lines = BrRe().Split(v.Groups[1].Value).Select(x => Text(KarteRe().Replace(x, ""))).Where(x => x.Length > 0).Take(2);
                var venue = string.Join(", ", lines);
                if (venue.Length > 0) venues[team] = venue.Length > 300 ? venue[..300] : venue;
            }
            foreach (Match row in RosterRowRe().Matches(seg))
            {
                if (Int(Text(row.Groups[1].Value)) is not { } nr) continue;
                var (name, title) = NameAndTitle(Text(row.Groups[2].Value));
                if (name.Length == 0) continue;
                roster.Add(new RosterEntry(team, nr, name, title, Int(Text(row.Groups[3].Value))));
            }
        }
        return new LigaData(season, roster, venues);
    }

    /// <summary>Alles, was eine Liga für LeagueHub braucht, plus Zähler für den Bericht.</summary>
    public sealed record Parsed(LeagueRefresh.Pages Pages, LeagueTournament Tournament, Counts Counts);

    public sealed record Counts
    {
        public int Rounds { get; init; }
        public int RoundsPlayed { get; init; }
        public int Matches { get; init; }
        public int BoardGames { get; init; }
        public int BoardGamesPlayed { get; init; }
        public int BoardPlayersUnmatched { get; init; }
        public int Players { get; init; }
        /// <summary>Meldeliste aus den Ligadaten (laufende Saison) oder aus den Brettern (ältere Saison).</summary>
        public string RosterFrom { get; init; } = "";
        public int ColorFromPage { get; init; }
        public int ColorRuleMismatches { get; init; }
        public int? Boards { get; init; }
    }

    /// <summary>
    /// Übersicht (Runden/Begegnungen), Runden-Seiten (Bretter) und — nur laufende Saison — Ligadaten zusammensetzen (rein).
    /// <paramref name="roundBoards"/>: Bretter je (Runde, Begegnungs-Nr.) aus den Runden-Seiten.
    /// </summary>
    public static Parsed Build(LeagueRef lref, Results overview, IReadOnlyDictionary<(int Round, int MatchNo), List<BoardRow>> roundBoards,
        LigaData? ligaData)
    {
        var season = lref.Season ?? overview.Season ?? throw new ArgumentException("Saison unbekannt", nameof(lref));
        var level = LevelOf(overview.Title) ?? throw new UnsupportedException($"„{overview.Title}“ ist keine Liga des Kreises");
        var useLigaData = ligaData is { } ld && ld.Season == season && ld.Roster.Count > 0;
        var roster = useLigaData ? ligaData!.Roster.ToList() : new List<RosterEntry>();
        var byNr = new Dictionary<(string, int), RosterEntry>();
        var byKey = new HashSet<(string, string)>();
        foreach (var r in roster) { byNr.TryAdd((r.Team, r.Nr), r); byKey.Add((r.Team, LeagueNames.NameKey(r.Name))); }

        var matches = new List<LeagueRefresh.MatchRow>();
        var games = new List<LeagueRefresh.GameRow>();
        var stats = new Dictionary<(string Team, string Name), (int? Rb, double Pts, int N)>();
        var dates = overview.Rounds.ToDictionary(r => r.Round, r => r);
        int colorPage = 0, colorMismatch = 0, unmatched = 0, played = 0;
        var playedRounds = new HashSet<int>();

        string Player(string team, int? nr, string name, int? dwz, string? title)
        {
            if (name.Length == 0) return "";
            if (!useLigaData)
            {
                // Ältere Saison: die Meldeliste entsteht aus den Brettern (wer gespielt hat; Nr. = Pos der Seite).
                if (byKey.Add((team, LeagueNames.NameKey(name))))
                {
                    var e = new RosterEntry(team, nr ?? 0, name, title, dwz);
                    roster.Add(e);
                    if (nr is { } n) byNr.TryAdd((team, n), e);
                }
                return name;
            }
            if (byKey.Contains((team, LeagueNames.NameKey(name)))) return name;
            if (nr is { } k && byNr.TryGetValue((team, k), out var hit) && LeagueNames.NameKey(hit.Name) == LeagueNames.NameKey(name)) return hit.Name;
            unmatched++;
            return name;
        }

        foreach (var m in overview.Matches)
        {
            var rd = dates.GetValueOrDefault(m.Round);
            var venue = m.Away == "spielfrei" ? null : ligaData?.Venues.GetValueOrDefault(m.Home);
            matches.Add(new LeagueRefresh.MatchRow(m.Round, m.MatchNo, m.Home, m.Away, m.HomePts, m.AwayPts, rd?.Date, rd?.Time,
                useLigaData ? venue : null));
            if (m.HomePts is not null) playedRounds.Add(m.Round);
            if (!roundBoards.TryGetValue((m.Round, m.MatchNo), out var boards)) continue;
            foreach (var b in boards)
            {
                var hp = Player(m.Home, b.HomeNr, b.HomeName, b.HomeDwz, b.HomeTitle);
                var ap = Player(m.Away, b.AwayNr, b.AwayName, b.AwayDwz, b.AwayTitle);
                var rule = LigamanagerSource.HomeWhiteByRule(b.Board);
                var homeWhite = b.HomeWhite ?? rule;
                if (b.HomeWhite is not null) colorPage++;
                if (homeWhite != rule) colorMismatch++;
                var (hs, @as, forfeit) = LigamanagerSource.Scores(b.Result);
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

        var tnr = TnrOf(season, lref.LigaId);
        var rosterRows = roster.Select(r => new LeagueRefresh.RosterRow(null, r.Title, r.Name, null, null, r.Dwz, null, r.Team,
            r.Nr > 0 ? r.Nr : null)).ToList();
        var statRows = stats.Select(kv => new LeagueRefresh.StatsRow(kv.Key.Team, kv.Value.Rb, kv.Key.Name, kv.Value.Pts, kv.Value.N, null)).ToList();
        var roundDates = overview.Rounds.ToDictionary(r => r.Round, r => r.Date);
        var pages = new LeagueRefresh.Pages(tnr, matches, games, roundDates, rosterRows, statRows);

        var dated = overview.Rounds.Select(r => LeagueDates.Parse(r.Date)).Where(d => d is not null).Select(d => d!.Value).ToList();
        int? boardsMax = games.Count > 0 ? games.Max(g => g.Board) : null;
        var resolved = lref with { Season = season };
        // „C-Klasse, Vorrunde Nord" → Liga „C-Klasse", Gruppe „Vorrunde Nord"
        var comma = overview.Title.IndexOf(',');
        var league = comma > 0 ? overview.Title[..comma].Trim() : overview.Title;
        var grp = comma > 0 ? overview.Title[(comma + 1)..].Trim() : "";
        var tournament = new LeagueTournament
        {
            Tnr = tnr,
            Name = Cut($"{overview.Title} {resolved.SeasonLabel}", 200),
            Season = resolved.SeasonLabel,
            Level = level,
            League = Cut(league, 40),
            Grp = Cut(grp, 20),
            Stage = "Liga",
            Start = dated.Count > 0 ? dated.Min().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : null,
            End = dated.Count > 0 ? dated.Max().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : null,
            Rounds = overview.Rounds.Count > 0 ? overview.Rounds.Count : null,
            Source = Source,
            SourceRef = resolved.Path,
            Boards = boardsMax,
        };
        var counts = new Counts
        {
            Rounds = overview.Rounds.Count,
            RoundsPlayed = playedRounds.Count,
            Matches = matches.Count,
            BoardGames = games.Count,
            BoardGamesPlayed = played,
            BoardPlayersUnmatched = unmatched,
            Players = roster.Count,
            RosterFrom = useLigaData ? "ligadaten" : "boards",
            ColorFromPage = colorPage,
            ColorRuleMismatches = colorMismatch,
            Boards = boardsMax,
        };
        return new Parsed(pages, tournament, counts);
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];

    // ── Abrufen + Einspielen ────────────────────────────────────────────────────────────────────

    /// <summary>Die Liga gibt es beim Kreis nicht (404 oder eine Seite ohne Liga-Kopf/Runden).</summary>
    public sealed class NotFoundException(string message) : Exception(message);
    /// <summary>Keine Erwachsenen-Liga des Kreises (Senioren, Jugend, Pokal, gespiegelte Verbandsliga) — siehe Klassenkommentar.</summary>
    public sealed class UnsupportedException(string message) : Exception(message);
    /// <summary>Die Nummer gehört schon einer Liga aus einer anderen Quelle (Sicherheitsnetz).</summary>
    public sealed class ConflictException(string message) : Exception(message);

    private async Task<string?> GetAsync(HttpClient client, string path, CancellationToken ct)
    {
        using var r = await client.GetAsync(path, ct);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        r.EnsureSuccessStatusCode();
        // WordPress liefert UTF-8; Umlaute stehen teils als Entity (&uuml;) — Text() löst beides auf.
        return Encoding.UTF8.GetString(await r.Content.ReadAsByteArrayAsync(ct));
    }

    private Task Wait(CancellationToken ct) => Pause > TimeSpan.Zero ? Task.Delay(Pause, ct) : Task.CompletedTask;

    /// <summary>Übersicht, Runden-Seiten der gespielten Runden und (laufende Saison) die Ligadaten holen und zusammensetzen.</summary>
    public async Task<Parsed> FetchAsync(LeagueRef lref, CancellationToken ct)
    {
        var client = _http.CreateClient(ClientName);
        var basePath = lref.Season is { } y ? $"ergebnisse/?Saison={y}&Liga={lref.LigaId}" : $"ergebnisse/?Liga={lref.LigaId}";
        var html = await GetAsync(client, basePath, ct) ?? throw new NotFoundException($"Liga {lref.LigaId} gibt es beim Schachkreis nicht");
        var overview = ParseResults(html);
        if (overview.Title.Length == 0 || overview.Rounds.Count == 0 || overview.Season is null)
            throw new NotFoundException($"Liga {lref.Path}: keine Ergebnis-Seite (kein Liga-Kopf oder keine Runden)");
        if (lref.Season is { } want && overview.Season != want)
            throw new NotFoundException($"Liga {lref.Path}: der Kreis zeigt Saison {overview.Season} statt {want}");
        if (LevelOf(overview.Title) is null)
            throw new UnsupportedException($"„{overview.Title}“ ist keine Erwachsenen-Liga des Kreises (Senioren, Jugend, Pokal und "
                + "Verbandsligen werden nicht eingespielt)");
        var season = overview.Season.Value;
        lref = lref with { Season = season };

        var boards = new Dictionary<(int, int), List<BoardRow>>();
        foreach (var round in overview.Matches.Where(m => m.HomePts is not null).Select(m => m.Round).Distinct().Order())
        {
            await Wait(ct);
            var rh = await GetAsync(client, $"ergebnisse/?Saison={season}&Liga={lref.LigaId}&Runde={round}", ct);
            if (rh is null) continue;
            foreach (var m in ParseResults(rh).Matches.Where(m => m.Round == round && m.Boards.Count > 0))
                boards[(m.Round, m.MatchNo)] = m.Boards;
        }
        LigaData? ligaData = null;
        // Die Ligadaten kennen nur die laufende Saison — für ältere gar nicht erst holen.
        if (overview.NewestSeason is null || overview.NewestSeason == season)
        {
            await Wait(ct);
            if (await GetAsync(client, $"ligadaten/?Liga={lref.LigaId}", ct) is { } lh) ligaData = ParseLigaData(lh);
        }
        return Build(lref, overview, boards, ligaData);
    }

    /// <summary>Ergebnis eines Imports (bzw. eines Probelaufs mit <c>dryRun</c>).</summary>
    public sealed record ImportResult(int Tnr, string Name, string Season, int Level, bool DryRun, Counts Counts, int FideFilled,
        int PlayersWithFide, int? Views);

    /// <summary>
    /// Eine Liga holen und — ohne <paramref name="dryRun"/> — ersetzen (Turnier-Zeile + Runden/Begegnungen/Bretter/Meldelisten in
    /// EINER Transaktion), fehlende FIDE-IDs aus der Region Bayern ergänzen und mit <paramref name="rebuildViews"/> die Ansichten
    /// neu rechnen. <paramref name="boards"/>: Bretter je Begegnung, solange noch keine Runde gespielt ist (sonst aus der
    /// Vorsaison derselben Liga-Id).
    /// </summary>
    public async Task<ImportResult> ImportAsync(LeagueRef lref, bool dryRun, CancellationToken ct, int? boards = null, bool rebuildViews = true)
    {
        var parsed = await FetchAsync(lref, ct);
        var t = parsed.Tournament;
        if (dryRun)
            return new ImportResult(t.Tnr, t.Name, t.Season, t.Level, true, parsed.Counts, 0, 0, null);

        var existing = await _db.LeagueTournaments.AsNoTracking().FirstOrDefaultAsync(x => x.Tnr == t.Tnr, ct);
        if (existing is not null && existing.Source != Source)
            throw new ConflictException($"Nummer {t.Tnr} gehört schon der Liga „{existing.Name}“ ({existing.Source ?? "chess-results"})");
        t.Boards ??= boards is > 0 and <= 16 ? boards : existing?.Boards ?? await PreviousBoardsAsync(t.Tnr, ct);

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
            fideFilled = await LeagueRegions.FillMissingFideAsync(_db, LeagueRegions.Bayern, ct);
        }, ct);
        _db.ChangeTracker.Clear();
        var withFide = await _db.LeaguePlayers.CountAsync(p => p.Tnr == t.Tnr && p.FideId != null, ct);
        int? views = rebuildViews ? await _league.RebuildViewsAsync(ct) : null;
        _log.LogInformation("LeagueHub: Zugspitze-Liga {Path} eingespielt — {Matches} Begegnungen, {Boards} Bretter, {Players} Spieler ({Fide} mit FIDE-ID)",
            t.SourceRef, parsed.Counts.Matches, parsed.Counts.BoardGames, parsed.Counts.Players, withFide);
        return new ImportResult(t.Tnr, t.Name, t.Season, t.Level, false, parsed.Counts, fideFilled, withFide, views);
    }

    /// <summary>Bretter derselben Liga-Id einer früheren Saison — vor der ersten Runde steht die Zahl nirgends auf den Seiten.</summary>
    private async Task<int?> PreviousBoardsAsync(int tnr, CancellationToken ct)
    {
        if (RefOf(tnr) is not { } me) return null;
        var rows = await _db.LeagueTournaments.AsNoTracking().Where(x => x.Source == Source && x.Tnr != tnr)
            .Select(x => new { x.Tnr, x.Boards }).ToListAsync(ct);
        foreach (var r in rows.Where(r => RefOf(r.Tnr) is { } o && o.LigaId == me.LigaId && o.Season < me.Season)
                     .OrderByDescending(r => r.Tnr))
        {
            if (r.Boards is > 0) return r.Boards;
            var max = await _db.LeagueGames.AsNoTracking().Where(g => g.Tnr == r.Tnr).MaxAsync(g => (int?)g.Board, ct);
            if (max is > 0) return max;
        }
        return null;
    }

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

    /// <summary>Der zweite &lt;title&gt; im Inhalt: „Ergebnisse Zugspitzliga 2026/27".</summary>
    [GeneratedRegex(@"<title>Ergebnisse\s+([^<]+?)\s+(\d{4})/\d{2}\s*</title>")]
    private static partial Regex TitleTagRe();
    [GeneratedRegex(@"<option\s+(?:selected\s+)?value='\.\./ergebnisse/\?Saison=(\d{4})&Liga=\d+'")]
    private static partial Regex SeasonOptionRe();
    [GeneratedRegex(@"<a name='r(\d+)'><a class=thlink[^>]*>\d+\.\s*Runde am [^,<]*,\s*(\d{2}\.\d{2}\.\d{4})(?:,\s*(\d{1,2}:\d{2})\s*Uhr)?", RegexOptions.Singleline)]
    private static partial Regex RoundHeadRe();
    [GeneratedRegex(@"<td class=mnserg><center><a name='p(\d+)'></a>\s*\d+\s*</td><td class=mnserg colspan=3>(.*?)</td><td class=mnserg><center>-</td><td class=mnserg colspan=3>(.*?)</td>(?:</td>)?<td class=mnserg><center>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex MatchHeadRe();
    [GeneratedRegex(@"<tr><td style='padding: 0; text-align: center;'>\s*\d+\s*</td>.*?</tr>", RegexOptions.Singleline)]
    private static partial Regex BoardRowRe();
    [GeneratedRegex(@"<td([^>]*)>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex CellRe();
    [GeneratedRegex(@"<h3>Ligadaten\s+[^<]+?\s+(\d{4})/\d{2}\s*</h3>")]
    private static partial Regex LigaDataHeadRe();
    [GeneratedRegex(@"<th[^>]*colspan=2>(.*?)</th>", RegexOptions.Singleline)]
    private static partial Regex TeamHeadRe();
    [GeneratedRegex(@"Spiellokal:</td>\s*<td>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex VenueRe();
    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrRe();
    [GeneratedRegex(@"\(\s*<a[^>]*>\s*Karte\s*</a>\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex KarteRe();
    [GeneratedRegex(@"<tr><td style='padding: 0; text-align: right; width:2%'>\s*(\d+)\.\s*</td><td[^>]*>(.*?)</td><td[^>]*>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex RosterRowRe();
}
