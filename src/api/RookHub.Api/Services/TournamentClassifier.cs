using System.Text.RegularExpressions;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Ordnet einem Verzeichniseintrag Publikum und Format zu: Jugendklasse, Geschlechtsklasse und
/// „ist das eine Liga".
///
/// <para><b>Was aus der Quelle kommt und was aus dem Namen.</b> Einzel gegen Mannschaft steht in
/// der chess-results-Turniersuche als eigenes Feld (Turnierart 2/3 = Mannschaften) und wird
/// deshalb NICHT hier geraten, sondern im Sweep aus einer zweiten Abfrage gesetzt. Alter und
/// Geschlecht kennt die Suche gar nicht — es gibt keine Spalte dafuer, auch nicht auf der
/// Turnierseite. Bleibt der NAME, und der ist bei diesen beiden Merkmalen erstaunlich
/// verlaesslich, weil Ausschreibungen die Klasse fast immer im Titel fuehren („Landesmeisterschaft
/// U12 weiblich", „UNDER 16 GIRLS"). Ein Turnier ohne Merkmal im Namen gilt als offenes
/// Erwachsenenturnier — die haeufigste und die harmlosere Fehlannahme.</para>
///
/// <para><b>Die Wortlisten sind zum Nachruesten gedacht.</b> Manche Namen tragen ihr Publikum nur
/// als lokale Gewohnheit („Schachrallye Telfs" ist immer Nachwuchs). Solche Faelle kommen hier
/// dazu, sonst nirgends — und der Klassifizierer bleibt rein und statisch, damit jede Regel Zeile
/// fuer Zeile testbar ist.</para>
/// </summary>
public static class TournamentClassifier
{
    /// <summary>
    /// Jugendklasse im Namen — gesucht wird im NORMALISIERTEN Text, weil dort „U-14", „U 14" und
    /// „U14" zu „u 14" bzw. „u14" zusammenfallen. „UNDER 16" ist im englischsprachigen Raum die
    /// Regel, nicht die Ausnahme. Hoechstens zwei Ziffern: „U2000" ist eine Ratinggrenze, kein
    /// Alter, und die Wortgrenze am Ende weist sie ab.
    /// </summary>
    private static readonly Regex AgeClass = new(@"\b(?:u|under)\s?(\d{1,2})\b", RegexOptions.Compiled);

    /// <summary>
    /// Die Geschlechtsklasse als EINZELNER Buchstabe hinter der Altersklasse („U12w", „u16 m").
    /// Ein freistehendes „w"/„m" wird bewusst NICHT gewertet: „Open Braunau 2026 B" zeigt, dass
    /// einzelne Buchstaben in diesen Namen Gruppenkennungen sind.
    /// </summary>
    private static readonly Regex AgeClassGender =
        new(@"\b(?:u|under)\s?\d{1,2}\s?(w|m)\b", RegexOptions.Compiled);

    /// <summary>
    /// Jugendturnier ohne Klassenangabe („Landesjugendmeisterschaft", „Schuelerliga",
    /// „Schachrallye"). Ohne diese Liste zaehlte so ein Turnier als Erwachsenenturnier und stuende
    /// trotz des Schalters „nur Erwachsene" mitten in der Liste.
    /// </summary>
    private static readonly string[] YouthWords =
    [
        "jugend", "schueler", "schuler", "schulschach", "school", "nachwuchs", "kinder", "junior", "youth",
        "cadet", "kids", "scholar", "mladi", "kadet", "rallye", "rally", "minis",
    ];

    /// <summary>
    /// Seniorenklassen. Nach der Normalisierung ist aus „Ü50" ein „u50" geworden — dieselbe Form,
    /// die die Altersregex als Klasse ueber 20 verwirft, also faellt sie hier nicht durchs Netz.
    /// </summary>
    private static readonly string[] SeniorWords =
        ["senior", "veteran", "u50", "u60", "u65", "s50", "s60", "s65"];

    /// <summary>
    /// Frauen-/Maedchenklasse, als Teilzeichenkette gesucht — die deutschen Komposita heissen
    /// „Damenliga", „Maedchenmeisterschaft".
    /// </summary>
    private static readonly string[] FemaleParts =
    [
        "damen", "frauen", "maedchen", "madchen", "girl", "women", "weiblich", "female", "feminin",
        // Die Sprachen der angebundenen Verbandskalender. Ohne sie waere der Filter „nur Frauen"
        // fuer jedes italienische, slowenische, slowakische, tschechische und ungarische Turnier
        // blind — und das sind zusammen mehr Eintraege als der deutschsprachige Bestand.
        "femminile",                       // it
        "zenske", "zensk", "zenska",       // sl/sk/cs (normalisiert aus zenské/ženská)
        "dievcat", "divky", "dievca",      // sk/cs Maedchen
        "noi", "leany", "lany",            // hu (noi = Frauen, leany = Maedchen)
    ];

    private static readonly string[] MaleParts =
    [
        "herren", "knaben", "burschen", "boy", "maennlich", "mannlich", "muski",
        // Dieselben Sprachen. „maschile" enthaelt kein Frauenwort, „chlapc"/"fiu" ebenso nicht —
        // die Falle „men steckt in women" wiederholt sich hier also nicht.
        "maschile",                        // it
        "moski", "muzi", "muzsk",          // sl/sk/cs
        "chlapc", "hosi",                  // sk/cs Knaben
        "fiu", "ferfi",                    // hu (fiu = Knabe, ferfi = Herren)
    ];

    /// <summary>
    /// Diese beiden duerfen NUR als ganzes Wort zaehlen: „men" steckt in „women" UND in „Damen",
    /// „male" steckt in „female". Als Teilzeichenkette gesucht machte jedes Frauenturnier
    /// gleichzeitig zu einem Herrenturnier — und damit (beides gesetzt) wieder zu einem offenen.
    /// </summary>
    private static readonly string[] MaleExact = ["men", "male", "mens"];

    private static readonly string[] FemaleExact = ["dames"];

    /// <summary>
    /// Namensteile, die eine Liga anzeigen. Als Teilzeichenkette gesucht, nicht als Wort: die
    /// deutschen Komposita heissen „Landesliga", „Mannschaftsliga", „Landesklasse".
    /// </summary>
    private static readonly string[] LeagueWords =
    [
        "liga", "klasse", "mannschaftsmeisterschaft", "mannschaftskampf", "wettkampf",
        "team championship", "teamchampionship", "division", "ekipno",
        // Die Sprachen der angebundenen Kalender. „ligy"/"lige" sind Beugungen von liga und
        // werden von „liga" NICHT getroffen; „csapat" ist ungarisch fuer Mannschaft.
        "ligy", "lige", "campionato a squadre", "squadre", "druzstev", "druzstiev", "csapat",
    ];

    /// <summary>
    /// Ab wie vielen Tagen ein MANNSCHAFTSturnier als Liga gilt. Ein Mannschaftsturnier am
    /// Wochenende dauert einen bis drei Tage; eine Saison laeuft von Oktober bis April. Fuenf
    /// Wochen liegen weit genug von beidem entfernt, dass die Grenze nicht wackelt.
    /// </summary>
    internal const int LeagueSeasonDays = 35;

    /// <summary>Alle Jugendklassen zusammen — der Gegenbegriff zu „Erwachsenenturnier".</summary>
    public const TournamentAgeGroups YouthMask =
        TournamentAgeGroups.U8 | TournamentAgeGroups.U10 | TournamentAgeGroups.U12
        | TournamentAgeGroups.U14 | TournamentAgeGroups.U16 | TournamentAgeGroups.U18
        | TournamentAgeGroups.U20 | TournamentAgeGroups.YouthUnspecified;

    public static TournamentAgeGroups AgeGroupsOf(string? name)
    {
        var normalized = GeoTextNormalizer.Normalize(name);
        if (normalized.Length == 0) return TournamentAgeGroups.None;

        var groups = TournamentAgeGroups.None;
        foreach (Match match in AgeClass.Matches(normalized))
            groups |= ClassFor(int.Parse(match.Groups[1].Value));

        if (Mentions(normalized, SeniorWords)) groups |= TournamentAgeGroups.Senior;

        // Ein Wort wie „Jugend" traegt keine Klasse — es sagt nur „nicht erwachsen". Steht schon
        // eine konkrete Klasse da, waere die zusaetzliche Marke bloss Rauschen.
        if ((groups & YouthMask) == 0 && Mentions(normalized, YouthWords))
            groups |= TournamentAgeGroups.YouthUnspecified;

        return groups;
    }

    /// <summary>
    /// Eine Jahreszahl auf die naechste offizielle Klasse aufrunden: „U9" und „U11" gibt es als
    /// Ausschreibung, in der Filterleiste aber nicht. Alles ueber 20 ist keine Jugendklasse mehr
    /// („U25" ist ein Studententurnier, „u50" eine Seniorenklasse).
    /// </summary>
    internal static TournamentAgeGroups ClassFor(int years) => years switch
    {
        <= 8 => TournamentAgeGroups.U8,
        <= 10 => TournamentAgeGroups.U10,
        <= 12 => TournamentAgeGroups.U12,
        <= 14 => TournamentAgeGroups.U14,
        <= 16 => TournamentAgeGroups.U16,
        <= 18 => TournamentAgeGroups.U18,
        <= 20 => TournamentAgeGroups.U20,
        _ => TournamentAgeGroups.None,
    };

    public static TournamentGender GenderOf(string? name)
    {
        var normalized = GeoTextNormalizer.Normalize(name);
        if (normalized.Length == 0) return TournamentGender.Open;

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var female = Mentions(normalized, FemaleParts) || words.Any(FemaleExact.Contains);
        var male = Mentions(normalized, MaleParts) || words.Any(MaleExact.Contains);

        var suffix = AgeClassGender.Match(normalized);
        if (suffix.Success)
        {
            if (suffix.Groups[1].Value == "w") female = true;
            else male = true;
        }

        // „Damen und Herren" ist ein gemeinsames Turnier, keine Klasse — beides gesetzt heisst
        // also offen, nicht „irgendwie beides".
        if (female == male) return TournamentGender.Open;
        return female ? TournamentGender.Female : TournamentGender.Male;
    }

    /// <summary>
    /// Ist das eine Liga? Zwei unabhaengige Wege, weil chess-results Ligen auf zwei Arten fuehrt:
    /// die ganze Saison als EIN Eintrag (Oktober bis April — daher die Dauer) oder jede Runde als
    /// eigenen Eintrag, der nur noch am Namen zu erkennen ist („Landesliga Runde 3").
    ///
    /// <para>Die Dauer zaehlt nur bei MANNSCHAFTSturnieren: ein monatelanges Einzelturnier ist
    /// eine Vereinsmeisterschaft, keine Liga, und fiele sonst faelschlich unter den Schalter.</para>
    /// </summary>
    public static bool LooksLikeLeague(string? name, TournamentKind kind, DateOnly? start, DateOnly? end)
    {
        if (Mentions(GeoTextNormalizer.Normalize(name), LeagueWords)) return true;

        return kind == TournamentKind.Team
               && start is { } from && end is { } to
               && to.DayNumber - from.DayNumber >= LeagueSeasonDays;
    }

    private static bool Mentions(string normalized, string[] words) =>
        normalized.Length > 0 && words.Any(w => normalized.Contains(w, StringComparison.Ordinal));
}
