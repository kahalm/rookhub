using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Die von Hand eingegebenen ersten Züge einer Ligapaarung (<see cref="LeagueGameMove"/>) als TEILPARTIE (0.725.0, Frage 2026-10-08:
/// „wenn ich die ersten Züge eingebe — fließen die in die Eröffnungsbäume bei der Vorbereitung ein?" — „ja mach das so").
/// <para>Kopfdaten aus der Paarung (<see cref="LeagueGame"/>): beide Spieler samt FIDE-ID und Elo, Farben aus
/// <see cref="LeagueGame.HomeColor"/>, Datum = Rundentermin (<see cref="LeagueRound"/>), Event = Liga + Saison + Runde, Ergebnis
/// aus Sicht Weiß (<see cref="LeagueFixtureGames.WhiteBlackResult"/>), Züge = die gespeicherte SAN, Quelle
/// <see cref="Source"/> im Kopf <see cref="LeagueProfileBuilder.SourceHeader"/>. Nicht gespeichert — gerechnet bei jedem Lesen
/// (ein Join der kleinen Tabelle <c>LeagueGameMoves</c> auf die Paarungen), damit ein geänderter Eintrag sofort zählt.</para>
/// <para><b>Volle Partie schlägt Teilpartie</b> (<see cref="FilterAsync"/>): eine Teilpartie zählt NICHT, wenn es zum Brett eine volle
/// Partie gibt — (a) eine Vereinspartie IRGENDEINES Vereins mit fester Zuordnung auf genau dieses Brett (Schlüssel Tnr/Runde/
/// Begegnung/Brett, <see cref="LeagueGameLinks"/>), oder (b) eine Partie derselben beiden Spieler (FIDE-ID, sonst Nachname, Farben
/// wie am Brett) höchstens <see cref="LeagueFixtureGames.DayTolerance"/> Tage neben dem Rundentermin — unter den Vereinspartien
/// beider Spieler (alle Vereine, nur mit Jahr: dann gleiches Jahr) und den mitgegebenen Kartenpartien. Archivierte Vereinspartien
/// blendet der globale Filter aus. Bewusst ohne Vereinskontext: die Spielerkarte ist für alle Vereine dieselbe, und die Züge
/// sind je Paarung global sichtbar.</para>
/// </summary>
public static class LeaguePartialGames
{
    /// <summary>Quelle der Teilpartien (Kopf <c>LeagueSource</c>, Schlüssel in <c>src</c> der Karte und in der Quellen-Tabelle).</summary>
    public const string Source = "Ligarunde";

    /// <summary>Eine Teilpartie: Paarung, beide Seiten (Farbe wie am Brett), Rundentermin und die fertige Partie fürs Profil.</summary>
    public sealed record Partial(LeagueGameLinks.Key Key, int LeagueGameId, string White, string? WhiteFide, string Black,
        string? BlackFide, DateOnly? Date, LeagueProfileBuilder.Game Game);

    /// <summary>Eine volle Partie zum Vergleich: Nachnamen + FIDE-IDs beider Farben, volles Datum oder nur das Jahr.</summary>
    public sealed record FullRef(string WhiteLast, string? WhiteFide, string BlackLast, string? BlackFide, DateOnly? Date, int? Year);

    /// <summary>Ist die Partie eine Teilpartie (Karte: nicht unter „letzte Partien", nicht im PGN-Download)?</summary>
    public static bool Is(LeagueProfileBuilder.Game g) => g.Source == Source;

    /// <summary>Alle Teilpartien (<paramref name="fides"/> <c>null</c>) bzw. die, an denen einer dieser Spieler sitzt. Nur gespielte
    /// Bretter (beide besetzt, nicht kampflos); doppelte Zeilen derselben Paarung zählen einmal (kleinste Id).</summary>
    public static async Task<List<Partial>> LoadAsync(AppDbContext db, IReadOnlyCollection<string>? fides, CancellationToken ct)
    {
        var q = from m in db.LeagueGameMoves.AsNoTracking()
                join g in db.LeagueGames.AsNoTracking()
                    on new { m.Tnr, m.Round, m.MatchNo, m.Board } equals new { g.Tnr, g.Round, g.MatchNo, g.Board }
                where g.Forfeit == 0 && g.HomePlayer != null && g.AwayPlayer != null && m.Moves != ""
                select new { m.Moves, Game = g };
        if (fides is not null)
        {
            if (fides.Count == 0) return new();
            var ids = fides.ToList();
            q = q.Where(x => (x.Game.HomeFide != null && ids.Contains(x.Game.HomeFide))
                             || (x.Game.AwayFide != null && ids.Contains(x.Game.AwayFide)));
        }
        var rows = (await q.ToListAsync(ct))
            .GroupBy(x => LeagueGameLinks.KeyOf(x.Game)).Select(x => x.OrderBy(r => r.Game.Id).First()).ToList();
        if (rows.Count == 0) return new();
        var tnrs = rows.Select(r => r.Game.Tnr).Distinct().ToList();
        var dates = (await db.LeagueRounds.AsNoTracking().Where(r => tnrs.Contains(r.Tnr)).Select(r => new { r.Tnr, r.Round, r.Date })
                .ToListAsync(ct))
            .GroupBy(r => (r.Tnr, r.Round)).ToDictionary(x => x.Key, x => x.First().Date);
        var leagues = (await db.LeagueTournaments.AsNoTracking().Where(t => tnrs.Contains(t.Tnr))
                .Select(t => new { t.Tnr, t.Name, t.Season }).ToListAsync(ct))
            .ToDictionary(t => t.Tnr, t => EventName(t.Name, t.Season));
        return rows.Select(r => Build(r.Game, r.Moves, dates.GetValueOrDefault((r.Game.Tnr, r.Game.Round)),
            leagues.GetValueOrDefault(r.Game.Tnr) ?? "Liga")).ToList();
    }

    /// <summary>„TMM Landesliga 2026/2027" bleibt, wie es heißt; ohne die Saison im Namen kommt sie dazu.</summary>
    internal static string EventName(string name, string season)
    {
        name = name.Trim();
        if (season.Length == 0 || name.Contains(season, StringComparison.Ordinal)
            || season.Length >= 4 && name.Contains(season[..4], StringComparison.Ordinal)) return name;
        return name.Length == 0 ? season : $"{name} {season}";
    }

    /// <summary>Die Teilpartie einer Paarung — PGN mit Kopf wie eine Turnierpartie, die Züge, am Ende das Ergebnis des Bretts.</summary>
    internal static Partial Build(LeagueGame g, string moves, DateOnly? date, string league)
    {
        var homeWhite = g.HomeColor != "s";
        var (white, whiteFide, whiteElo) = homeWhite ? (g.HomePlayer!, g.HomeFide, g.HomeElo) : (g.AwayPlayer!, g.AwayFide, g.AwayElo);
        var (black, blackFide, blackElo) = homeWhite ? (g.AwayPlayer!, g.AwayFide, g.AwayElo) : (g.HomePlayer!, g.HomeFide, g.HomeElo);
        var result = PgnResult(LeagueFixtureGames.WhiteBlackResult(g.Result, homeWhite));
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Event"] = $"{league}, Runde {g.Round}",
            ["Site"] = "?",
            ["Date"] = date is { } d ? d.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) : "????.??.??",
            ["Round"] = g.Round.ToString(CultureInfo.InvariantCulture),
            ["White"] = white, ["Black"] = black, ["Result"] = result,
        };
        if (whiteElo is { } we) headers["WhiteElo"] = we.ToString(CultureInfo.InvariantCulture);
        if (blackElo is { } be) headers["BlackElo"] = be.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(whiteFide)) headers["WhiteFideId"] = whiteFide;
        if (!string.IsNullOrEmpty(blackFide)) headers["BlackFideId"] = blackFide;
        headers["Board"] = g.Board.ToString(CultureInfo.InvariantCulture);
        headers[LeagueProfileBuilder.SourceHeader] = Source;

        var sb = new StringBuilder();
        foreach (var k in new[] { "Event", "Site", "Date", "Round", "White", "Black", "Result", "WhiteElo", "BlackElo",
                     "WhiteFideId", "BlackFideId", "Board", LeagueProfileBuilder.SourceHeader })
            if (headers.TryGetValue(k, out var v)) sb.Append(PgnWriter.Tag(k, v));
        sb.Append('\n');
        var sans = moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < sans.Length; i++)
        {
            if (i % 2 == 0) sb.Append(i / 2 + 1).Append(". ");
            sb.Append(sans[i]).Append(' ');
        }
        sb.Append(result);
        return new Partial(LeagueGameLinks.KeyOf(g), g.Id, white, NullIfEmpty(whiteFide), black, NullIfEmpty(blackFide), date,
            new LeagueProfileBuilder.Game(headers, sb.ToString(), Source));
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>„1 - 0", „½ - ½", „0 - 1" (chess-results, schon aus Sicht Weiß) → PGN; alles andere „*".</summary>
    internal static string PgnResult(string result)
    {
        var parts = result.Replace(" ", "").Split('-');
        if (parts.Length != 2) return "*";
        static double? P(string s) => s switch
        {
            "1" => 1, "0" => 0, "½" or "1/2" or "0.5" or "0,5" => 0.5, _ => null,
        };
        return (P(parts[0]), P(parts[1])) switch
        {
            (1, 0) => "1-0",
            (0, 1) => "0-1",
            (0.5, 0.5) => "1/2-1/2",
            _ => "*",
        };
    }

    /// <summary>Volles Datum „2026.10.11" bzw. nur das Jahr — „2026.??.??" ist KEIN 1. Januar.</summary>
    internal static (DateOnly? Date, int? Year) DateOf(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return (null, null);
        var p = date.Trim().Split('.');
        if (!int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out var y) || y < 1900) return (null, null);
        if (p.Length == 3 && int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m is >= 1 and <= 12
            && int.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out var d) && d >= 1 && d <= DateTime.DaysInMonth(y, m))
            return (new DateOnly(y, m, d), y);
        return (null, y);
    }

    /// <summary>Vergleichsdaten einer gespeicherten Partie (Kopfzeilen).</summary>
    public static FullRef RefOf(IReadOnlyDictionary<string, string> h)
    {
        var (date, year) = DateOf(h.GetValueOrDefault("Date"));
        return new FullRef(LeagueProfileBuilder.LastName(h.GetValueOrDefault("White")), NullIfEmpty(h.GetValueOrDefault("WhiteFideId")?.Trim()),
            LeagueProfileBuilder.LastName(h.GetValueOrDefault("Black")), NullIfEmpty(h.GetValueOrDefault("BlackFideId")?.Trim()), date, year);
    }

    /// <summary>Vergleichsdaten einer Vereinspartie — mit dem echten Namen bzw. der FIDE-ID hinter einer anonymisierten Seite.</summary>
    public static FullRef RefOf(LeagueClubGame c) => new(
        LeagueProfileBuilder.LastName(c.WhiteRealName ?? c.White), NullIfEmpty(c.WhiteFide ?? c.WhiteRealFide),
        LeagueProfileBuilder.LastName(c.BlackRealName ?? c.Black), NullIfEmpty(c.BlackFide ?? c.BlackRealFide), null, c.Year);

    /// <summary>Dieselbe Partie wie das Brett der Teilpartie? Beide Farben (FIDE-ID, wo beide eine haben, sonst Nachname) und
    /// Datum ±<see cref="LeagueFixtureGames.DayTolerance"/> Tage — kennt die volle Partie nur das Jahr, das gleiche Jahr.</summary>
    public static bool Covers(FullRef f, Partial p)
    {
        if (p.Date is not { } d) return false;
        if (!Side(f.WhiteLast, f.WhiteFide, p.White, p.WhiteFide) || !Side(f.BlackLast, f.BlackFide, p.Black, p.BlackFide)) return false;
        return f.Date is { } fd ? Math.Abs(fd.DayNumber - d.DayNumber) <= LeagueFixtureGames.DayTolerance : f.Year == d.Year;
    }

    private static bool Side(string last, string? fide, string player, string? playerFide) =>
        fide is not null && playerFide is not null ? fide == playerFide
            : last.Length > 0 && last == LeagueProfileBuilder.LastName(player);

    /// <summary>
    /// Die Teilpartien ohne die, zu deren Brett es eine volle Partie gibt (Regel im Klassenkommentar). <paramref name="profileGames"/>:
    /// die Kartenpartien, gegen die zu prüfen ist (Karte eines Spielers bzw. alle gezählten); die Vereinspartien holt die Methode selbst
    /// (fest zugeordnete + die beider Spieler, alle Vereine).
    /// </summary>
    public static async Task<List<Partial>> FilterAsync(AppDbContext db, List<Partial> partials, IEnumerable<FullRef> profileGames,
        CancellationToken ct)
    {
        if (partials.Count == 0) return partials;
        var tnrs = partials.Select(p => p.Key.Tnr).Distinct().ToList();
        var ids = partials.Select(p => p.LeagueGameId).Distinct().ToList();
        var fides = partials.SelectMany(p => new[] { p.WhiteFide, p.BlackFide }).OfType<string>().Distinct().ToList();
        var club = await db.LeagueClubGames.AsNoTracking()
            .Where(c => (c.LeagueTnr != null && tnrs.Contains(c.LeagueTnr.Value))
                        || (c.LeagueGameId != null && ids.Contains(c.LeagueGameId.Value))
                        || (c.WhiteFide != null && fides.Contains(c.WhiteFide))
                        || (c.BlackFide != null && fides.Contains(c.BlackFide))
                        || (c.WhiteRealFide != null && fides.Contains(c.WhiteRealFide))
                        || (c.BlackRealFide != null && fides.Contains(c.BlackRealFide)))
            // ohne das PGN — nur, was der Vergleich braucht
            .Select(c => new LeagueClubGame
            {
                Id = c.Id, Year = c.Year, White = c.White, Black = c.Black, WhiteFide = c.WhiteFide, BlackFide = c.BlackFide,
                WhiteRealName = c.WhiteRealName, WhiteRealFide = c.WhiteRealFide, BlackRealName = c.BlackRealName,
                BlackRealFide = c.BlackRealFide, LeagueGameId = c.LeagueGameId, LeagueTnr = c.LeagueTnr, LeagueRound = c.LeagueRound,
                LeagueMatchNo = c.LeagueMatchNo, LeagueBoard = c.LeagueBoard,
            })
            .ToListAsync(ct);
        var linked = club.Select(LeagueGameLinks.KeyOf).OfType<LeagueGameLinks.Key>().ToHashSet();
        var linkedIds = club.Where(c => c.LeagueGameId is not null).Select(c => c.LeagueGameId!.Value).ToHashSet();
        var refs = club.Select(RefOf).Concat(profileGames).ToList();
        return partials.Where(p => !linked.Contains(p.Key) && !linkedIds.Contains(p.LeagueGameId) && !refs.Any(f => Covers(f, p))).ToList();
    }
}
