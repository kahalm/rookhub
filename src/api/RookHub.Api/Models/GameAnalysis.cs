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

    public GameAnalysisStatus Status { get; set; } = GameAnalysisStatus.Pending;

    /// <summary>Anzahl der zu analysierenden Halbzüge (= Zeilen in <see cref="Positions"/>).</summary>
    public int PlyCount { get; set; }

    [MaxLength(500)] public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

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

    /// <summary>So viele eingeworfene Partien darf ein Nutzer gleichzeitig offen haben. Der Deckel
    /// gilt NUR fuer <see cref="GameAnalysisOrigin.Guess"/>: von Hand eingereihte Partien laufen wie
    /// bisher ungezaehlt, denn dort rechnet die eigene Maschine.</summary>
    public const int MaxOpenGuessGamesPerUser = 5;

    /// <summary>Deckel für die Länge einer Partie (Halbzüge) — schützt vor einem PGN-Monster.</summary>
    public const int MaxPlies = 300;

    /// <summary>
    /// So viele Auftraege haelt eine Partie gleichzeitig offen. Bewusst deutlich unter
    /// <c>AnalysisJobService.MaxOpenJobsPerUser</c> (50), damit daneben noch von Hand eingereiht
    /// werden kann.
    ///
    /// <para>Die Zahl verteilt die Partie ueber die hinterlegten Engines: der Worker rechnet je
    /// Engine eine Suche, zwoelf offene Auftraege halten also bis zu zwoelf Engines beschaeftigt.
    /// Dass mehrere PARTIEN nicht gegenseitig verhungern, regelt seit 0.466.0 nicht mehr diese
    /// Zahl, sondern die Reihenfolge: es wird immer nur EINE Partie je Nutzer weitergefuettert
    /// (<c>GameAnalysisService.IsOwnersTurnAsync</c>).</para>
    /// </summary>
    public const int MaxOpenJobsPerGame = 12;

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
}
