using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>Eine Turnierseite, von der ein Verzeichniseintrag stammt.</summary>
public enum DirectorySourceKind
{
    /// <summary>Altbestand ohne Herkunftsvermerk.</summary>
    Unknown = 0,

    /// <summary>chess-results.com — die Turniersuche, aus der das Verzeichnis heute lebt.</summary>
    ChessResults = 1,

    /// <summary>
    /// calendar.fide.com. Technisch abfragbar (POST <c>calendar_server.php</c> mit
    /// <c>show=apilist</c> liefert JSON), inhaltlich am 2026-09-07 aber wertlos: 661 Ereignisse,
    /// ALLE aus 2025, nichts in der Zukunft. Der Wert steht hier, damit die Quelle einen Namen
    /// hat, sobald sie wieder gepflegt wird.
    /// </summary>
    Fide = 2,

    /// <summary>Von Hand eingetragen — etwa nach einem Hinweis ueber „mein Turnier fehlt".</summary>
    Manual = 3,

    /// <summary>
    /// Der ANKUENDIGUNGS-Kalender von chess-results (<c>Kalender.aspx</c>) — dieselbe Seite wie
    /// <see cref="ChessResults"/>, aber ein anderer Datenbestand.
    ///
    /// <para>Die Turniersuche fuellt sich, wenn der Veranstalter seine Swiss-Manager-Datei
    /// hochlaedt — typisch Tage bis Wochen vorher. Der Kalender wird VORAB gepflegt. Am
    /// 2026-09-07 fuer AUT gemessen: die Suche kannte 8 im November beginnende Turniere und 7 im
    /// Dezember, der Kalender 23 und 16; von 143 kuenftigen Kalendereintraegen fehlten 93 in der
    /// Suche. Getrennt gefuehrt, weil die beiden Bestaende auseinanderlaufen und man bei einem
    /// Widerspruch wissen muss, welcher es sagt.</para>
    /// </summary>
    ChessResultsCalendar = 4,

    /// <summary>
    /// Der Kalender des italienischen Verbands (federscacchi.com).
    ///
    /// <para>Die wichtigste Zusatzquelle: Italien faehrt sein Turnierwesen auf Vega/vesus, nicht
    /// auf chess-results — von 285 Eintraegen verlinkt KEIN EINZIGER dorthin, und eine
    /// Namensstichprobe von 15 fand nur 3. Rund vier Fuenftel der italienischen Turniere fehlen
    /// dort also, und zwar dauerhaft.</para>
    /// </summary>
    ItalianChessFederation = 5,

    /// <summary>
    /// Der Kalender des slowenischen Verbands (sah-zveza.si).
    ///
    /// <para>Das krasseste Verhaeltnis aller geprueften Quellen: 78 kuenftige Turniere gegen 7
    /// auf chess-results. Und nicht bloss Vorlauf — im Rueckblick auf einen abgeschlossenen Monat
    /// erscheinen 36 von 88 Eintraegen dort NIE. Sein Sonderwert: die Trefferliste traegt die
    /// POSTLEITZAHL, die Verortung braucht also keinen Abruf je Turnier.</para>
    /// </summary>
    SlovenianChessFederation = 6,

    /// <summary>
    /// Der Kalender des slowakischen Verbands (chess.sk).
    ///
    /// <para>Die einzige gepruefte Quelle mit einer AUSDRUECKLICH angebotenen Schnittstelle (im
    /// Fussteil als „Free api specification" verlinkt). 79 kuenftige Turniere, 53 % davon ohne
    /// chess-results-Verweis — und der Rest ist der eigentliche Gewinn: 27 Eintraege nennen die
    /// chess-results-Nummer selbst, die Zuordnung ist dort also EXAKT statt ueber einen
    /// Namensvergleich geraten.</para>
    /// </summary>
    SlovakChessFederation = 7,

    /// <summary>
    /// Der Kalender des ungarischen Verbands (chess.hu).
    ///
    /// <para>Sein Ertrag ist VORLAUF: ab November 2026 fuehrt er 41 Turniere, wo chess-results 5
    /// kennt. Im Rueckblick landen 80 % irgendwann doch dort — 20 % nie. Ein Abruf (ein POST; ein
    /// GET antwortet 404) bringt den ganzen Kalender.</para>
    /// </summary>
    HungarianChessFederation = 8,

    /// <summary>
    /// Der Terminkalender des tschechischen Verbands (chess.cz).
    ///
    /// <para>Klein im Volumen (rund 38 echte Turniere, 13 nicht auf chess-results — dort stehen
    /// im selben Zeitraum 146), aber er liefert etwas, das sonst teuer ist: 33 LIGARUNDEN, also
    /// fertige Spieltermine, fuer die sonst je Turnier eine eigene chess-results-Seite geholt
    /// wird.</para>
    /// </summary>
    CzechChessFederation = 9,

    /// <summary>
    /// Der Kalender des polnischen Verbands (chessarbiter.com).
    ///
    /// <para>Die ergiebigste Einzelquelle des Projekts: 611 kuenftige Turniere in EINEM Abruf,
    /// mehr als alle uebrigen Verbandskalender zusammen. Und die einzige, die die
    /// TEILNEHMERZAHL schon vor dem Turnier nennt.</para>
    /// </summary>
    PolishChessFederation = 10,

    /// <summary>
    /// Die Turnierdatenbank des Deutschen Schachbunds (schachbund.de).
    ///
    /// <para>Ein reines Meldesystem ohne Ergebnismeldung — deshalb stehen dort Turnierarten, die
    /// chess-results praktisch nie fuehrt: Vereins-Abendturniere, Jugend-Cups, Fernschach,
    /// Problemschach, Schach960.</para>
    /// </summary>
    GermanChessFederation = 11,

    /// <summary>
    /// Der Kalender des englischen Verbands (englishchess.org.uk).
    ///
    /// <para>Eine der groessten Luecken im Bestand: 256 kuenftige Turniere, 86 % davon nicht auf
    /// chess-results. Und die einzige Quelle, die KOORDINATEN mitliefert — fuer zwei Drittel der
    /// Turniere entfaellt das Geocoding vollstaendig.</para>
    /// </summary>
    EnglishChessFederation = 12,

    /// <summary>
    /// Der Kalender der Irish Chess Union (icu.ie).
    ///
    /// <para>81 kuenftige Turniere, 94 % davon nicht auf chess-results. Einzelne Detailseiten
    /// verlinken die chess-results-Nummer selbst — dort ist die Zuordnung exakt.</para>
    /// </summary>
    IrishChessUnion = 13,

    /// <summary>
    /// Der Kalender des franzoesischen Verbands (echecs.asso.fr).
    ///
    /// <para>Der groesste Ertrag der dritten Runde: von 40 gegengeprueften Turnieren stehen
    /// ZWEI auf chess-results. Die FFE fuehrt die nicht-FIDE-gewerteten Vereinsturniere, die
    /// dort praktisch gar nicht vorkommen.</para>
    /// </summary>
    FrenchChessFederation = 14,

    /// <summary>
    /// Der Aktivitaeten-Feed des norwegischen Verbands (sjakk.no).
    ///
    /// <para>Der eindeutigste Fall der Runde: chess-results fuehrt fuer NOR NULL kuenftige
    /// Turniere, der Feed 81. Der Haken ist der Ort — der Feed hat kein Adressfeld.</para>
    /// </summary>
    NorwegianChessFederation = 15,

    /// <summary>Der Kalender von Chess Scotland (chessscotland.com) — 44 kuenftige in EINEM Abruf, bis 2028.</summary>
    ScottishChessFederation = 16,

    /// <summary>
    /// Der Kalender des rumaenischen Verbands (frsah.ro) — wie England „The Events Calendar",
    /// ein Abruf, 31 kuenftige Turniere samt der nationalen Mannschaftsligen.
    /// </summary>
    RomanianChessFederation = 17,

    /// <summary>
    /// Der Saisonkalender der Welsh Chess Union (welshchessunion.uk) — Fliesstext mit
    /// vollstaendiger Postleitzahl, aber OHNE stabile Kennung.
    /// </summary>
    WelshChessUnion = 18,

    /// <summary>
    /// Der Terminkalender des niederlaendischen Verbands (schaakbond.nl) — 177 kuenftige
    /// Eintraege gegen 12 auf chess-results, aber ohne Rundenzahl und Teilnehmerzahl.
    /// </summary>
    DutchChessFederation = 19,
}

/// <summary>
/// Auf WELCHER Seite dieses Turnier gefunden wurde — und unter welcher Nummer dort.
///
/// <para><b>Warum eine eigene Tabelle.</b> Dasselbe Turnier steht auf mehreren Seiten, und es
/// werden mehr: chess-results heute, morgen ein Verbandskalender, der ueber „mein Turnier fehlt"
/// gemeldet wurde. Ohne Herkunftsvermerk ist spaeter nicht mehr zu sagen, woher eine Angabe kommt
/// — und genau das entscheidet, welche Angabe bei einem Widerspruch gewinnt und welche Seite man
/// aufruft, um nachzusehen.</para>
///
/// <para><b>Was diese Tabelle heute NICHT ist.</b> Die Identitaet eines Eintrags haengt weiterhin
/// an <see cref="TournamentDirectoryEntry.ChessResultsId"/>: die Nummer ist der Schluessel der
/// Adresse (<c>/tournaments/calendar/{id}</c>), des Abos, des Crawl-Auftrags und des
/// Teilen-Links. Solange keine zweite Quelle wirklich Turniere liefert, waere es
/// Vorratsarbeit, das umzubauen. Sobald eine es tut, ist der Weg: eine eigene Schluesselspalte am
/// Eintrag, `ChessResultsId` wird zu einer Quelle unter mehreren (nullable), und die genannten
/// vier Stellen holen die chess-results-Nummer aus DIESER Tabelle.</para>
/// </summary>
public class TournamentDirectorySource
{
    public int Id { get; set; }

    public int TournamentDirectoryEntryId { get; set; }
    public TournamentDirectoryEntry Entry { get; set; } = null!;

    public DirectorySourceKind Kind { get; set; }

    /// <summary>
    /// Die Nummer, unter der DIESE Seite das Turnier fuehrt (chess-results-dbkey, FIDE-event_id).
    /// Global eindeutig je Quelle — eine Nummer gehoert zu genau einem Turnier.
    /// </summary>
    [Required, MaxLength(60)]
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Die Seite selbst, zum Nachsehen. Fuer chess-results aus der Nummer gebildet.</summary>
    [MaxLength(500)]
    public string? Url { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
