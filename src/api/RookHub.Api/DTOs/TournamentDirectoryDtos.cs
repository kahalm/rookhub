using System.ComponentModel.DataAnnotations;
using RookHub.Api.Models;

namespace RookHub.Api.DTOs;

/// <summary>Ein Verzeichniseintrag, wie ihn Liste, Kalender und Detailansicht brauchen.</summary>
public class DirectoryEntryDto
{
    public string ChessResultsId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Federation { get; set; }
    public string? State { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Location { get; set; }
    public string? TimeControl { get; set; }
    public string Speed { get; set; } = nameof(TournamentSpeed.Unknown);
    public string? Organizer { get; set; }
    public string? Director { get; set; }
    public string? ChiefArbiter { get; set; }
    public int? Rounds { get; set; }
    public int? PlayerCount { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    /// <summary>Herkunft der Koordinaten - "Region" heisst: nur ungefaehr, Bundesland-Mittelpunkt.</summary>
    public string GeoSource { get; set; } = nameof(Models.GeoSource.None);
    public string? GeoPlaceName { get; set; }
    /// <summary>Entfernung zum Suchmittelpunkt in km; nur bei einer Umkreissuche gesetzt.</summary>
    public double? DistanceKm { get; set; }
    public bool Cancelled { get; set; }
    public bool Subscribed { get; set; }

    /// <summary>
    /// Wie viele Gruppen desselben Turniers dieser Eintrag zusammenfasst (1 = einzelnes Turnier).
    /// chess-results fuehrt „Open Braunau 2026 A/B/C" als drei Zeilen; hier ist es eine.
    /// </summary>
    public int GroupSize { get; set; } = 1;

    /// <summary>Die Gruppen mit ihrer Beschriftung — leer, wo chess-results den Zusatz abschnitt.</summary>
    public List<DirectoryGroupMemberDto> Groups { get; set; } = [];

    /// <summary>
    /// ALLE Spielorte, sobald es mehr als einen gibt (Ligen: „Mayrhofen, St.Veit"). Leer bei einem
    /// einzelnen Ort — der steht dann in Lat/Lon/GeoPlaceName. Die Karte zeichnet je Ort einen
    /// Punkt, sonst verschwaende ein Turnier die Haelfte seiner Orte.
    /// </summary>
    public List<DirectoryVenueDto> Venues { get; set; } = [];

    /// <summary>„Individual", „Team" oder „Unknown" (noch nicht geklaert) — aus der Quelle.</summary>
    public string Kind { get; set; } = nameof(TournamentKind.Unknown);

    /// <summary>Saisonwettbewerb statt Turnier — abgeleitet, siehe TournamentClassifier.</summary>
    public bool IsLeague { get; set; }

    /// <summary>
    /// Alters-/Nachwuchsklassen als Namen („U12", „U14", „YouthUnspecified", „Senior") — aus dem
    /// Turniernamen gelesen. Leer = offenes Erwachsenenturnier.
    /// </summary>
    public List<string> AgeGroups { get; set; } = [];

    /// <summary>„Open", „Female" oder „Male".</summary>
    public string Gender { get; set; } = nameof(TournamentGender.Open);

    /// <summary>
    /// Die einzelnen SPIELTERMINE, wenn sie bekannt sind. Bei einer Liga sind das elf Tage von
    /// September bis April — nicht die 200 Tage dazwischen. Leer heisst „nicht bekannt": dann
    /// gilt der Zeitraum von <see cref="StartDate"/> bis <see cref="EndDate"/>.
    /// </summary>
    public List<DirectoryRoundDto> RoundDates { get; set; } = [];

    public static DirectoryEntryDto FromEntity(
        TournamentDirectoryEntry e, double? distanceKm = null, bool subscribed = false,
        IReadOnlyList<TournamentDirectoryEntry>? groups = null) => new()
    {
        ChessResultsId = e.ChessResultsId,
        // Bei mehreren Gruppen der Name OHNE Kuerzel — „Open Braunau 2026" statt „… A".
        Name = groups is { Count: > 1 } ? (e.BaseName ?? e.Name) : e.Name,
        Federation = e.Federation,
        State = e.State,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        Location = e.LocationText,
        TimeControl = e.TimeControlText,
        Speed = e.Speed.ToString(),
        Organizer = e.Organizer,
        Director = e.Director,
        ChiefArbiter = e.ChiefArbiter,
        Rounds = e.Rounds,
        Lat = e.Lat,
        Lon = e.Lon,
        GeoSource = e.GeoSource.ToString(),
        GeoPlaceName = e.GeoPlaceName,
        DistanceKm = distanceKm is null ? null : Math.Round(distanceKm.Value, 1),
        Cancelled = e.RemovedAt != null,
        Subscribed = subscribed,
        Kind = e.Kind.ToString(),
        RoundDates = e.RoundDates
            .OrderBy(r => r.Number)
            .Select(r => new DirectoryRoundDto { Round = r.Number, Date = r.Date, Time = r.TimeText })
            .ToList(),
        IsLeague = e.IsLeague,
        AgeGroups = AgeGroupNames(e.AgeGroups),
        Gender = e.Gender.ToString(),
        GroupSize = groups?.Count ?? 1,
        // Die Teilnehmerzahl der Gruppen summiert sich — sie ist die Groesse des GANZEN Turniers.
        PlayerCount = groups is { Count: > 1 } ? groups.Sum(g => g.PlayerCount ?? 0) : e.PlayerCount,
        Venues = e.Venues
            .OrderBy(v => v.Ordinal)
            .Select(v => new DirectoryVenueDto
            {
                Name = v.Name, Lat = v.Lat, Lon = v.Lon, GeoSource = v.GeoSource.ToString(),
            })
            .ToList(),
        Groups = groups is { Count: > 1 }
            ? groups.Select(g => new DirectoryGroupMemberDto
            {
                ChessResultsId = g.ChessResultsId,
                Label = Services.TournamentNameGrouping.GroupLabel(g.Name),
                PlayerCount = g.PlayerCount,
                Rounds = g.Rounds,
            }).ToList()
            : [],
    };

    /// <summary>
    /// Das Bitfeld als Namensliste. Bewusst nicht als Zahl: ein Frontend, das „4" bekommt, muss
    /// die Bit-Belegung nachbauen — und stimmt dann irgendwann nicht mehr mit dem Server ueberein.
    /// </summary>
    internal static List<string> AgeGroupNames(TournamentAgeGroups groups) =>
        Enum.GetValues<TournamentAgeGroups>()
            .Where(g => g != TournamentAgeGroups.None && groups.HasFlag(g))
            .Select(g => g.ToString())
            .ToList();
}

/// <summary>Ein Spieltermin eines Turniers — eine Runde mit ihrem Datum.</summary>
public class DirectoryRoundDto
{
    public int Round { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>Uhrzeit als Rohtext („14:00 Uhr").</summary>
    public string? Time { get; set; }
}

/// <summary>Ein einzelner Spielort eines Turniers mit mehreren.</summary>
public class DirectoryVenueDto
{
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string GeoSource { get; set; } = "";
}

/// <summary>Eine Gruppe (A/B/C) innerhalb eines zusammengefassten Turniers.</summary>
public class DirectoryGroupMemberDto
{
    public string ChessResultsId { get; set; } = "";
    /// <summary>„A", „Gruppe 2" — leer, wenn chess-results den Zusatz im Namen abgeschnitten hat.</summary>
    public string Label { get; set; } = "";
    public int? PlayerCount { get; set; }
    public int? Rounds { get; set; }
}

public class DirectoryPageDto
{
    public List<DirectoryEntryDto> Items { get; set; } = [];
    public int Total { get; set; }
    /// <summary>true, wenn der Umkreis-Vorfilter die Obergrenze erreicht hat - Radius verkleinern.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// Ein Kalendermonat: die Turniere EINMAL, die Tage nur mit ihren Nummern.
///
/// <para>Vorher stand an jedem Tag der VOLLE Eintrag. Ein mehrtaegiges Turnier steht an jedem
/// seiner Tage, und ein Monat auf dem Dev-Server hatte damit 5962 Eintraege fuer 200 verschiedene
/// Turniere — 3 MB JSON, von denen 97 % Wiederholung waren. Der Kalender ist die Startseite der
/// Turnierseite, das war also der Aufbau JEDES Aufrufs.</para>
/// </summary>
public class DirectoryCalendarDto
{
    /// <summary>Jedes Turnier des Monats genau einmal.</summary>
    public List<DirectoryEntryDto> Tournaments { get; set; } = [];
    public List<DirectoryCalendarDayDto> Days { get; set; } = [];

    /// <summary>
    /// true = der Monat hat mehr Turniere, als diese Antwort traegt. Ohne diese Angabe faellt ein
    /// Deckel niemandem auf: der Kalender saehe schlicht vollstaendig aus.
    /// </summary>
    public bool Truncated { get; set; }
}

/// <summary>Ein Tag im Kalender mit den Nummern der an diesem Tag LAUFENDEN Turniere.</summary>
public class DirectoryCalendarDayDto
{
    public DateOnly Date { get; set; }
    public List<string> Ids { get; set; } = [];
}

public class DirectorySearchProfileDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? PlaceQuery { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public int RadiusKm { get; set; }
    public List<string> Federations { get; set; } = [];
    public List<string> Speeds { get; set; } = [];
    public bool WeekendOnly { get; set; }
    public int? MinPlayers { get; set; }
    public bool NotifyNew { get; set; }
    public int SortOrder { get; set; }

    public static DirectorySearchProfileDto FromEntity(TournamentSearchProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        PlaceQuery = p.PlaceQuery,
        Lat = p.Lat,
        Lon = p.Lon,
        RadiusKm = p.RadiusKm,
        Federations = Services.TournamentDirectoryService.SplitCsv(p.Federations),
        Speeds = Services.TournamentDirectoryService.SplitCsv(p.Speeds),
        WeekendOnly = p.WeekendOnly,
        MinPlayers = p.MinPlayers,
        NotifyNew = p.NotifyNew,
        SortOrder = p.SortOrder,
    };
}

public class SearchProfileInputDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(200)]
    public string? PlaceQuery { get; set; }

    [Range(-90, 90)]
    public double Lat { get; set; }

    [Range(-180, 180)]
    public double Lon { get; set; }

    [Range(1, 2000)]
    public int RadiusKm { get; set; } = 100;

    public List<string>? Federations { get; set; }
    public List<string>? Speeds { get; set; }
    public bool WeekendOnly { get; set; }

    [Range(0, 10000)]
    public int? MinPlayers { get; set; }

    public bool NotifyNew { get; set; } = true;

    [Range(0, 1000)]
    public int SortOrder { get; set; }
}

/// <summary>Ein Ortsvorschlag fuer das Suchprofil-Formular.</summary>
public class GeoPlaceSuggestionDto
{
    public string Label { get; set; } = "";
    public string Country { get; set; } = "";
    public string? PostalCode { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
}

/// <summary>
/// Die Publikums- und Formatfilter der Filterleiste, gebuendelt als EIN Bindungs-Objekt.
///
/// <para>Sie gehoeren an alle drei Ansichten (Liste, Karte, Kalender) und waeren dort sonst
/// fuenfmal drei Parameter — die Signaturen sind schon lang. Gebunden wird aus der Query
/// (<c>?kinds=team&amp;ageGroups=U12,U14&amp;genders=female&amp;adultsOnly=true&amp;hideLeagues=true</c>);
/// Listen kommen kommagetrennt, wie ueberall in diesem Verzeichnis.</para>
/// </summary>
public class DirectoryAudienceQuery
{
    /// <summary>„individual", „team", „unknown" — kommagetrennt. Leer = alles.</summary>
    public string? Kinds { get; set; }

    /// <summary>„u8", „u10", … „senior" — kommagetrennt. Leer = keine Einschraenkung.</summary>
    public string? AgeGroups { get; set; }

    /// <summary>„open", „female", „male" — kommagetrennt.</summary>
    public string? Genders { get; set; }

    /// <summary>Nur Turniere ohne Jugendmerkmal im Namen.</summary>
    public bool AdultsOnly { get; set; }

    /// <summary>Saisonwettbewerbe ausblenden.</summary>
    public bool HideLeagues { get; set; }
}

/// <summary>
/// „Dieses Turnier ist falsch eingeordnet" — die Meldung eines Nutzers. JEDES Feld ist freiwillig:
/// wer nur auf den Knopf drueckt, meldet „hier stimmt etwas nicht", und auch das ist brauchbar.
///
/// Strukturierte Vorschlagsfelder (Ort, Art, Klasse, Bedenkzeit, Liga) standen hier einmal und
/// sind wieder weg: sie verlangten vom Melder genau die Wertetabelle, die er nicht kennen muss,
/// und machten aus einer Rueckmeldung ein Formular. Ein Satz Freitext sagt dasselbe besser —
/// den IST-Stand stellt der Server ohnehin daneben.
/// </summary>
public class DirectoryReportDto
{
    /// <summary>Was aus Sicht des Melders falsch ist.</summary>
    [MaxLength(2000)]
    public string? Message { get; set; }

    /// <summary>
    /// „Wie heissen solche Turniere bei euch?" — die wertvollste Frage des ganzen Formulars.
    ///
    /// <para>Alter und Publikum eines Turniers stehen nur im Namen, und die Namen sind REGIONAL:
    /// in Tirol heisst das Nachwuchsturnier „Schachrallye", woanders anders. Solche Kennungen kann
    /// niemand von aussen erraten — wer sie einmal nennt, verbessert die Einordnung fuer alle
    /// kuenftigen Ausschreibungen derselben Reihe (Wortliste im TournamentClassifier).</para>
    /// </summary>
    [MaxLength(200)]
    public string? NamePattern { get; set; }

    /// <summary>
    /// Eine Seite, auf der die richtige Einordnung besser ersichtlich ist (Ausschreibung,
    /// Verbandskalender). Oft schneller als jede Erklaerung im Freitext.
    /// </summary>
    [MaxLength(500)]
    public string? SourceLink { get; set; }
}

/// <summary>
/// „Mein Turnier fehlt hier" — der Hinweis auf eine Quelle, die noch nicht gecrawlt wird. Der
/// LINK ist das Pflichtfeld: eine Verbands- oder Vereinsseite laesst sich zusaetzlich auswerten,
/// eine Aufzaehlung einzelner Termine im Freitext nicht.
/// </summary>
public class DirectorySourceSuggestionDto
{
    /// <summary>Moeglichst offizielle Seite, auf der die Turniere dieses Veranstalters stehen.</summary>
    [Required, MaxLength(500)]
    public string? Link { get; set; }

    [MaxLength(2000)]
    public string? Message { get; set; }
}
