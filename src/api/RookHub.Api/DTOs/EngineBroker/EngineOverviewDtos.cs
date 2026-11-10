namespace RookHub.Api.DTOs.EngineBroker;

/// <summary>Antwort von <c>GET /api/admin/engines/overview</c> (Admin-Seite „Engines", 0.946.0).</summary>
/// <param name="GeneratedAt">Zeitpunkt der Zusammenstellung (UTC; bis zu <c>EngineOverviewService.CacheFor</c> alt).</param>
/// <param name="ServerStartedAt">Start der API — ab da zählen die Broker-Zähler im Speicher.</param>
/// <param name="BucketMinutes">Genauigkeit der Durchsatz-Fenster in Minuten.</param>
public record EngineOverviewDto(
    DateTime GeneratedAt,
    DateTime ServerStartedAt,
    int BucketMinutes,
    EngineOverviewPauseDto Pause,
    EngineOverviewQueueDto Queue,
    EngineOverviewThroughputDto Totals,
    IReadOnlyList<EngineOverviewEngineDto> Engines);

/// <summary>Admin-Schalter „Hintergrundjobs pausieren" (nur lesend; umlegen im Reiter „Hintergrundjobs").</summary>
public record EngineOverviewPauseDto(bool Paused, bool ForcedByConfig, DateTime? Since, string? By);

/// <summary>Offene Analyse-Aufträge über alle Engines, dazu die unfertigen Partie-Analysen, aus denen die Pumpe nachfüttert.</summary>
public record EngineOverviewQueueDto(
    int Queued,
    int Paused,
    int Running,
    DateTime? OldestWaitingSince,
    IReadOnlyList<EngineOverviewQueueKindDto> ByKind,
    int GameAnalysesPending,
    int GameAnalysesRunning);

/// <summary>Offene Aufträge einer Stufe (<c>JobPriority</c>: interactive, waiting, clubNew, clubBackfill, league, master).</summary>
public record EngineOverviewQueueKindDto(string Kind, int Queued, int Paused, int Running, DateTime? OldestWaitingSince);

/// <summary>Durchsatz in drei Fenstern (letzte Stunde, 24 h, 7 Tage — auf <c>BucketMinutes</c> genau).</summary>
public record EngineOverviewThroughputDto(EngineThroughputWindowDto Hour, EngineThroughputWindowDto Day, EngineThroughputWindowDto Week);

/// <summary>Beendete Läufe eines Fensters nach Ausgang; <paramref name="AvgJobSeconds"/> = mittlere Rechenzeit je fertigem
/// Auftrag, <paramref name="KiloNodesPerSecond"/> = Knoten je Wanduhr-Sekunde der Läufe mit Knotenangabe (null = keine).</summary>
public record EngineThroughputWindowDto(
    int Runs, int Done, int Failed, int Interrupted, int Retried, int Unavailable, int Timeouts, int Errors,
    long BusySeconds, double? AvgJobSeconds, double? KiloNodesPerSecond);

/// <summary>Eine Engine. <paramref name="Source"/>: <c>rookhub</c> (eigener Broker, <c>rhe_</c>), <c>lichess</c>
/// (<c>eei_</c> — Name/Threads stehen nur bei Lichess, deshalb leer), <c>other</c>. <paramref name="Registered"/> =
/// Registrierung vorhanden (eine <c>rhe_</c>-Kennung ohne Registrierung steht nur noch in Listen/Aufträgen).
/// <paramref name="State"/>: <c>working</c> (Hintergrundauftrag läuft), <c>live</c> (nur Live-Analyse), <c>idle</c>
/// (online, nichts zu tun), <c>offline</c>, <c>unknown</c> (Lichess: kein Online-Status ohne Token-Abfrage).</summary>
public record EngineOverviewEngineDto(
    string Id,
    string? Name,
    string Source,
    bool Registered,
    int? OwnerUserId,
    string? OwnerName,
    bool? Online,
    DateTime? LastSeenAt,
    DateTime? RegisteredAt,
    int? MaxThreads,
    int? MaxHash,
    IReadOnlyList<string> Variants,
    IReadOnlyList<EngineOverviewUserDto> BackgroundFor,
    bool HouseEngine,
    bool ExplicitOnly,
    string State,
    int LiveStreams,
    int ProviderPolling,
    int BrokerQueued,
    EngineOverviewCurrentJobDto? Current,
    int WaitingQueued,
    int WaitingPaused,
    DateTime? OldestWaitingSince,
    EngineOverviewThroughputDto Throughput,
    EngineBrokerCountersDto? Broker);

public record EngineOverviewUserDto(int UserId, string? Name);

/// <summary>Der Auftrag, der gerade auf der Engine rechnet: Stand aus dem Speicher des Workers (<c>Depth</c>/<c>Nps</c>/
/// <c>Nodes</c>/<c>Seconds</c>, ohne Lauf im Speicher die zuletzt gesicherten Werte), Partie über die Stellung, die
/// ihn angefordert hat (leer bei Einzelaufträgen).</summary>
public record EngineOverviewCurrentJobDto(
    int JobId,
    int OwnerUserId,
    string? OwnerName,
    string? Title,
    string Fen,
    string Kind,
    bool Background,
    bool Batch,
    int TargetDepth,
    long? TargetNodes,
    int MultiPv,
    int ReachedDepth,
    int Depth,
    int Nps,
    long Nodes,
    int Seconds,
    DateTime? RunningSince,
    int? GameAnalysisId,
    int? Ply,
    string? GameTitle);

/// <summary>Zähler des eigenen Brokers je Engine seit dem API-Start (nur <c>rhe_</c>): <c>ProviderTimeouts</c> und
/// <c>QueueFull</c> sind die 503 an den Anfragenden.</summary>
public record EngineBrokerCountersDto(
    long Jobs, long Completed, long WithoutBestmove, long RequesterGone, long ProviderGone, long ProviderTimeouts,
    long QueueFull, long OngoingExpired);
