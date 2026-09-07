using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Woher die Koordinaten eines Verzeichniseintrags stammen. Steht mit in der Zeile, damit der
/// Admin-Report die Qualitaet sichtbar machen kann: eine Postleitzahl trifft den Ort, ein
/// Bundesland-Zentroid liegt gerne 80 km daneben und darf eine Umkreissuche nicht wie ein
/// exakter Treffer aussehen.
/// </summary>
public enum GeoSource
{
    None = 0,
    PostalCode = 1,
    City = 2,
    Region = 3,
    Manual = 4,
    Nominatim = 5,
    /// <summary>
    /// Ein Ortsname wurde gefunden, war aber MEHRDEUTIG — der Name gibt es an mehreren, weit
    /// auseinanderliegenden Stellen, und keine Zusatzinformation entscheidet. Gemessen auf dem
    /// Dev-Stand: 171 Eintraege, davon 29 mit einem nachweislich falschen Pin (bis 489 km
    /// daneben). Solche Eintraege bekommen deshalb KEINE Koordinaten und landen in der
    /// Admin-Arbeitsliste — ein Pin, der Genauigkeit behauptet und sie nicht hat, ist schlimmer
    /// als kein Pin.
    /// </summary>
    Ambiguous = 6,
    /// <summary>
    /// Ueber die VEREINSNAMEN des Turniers entschieden. chess-results kuerzt den Spielort ab
    /// („St.Veit"), und diese Abkuerzung kann zufaellig exakt der Name eines ANDEREN Ortes sein —
    /// „St. Veit" liegt in Tirol, gemeint war „St. Veit an der Glan" in Kaernten. Aus dem Namen
    /// allein ist das nicht zu erkennen; die Vereinsnamen tragen die Unterscheidung mit
    /// („SV ASKOE St. Veit/Glan"). Siehe <c>VenueDisambiguationService</c>.
    /// </summary>
    TeamHint = 7,
}

/// <summary>
/// Ein Turnier aus der chess-results-Turniersuche. Gefuellt vom naechtlichen Sweep, NICHT vom
/// Einzelturnier-Crawl: hier steht nur, DASS und WO ein Turnier stattfindet. Die Teilnehmer- und
/// Paarungsdaten liegen weiterhin in der Crawler-DB und werden erst geholt, wenn jemand das
/// Turnier importiert oder abonniert.
/// </summary>
public class TournamentDirectoryEntry
{
    public int Id { get; set; }

    /// <summary>
    /// Die IDENTITAET dieses Eintrags — der Schluessel in der Adresse
    /// (<c>/tournaments/calendar/{id}</c>), im Ausblenden, in der Meldung und im Teilen-Link.
    ///
    /// <para><b>Warum es diese Spalte gibt.</b> Bis 0.427.0 war das die
    /// chess-results-Nummer, weil es nur diese eine Quelle gab. Seit dem FIDE-Kalender gibt es
    /// Turniere OHNE chess-results-Nummer — und die brauchen trotzdem eine Adresse. Fuer
    /// chess-results-Eintraege ist der Wert identisch mit <see cref="ChessResultsId"/> (bewusst
    /// doppelt gehalten: so bleibt jede Abfrage einfach, und 20 Zeichen auf fuenftausend Zeilen
    /// sind kein Preis), fuer FIDE-Eintraege lautet er <c>f&lt;Ereignisnummer&gt;</c>.</para>
    /// </summary>
    [Required, MaxLength(24)]
    public string PublicId { get; set; } = string.Empty;

    /// <summary>
    /// chess-results-dbkey, identisch mit der ID in tnr&lt;id&gt;.aspx — <c>null</c>, wenn das
    /// Turnier dort nicht ausgeschrieben ist.
    ///
    /// <para>Daran haengt alles, was chess-results BRAUCHT: das Turnier holen (Crawl-Auftrag),
    /// es merken (das Abo traegt dieselbe Nummer), die Vereinsnamen zur Ortsaufloesung und den
    /// Rundenplan. Ein FIDE-Eintrag kann das alles nicht, und die Anzeige muss es sagen statt
    /// Knoepfe anzubieten, die ins Leere fuehren.</para>
    /// </summary>
    [MaxLength(20)]
    public string? ChessResultsId { get; set; }

    [Required, MaxLength(500)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Der Name ohne Gruppen-Kuerzel („Open Braunau 2026" fuer die Gruppen A/B/C). Wird angezeigt,
    /// wenn mehrere Gruppen zu einem Eintrag zusammengefasst sind.
    /// </summary>
    [MaxLength(500)]
    public string? BaseName { get; set; }

    /// <summary>
    /// Klammert die Gruppen EINES Turniers zusammen: Hash aus Basisname, Foederation, Termin und
    /// Ort. chess-results fuehrt jede Gruppe als eigene Zeile mit eigenem dbkey — ohne diesen
    /// Schluessel steht ein viergruppiges Open viermal in der Liste. Vorberechnet, damit die
    /// Datenbank danach gruppieren kann, ohne jede Zeile zu laden.
    /// </summary>
    [MaxLength(32)]
    public string? GroupKey { get; set; }

    /// <summary>FIDE-Foederationscode (AUT, GER, ...) - zugleich der Sweep-Schluessel.</summary>
    [MaxLength(3)]
    public string? Federation { get; set; }

    /// <summary>Bundesland/Region, wie chess-results sie meldet. Fallback fuers Geocoding.</summary>
    [MaxLength(100)]
    public string? State { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Beginnt das Turnier an einem Samstag oder Sonntag? Beim Schreiben ausgerechnet, weil
    /// <c>DateOnly.DayOfWeek</c> vom MySQL-Provider nicht verlaesslich uebersetzt wird - der Filter
    /// muss aber in SQL laufen, sonst zerbricht die Seitennavigation daran.
    /// </summary>
    public bool StartsOnWeekend { get; set; }

    /// <summary>Spielort als Freitext ("Rifer Hauptstrasse 37 5400 Hallein"). Quelle des Geocodings.</summary>
    [MaxLength(500)]
    public string? LocationText { get; set; }

    [MaxLength(300)]
    public string? TimeControlText { get; set; }

    /// <summary>Aus <see cref="TimeControlText"/> geraten - chess-results liefert keine Kategorie.</summary>
    public TournamentSpeed Speed { get; set; } = TournamentSpeed.Unknown;

    [MaxLength(300)]
    public string? Organizer { get; set; }

    [MaxLength(300)]
    public string? Director { get; set; }

    [MaxLength(300)]
    public string? ChiefArbiter { get; set; }

    public int? Rounds { get; set; }
    public int? PlayerCount { get; set; }

    /// <summary>
    /// Naeherung aus der relativen "Last update"-Angabe. Grob gerundet - taugt zum Sortieren
    /// ("zuletzt geaendert"), nicht als exakter Zeitstempel und nicht zur Aenderungserkennung
    /// (dafuer gibt es <see cref="ChangeHash"/>).
    /// </summary>
    public DateTime? UpstreamUpdatedAt { get; set; }

    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public GeoSource GeoSource { get; set; } = GeoSource.None;

    /// <summary>Der Ort, auf den das Geocoding gefallen ist - fuer die Anzeige "ungefaehr bei ...".</summary>
    [MaxLength(200)]
    public string? GeoPlaceName { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Wie viele Sweeps in Folge diesen Eintrag NICHT mehr geliefert haben. Erst ab zwei gilt er
    /// als abgesagt: ein einzelner fehlgeschlagener oder unvollstaendiger Sweep wuerde sonst
    /// reihenweise Absage-Meldungen ausloesen.
    /// </summary>
    public int MissedSweeps { get; set; }

    /// <summary>Gesetzt, sobald der Eintrag als verschwunden gilt. Bleibt in der Tabelle stehen.</summary>
    public DateTime? RemovedAt { get; set; }

    /// <summary>
    /// Hash ueber die Felder, deren Aenderung eine Benachrichtigung wert ist (Start, Ende, Ort).
    /// Bewusst NICHT ueber alle Felder: eine wachsende Meldeliste ist keine Terminaenderung.
    /// </summary>
    [MaxLength(64)]
    public string? ChangeHash { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Wann zuletzt versucht wurde, den Spielort ueber die Vereinsnamen aufzuloesen. Ohne diesen
    /// Vermerk holte der naechtliche Durchgang immer wieder dieselben Turnierseiten.
    /// </summary>
    public DateTime? TeamHintCheckedAt { get; set; }

    /// <summary>
    /// Einzel- oder Mannschaftsturnier. Kommt AUS DER QUELLE: die chess-results-Turniersuche hat
    /// ein Turnierart-Feld, und die Arten 2/3 („Rundenturnier/Schweizer System fuer
    /// Mannschaften") sind genau die Mannschaftsturniere. Der Sweep fragt sie deshalb in einem
    /// zweiten Durchgang gezielt ab, statt am Namen zu raten. <c>Unknown</c> heisst „noch nicht
    /// geklaert" — und nur das; ein fehlgeschlagener Klassifizierungs-Durchgang darf eine schon
    /// bekannte Art nicht auf „Einzel" zuruecksetzen.
    /// </summary>
    public TournamentKind Kind { get; set; } = TournamentKind.Unknown;

    /// <summary>
    /// Das TURNIERSYSTEM — Schweizer System oder Rundenturnier.
    ///
    /// <para>Kommt wie <see cref="Kind"/> aus der Quelle und aus DENSELBEN Abfragen: die vier
    /// chess-results-Turnierarten sind das Kreuzprodukt aus beidem (0 Schweizer Einzel,
    /// 1 Rundenturnier Einzel, 2 Rundenturnier Mannschaft, 3 Schweizer Mannschaft). Wer die vier
    /// Durchgaenge ohnehin macht, bekommt das System geschenkt.</para>
    ///
    /// <para>Bei FIDE-Eintraegen stammt es aus dem Feld „Tournament system" des Detail-Fragments
    /// und kann dort auch <see cref="TournamentSystem.Other"/> sein (so steht die
    /// 46. Schacholympiade da). <c>Unknown</c> heisst wie ueberall „noch nicht geklaert".</para>
    /// </summary>
    public TournamentSystem System { get; set; } = TournamentSystem.Unknown;

    /// <summary>
    /// Liga (Saisonwettbewerb) statt Turnier. Abgeleitet, nicht gemeldet — siehe
    /// <see cref="Services.TournamentClassifier.LooksLikeLeague"/>. Steht als eigene Spalte, damit
    /// der Schalter „Ligen ausblenden" in SQL filtern kann.
    /// </summary>
    public bool IsLeague { get; set; }

    /// <summary>
    /// Jugend-/Seniorenklassen aus dem Namen. Ein Turnier kann mehrere fuehren („Landesmeisterschaft
    /// U8-U18"), daher ein Bitfeld. <c>None</c> = kein Merkmal im Namen = offenes
    /// Erwachsenenturnier.
    /// </summary>
    public TournamentAgeGroups AgeGroups { get; set; } = TournamentAgeGroups.None;

    /// <summary>Geschlechtsklasse aus dem Namen; <c>Open</c> ist der Normalfall.</summary>
    public TournamentGender Gender { get; set; } = TournamentGender.Open;

    /// <summary>
    /// ALLE Spielorte. Bei Ligen nennt chess-results mehrere („Mayrhofen, St.Veit"); die
    /// Koordinaten am Eintrag selbst sind der ERSTE davon (siehe <see cref="TournamentDirectoryVenue"/>).
    /// </summary>
    public List<TournamentDirectoryVenue> Venues { get; set; } = [];

    /// <summary>
    /// Die einzelnen SPIELTERMINE, wenn sie bekannt sind. Bei einer Liga ist das der Unterschied
    /// zwischen „elf Spieltagen" und „200 Tagen Kalenderrauschen" — siehe
    /// <see cref="TournamentDirectoryRound"/>. Leer heisst „nicht abgefragt oder nicht
    /// hinterlegt": dann gilt der ganze Zeitraum.
    /// </summary>
    public List<TournamentDirectoryRound> RoundDates { get; set; } = [];

    /// <summary>
    /// Wann zuletzt versucht wurde, den Rundenplan zu holen. Ohne diesen Vermerk holte der
    /// naechtliche Durchgang immer wieder dieselben Seiten — und die meisten Turniere haben gar
    /// keinen Plan hinterlegt, der Fehlversuch ist also der Normalfall und muss sich merken
    /// lassen. Wird beim Sweep geleert, sobald sich der Termin des Turniers geaendert hat.
    /// </summary>
    public DateTime? RoundPlanCheckedAt { get; set; }

    /// <summary>
    /// Wann zuletzt versucht wurde, die FIDE-Detailangaben zu holen. Derselbe Gedanke wie bei
    /// <see cref="RoundPlanCheckedAt"/>: ein Abruf je Ereignis, und ein Ereignis ohne gepflegte
    /// Angaben ist kein Fehler, sondern ein Ergebnis, das sich merken lassen muss. Nur fuer
    /// Eintraege mit einer FIDE-Herkunft ueberhaupt gesetzt.
    /// </summary>
    public DateTime? FideDetailCheckedAt { get; set; }

    /// <summary>
    /// Auf welchen Seiten dieses Turnier gefunden wurde. Dasselbe Turnier steht auf mehreren, und
    /// es werden mehr — siehe <see cref="TournamentDirectorySource"/>.
    /// </summary>
    public List<TournamentDirectorySource> Sources { get; set; } = [];
}

/// <summary>Bedenkzeit-Kategorie, aus dem Freitext geraten.</summary>
public enum TournamentSpeed
{
    Unknown = 0,
    Standard = 1,
    Rapid = 2,
    Blitz = 3,
}

/// <summary>Einzel- oder Mannschaftsturnier, wie die chess-results-Turniersuche es fuehrt.</summary>
public enum TournamentKind
{
    /// <summary>Noch nicht geklaert (Altbestand, oder der Klassifizierungs-Durchgang fiel aus).</summary>
    Unknown = 0,
    Individual = 1,
    Team = 2,
}

/// <summary>
/// Das Turniersystem. Bewusst getrennt von <see cref="TournamentKind"/>: chess-results fuehrt
/// beides als EIN Feld (vier Arten = Kreuzprodukt), aber es sind zwei Fragen — „spielen
/// Mannschaften?" und „jeder gegen jeden oder Schweizer System?".
/// </summary>
public enum TournamentSystem
{
    /// <summary>Noch nicht geklaert — nicht „keins".</summary>
    Unknown = 0,
    Swiss = 1,
    RoundRobin = 2,

    /// <summary>
    /// Etwas anderes, und die Quelle sagt es ausdruecklich. Gibt es nur bei FIDE, wo das Feld
    /// „Tournament system" diesen Wert kennt (die 46. Schacholympiade steht so da) — und
    /// unterscheidet sich von <see cref="Unknown"/> genau darin, dass nachgesehen wurde.
    /// </summary>
    Other = 3,
}

/// <summary>
/// Alters-/Nachwuchsklassen eines Turniers. Bitfeld, weil eine Ausschreibung mehrere Klassen an
/// einem Termin fuehren kann. <c>Senior</c> ist ausdruecklich KEINE Jugendklasse — Seniorenschach
/// ist Erwachsenenschach und bleibt beim Schalter „nur Erwachsene" sichtbar.
/// </summary>
[Flags]
public enum TournamentAgeGroups
{
    None = 0,
    U8 = 1,
    U10 = 2,
    U12 = 4,
    U14 = 8,
    U16 = 16,
    U18 = 32,
    U20 = 64,
    /// <summary>„Jugendmeisterschaft", „Schachrallye" — Nachwuchs ohne genannte Klasse.</summary>
    YouthUnspecified = 128,
    Senior = 256,
}

/// <summary>Geschlechtsklasse eines Turniers.</summary>
public enum TournamentGender
{
    /// <summary>Kein Merkmal im Namen — der Normalfall, offen fuer alle.</summary>
    Open = 0,
    Female = 1,
    Male = 2,
}
