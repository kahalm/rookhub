using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Welche Liga-Partie der Stapel als Nächstes rechnet (0.665.0, Wunsch 2026-10-05: „analysier im Hintergrund auch alle
/// Partien für LeagueHub — zumindest von Spielern, die aktuell in der Liga mitspielen. Erst die neuesten Partien (2 Jahre
/// zurück), dann immer bevorzugt die Gegner von Schwaz nächste Runde (seit dem Mandanten-Schritt 2026-10-07: die Gegner JEDES
/// Vereins in <see cref="LeagueClub"/>), dann der Rest, und wenn das alles fertig ist erst
/// wieder die Meisterpartien").
/// <list type="bullet">
/// <item>Quelle: die Profile (<see cref="LeaguePlayerProfile.Pgn"/>: Lumbra, Megabase, chess-results, Übertragungen) aller
/// Spieler mit FIDE-ID auf einer Meldeliste der laufenden Saison; nur Partien der letzten <see cref="Years"/> Jahre
/// (Datum im Kopf, ein Jahr allein reicht), nur aus der Grundstellung.</item>
/// <item>Zweite Quelle (2026-10-05): die Online-Partien derselben Spieler, aber nur langsamer als Blitz
/// (<see cref="SlowSpeeds"/>) — rund 28 600 Partien neben den 13 000 aus den Profilen. Dieselbe Entdopplung
/// ueber den Zug-Schluessel: wer dieselbe Partie in beiden Quellen hat, bekommt sie einmal.</item>
/// <item>Die gebaute Liste haelt je Partie ihr PGN im Speicher. Mit dem 5-Jahre-Fenster und beiden Quellen
/// sind das beim heutigen Bestand rund 42 000 Partien statt 3500 — gemessen am 2026-10-05 etwa 30 MB in
/// einem Prozess, der 6 GB darf. Waechst der Bestand um Groessenordnungen, muss die Liste Zeiger statt
/// Text halten.</item>
/// <item>Reihenfolge: erst die Spieler der Gegner der Vereine in der nächsten noch nicht gespielten Runde (je Mannschaft eines
/// Vereins), dann alle übrigen — jeweils die neueste Partie zuerst.</item>
/// <item>Dieselbe Partie in zwei Profilen (zwei Ligaspieler gegeneinander) zählt einmal; eine schon gerechnete (eigene
/// Liga-Analyse oder die Analyse einer Vereinspartie mit denselben Zügen) wird übersprungen.</item>
/// </list>
/// Die Liste lebt im Speicher und wird alle <see cref="Refresh"/> neu gebaut (die nächste Runde rückt vor, neue Partien
/// kommen in die Profile).
/// </summary>
public sealed class LeagueAnalysisQueue
{
    /// <summary>Wie weit zurueck Partien aus den Profilen genommen werden. Seit 2026-10-05 fuenf statt
    /// zwei Jahre (Wunsch: „mach mal die letzten 5 Jahre der Ligaspieler") — gemessen am selben Tag rund
    /// 13 000 Partien statt 3500, waehrend die Profile insgesamt 56 000 tragen. Der Preis steht im
    /// Klassenkommentar: die Liste im Speicher waechst mit dem Fenster.</summary>
    public const int Years = 5;

    /// <summary>Welche Online-Partien ueberhaupt in Frage kommen: alles LANGSAMER als Blitz (Wunsch
    /// 2026-10-05). Bullet und Blitz bleiben draussen — es sind mit Abstand die meisten (704 000 von
    /// 752 000), und eine Suche bis Tiefe 30 sagt ueber eine Partie, die in Minuten gespielt wurde,
    /// wenig ueber die Spielweise ihres Spielers. Fernschach ist ausdruecklich dabei.</summary>
    public static readonly string[] SlowSpeeds = ["rapid", "classical", "correspondence"];
    public static readonly TimeSpan Refresh = TimeSpan.FromHours(1);

    public sealed record Item(string Pgn, string Key, bool Opponent, DateOnly Date);

    private readonly object _gate = new();
    private List<Item> _items = new();
    private DateTime _builtAt = DateTime.MinValue;
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);

    /// <summary>Eine Partie, die sich nicht rechnen ließ (unspielbares PGN), nicht noch einmal anbieten.</summary>
    public void Skip(string key) { lock (_gate) _done.Add(key); }

    /// <summary>Die nächste noch nicht gerechnete Liga-Partie samt ihrem Zug-Schlüssel (kanonische SAN) — oder <c>null</c>.</summary>
    public async Task<(Item Item, string MovesHash)?> NextAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        List<Item> items;
        lock (_gate)
        {
            items = _items;
            if (now - _builtAt < Refresh && items.Count > 0) goto pick;
        }
        items = await BuildAsync(db, now, ct);
        lock (_gate) { _items = items; _builtAt = now; }
    pick:
        foreach (var it in items)
        {
            lock (_gate) if (_done.Contains(it.Key)) continue;
            var plies = GamePlies.Parse(it.Pgn, GameAnalysisDefaults.MaxPlies);
            if (plies is not { } p || p.Plies.Count == 0) { Skip(it.Key); continue; }
            var hash = LeagueClubService.HashOf(p.Plies.Select(x => x.San).ToList());
            var analyzed = await db.GameAnalyses.AnyAsync(a => a.MovesHash == hash && a.Status != GameAnalysisStatus.Failed, ct)
                || await db.GameAnalyses.AnyAsync(a => a.Origin == GameAnalysisOrigin.Club && a.EngineId == null && a.Status != GameAnalysisStatus.Failed
                    && db.LeagueClubGames.Any(c => c.Id == a.LeagueClubGameId && c.MovesHash == hash), ct);
            Skip(it.Key);   // so oder so erledigt: gleich eingereiht oder schon gerechnet
            if (!analyzed) return (it, hash);
        }
        return null;
    }

    /// <summary>Die geordnete Liste bauen (siehe Klassenkommentar).</summary>
    internal static async Task<List<Item>> BuildAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        // Die laufende Saison JE REGION (Rest-Bug aus 0.712.0, behoben 0.720.0): mit der globalen jüngsten Saison fiele eine Region,
        // deren neue Saison noch nicht eingespielt ist, ganz heraus — und Vorsaisonen (z. B. Bundesliga 2024/25 für die Merkmale)
        // zählen nie mit.
        var leagues = await db.LeagueTournaments.AsNoTracking().Where(t => t.Stage == "Liga")
            .Select(t => new { t.Tnr, t.Season, t.Source }).ToListAsync(ct);
        if (leagues.Count == 0) return new();
        var current = leagues.GroupBy(t => LeagueRegions.Of(t.Source))
            .ToDictionary(g => g.Key, g => g.Select(t => t.Season).Max(StringComparer.Ordinal)!);
        var tnrs = leagues.Where(t => t.Season == current[LeagueRegions.Of(t.Source)]).Select(t => t.Tnr).ToList();
        var players = await db.LeaguePlayers.AsNoTracking().Where(p => tnrs.Contains(p.Tnr) && p.FideId != null && p.FideId != "")
            .Select(p => new { p.Tnr, p.Team, p.FideId }).ToListAsync(ct);
        var opponents = await NextRoundOpponentFidesAsync(db, tnrs, players.Select(p => (p.Tnr, p.Team, p.FideId!)).ToList(), ct);
        var fides = players.Select(p => p.FideId!).Distinct().ToList();

        var cutoff = DateOnly.FromDateTime(now).AddYears(-Years);
        var items = new List<Item>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in fides.Chunk(100))
        {
            var profiles = await db.LeaguePlayerProfiles.AsNoTracking().Where(p => chunk.Contains(p.FideId))
                .Select(p => new { p.FideId, p.Pgn }).ToListAsync(ct);
            foreach (var prof in profiles)
                foreach (var (headers, moveText) in PgnParser.SplitGames(prof.Pgn ?? string.Empty))
                {
                    if (headers.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen)) continue;   // Stellungspartie
                    if (DateOf(headers.GetValueOrDefault("Date")) is not { } date || date < cutoff) continue;
                    var sans = PgnParser.ExtractMainlineSans(moveText);
                    if (sans.Count < 2) continue;
                    var key = LeagueClubService.HashOf(sans);
                    if (!seen.Add(key)) continue;
                    items.Add(new Item(PgnOf(headers, moveText), key, opponents.Contains(prof.FideId), date));
                }
        }
        await AddOnlineGamesAsync(db, fides, opponents, cutoff, seen, items, ct);
        return items.OrderByDescending(i => i.Opponent).ThenByDescending(i => i.Date).ToList();
    }

    /// <summary>
    /// Zweite Quelle (2026-10-05): die Online-Partien derselben Spieler (lichess, chess.com), aber nur die
    /// langsamen (<see cref="SlowSpeeds"/>). Gemessen an diesem Tag kommen damit rund 28 600 Partien dazu,
    /// waehrend die Profile 13 000 beisteuern.
    ///
    /// <para>Konten BEIDER Sicherheitsstufen zaehlen mit („sicher" und „wahrscheinlich", 23 300 zu 5300) —
    /// dieselbe Menge, die der Eroeffnungsbaum im Profil zeigt. Eine falsch zugeordnete Partie kostet hier
    /// Rechenzeit, aber nichts Bleibendes: die Analyse haengt an der PARTIE, nicht am Spieler.</para>
    ///
    /// <para>Entdoppelt wird ueber denselben Zug-Schluessel wie die Profilpartien: dieselbe Partie aus zwei
    /// Quellen steht einmal in der Liste. Die Zuege liegen als blankes SAN in einer Spalte, die Nummern
    /// kommen hier dazu — ohne sie liest kein PGN-Parser den Text als Partie.</para>
    /// </summary>
    private static async Task AddOnlineGamesAsync(AppDbContext db, List<string> fides, HashSet<string> opponents,
        DateOnly cutoff, HashSet<string> seen, List<Item> items, CancellationToken ct)
    {
        var since = cutoff.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        foreach (var chunk in fides.Chunk(100))
        {
            var games = await db.LeagueOnlineGames.AsNoTracking()
                .Where(g => chunk.Contains(g.FideId) && g.PlayedAt >= since && SlowSpeeds.Contains(g.Speed) && g.Plies >= 2)
                .Select(g => new
                {
                    g.FideId, g.PlayedAt, g.Speed, g.White, g.Result, g.Opponent, g.Moves,
                    Site = g.Account.Site, User = g.Account.UserName,
                })
                .ToListAsync(ct);

            foreach (var g in games)
            {
                var sans = g.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (sans.Count < 2) continue;
                var key = LeagueClubService.HashOf(sans);
                if (!seen.Add(key)) continue;

                var date = DateOnly.FromDateTime(g.PlayedAt);
                var me = string.IsNullOrWhiteSpace(g.User) ? g.FideId : g.User;
                var other = string.IsNullOrWhiteSpace(g.Opponent) ? "?" : g.Opponent!;
                var headers = new Dictionary<string, string>
                {
                    ["Event"] = $"{g.Site} {g.Speed}",
                    ["Site"] = g.Site,
                    ["Date"] = date.ToString("yyyy.MM.dd"),
                    ["White"] = g.White ? me : other,
                    ["Black"] = g.White ? other : me,
                    ["Result"] = g.Result,
                };
                items.Add(new Item(PgnOf(headers, Numbered(sans)), key, opponents.Contains(g.FideId), date));
            }
        }
    }

    /// <summary>„e4 e5 Nf3" → „1. e4 e5 2. Nf3".</summary>
    internal static string Numbered(IReadOnlyList<string> sans)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < sans.Count; i++)
        {
            if (i % 2 == 0) sb.Append(i / 2 + 1).Append(". ");
            sb.Append(sans[i]);
            if (i < sans.Count - 1) sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Die FIDE-IDs der Gegner der Vereine in der nächsten Runde: je Mannschaft eines Vereins (<see cref="LeagueClub.OwnsTeam"/>,
    /// alle Vereine — ein Verein ist dem anderen kein Vorrang schuldig) die kleinste Runde ihrer Begegnungen ohne Ergebnis, dort
    /// der Gegner und dessen Meldeliste.
    /// </summary>
    internal static async Task<HashSet<string>> NextRoundOpponentFidesAsync(AppDbContext db, List<int> tnrs,
        List<(int Tnr, string Team, string Fide)> players, CancellationToken ct)
    {
        var clubs = await db.LeagueClubs.AsNoTracking().ToListAsync(ct);
        bool Own(string team) => clubs.Any(c => c.OwnsTeam(team));
        // offene Begegnungen der laufenden Ligen (eine Saison, ein paar hundert Zeilen) — die Vereinsregel prüft der Speicher
        var open = (await db.LeagueMatches.AsNoTracking()
                .Where(m => tnrs.Contains(m.Tnr) && m.HomePts == null && m.AwayPts == null)
                .Select(m => new { m.Tnr, m.Round, m.Home, m.Away }).ToListAsync(ct))
            .Where(m => Own(m.Home) || Own(m.Away)).ToList();
        // eine Begegnung zweier Vereine zählt für beide: jede eigene Mannschaft hat ihren Gegner
        var opponentTeams = open
            .SelectMany(m => new[] { (m.Tnr, m.Round, Team: m.Home, Opp: m.Away), (m.Tnr, m.Round, Team: m.Away, Opp: m.Home) })
            .Where(x => Own(x.Team))
            .GroupBy(x => (x.Tnr, x.Team))
            .Select(g => g.OrderBy(x => x.Round).First())
            .Select(x => (x.Tnr, x.Opp))
            .ToHashSet();
        return players.Where(p => opponentTeams.Contains((p.Tnr, p.Team))).Select(p => p.Fide).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>„2025.03.14" → Datum; „2025.??.??" → 1. Jänner 2025; sonst <c>null</c>.</summary>
    internal static DateOnly? DateOf(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        var parts = date.Trim().Split('.');
        if (!int.TryParse(parts[0], out var y) || y < 1900) return null;
        var m = parts.Length > 1 && int.TryParse(parts[1], out var mm) && mm is >= 1 and <= 12 ? mm : 1;
        var d = parts.Length > 2 && int.TryParse(parts[2], out var dd) && dd >= 1 && dd <= DateTime.DaysInMonth(y, m) ? dd : 1;
        return new DateOnly(y, m, d);
    }

    private static string PgnOf(Dictionary<string, string> headers, string moveText)
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in headers) sb.Append('[').Append(k).Append(" \"").Append(v.Replace("\"", "")).Append("\"]\n");
        sb.Append('\n').Append(moveText.Trim()).Append('\n');
        return sb.ToString();
    }
}
