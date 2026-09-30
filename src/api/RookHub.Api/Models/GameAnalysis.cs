using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public enum GameAnalysisStatus
{
    /// <summary>Stellungen angelegt, noch nicht (vollständig) eingereiht.</summary>
    Pending = 0,
    /// <summary>Mindestens ein Auftrag läuft bzw. wartet.</summary>
    Running = 1,
    /// <summary>Jede Stellung hat ihre Kandidatenliste.</summary>
    Done = 2,
    /// <summary>Abgebrochen — der Fehler steht in <see cref="GameAnalysis.LastError"/>.</summary>
    Failed = 3,
}

/// <summary>Woher die Partie kam — sie entscheidet ueber den Deckel, nicht ueber die Rechnung.</summary>
public enum GameAnalysisOrigin
{
    /// <summary>Von Hand auf der Seite „Partie-Analysen" eingereiht (Tiefe und Linien frei waehlbar).</summary>
    Manual = 0,
    /// <summary>Auf der Punktepartie-Seite eingeworfen: feste Tiefe, kein Regler, eigener Deckel.</summary>
    Guess = 1,
    /// <summary>
    /// Ueber „Partie analysieren" an einer GESPEICHERTEN Partie angestossen (<c>/games</c>, <c>/g/…</c>).
    /// Derselbe Weg wie <see cref="Guess"/> — Haus-Engine, feste Tiefe, fuenf Linien, derselbe Deckel —,
    /// nur anders etikettiert: die Analyse gehoert zur Partie (<see cref="SavedGame.GameAnalysisId"/>)
    /// und dient deren Bewertungskurve. In der Liste „Eigene Analysen" (Punktepartie-Seite und
    /// Partie-Analysen) erscheint sie deshalb NICHT; wer sie sucht, findet sie an der Partie.
    /// </summary>
    SavedGame = 2,
    /// <summary>
    /// Meisterpartie aus dem Bibliotheksbestand, vom Stapel analysiert (<c>MasterAnalysisScheduler</c>, 2026-09-28):
    /// zu denselben Zeiten wie die Spark-Uebersetzung (<c>QuietHours</c>), auf den Hintergrund-Engines der Haus-Engine,
    /// AUSSCHLIESSLICH als Hintergrundarbeit — jeder andere Auftrag hat Vorrang und verdraengt sie sogar, wenn sie
    /// schon rechnet. Fuer ALLE lesbar (wer die Meisterpartie oeffnet, bekommt die fertige Analyse, statt sie neu
    /// rechnen zu lassen), aber bewusst NICHT <see cref="GameAnalysis.IsPublic"/>: der Punktepartie-Bestand
    /// (<c>ListPublicAsync</c>, ungeblaettert) bliebe sonst unter ueber hunderttausend Partien begraben.
    /// In keiner persoenlichen Liste und nicht in der Reihenfolge der eigenen Partien des Besitzers.
    /// </summary>
    Library = 3,
    /// <summary>
    /// Partie aus der Vereins-Datenbank von LeagueHub (<see cref="LeagueClubGame"/>), vom selben Stapel analysiert wie
    /// <see cref="Library"/> (Wunsch 2026-09-28: „wirf die Partien aus dem Vereinsverzeichnis auch immer in die
    /// Analyse") — dieselben Zeiten, dieselben Engines, dieselbe Hintergrundarbeit, aber VOR den Meisterpartien: es
    /// sind wenige, und jede neu hochgeladene kommt beim nächsten Takt dran. Anders als eine Meisterpartie NICHT für
    /// alle lesbar — die Vereins-Datenbank sieht nur, wer in LeagueHub die Vereinspartien sehen darf. Verknüpft über
    /// <see cref="GameAnalysis.LeagueClubGameId"/>; wird die Partie gelöscht, geht die Analyse mit, wird sie korrigiert
    /// (Namen, „Schwaz"), zieht der Kopf der Analyse nach.
    /// </summary>
    Club = 4,
}

/// <summary>Die Etiketten des Stapels (<c>MasterAnalysisScheduler</c>).</summary>
public static class GameAnalysisOrigins
{
    /// <summary>Vom Stapel angelegt: reine Hintergrundarbeit, nur außerhalb der Sperrzeiten, in keiner Liste des
    /// Besitzers und nicht in der Reihenfolge seiner eigenen Partien. In Abfragen steht dieselbe Bedingung
    /// ausgeschrieben (<c>Origin != Library &amp;&amp; Origin != Club</c>) — ein Methodenaufruf ließe sich nicht übersetzen.</summary>
    public static bool IsBatch(GameAnalysisOrigin origin) =>
        origin is GameAnalysisOrigin.Library or GameAnalysisOrigin.Club;
}

/// <summary>
/// Eine GANZE Partie, von der Hintergrund-Engine Stellung für Stellung durchgerechnet — die
/// Vorstufe der Punktepartie (siehe TODO.md) und für sich schon nützlich („diese Partie einmal
/// komplett analysieren" statt Stellung für Stellung von Hand einzureihen).
///
/// <para>Die Analyse selbst läuft über die bestehenden <see cref="AnalysisJob"/>s: derselbe
/// Broker-Pfad, derselbe Vorrang der Live-Analyse, dieselbe Fortsetzungs-Logik. Neu ist nur die
/// Klammer darum — Partie zerlegen, Aufträge in Blöcken nachfüttern, Ergebnisse einsammeln.</para>
///
/// <para><b>Warum die Ergebnisse kopiert werden</b> (in <see cref="GameAnalysisPosition.CandidatesJson"/>)
/// statt auf den Auftrag zu zeigen: <c>AnalysisJobService.MaxJobsPerUser</c> räumt die ÄLTESTEN
/// fertigen Aufträge weg, sobald ein Nutzer über 200 kommt. Eine 80-Halbzug-Partie erzeugt 80
/// Aufträge — ohne Kopie hätte der Trimmer die Analyse nach zweieinhalb Partien wieder aufgefressen.</para>
/// </summary>
public class GameAnalysis
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    [MaxLength(200)] public string? Title { get; set; }

    /// <summary>Roh-PGN der Partie (Quelle; die Stellungen liegen daneben in eigenen Zeilen).</summary>
    [Required] public string Pgn { get; set; } = string.Empty;

    [MaxLength(120)] public string? White { get; set; }
    [MaxLength(120)] public string? Black { get; set; }
    [MaxLength(32)]  public string? Result { get; set; }
    [MaxLength(200)] public string? Event { get; set; }

    /// <summary>Startstellung der Partie (PGN-Header <c>[FEN]</c>, sonst die Grundstellung).</summary>
    [Required, MaxLength(120)] public string StartFen { get; set; } = string.Empty;

    public int TargetDepth { get; set; } = GameAnalysisDefaults.TargetDepth;

    /// <summary>Linien je Stellung — höchstens <c>AnalysisJobService.MaxMultiPv</c> (Protokoll-Limit 5).</summary>
    public int MultiPv { get; set; } = GameAnalysisDefaults.MultiPv;

    /// <summary>Engine, auf der gerechnet wird (Lichess <c>eei_…</c>); leer = Hintergrund-Engine des Profils.</summary>
    [MaxLength(64)] public string? EngineId { get; set; }

    /// <summary>
    /// Kuratierter Bestand: diese Partie darf JEDER als Punktepartie spielen — auch ohne Anmeldung.
    /// Gesetzt vom Besitzer oder einem Admin (<c>PUT /api/game-analyses/{id}/public</c>).
    ///
    /// <para>Bewusst ein FLAG an der EINEN Analyse und keine Kopie je Nutzer: die Engine-Arbeit
    /// (~20 s je Halbzug, bei 80 Halbzügen eine halbe Stunde) fällt damit einmal an statt für jeden
    /// Besucher erneut. Und bewusst nicht <c>IsPublic</c> am Buch nachgebaut — hier hängt nichts an
    /// Kapiteln oder Fortschritt, es ist genau diese eine Frage.</para>
    ///
    /// <para><b>Die eiserne Regel bleibt:</b> öffentlich heißt spielbar, nicht lesbar. Die Züge
    /// stehen weiter hinter dem Fortschritt der SITZUNG (<see cref="GuessSession"/>) — auch die
    /// anonyme läuft deshalb über eine Sitzung am Server und nicht über einen Zustand im Browser.
    /// <c>GET /api/game-analyses/{id}</c> liefert nach wie vor nur EIGENE Analysen.</para>
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// Wer die Engine stellt, wenn es nicht der Besitzer selbst ist (Haus-Engine, siehe
    /// <see cref="LichessEngineCredential.ShareAsHouseEngine"/>); <c>null</c> = eigene.
    /// Wird an jeden <see cref="AnalysisJob"/> dieser Partie durchgereicht — Token und Engine kommen
    /// dann von DIESEM Konto, waehrend die Partie und ihre Auftraege dem Einwerfer gehoeren.
    /// </summary>
    public int? EngineOwnerUserId { get; set; }

    /// <summary>Von Hand eingereiht oder auf der Punktepartie-Seite eingeworfen.</summary>
    public GameAnalysisOrigin Origin { get; set; } = GameAnalysisOrigin.Manual;

    /// <summary>
    /// Aus welcher Zeile des Rohbestands diese Analyse angefordert wurde; <c>null</c> = eingeworfen
    /// oder von Hand eingereiht.
    ///
    /// <para>Die Richtung ist Absicht: VIELE Analysen zu EINER Bibliothekspartie. Fordern zwei Leute
    /// dieselbe Partie an, bekommt jeder seine eigene — der umgekehrte Verweis
    /// (<see cref="LibraryGame.GameAnalysisId"/>) koennte immer nur einen von beiden halten und
    /// bleibt der Kuratierungs-Vermerk.</para>
    ///
    /// <para>Kein Fremdschluessel: der Rohbestand ist ein Arbeitsvorrat, der auch einmal ganz neu
    /// eingelesen werden kann. Eine gerechnete Analyse darf daran nicht haengen.</para>
    /// </summary>
    public int? LibraryGameId { get; set; }

    /// <summary>
    /// Die Vereinspartie (<see cref="LeagueClubGame"/>), zu der diese Analyse gehört (<see cref="GameAnalysisOrigin.Club"/>);
    /// sonst <c>null</c>. Kein Fremdschlüssel wie bei <see cref="LibraryGameId"/>, aber anders als dort räumt das
    /// Löschen der Partie die Analyse mit ab (<c>LeagueClubService.DeleteAsync</c>): sie trägt die Namen der Partie.
    /// </summary>
    public int? LeagueClubGameId { get; set; }

    /// <summary>
    /// Ab welchem Halbzug das Raten sinnvoll beginnt — einmal ermittelt und gemerkt, weil die
    /// Antwort an der Partie haengt und nicht am Durchlauf (siehe <c>GuessStartPly</c>).
    /// <c>null</c> = noch nicht bestimmt; dann greift die Vorgabe.
    /// </summary>
    public int? SuggestedStartPly { get; set; }

    /// <summary>
    /// Die ersten Halbzuege normalisiert („e4 e5 Nf3 Nc6"), wie <see cref="LibraryGame.OpeningLine"/>
    /// — die Grundlage des Stellungsfilters auf der Punktepartie-Seite: „welche Partien spielen
    /// dieselben ersten k Zuege" ist damit eine Praefix-Suche auf einem Index.
    ///
    /// <para>Eigene Spalte und nicht bei Bedarf aus <see cref="GameAnalysisPosition.GameMoveSan"/>
    /// gerechnet: der Baum fragt bei JEDEM Klick, und ein Selbst-Verbund ueber zehn Halbzuege
    /// waere zehn Verbuende ueber Zehntausende Zeilen.</para>
    /// </summary>
    public string? OpeningLine { get; set; }

    public GameAnalysisStatus Status { get; set; } = GameAnalysisStatus.Pending;

    /// <summary>Anzahl der zu analysierenden Halbzüge (= Zeilen in <see cref="Positions"/>).</summary>
    public int PlyCount { get; set; }

    /// <summary>
    /// Genauigkeit von Weiss bzw. Schwarz in Prozent (Lichess-Formel, <see cref="Services.GameAccuracy"/>) —
    /// gerechnet, sobald die Analyse fertig ist, und hier ABGELEGT, damit Listen sie zeigen koennen, ohne je
    /// Partie die Stellungen zu laden (die Partienliste fragt waehrend der Rechnung alle zehn Sekunden).
    /// <c>null</c> = noch nicht gerechnet (Analysen von vor 0.515.0 traegt die Partienliste nach) oder die
    /// Seite hat keinen bewertbaren Zug.
    /// </summary>
    public double? AccuracyWhite { get; set; }
    public double? AccuracyBlack { get; set; }

    [MaxLength(500)] public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    /// <summary>
    /// ZWEITER Durchgang („Vertiefung", seit 0.523.0): nach dem schnellen ersten Durchgang
    /// (<see cref="TargetDepth"/>/<see cref="MultiPv"/>, fuer „Partie analysieren" Tiefe 20 mit EINER Linie — Kurve,
    /// Genauigkeit und Fehler stehen damit nach wenigen Minuten) wird jede Stellung noch einmal mit dieser Tiefe und
    /// <see cref="RefineMultiPv"/> Linien gerechnet und ersetzt. Die Analyse bleibt dabei <see cref="GameAnalysisStatus.Done"/>
    /// und wird Stueck fuer Stueck besser. <c>null</c> = kein zweiter Durchgang (von Hand eingereiht, Punktepartie,
    /// Altbestand).
    /// </summary>
    public int? RefineDepth { get; set; }
    public int? RefineMultiPv { get; set; }
    /// <summary>Wann der zweite Durchgang fertig wurde; <c>null</c> = laeuft noch (oder gibt es nicht).</summary>
    public DateTime? RefinedAt { get; set; }

    public List<GameAnalysisPosition> Positions { get; set; } = new();
}

/// <summary>Vorgabewerte einer Partie-Analyse — an EINER Stelle, damit Controller, Service und
/// Frontend nicht auseinanderlaufen.</summary>
public static class GameAnalysisDefaults
{
    /// <summary>Tiefe 30: Tiefe 40 kostet grob das Zehnfache (bei 5 Linien Minuten je Iteration) und
    /// macht aus einer Partie ein Nachtprojekt — 30 ist der Kompromiss aus Aussagekraft und Durchsatz.</summary>
    public const int TargetDepth = 30;

    /// <summary>5 = Protokoll-Maximum des Lichess-External-Engine-Protokolls (<c>work.multiPv</c> 1..5).</summary>
    public const int MultiPv = 5;

    /// <summary>
    /// Tiefe der auf der Punktepartie-Seite eingeworfenen Partien — FEST, und im Formular steht
    /// kein Regler dafuer.
    ///
    /// <para>Zwei Gruende. Erstens rechnet dort meist nicht die eigene Maschine, sondern die
    /// Haus-Engine: einen Regler anzubieten hiesse, fremde Rechenzeit zur Selbstbedienung zu
    /// stellen (Tiefe 40 kostet grob das Zehnfache von 30). Zweitens braucht die Punktepartie die
    /// Tiefe gar nicht: gewertet wird gegen den TATSAECHLICH gespielten Zug, die Engine liefert nur
    /// die Rangfolge der Alternativen — und die steht bei 20 im Wesentlichen so wie bei 30. Wer die
    /// Tiefe wirklich braucht, reiht die Partie weiter von Hand ueber „Partie-Analysen" ein.</para>
    /// </summary>
    public const int GuessTargetDepth = 20;

    /// <summary>
    /// Tiefe fuer „Partie analysieren" an einer GESPEICHERTEN Partie (<see cref="GameAnalysisOrigin.SavedGame"/>).
    /// Anders als bei der Punktepartie ist hier die BEWERTUNG das Ergebnis: Kurve, Genauigkeit und
    /// Zug-Klassen haengen an den Zahlen, und ein Opfer, das die Engine erst zwei Zuege spaeter
    /// versteht, steht bei 20 noch als Fehler in der Kurve. Deshalb tiefer als dort.
    ///
    /// <para>Seit 0.523.0 ist das die Tiefe der VERTIEFUNG (<see cref="GameAnalysis.RefineDepth"/>, fuenf Linien), der
    /// erste Durchgang rechnet schnell mit <see cref="SavedGameFastDepth"/>. Geschichte: 30 am 2026-09-23, 25 ab
    /// 2026-09-24 (auf 30 mit fuenf Linien kostete eine Partie mit 47 Stellungen eine halbe Stunde, und damals
    /// wartete man auf genau diese Rechnung), seit 0.555.2 wieder 30 (gewuenscht 2026-09-27): seit die Vertiefung
    /// im Hintergrund laeuft, wartet niemand mehr auf sie — Kurve und Fehler stehen nach dem schnellen Durchgang.
    /// Der Deckel (<see cref="MaxOpenGuessGamesPerUser"/>) bleibt gemeinsam.</para>
    /// </summary>
    public const int SavedGameTargetDepth = 30;

    /// <summary>So viele eingeworfene Partien darf ein Nutzer gleichzeitig offen haben. Der Deckel
    /// zaehlt <see cref="GameAnalysisOrigin.Guess"/> UND <see cref="GameAnalysisOrigin.SavedGame"/>
    /// zusammen — beide rechnen auf fremder Rechenzeit, und getrennte Deckel hiessen doppelt so viele
    /// Plaetze fuer jeden, der beide Wege kennt. Von Hand eingereihte Partien laufen wie bisher
    /// ungezaehlt, denn dort rechnet die eigene Maschine.</summary>
    public const int MaxOpenGuessGamesPerUser = 5;

    /// <summary>Deckel für die Länge einer Partie (Halbzüge) — schützt vor einem PGN-Monster.</summary>
    public const int MaxPlies = 300;

    /// <summary>
    /// So viele Auftraege haelt eine Partie gleichzeitig offen.
    ///
    /// <para><b>Am 2026-09-13 auf Prod ausgemessen</b>, drei Fenster zu je zwanzig Minuten, und das
    /// Ergebnis widerlegt die naheliegende Annahme (mehr Engines und tiefere Schlangen seien
    /// besser):</para>
    ///
    /// <list type="table">
    /// <item><term>16 Engines, Block 32</term><description>957 Stellungen je Stunde</description></item>
    /// <item><term>5 Engines, Block 32</term><description><b>1596</b> — die Bestkonfiguration</description></item>
    /// <item><term>5 Engines, Block 96</term><description>1104</description></item>
    /// <item><term>16 Engines, Block 96</term><description>786</description></item>
    /// </list>
    ///
    /// <para>Die letzte Zeile hat dieselbe Schlangentiefe wie die zweite (sechs Auftraege je
    /// Engine) und liefert die Haelfte — nicht die Tiefe entscheidet also, sondern die ZAHL der
    /// Engines, und zwar gegenlaeufig. Belegt ist davon der Anteil, der auf die 503-Wechsel geht:
    /// im 16-Engine-Fenster 156 Wechsel gegen null in beiden 5-Engine-Fenstern, und jeder legt
    /// einen Auftrag <c>EngineSwitchBackoffSeconds</c> (15 s) schlafen. Warum ein GROESSERER Block
    /// bei gleicher Engine-Zahl schadet, ist offen.</para>
    ///
    /// <para>Vorbehalt zur Messung: jedes Fenster umfasst sechs bis sieben verschiedene Partien,
    /// und Partien unterscheiden sich darin, wie schnell ihre Stellungen rechnen. Die REIHENFOLGE
    /// ist ueber alle vier Punkte konsistent, die Zahlen sind es nicht auf zehn Prozent.</para>
    ///
    /// <para>Wer hier dreht, misst bitte nach — und nicht nur eine Richtung: diese Zahl, die Laenge
    /// der Engine-Liste im Profil und <c>AnalysisJobService.MaxOpenJobsPerUser</c> (150, muss ueber
    /// dieser Zahl bleiben) wirken zusammen.</para>
    /// </summary>
    public const int MaxOpenJobsPerGame = 32;

    /// <summary>
    /// Wie oft ein Auftrag zu DERSELBEN Stellung scheitern darf, bevor sie endgueltig als
    /// unbewertbar gilt. Ein Fehlschlag heisst hier fast immer „die Engine war gerade nicht zu
    /// gebrauchen" und nicht „diese Stellung geht nicht" — eine Stellung der Partie hat immer einen
    /// legalen Zug, Matt oder Patt kann sie gar nicht sein. Beim ERSTEN Mal aufzugeben hiess: eine
    /// tote Engine loescht stillschweigend Stellungen aus der Partie (am 2026-09-10 an 25 Stueck
    /// passiert, alle mussten von Hand zurueckgesetzt werden). Drei Anlaeufe, dann ist Schluss —
    /// sonst liefe eine wirklich unloesbare Stellung ewig im Kreis.
    /// </summary>
    public const int MaxPositionAttempts = 3;

    /// <summary>Erster Durchgang von „Partie analysieren" (seit 0.523.0): Tiefe 20, EINE Linie — schnell genug, dass
    /// Kurve, Genauigkeit und die eigenen Fehler nach wenigen Minuten stehen (gewuenscht 2026-09-24).</summary>
    public const int SavedGameFastDepth = 20;
    public const int SavedGameFastMultiPv = 1;

    /// <summary>UNTERGRENZE der offenen Vertiefungs-Auftraege je Partie. Tatsaechlich gilt die Zahl der
    /// Hintergrund-Engines des Engine-Besitzers, gedeckelt bei <see cref="MaxOpenJobsPerGame"/>
    /// (<c>GameAnalysisTurnRules.RefineJobCap</c>) — bis 0.567.2 galt fest diese 8, und bei 16 Engines lag die Haelfte brach.
    /// Die Untergrenze haelt kleinen Konten (eine Engine) den bisherigen Vorrat.</summary>
    public const int MaxOpenRefineJobsPerGame = 8;
}
