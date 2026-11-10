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
    IReadOnlyList<EngineOverviewEngineDto> Engines,
    IReadOnlyList<EngineOverviewMachineDto> Machines,
    IReadOnlyList<EngineOverviewTokenDto> Tokens,
    IReadOnlyList<EngineOverviewHintDto> Hints,
    EngineOverviewQuietDto Quiet,
    double? QueueEmptyInHours,
    int BackgroundCap);

/// <summary>Sperrzeiten von RookHub (<c>TextLlm:QuietHours</c>) — gelten für Stapel-Aufträge auf Engines OHNE gemeldeten
/// Zeitplan. <paramref name="Windows"/>: <c>day</c> 0 = Sonntag … 6 = Samstag (wie <c>DayOfWeek</c>), Uhrzeit HH:mm in
/// <paramref name="TimeZone"/>.</summary>
public record EngineOverviewQuietDto(bool Enabled, bool QuietNow, DateTime? QuietUntil, string TimeZone,
    IReadOnlyList<EngineOverviewQuietWindowDto> Windows);

public record EngineOverviewQuietWindowDto(int Day, string From, string To);

/// <summary>Mittleres Tempo (kN/s) der letzten 7 Tage, getrennt nach „normal“ (Zeitplan 100 % bzw. außerhalb der
/// Sperrzeit) und „Schonzeit“ (Zeitplan &lt; 100 % bzw. RookHub-Sperrzeit) — eingeordnet nach der HEUTIGEN Regel.</summary>
public record EngineSpeedSplitDto(double? Normal, double? Reduced);

/// <summary>Gemeldeter Zeitplan einer Engine (<c>EngineClientSchedules</c>).</summary>
public record EngineScheduleDto(int Slot, int Total, string Scope, string Rule, string TimeZone, DateTime ReportedAt,
    bool OpenNow, int PercentNow);

/// <summary>Eine Maschine: Engines eines Kontos mit gleichem Namensstamm (ohne „Hintergrund N“/Nummer); ein noch
/// unbekannter Token (<c>null</c>) trennt nicht, nur zwei verschiedene bekannte Tokens (dann trägt <c>Name</c> den
/// Token-Namen). <c>ApiTokenId</c>/<c>TokenName</c> = der bekannte Token, <c>null</c> = noch nicht zugeordnet.
/// Lichess-Engines je Konto als eine Gruppe.</summary>
public record EngineOverviewMachineDto(
    string Key, string Name, int? OwnerUserId, string? OwnerName, string Source, int? ApiTokenId, string? TokenName,
    IReadOnlyList<string> EngineIds, int Online, int Total, int Working, int Threads, int DoneDay, int? BusyPercentDay,
    EngineSpeedSplitDto Speed, EngineMachineScheduleDto? Schedule);

/// <summary>Zeitplan einer Maschine: Regel der jüngsten Meldung, wie viele Engines gemeldet haben und jetzt laufen
/// dürfen (<c>EngineAvailability.IsOpen</c>), heutiger Prozentsatz und das nächste Umschalten (binnen 7 Tagen).</summary>
public record EngineMachineScheduleDto(string Rule, string TimeZone, string Scope, int Total, DateTime ReportedAt,
    int Reported, int OpenNow, int PercentNow, DateTime? NextChangeAt, int? NextPercent);

/// <summary>Ein API-Token mit Bereich <c>engine</c> — NIE der Token oder sein Hash, nur das Präfix wie im Profil.</summary>
public record EngineOverviewTokenDto(int Id, string Name, string Prefix, int OwnerUserId, string? OwnerName,
    DateTime CreatedAt, DateTime? LastUsedAt, DateTime? ExpiresAt, bool Expired, IReadOnlyList<EngineOverviewTokenEngineDto> Engines);

public record EngineOverviewTokenEngineDto(string Id, string Name, bool? Online);

/// <summary>Hinweis oben auf der Seite. <paramref name="Kind"/>: <c>stuckJobs</c> (Aufträge auf einer Engine, die offline
/// ist oder in keiner Liste steht), <c>notInList</c> (Hintergrund-Engines einer Maschine in keiner Liste), <c>listCap</c>
/// (Hintergrund-Liste eines Kontos zur Hälfte oder mehr voll), <c>staleToken</c> (Engine-Token seit &gt; 30 Tagen ohne
/// Zugriff).</summary>
public record EngineOverviewHintDto(string Kind, string? EngineId, string? Name, int? UserId, int Count, int? Max,
    DateTime? Since, string? Detail);

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
    EngineBrokerCountersDto? Broker,
    string MachineKey,
    EngineScheduleDto? Schedule,
    string? IdleReason,
    EngineSpeedSplitDto Speed,
    int? ApiTokenId);

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

/// <summary>Rumpf von <c>PUT /api/admin/engines/{engineId}/background</c> (0.951.0): die Engine in die Hintergrund-Liste
/// des Kontos <paramref name="UserId"/> aufnehmen (<paramref name="Enabled"/> = true) bzw. daraus entfernen.</summary>
public record SetEngineBackgroundRequest(int UserId, bool Enabled);

/// <summary>Antwort darauf: <paramref name="Changed"/> = false, wenn sie schon drin bzw. schon draußen war;
/// <paramref name="BackgroundEngineIds"/> = die Liste des Kontos danach.</summary>
public record SetEngineBackgroundResponse(string EngineId, int UserId, bool Enabled, bool Changed,
    IReadOnlyList<string> BackgroundEngineIds);
