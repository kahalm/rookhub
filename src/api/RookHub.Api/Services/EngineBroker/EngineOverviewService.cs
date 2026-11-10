using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs.EngineBroker;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Admin-Seite „Engines" (0.946.0, <c>GET /api/admin/engines/overview</c>): alle Engines aller Konten, wer sie im
/// Hintergrund nutzt, was wartet, was gerade rechnet und der Durchsatz. Nur lesend (geschrieben wird die Zuordnung über
/// <c>EngineSettingsService.SetBackgroundMembershipAsync</c>, danach <see cref="Invalidate"/>).
///
/// <para><b>Quellen:</b> Registrierungen (<c>ExternalEngineRegistrations</c>, klein) und Hintergrund-Listen
/// (<c>LichessEngineCredentials</c>, eine Zeile je Konto) aus der DB; Online-Stempel aus
/// <see cref="EngineSelectorDirectory"/>; wartende Polls/Aufträge und Broker-Zähler aus <see cref="EngineHub"/>; Live-Streams
/// aus <see cref="EngineActivityTracker"/>; Stand der laufenden Aufträge aus <see cref="AnalysisJobLive"/>; Durchsatz aus
/// <see cref="EngineThroughputBucket"/> plus die noch nicht geschriebenen Zuwächse (<see cref="EngineThroughputRecorder"/>).
/// Lichess-Engines (<c>eei_</c>) erscheinen nur über ihre Kennung: Name und Threads stünden bei Lichess, und eine
/// Abfrage je Konto mit dessen Token ist für eine Übersicht zu teuer.</para>
///
/// <para><b>Abfragen</b> (alle über Indizes): offene Aufträge gruppiert nach Engine/Status/Stufe über
/// (EngineId, Status, Priority, …, CreatedAt) bzw. (Status, EngineId); laufende Aufträge über (Status, EngineId); deren
/// Partie über (AnalysisJobId, GameAnalysisId); unfertige Partie-Analysen über (Status); Durchsatz-Körbe über
/// (BucketStart). Das Ergebnis wird <see cref="CacheFor"/> zwischengespeichert — mehrere offene Admin-Tabs fragen die
/// Datenbank nicht öfter.</para>
/// </summary>
public sealed class EngineOverviewService(
    AppDbContext db,
    IMemoryCache cache,
    EngineHub hub,
    EngineSelectorDirectory directory,
    LocalBrokerOptions brokerOptions,
    EngineActivityTracker tracker,
    AnalysisJobLive live,
    EngineThroughputRecorder throughput,
    BackgroundPause pause,
    IConfiguration config,
    QuietHours? quiet = null)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);
    private const string CacheKey = "admin-engine-overview";
    private static readonly DateTime ServerStartedAt = StartTime();

    private static readonly AnalysisJobStatus[] OpenStatuses =
        [AnalysisJobStatus.Queued, AnalysisJobStatus.Running, AnalysisJobStatus.Paused];

    /// <summary>Zwischengespeicherte Übersicht (höchstens <see cref="CacheFor"/> alt).</summary>
    public async Task<EngineOverviewDto> GetAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out EngineOverviewDto? hit) && hit is not null) return hit;
        var dto = await BuildAsync(DateTime.UtcNow, ct);
        cache.Set(CacheKey, dto, CacheFor);
        return dto;
    }

    /// <summary>Zwischenspeicher verwerfen — nach einer Änderung der Hintergrund-Zuordnung (0.951.0), damit die Seite sofort
    /// den neuen Stand zeigt.</summary>
    public void Invalidate() => cache.Remove(CacheKey);

    /// <summary>Stufenname wie im Frontend (<c>admin.engines.kind.*</c>).</summary>
    public static string KindOf(JobPriority p) => p switch
    {
        JobPriority.Interactive => "interactive",
        JobPriority.Waiting => "waiting",
        JobPriority.ClubNew => "clubNew",
        JobPriority.ClubBackfill => "clubBackfill",
        JobPriority.League => "league",
        JobPriority.Master => "master",
        _ => "other",
    };

    /// <summary>Quelle einer Kennung: <c>rookhub</c> (rhe_), <c>lichess</c> (eei_), sonst <c>other</c>.</summary>
    public static string SourceOf(string engineId) =>
        ProviderSecrets.IsLocalEngineId(engineId) ? "rookhub"
        : engineId.StartsWith("eei_", StringComparison.Ordinal) ? "lichess"
        : "other";

    /// <summary>Ab so vielen fertigen Aufträgen in der letzten Stunde gilt die Abschätzung „Warteschlange leer in …“.</summary>
    public const int EstimateMinDonePerHour = 10;

    /// <summary>Ein Engine-Token gilt nach so langer Zeit ohne Zugriff als verwaist (Hinweis oben).</summary>
    public static readonly TimeSpan StaleTokenAfter = TimeSpan.FromDays(30);

    /// <summary>Ohne Zwischenspeicher (öffentlich für Tests).</summary>
    public async Task<EngineOverviewDto> BuildAsync(DateTime now, CancellationToken ct)
    {
        // ── Datenbank ────────────────────────────────────────────────────────────────────────────────────────
        var regs = await db.ExternalEngineRegistrations.AsNoTracking()
            .Select(r => new { r.Id, r.UserId, r.Name, r.ProviderSelector, r.MaxThreads, r.MaxHash, r.Variants, r.CreatedAt, r.LastSeenAt, r.ApiTokenId })
            .ToListAsync(ct);
        var creds = await db.LichessEngineCredentials.AsNoTracking()
            .Where(c => c.BackgroundEngineIds != null)
            .Select(c => new { c.UserId, c.BackgroundEngineIds, c.ShareAsHouseEngine })
            .ToListAsync(ct);
        var groups = await db.AnalysisJobs.AsNoTracking()
            .Where(j => OpenStatuses.Contains(j.Status))
            .GroupBy(j => new { j.EngineId, j.Status, j.Priority, j.Batch })
            .Select(g => new { g.Key.EngineId, g.Key.Status, g.Key.Priority, g.Key.Batch, Count = g.Count(), Oldest = g.Min(j => j.CreatedAt) })
            .ToListAsync(ct);
        var running = await db.AnalysisJobs.AsNoTracking()
            .Where(j => j.Status == AnalysisJobStatus.Running)
            .Select(j => new
            {
                j.Id, j.UserId, j.EngineId, j.Title, j.Fen, j.Priority, j.Background, j.Batch, j.TargetDepth, j.TargetNodes,
                j.MultiPv, j.ReachedDepth, j.CurrentDepth, j.CurrentNps, j.SecondsSpent, j.LastRunAt,
            })
            .ToListAsync(ct);
        var runningIds = running.Select(r => (int?)r.Id).ToList();
        var positions = runningIds.Count == 0 ? []
            : await db.GameAnalysisPositions.AsNoTracking()
                .Where(p => runningIds.Contains(p.AnalysisJobId))
                .Select(p => new { JobId = p.AnalysisJobId!.Value, p.GameAnalysisId, p.Ply })
                .ToListAsync(ct);
        var gameIds = positions.Select(p => p.GameAnalysisId).Distinct().ToList();
        var games = gameIds.Count == 0 ? new Dictionary<int, string?>()
            : (await db.GameAnalyses.AsNoTracking().Where(g => gameIds.Contains(g.Id))
                .Select(g => new { g.Id, g.Title, g.White, g.Black }).ToListAsync(ct))
                .ToDictionary(g => g.Id, g => GameTitle(g.Title, g.White, g.Black));
        var openGames = await db.GameAnalyses.AsNoTracking()
            .Where(g => g.Status == GameAnalysisStatus.Pending || g.Status == GameAnalysisStatus.Running)
            .GroupBy(g => g.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        // Zeitpläne: eine Zeile je gemeldeter Engine (klein). Engine-Tokens: eine Handvoll Zeilen, ohne Hash.
        var schedules = await db.EngineClientSchedules.AsNoTracking().ToListAsync(ct);
        var tokens = await db.UserApiTokens.AsNoTracking()
            .Where(t => t.Scope == Accounts.ApiTokenService.EngineScope)
            .Select(t => new { t.Id, t.UserId, t.Name, t.Prefix, t.CreatedAt, t.LastUsedAt, t.ExpiresAt })
            .ToListAsync(ct);

        // Durchsatz: die Körbe der letzten 7 Tage EINMAL laden (Index BucketStart) und im Speicher in Stunde/Tag/Woche
        // und normal/Schonzeit aufteilen; dazu die noch nicht geschriebenen Zuwächse.
        var buckets = await LoadBucketsAsync(now.AddDays(-7), ct);
        buckets.AddRange(throughput.Pending().Where(b => b.BucketStart >= now.AddDays(-7)));

        var userIds = regs.Select(r => r.UserId).Concat(creds.Select(c => c.UserId)).Concat(running.Select(r => r.UserId))
            .Concat(tokens.Select(t => t.UserId)).Distinct().ToList();
        var names = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.AppUsers.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        string? NameOf(int id) => names.TryGetValue(id, out var n) ? n : null;

        // ── Speicher ─────────────────────────────────────────────────────────────────────────────────────────
        var queues = hub.QueueSnapshot();
        var liveStreams = tracker.LiveEngines();
        var liveRuns = live.Snapshot(now);
        var explicitOnly = ExplicitOnlyEngines.From(config);
        var p = await pause.GetAsync(ct);

        // ── Zusammenführen ───────────────────────────────────────────────────────────────────────────────────
        var backgroundFor = new Dictionary<string, List<EngineOverviewUserDto>>(StringComparer.Ordinal);
        var house = new HashSet<string>(StringComparer.Ordinal);
        var listOf = new Dictionary<int, List<string>>();
        foreach (var c in creds)
        {
            var list = c.BackgroundEngineIds!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal).ToList();
            listOf[c.UserId] = list;
            foreach (var id in list)
            {
                if (!backgroundFor.TryGetValue(id, out var users)) backgroundFor[id] = users = [];
                users.Add(new EngineOverviewUserDto(c.UserId, NameOf(c.UserId)));
                if (c.ShareAsHouseEngine) house.Add(id);
            }
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        ids.UnionWith(regs.Select(r => r.Id));
        ids.UnionWith(backgroundFor.Keys);
        ids.UnionWith(groups.Select(g => g.EngineId));
        ids.UnionWith(liveStreams.Keys);
        ids.UnionWith(buckets.Select(b => b.EngineId));
        ids.Remove(string.Empty);

        var regById = regs.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var scheduleOf = new Dictionary<string, EngineClientSchedule>(StringComparer.Ordinal);
        foreach (var r in regs)
        {
            var sch = schedules.FirstOrDefault(x => x.UserId == r.UserId && x.EngineName == r.Name);
            if (sch is not null) scheduleOf[r.Id] = sch;
        }
        var bucketsOf = buckets.GroupBy(b => b.EngineId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        bool Reduced(string engineId, DateTime start) =>
            EngineOverviewSchedules.IsReduced(scheduleOf.GetValueOrDefault(engineId), quiet, start);

        // Wartende je Besitzer der Engine (für den Leerlauf-Grund): gesamt und ohne Stapel.
        int? OwnerOf(string engineId) => regById.TryGetValue(engineId, out var r) ? r.UserId
            : backgroundFor.TryGetValue(engineId, out var b) && b.Count == 1 ? b[0].UserId : null;
        var waitingByOwner = new Dictionary<int, (int All, int NonBatch)>();
        foreach (var g in groups.Where(g => g.Status != AnalysisJobStatus.Running))
        {
            if (OwnerOf(g.EngineId) is not { } o) continue;
            var cur = waitingByOwner.GetValueOrDefault(o);
            waitingByOwner[o] = (cur.All + g.Count, cur.NonBatch + (g.Batch ? 0 : g.Count));
        }

        var engines = new List<EngineOverviewEngineDto>();
        var idleCandidates = new List<(int Index, int Owner)>();
        foreach (var id in ids)
        {
            regById.TryGetValue(id, out var reg);
            var source = SourceOf(id);
            var bg = backgroundFor.TryGetValue(id, out var bgl) ? bgl.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList() : [];
            int? ownerId = reg?.UserId ?? (bg.Count == 1 ? bg[0].UserId : null);

            DateTime? lastSeen = null;
            bool? online = null;
            var polling = 0;
            var brokerQueued = 0;
            if (reg is not null)
            {
                lastSeen = directory.LastSeen(reg.ProviderSelector) ?? reg.LastSeenAt;
                online = lastSeen is { } t && now - t <= brokerOptions.OnlineWindow;
                if (queues.TryGetValue(reg.ProviderSelector, out var q)) { brokerQueued = q.Queued; polling = q.Polling; }
            }
            else if (source == "rookhub") online = false;   // Registrierung weg: niemand kann mehr abholen

            var mine = groups.Where(g => g.EngineId == id).ToList();
            var job = running.FirstOrDefault(r => r.EngineId == id);
            EngineOverviewCurrentJobDto? current = null;
            if (job is not null)
            {
                liveRuns.TryGetValue(job.Id, out var lr);
                var pos = positions.FirstOrDefault(x => x.JobId == job.Id);
                current = new EngineOverviewCurrentJobDto(job.Id, job.UserId, NameOf(job.UserId), job.Title, job.Fen,
                    KindOf(job.Priority), job.Background, job.Batch, job.TargetDepth, job.TargetNodes, job.MultiPv, job.ReachedDepth,
                    lr is { Depth: > 0 } ? lr.Depth : job.CurrentDepth,
                    lr is { Nps: > 0 } ? lr.Nps : job.CurrentNps,
                    lr?.Nodes ?? 0,
                    lr?.Seconds ?? job.SecondsSpent,
                    lr?.StartedUtc ?? job.LastRunAt,
                    pos?.GameAnalysisId, pos?.Ply,
                    pos is not null && games.TryGetValue(pos.GameAnalysisId, out var gt) ? gt : null);
            }
            var liveCount = liveStreams.TryGetValue(id, out var lc) ? lc : 0;
            var state = current is not null ? "working"
                : liveCount > 0 ? "live"
                : online == true ? "idle"
                : online == false ? "offline"
                : "unknown";
            var waiting = mine.Where(g => g.Status != AnalysisJobStatus.Running).ToList();
            var counters = hub.Stats.For(id);
            var own = bucketsOf.GetValueOrDefault(id) ?? [];
            scheduleOf.TryGetValue(id, out var schedule);

            engines.Add(new EngineOverviewEngineDto(
                id, reg?.Name, source, reg is not null, ownerId, ownerId is { } oid ? NameOf(oid) : null,
                online, lastSeen, reg?.CreatedAt, reg?.MaxThreads, reg?.MaxHash,
                reg is null ? [] : reg.Variants.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                bg, house.Contains(id), explicitOnly.Contains(id), state, liveCount, polling, brokerQueued, current,
                waiting.Where(g => g.Status == AnalysisJobStatus.Queued).Sum(g => g.Count),
                waiting.Where(g => g.Status == AnalysisJobStatus.Paused).Sum(g => g.Count),
                waiting.Count == 0 ? null : waiting.Min(g => g.Oldest),
                new EngineOverviewThroughputDto(ToWindow(Sum(own, now.AddHours(-1))), ToWindow(Sum(own, now.AddDays(-1))),
                    ToWindow(Sum(own, now.AddDays(-7)))),
                counters is null ? null : Counters(counters),
                EngineOverviewSchedules.MachineKey(ownerId, source, reg?.Name), // endgültig unten (MachineKeys)
                schedule is null ? null : EngineOverviewSchedules.ToDto(schedule, now),
                null,
                SplitMany(own, Reduced),
                reg?.ApiTokenId));
            if (state == "idle" && ownerId is { } owner) idleCandidates.Add((engines.Count - 1, owner));
        }

        // Leerlauf-Grund: dieselbe Prüfung wie die Vergabe (EngineAvailability.UsableAsync auf der Liste des Besitzers).
        var nowOffset = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
        foreach (var byOwner in idleCandidates.GroupBy(c => c.Owner))
        {
            var list = ExplicitOnlyEngines.Automatic(listOf.GetValueOrDefault(byOwner.Key) ?? [], explicitOnly);
            var usableAll = (await EngineAvailability.UsableAsync(db, byOwner.Key, list, batch: false, quiet, nowOffset, ct)).ToHashSet(StringComparer.Ordinal);
            var usableBatch = (await EngineAvailability.UsableAsync(db, byOwner.Key, list, batch: true, quiet, nowOffset, ct)).ToHashSet(StringComparer.Ordinal);
            var w = waitingByOwner.GetValueOrDefault(byOwner.Key);
            foreach (var (index, owner) in byOwner)
            {
                var e = engines[index];
                engines[index] = e with
                {
                    IdleReason = EngineOverviewSchedules.IdleReason(e.Id, p.Paused, e.ExplicitOnly,
                        listOf.GetValueOrDefault(owner)?.Contains(e.Id) ?? false,
                        e.Schedule is not null, usableAll, usableBatch, w.All, w.NonBatch),
                };
            }
        }

        // Maschinen-Schlüssel erst mit ALLEN Engines: ob ein Stamm nach Token getrennt wird, hängt an den übrigen.
        var machineKeys = EngineOverviewSchedules.MachineKeys(
            engines.Select(e => (e.OwnerUserId, e.Source, e.ApiTokenId, e.Name)).ToList());
        var splitKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < engines.Count; i++)
        {
            engines[i] = engines[i] with { MachineKey = machineKeys[i].Key };
            if (machineKeys[i].Split) splitKeys.Add(machineKeys[i].Key);
        }

        engines = engines
            .OrderBy(e => StateRank(e.State))
            .ThenBy(e => e.OwnerName ?? "￿", StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name ?? e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var waitingAll = groups.Where(g => g.Status != AnalysisJobStatus.Running).ToList();
        var byKind = groups.GroupBy(g => g.Priority).OrderBy(g => g.Key)
            .Select(g => new EngineOverviewQueueKindDto(KindOf(g.Key),
                g.Where(x => x.Status == AnalysisJobStatus.Queued).Sum(x => x.Count),
                g.Where(x => x.Status == AnalysisJobStatus.Paused).Sum(x => x.Count),
                g.Where(x => x.Status == AnalysisJobStatus.Running).Sum(x => x.Count),
                g.Any(x => x.Status != AnalysisJobStatus.Running) ? g.Where(x => x.Status != AnalysisJobStatus.Running).Min(x => x.Oldest) : null))
            .ToList();
        var queue = new EngineOverviewQueueDto(
            groups.Where(g => g.Status == AnalysisJobStatus.Queued).Sum(g => g.Count),
            groups.Where(g => g.Status == AnalysisJobStatus.Paused).Sum(g => g.Count),
            groups.Where(g => g.Status == AnalysisJobStatus.Running).Sum(g => g.Count),
            waitingAll.Count == 0 ? null : waitingAll.Min(g => g.Oldest),
            byKind,
            openGames.Where(g => g.Status == GameAnalysisStatus.Pending).Sum(g => g.Count),
            openGames.Where(g => g.Status == GameAnalysisStatus.Running).Sum(g => g.Count));

        var totals = new EngineOverviewThroughputDto(ToWindow(Sum(buckets, now.AddHours(-1))), ToWindow(Sum(buckets, now.AddDays(-1))),
            ToWindow(Sum(buckets, now.AddDays(-7))));

        // ── Maschinen ────────────────────────────────────────────────────────────────────────────────────────
        var tokenById = tokens.ToDictionary(t => t.Id);
        var machines = engines.GroupBy(e => e.MachineKey).Select(g =>
        {
            var list = g.ToList();
            var first = list[0];
            var name = first.Source switch
            {
                "rookhub" when first.Name is not null => EngineOverviewSchedules.Stem(list.Select(e => e.Name!).OrderBy(n => n.Length).First()),
                "lichess" => "Lichess",
                _ => "—",
            };
            var tokenId = list.Select(e => e.ApiTokenId).FirstOrDefault(t => t is not null);
            var tokenName = tokenId is { } tid && tokenById.TryGetValue(tid, out var tok) ? tok.Name : null;
            // Zwei bekannte Tokens unter einem Stamm: der Kopf nennt den Token, sonst hießen beide gleich.
            if (splitKeys.Contains(g.Key) && tokenId is { } splitId) name = $"{name} · {tokenName ?? $"#{splitId}"}";
            var own = list.SelectMany(e => bucketsOf.GetValueOrDefault(e.Id) ?? []).ToList();
            var day = Sum(own, now.AddDays(-1));
            var reported = list.Select(e => scheduleOf.GetValueOrDefault(e.Id)).OfType<EngineClientSchedule>().ToList();
            var registered = list.Count(e => e.Registered);
            return new EngineOverviewMachineDto(g.Key, name, first.OwnerUserId, first.OwnerName, first.Source, tokenId,
                tokenName,
                list.Select(e => e.Id).ToList(),
                list.Count(e => e.Online == true), list.Count, list.Count(e => e.State == "working"),
                list.Sum(e => e.MaxThreads ?? 0), day.Done,
                registered == 0 ? null : (int)Math.Min(100, Math.Round(day.RunSeconds * 100.0 / (registered * 86_400.0))),
                SplitMany(own, Reduced), EngineOverviewSchedules.MachineSchedule(reported, now));
        })
        .OrderByDescending(m => m.Working).ThenByDescending(m => m.Online)
        .ThenBy(m => m.OwnerName ?? "￿", StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

        // ── Tokens ───────────────────────────────────────────────────────────────────────────────────────────
        var tokenDtos = tokens
            .Select(t => new EngineOverviewTokenDto(t.Id, t.Name, t.Prefix, t.UserId, NameOf(t.UserId), t.CreatedAt, t.LastUsedAt,
                t.ExpiresAt, t.ExpiresAt is { } x && x < now,
                engines.Where(e => e.ApiTokenId == t.Id).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(e => new EngineOverviewTokenEngineDto(e.Id, e.Name ?? e.Id, e.Online)).ToList()))
            .OrderBy(t => t.OwnerName ?? "￿", StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ── Hinweise ─────────────────────────────────────────────────────────────────────────────────────────
        var hints = new List<EngineOverviewHintDto>();
        var stuck = engines.Where(e => e.WaitingQueued + e.WaitingPaused > 0
                && (e.State == "offline" || (e.OwnerUserId is { } o && !(listOf.GetValueOrDefault(o)?.Contains(e.Id) ?? false))))
            .OrderByDescending(e => e.WaitingQueued + e.WaitingPaused).Take(10).ToList();
        foreach (var e in stuck)
        {
            var lastError = await db.AnalysisJobs.AsNoTracking()
                .Where(j => j.EngineId == e.Id && (j.Status == AnalysisJobStatus.Queued || j.Status == AnalysisJobStatus.Paused) && j.LastError != null)
                .OrderByDescending(j => j.UpdatedAt).Select(j => j.LastError).FirstOrDefaultAsync(ct);
            // Detail: Grund (offline | notInList), dahinter nach „|“ der letzte Fehlertext eines wartenden Auftrags.
            var why = e.State == "offline" ? "offline" : "notInList";
            hints.Add(new EngineOverviewHintDto("stuckJobs", e.Id, e.Name ?? e.Id, e.OwnerUserId, e.WaitingQueued + e.WaitingPaused,
                null, e.OldestWaitingSince, lastError is null ? why : $"{why}|{Truncate(lastError, 200)}"));
        }
        foreach (var m in machines.Where(m => m.Source == "rookhub"))
        {
            var orphan = engines.Where(e => m.EngineIds.Contains(e.Id) && e.Registered && e.BackgroundFor.Count == 0
                && (e.Schedule is { Slot: > 1 } || (e.Name ?? "").Contains("intergrund", StringComparison.OrdinalIgnoreCase)
                    || (e.Name ?? "").Contains("ackground", StringComparison.OrdinalIgnoreCase))).ToList();
            if (orphan.Count > 0)
                hints.Add(new EngineOverviewHintDto("notInList", null, m.Name, m.OwnerUserId, orphan.Count, m.Total, null,
                    string.Join(", ", orphan.Select(e => e.Name))));
        }
        foreach (var (owner, list) in listOf.Where(kv => kv.Value.Count * 2 >= LichessEngineCredential.MaxBackgroundEngines))
            hints.Add(new EngineOverviewHintDto("listCap", null, NameOf(owner), owner, list.Count, LichessEngineCredential.MaxBackgroundEngines, null, null));
        foreach (var t in tokenDtos.Where(t => !t.Expired && (t.LastUsedAt is null ? now - t.CreatedAt > StaleTokenAfter : now - t.LastUsedAt > StaleTokenAfter)))
            hints.Add(new EngineOverviewHintDto("staleToken", null, t.Name, t.OwnerUserId, t.Engines.Count, null, t.LastUsedAt, t.OwnerName));

        var waitingCount = queue.Queued + queue.Paused;
        double? emptyIn = totals.Hour.Done >= EstimateMinDonePerHour && waitingCount > 0
            ? Math.Round((double)waitingCount / totals.Hour.Done, 1, MidpointRounding.AwayFromZero) : null;

        var quietDto = new EngineOverviewQuietDto(quiet?.Enabled == true, quiet?.IsQuiet(nowOffset) == true,
            quiet is not null && quiet.IsQuiet(nowOffset) ? quiet.EndOf(nowOffset).UtcDateTime : null,
            quiet?.TimeZoneId ?? QuietHours.DefaultTimeZone,
            quiet?.Windows.Select(w => new EngineOverviewQuietWindowDto((int)w.Day, w.From.ToString(@"hh\:mm"), w.To.ToString(@"hh\:mm"))).ToList() ?? []);

        return new EngineOverviewDto(now, ServerStartedAt, EngineThroughputBucket.BucketMinutes,
            new EngineOverviewPauseDto(p.Paused, p.ForcedByConfig, p.Since, p.By), queue, totals, engines,
            machines, tokenDtos, hints, quietDto, emptyIn, LichessEngineCredential.MaxBackgroundEngines);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>Körbe ab <paramref name="since"/> (Index BucketStart) — einmal laden, im Speicher aufteilen.</summary>
    private async Task<List<EngineThroughputBucket>> LoadBucketsAsync(DateTime since, CancellationToken ct)
        => await db.EngineThroughputBuckets.AsNoTracking().Where(b => b.BucketStart >= since).ToListAsync(ct);

    private static EngineThroughputBucket Sum(IEnumerable<EngineThroughputBucket> rows, DateTime since)
    {
        var sum = new EngineThroughputBucket();
        foreach (var b in rows) if (b.BucketStart >= since) EngineThroughputRecorder.Add(sum, b);
        return sum;
    }

    /// <summary>kN/s normal/Schonzeit über die übergebenen Körbe (je Korb nach der Regel SEINER Engine eingeordnet).</summary>
    private static EngineSpeedSplitDto SplitMany(IEnumerable<EngineThroughputBucket> rows, Func<string, DateTime, bool> reduced)
    {
        long nN = 0, sN = 0, nR = 0, sR = 0;
        foreach (var b in rows)
        {
            if (b.NodeSeconds <= 0) continue;
            if (reduced(b.EngineId, b.BucketStart)) { nR += b.Nodes; sR += b.NodeSeconds; }
            else { nN += b.Nodes; sN += b.NodeSeconds; }
        }
        return new EngineSpeedSplitDto(sN > 0 ? Math.Round(nN / 1000.0 / sN, 1) : null, sR > 0 ? Math.Round(nR / 1000.0 / sR, 1) : null);
    }

    private static int StateRank(string state) => state switch
    {
        "working" => 0, "live" => 1, "idle" => 2, "unknown" => 3, _ => 4,
    };

    private static string? GameTitle(string? title, string? white, string? black)
    {
        if (!string.IsNullOrWhiteSpace(title)) return title;
        if (string.IsNullOrWhiteSpace(white) && string.IsNullOrWhiteSpace(black)) return null;
        return $"{white ?? "?"} – {black ?? "?"}";
    }

    internal static EngineThroughputWindowDto ToWindow(EngineThroughputBucket b) => new(
        b.Runs, b.Done, b.Failed, b.Interrupted, b.Retried, b.Unavailable, b.Timeouts, b.Errors, b.RunSeconds,
        b.Done > 0 ? Math.Round((double)b.DoneJobSeconds / b.Done, 1) : null,
        b.NodeSeconds > 0 ? Math.Round(b.Nodes / 1000.0 / b.NodeSeconds, 1) : null);

    private static EngineBrokerCountersDto Counters(BrokerStats.EngineCounters c)
    {
        lock (c)
            return new EngineBrokerCountersDto(c.Jobs, c.Completed, c.WithoutBestmove, c.RequesterGone, c.ProviderGone,
                c.ProviderTimeouts, c.QueueFull, c.OngoingExpired);
    }

    private static DateTime StartTime()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { return DateTime.UtcNow; }
    }
}
