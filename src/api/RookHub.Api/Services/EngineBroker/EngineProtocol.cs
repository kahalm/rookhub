namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Grenzen der External-Engine-Anbindung (Protokoll von lila-engine) an EINER Stelle. Live-Pfad
/// (<c>EngineController</c>), Hintergrund-Aufträge (<see cref="AnalysisJobService"/>, <c>AnalysisJobWorker</c>)
/// und der eigene Broker (<see cref="WorkSanitizer"/>, <see cref="UciLineParser"/>) prüfen bewusst jeder für
/// sich — die ZAHL steht aber nur hier. Wer eine Grenze anhebt, hebt sie für alle Riegel zugleich; sonst
/// klemmte etwa der Worker still auf das alte Maximum, und der Parser verwürfe die zusätzlichen Linien.
/// </summary>
public static class EngineProtocol
{
    /// <summary>Linien je Suche: <c>work.multiPv</c> 1..5 — der Lichess-Broker weist mehr beim Deserialisieren ab.</summary>
    public const int MaxMultiPv = 5;

    /// <summary>Höchste Suchtiefe, die RookHub durchreicht (Live und Hintergrund).</summary>
    public const int MaxDepth = 60;

    /// <summary>Höchstens so viele Züge ab der Ausgangsstellung (lila-engine <c>Work::sanitize</c>).</summary>
    public const int MaxMoves = 600;

    /// <summary>Fehlertext bei zu vielen Linien — wortgleich mit lila-engine.</summary>
    public static readonly string MultiPvRangeError = $"invalid multipv: supported range is 1 to {MaxMultiPv}";
}
