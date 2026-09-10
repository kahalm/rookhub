using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public enum GuessSessionStatus
{
    Running = 0,
    Done = 1,
}

/// <summary>
/// Ein Durchlauf durch eine analysierte Partie („Punktepartie"): Der Nutzer übernimmt EINE Seite,
/// rät ab einem bestimmten Zug jeden Halbzug und bekommt je Zug eine Stufe samt Punkten
/// (<see cref="GuessGrade"/>). Der Gegenzug wird automatisch nachgespielt.
///
/// <para><b>Die Fortsetzung verlässt den Server nicht.</b> Die Sitzung merkt sich, wo sie steht;
/// ausgeliefert wird immer nur die aktuelle Stellung. Den Partiezug bekommt der Client erst als
/// Antwort auf seinen Rateversuch — vorher hätte er die Lösung.</para>
///
/// <para><b>Auch OHNE Anmeldung läuft der Fortschritt hier</b> und nicht im Browser (seit
/// 0.459.0): genau dieser Zustand ist es, hinter dem die Züge stehen. Ein Client, der selbst
/// mitzählt, müsste dem Server sagen, bei welchem Halbzug er steht — und könnte damit jeden Zug
/// der Partie einzeln abfragen. Deshalb dasselbe Muster wie bei den anonymen Puzzle-Versuchen:
/// die Zeile gehört entweder einem Konto (<see cref="UserId"/>) oder einer vom Browser vergebenen
/// Kennung (<see cref="AnonymousSessionId"/>) — genau eines von beiden ist gesetzt.</para>
/// </summary>
public class GuessSession
{
    public int Id { get; set; }

    /// <summary><c>null</c> bei einem anonymen Durchlauf (dann trägt
    /// <see cref="AnonymousSessionId"/> den Besitzer).</summary>
    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Vom Browser vergebene Kennung eines anonymen Durchlaufs (UUID-Form, siehe
    /// <c>ValidationConstants.SessionIdPattern</c>); <c>null</c> bei einem angemeldeten.</summary>
    [MaxLength(36)] public string? AnonymousSessionId { get; set; }

    public int GameAnalysisId { get; set; }
    public GameAnalysis? GameAnalysis { get; set; }

    /// <summary>Geratene Seite: <c>true</c> = Weiß (rät die geraden Halbzüge).</summary>
    public bool GuessWhite { get; set; } = true;

    /// <summary>Erster Halbzug, der geraten wird (davor wird die Partie nur gezeigt).</summary>
    public int StartPly { get; set; }

    /// <summary>Nächster zu ratender Halbzug. Steht er über dem letzten, ist die Sitzung durch.</summary>
    public int CurrentPly { get; set; }

    public GuessSessionStatus Status { get; set; } = GuessSessionStatus.Running;

    /// <summary>Aufsummierte Rechenzeit des Nutzers (Client schickt Deltas).</summary>
    public int SecondsSpent { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public List<GuessMove> Moves { get; set; } = new();
}

/// <summary>
/// Ein geratener Halbzug. Gespeichert wird die STUFE, nicht die Punktzahl — die entsteht in
/// <see cref="GuessGrades.PointsFor"/> (wie bei <see cref="CalculationGrade"/>), damit eine spätere
/// Neugewichtung die Vergangenheit nicht umschreibt. Auch die Summe der Sitzung wird daraus
/// gerechnet und nicht mitgeschleppt.
/// </summary>
public class GuessMove
{
    public int Id { get; set; }

    public int GuessSessionId { get; set; }
    public GuessSession? GuessSession { get; set; }

    public int Ply { get; set; }

    /// <summary>Der geratene Zug (Standard-UCI). Leer = übersprungen (0 Punkte, keine Strafe).</summary>
    [MaxLength(10)] public string? PlayedUci { get; set; }

    /// <summary><c>null</c> = übersprungen oder nicht wertbare Stellung.</summary>
    public GuessGrade? Grade { get; set; }

    /// <summary>Wertunterschied zum Partiezug in Centipawns (Anzeige: „warum diese Punkte?").</summary>
    public int? DiffCp { get; set; }

    public int SecondsSpent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
