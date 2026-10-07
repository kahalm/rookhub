using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services.League;

/// <summary>
/// Was die Online-Konten-Suche je Liga-Region wissen muss (0.712.0, Wunsch 2026-10-07 „Online-Konten-Zuordnung für die Region
/// Bayern"): Team-Suche (Orte, feste Lichess-Teams), Konto-Prüfung (Land, Orte im Profil, Beschriftungen) und die Online-Liga
/// der Region. Bis 0.710.0 stand das fest auf Tirol (<c>AT</c>, <c>TirolPlace</c>, „Tiroler Lichess-Teams"); für einen
/// Weilheimer war ein deutsches Profil damit „weder Österreich noch seine Föderation".
/// <para>Die Region eines Spielers ist die seiner jüngsten Meldeliste (<see cref="LeagueRegions.Of"/> der Liga-Quelle).</para>
/// </summary>
/// <param name="Id"><see cref="LeagueRegions.Tirol"/> / <see cref="LeagueRegions.Bayern"/>.</param>
/// <param name="Country">Landeskürzel der Profile (Lichess-Flagge, chess.com-Land): „AT" / „DE".</param>
/// <param name="CountryName">„Österreich" / „Deutschland".</param>
/// <param name="Teams">„Tiroler Lichess-Teams" / „Bayerische Lichess-Teams" (Beschriftung im (i)).</param>
/// <param name="PlaceLabel">„Tiroler Ort" / „Bayerischer Ort".</param>
/// <param name="Places">Orte für die Lichess-Team-Suche; ein Eintrag darf Schreibweisen mit „|" anhängen („München|Münchner") —
/// der erste ist der Schlüssel (<see cref="LeagueTeamScout.ClubKeys"/>).</param>
/// <param name="AreaPlaces">Orte, die nur die SUCHE kennt (Kreis/Bezirk: „Zugspitze", „Oberbayern") — kein Verein.</param>
/// <param name="FixedTeams">Lichess-Teams, die die Suche nicht findet (Ort nicht im Namen, „grobes-schach" = SC Gröbenzell) oder
/// die zum Kreis/Bezirk gehören; Jugend-Teams nie (Minderjährige).</param>
/// <param name="Adjectives">Auch „Gautinger", „Windacher", „Tölzer" als Ort zählen (Bayern; in Tirol bleibt es beim Ortsnamen).</param>
/// <param name="OnlineLeague">Die Online-Liga der Region (Tirol: Online-TMM 2021; Bayern: ZugLiga/ObbLiga des Kreises bzw. Bezirks).</param>
/// <param name="OnlineLeagueSeries">Erkennt deren Serien (<see cref="LeagueTeamScout.EventSeries"/>) in den Team-Battles.</param>
public sealed record LeagueOnlineRegion(string Id, string Country, string CountryName, string Teams, string PlaceLabel,
    IReadOnlyList<string> Places, IReadOnlyList<string> AreaPlaces, IReadOnlyList<string> FixedTeams, bool Adjectives,
    string OnlineLeague, Regex OnlineLeagueSeries)
{
    /// <summary>Die Orte, die einen Verein kennzeichnen (ohne Kreis/Bezirk).</summary>
    public IReadOnlyList<string> ClubPlaces => Places.Where(p => !AreaPlaces.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>Ist der Mannschafts- bzw. Team-Name einer aus der Region (Ort als Wort im Namen, auch Kreis/Bezirk)? — für die Team-Suche.</summary>
    public bool IsLocalTeam(string? name) => LeagueTeamScout.ClubKeys(name, Places, Adjectives).Count > 0;

    /// <summary>Die Vereins-Schlüssel eines Namens: die Orte als ganze Wörter — NICHT für ein Kreis-/Bezirks-Team
    /// („Schachkreis Zugspitze", „Schachbezirk Oberbayern", „Schachkreis Ingolstadt-Freising"): wer dort Mitglied ist oder für
    /// den Kreis spielt, gehört damit zu keinem Verein.</summary>
    public List<string> ClubKeys(string? name) =>
        LeagueOnlineRegions.IsAreaTeam(name) ? new() : LeagueTeamScout.ClubKeys(name, ClubPlaces, Adjectives);

    /// <summary>Der Ort der Region in einem Profiltext (Ort/Bio), sonst <c>null</c>. Tirol wie bisher (<see cref="LeagueAccountFinder.TirolPlace"/>).</summary>
    public string? PlaceIn(string? text)
    {
        if (Id == LeagueRegions.Tirol) return LeagueAccountFinder.TirolPlace(text);
        return LeagueOnlineRegions.PlaceRegex(Places, Adjectives).Match(text ?? "") is { Success: true } m ? m.Value : null;
    }
}

/// <summary>Die Regionen der Online-Konten-Suche (siehe <see cref="LeagueOnlineRegion"/>). Konfigurierbar je Region:
/// <c>LeagueOnline:TeamPlaces:{region}</c> (Tirol auch weiter <c>LeagueOnline:TeamPlaces</c>) und <c>LeagueOnline:Teams:{region}</c>
/// (feste Lichess-Team-Ids), beides Komma-Listen.</summary>
public static class LeagueOnlineRegions
{
    public static readonly string[] TirolPlaces =
    {
        "Tirol", "Innsbruck", "Schwaz", "Kufstein", "Wörgl", "Telfs", "Jenbach", "Absam", "Zirl", "Landeck", "Imst", "Lienz",
        "Rattenberg", "Zillertal", "Wattens", "Kitzbühel", "Reutte", "Hall", "Mils", "Fügen", "Kundl",
    };

    /// <summary>Orte der Team-Suche in Bayern (Recherche 07.10.2026: die Vereine um Weilheim, den Schachkreis Zugspitze und den
    /// Bezirk Oberbayern, dazu Augsburg/Ingolstadt/Rosenheim). „Bayern" allein NICHT — 110 Fremdtreffer.</summary>
    public static readonly string[] BayernPlaces =
    {
        "Weilheim", "Starnberg", "Gräfelfing", "Germering", "Gröbenzell", "Gauting", "Gilching", "Ammersee", "Windach",
        "Fürstenfeldbruck", "Tölz", "Tegernsee", "Miesbach", "Penzberg", "Geretsried", "Wolfratshausen", "Garching", "Dachau",
        "Augsburg", "Haunstetten", "Kriegshaber", "Landsberg", "Kaufbeuren", "Rosenheim", "Freising", "Ingolstadt",
        "München|Münchner|Münchener", "Zugspitze", "Oberbayern",
    };

    /// <summary>Kreis und Bezirk — nur Suchbegriffe, kein Verein.</summary>
    public static readonly string[] BayernAreaPlaces = { "Zugspitze", "Oberbayern" };

    /// <summary>Lichess-Teams der Region Bayern (Recherche 07.10.2026; Jugend-Teams bewusst NICHT). Kreis/Bezirk zuerst — deren
    /// Mitglieder kommen in den Bestand, zählen aber nicht als „sein Verein" (<see cref="IsAreaTeam"/>).</summary>
    public static readonly string[] BayernTeams =
    {
        "schachkreis-zugspitze", "schachbezirk-oberbayern", "schachkreis-zugspitze-lounge", "schachkreis-ingolstadt-freising",
        "schachkreis-inn-chiemgau",
        "sk-weilheim-und-freunde", "schachclub-starnberg", "sk-graefelfing", "gautinger-sc", "schachklub-germering-ev",
        "grobes-schach", "windacher-chess-academy", "sc-ammersee", "schachfreunde-topschach-gilching-ev", "schachfreunde-bad-tolz",
        "tolzer-schachtiger", "tegernsee", "gemeinschaftsturniere-mit-tv-tegernsee", "schachabteilung-des-tus-furstenfeldbruck",
        "sc-garching-1980-ev", "sg-penzberg-geretsried", "tus-geretsried", "sg-wolfratshausen--geretsried",
        "sc-wolfratshausen-wolferines", "schachfreunde-munchen", "fc-bayern-munchen-schachabteilung-offenes-team",
        "sf-dachau-1932-ev", "sk-ingolstadt", "tsv-haunstetten", "schachfreunde-augsburg", "schachgesellschaft-augsburg-1873",
        "sk-kriegshaber", "sk-caissa-augsburg", "rochade-augsburg", "tsv-landsberg", "sc-1892-kaufbeuren",
        "rosenheimer-schachverein", "sk-freising", "schach-im-landkreis-miesbach-erwachsene",
    };

    private static readonly Regex Tmm2021 = new(@"\bT\s*O?\s*M\s*M\b.*2021|2021.*\bT\s*O?\s*M\s*M\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    /// <summary>Die Online-Ligen des Schachkreises Zugspitze und des Bezirks Oberbayern (2020/21–2021/22) — Serien nach
    /// <see cref="LeagueTeamScout.EventSeries"/>: „ZugLiga", „ObbLiga", „ZugspitzProbeLiga".</summary>
    private static readonly Regex BayernLeague = new(@"^(Zug|Obb|ZugspitzProbe)Liga$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static readonly LeagueOnlineRegion Tirol = new(LeagueRegions.Tirol, "AT", "Österreich", "Tiroler Lichess-Teams", "Tiroler Ort",
        TirolPlaces, Array.Empty<string>(), Array.Empty<string>(), Adjectives: false, "Online-TMM 2021", Tmm2021);

    public static readonly LeagueOnlineRegion Bayern = new(LeagueRegions.Bayern, "DE", "Deutschland", "Bayerische Lichess-Teams",
        "Bayerischer Ort", BayernPlaces, BayernAreaPlaces, BayernTeams, Adjectives: true, "Online-Liga Zugspitze/Oberbayern", BayernLeague);

    public static IReadOnlyList<LeagueOnlineRegion> All { get; } = [Tirol, Bayern];

    /// <summary>Die Region nach ihrer Kennung; unbekannt/<c>null</c> → Tirol (wie vor 0.712.0).</summary>
    public static LeagueOnlineRegion Of(string? region) => region == LeagueRegions.Bayern ? Bayern : Tirol;

    /// <summary>Die Regionen mit den Abweichungen aus der Konfiguration.</summary>
    public static IReadOnlyList<LeagueOnlineRegion> Configured(IConfiguration? config) => All.Select(r => Configured(r, config)).ToList();

    public static LeagueOnlineRegion Configured(LeagueOnlineRegion r, IConfiguration? config)
    {
        if (config is null) return r;
        static string[]? List(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var places = List(config[$"LeagueOnline:TeamPlaces:{r.Id}"])
                     ?? (r.Id == LeagueRegions.Tirol ? List(config["LeagueOnline:TeamPlaces"]) : null);
        var teams = List(config[$"LeagueOnline:Teams:{r.Id}"]);
        return places is null && teams is null ? r
            : r with { Places = places ?? r.Places, FixedTeams = teams ?? r.FixedTeams };
    }

    /// <summary>Kreis- oder Bezirks-Team („Schachkreis Zugspitze", „Schachbezirk Oberbayern", „Kreis Inn-Chiemgau") — kein Verein.</summary>
    public static bool IsAreaTeam(string? name) =>
        Regex.IsMatch(LeagueAccountFinder.Plain(name).ToLowerInvariant(), @"\b(schach)?(kreis|bezirk)\b");

    /// <summary>Jugend-Team oder -Wettbewerb (Minderjährige — nicht in den Bestand).</summary>
    public static bool IsYouth(string? s) => (s ?? "").Contains("jugend", StringComparison.OrdinalIgnoreCase);

    /// <summary>Regex für Orte in einem Profiltext: jede Umlaut-Schreibweise (ä/ae/a), mit <paramref name="adjectives"/> auch „…er".</summary>
    internal static Regex PlaceRegex(IEnumerable<string> places, bool adjectives)
    {
        static string Alt(string p)
        {
            var sb = new StringBuilder();
            foreach (var c in p.ToLowerInvariant())
                sb.Append(c switch
                {
                    'ä' => "(?:ä|ae|a)", 'ö' => "(?:ö|oe|o)", 'ü' => "(?:ü|ue|u)", 'ß' => "(?:ß|ss)",
                    _ => Regex.Escape(c.ToString()),
                });
            return sb.ToString();
        }
        var alts = places.SelectMany(p => p.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Select(Alt);
        return new Regex($@"\b(?:{string.Join("|", alts)}){(adjectives ? "(?:er)?" : "")}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Die laufende Saison JE REGION (jüngste Saison der Ligen dieser Region). Bis 0.710.0 nahmen Konto-Suche und Team-Suche die
    /// jüngste Saison ÜBERHAUPT — mit bayerischen Ligen (2026/27) vor den Tiroler (2025/26) fielen die Tiroler Spieler heraus.
    /// </summary>
    public static async Task<Dictionary<string, string>> CurrentSeasonsAsync(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.LeagueTournaments.AsNoTracking().Select(t => new { t.Source, t.Season }).Distinct().ToListAsync(ct);
        return rows.GroupBy(r => LeagueRegions.Of(r.Source))
            .ToDictionary(g => g.Key, g => g.Select(r => r.Season).Max(StringComparer.Ordinal)!);
    }

    /// <summary>Die Turniere der laufenden Saison jeder Region (<see cref="CurrentSeasonsAsync"/>).</summary>
    public static async Task<List<int>> CurrentSeasonTnrsAsync(AppDbContext db, CancellationToken ct)
    {
        var seasons = await CurrentSeasonsAsync(db, ct);
        var rows = await db.LeagueTournaments.AsNoTracking().Select(t => new { t.Tnr, t.Source, t.Season }).ToListAsync(ct);
        return rows.Where(t => seasons.TryGetValue(LeagueRegions.Of(t.Source), out var s) && s == t.Season).Select(t => t.Tnr).ToList();
    }
}
