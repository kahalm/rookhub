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
    IConfiguration config)
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

    /// <summary>Ohne Zwischenspeicher (öffentlich für Tests).</summary>
    public async Task<EngineOverviewDto> BuildAsync(DateTime now, CancellationToken ct)
    {
        // ── Datenbank ────────────────────────────────────────────────────────────────────────────────────────
        var regs = await db.ExternalEngineRegistrations.AsNoTracking()
            .Select(r => new { r.Id, r.UserId, r.Name, r.ProviderSelector, r.MaxThreads, r.MaxHash, r.Variants, r.CreatedAt, r.LastSeenAt })
            .ToListAsync(ct);
        var creds = await db.LichessEngineCredentials.AsNoTracking()
            .Where(c => c.BackgroundEngineIds != null)
            .Select(c => new { c.UserId, c.BackgroundEngineIds, c.ShareAsHouseEngine })
            .ToListAsync(ct);
        var groups = await db.AnalysisJobs.AsNoTracking()
            .Where(j => OpenStatuses.Contains(j.Status))
            .GroupBy(j => new { j.EngineId, j.Status, j.Priority })
            .Select(g => new { g.Key.EngineId, g.Key.Status, g.Key.Priority, Count = g.Count(), Oldest = g.Min(j => j.CreatedAt) })
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

        var hourWin = await WindowAsync(now.AddHours(-1), ct);
        var dayWin = await WindowAsync(now.AddDays(-1), ct);
        var weekWin = await WindowAsync(now.AddDays(-7), ct);
        var pending = throughput.Pending();
        AddPending(hourWin, pending, now.AddHours(-1));
        AddPending(dayWin, pending, now.AddDays(-1));
        AddPending(weekWin, pending, now.AddDays(-7));

        var userIds = regs.Select(r => r.UserId).Concat(creds.Select(c => c.UserId)).Concat(running.Select(r => r.UserId))
            .Distinct().ToList();
        var names = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.AppUsers.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        string? NameOf(int id) => names.TryGetValue(id, out var n) ? n : null;

        // ── Speicher ─────────────────────────────────────────────────────────────────────────────────────────
        var queues = hub.QueueSnapshot();
        var liveStreams = tracker.LiveEngines();
        var liveRuns = live.Snapshot(now);
        var explicitOnly = ExplicitOnlyEngines.From(config);

        // ── Zusammenführen ───────────────────────────────────────────────────────────────────────────────────
        var backgroundFor = new Dictionary<string, List<EngineOverviewUserDto>>(StringComparer.Ordinal);
        var house = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in creds)
        {
            foreach (var id in c.BackgroundEngineIds!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal))
            {
                if (!backgroundFor.TryGetValue(id, out var list)) backgroundFor[id] = list = [];
                list.Add(new EngineOverviewUserDto(c.UserId, NameOf(c.UserId)));
                if (c.ShareAsHouseEngine) house.Add(id);
            }
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        ids.UnionWith(regs.Select(r => r.Id));
        ids.UnionWith(backgroundFor.Keys);
        ids.UnionWith(groups.Select(g => g.EngineId));
        ids.UnionWith(liveStreams.Keys);
        ids.UnionWith(hourWin.Keys);
        ids.UnionWith(dayWin.Keys);
        ids.UnionWith(weekWin.Keys);
        ids.Remove(string.Empty);

        var regById = regs.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var engines = new List<EngineOverviewEngineDto>();
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
                var pos = positions.FirstOrDefault(p => p.JobId == job.Id);
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

            engines.Add(new EngineOverviewEngineDto(
                id, reg?.Name, source, reg is not null, ownerId, ownerId is { } oid ? NameOf(oid) : null,
                online, lastSeen, reg?.CreatedAt, reg?.MaxThreads, reg?.MaxHash,
                reg is null ? [] : reg.Variants.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                bg, house.Contains(id), explicitOnly.Contains(id), state, liveCount, polling, brokerQueued, current,
                waiting.Where(g => g.Status == AnalysisJobStatus.Queued).Sum(g => g.Count),
                waiting.Where(g => g.Status == AnalysisJobStatus.Paused).Sum(g => g.Count),
                waiting.Count == 0 ? null : waiting.Min(g => g.Oldest),
                new EngineOverviewThroughputDto(WindowOf(hourWin, id), WindowOf(dayWin, id), WindowOf(weekWin, id)),
                counters is null ? null : Counters(counters)));
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

        var p = await pause.GetAsync(ct);
        return new EngineOverviewDto(now, ServerStartedAt, EngineThroughputBucket.BucketMinutes,
            new EngineOverviewPauseDto(p.Paused, p.ForcedByConfig, p.Since, p.By), queue,
            new EngineOverviewThroughputDto(Total(hourWin), Total(dayWin), Total(weekWin)), engines);
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

    /// <summary>Körbe ab <paramref name="since"/> je Engine summiert (Index BucketStart).</summary>
    private async Task<Dictionary<string, EngineThroughputBucket>> WindowAsync(DateTime since, CancellationToken ct)
    {
        var rows = await db.EngineThroughputBuckets.AsNoTracking()
            .Where(b => b.BucketStart >= since)
            .GroupBy(b => b.EngineId)
            .Select(g => new
            {
                EngineId = g.Key,
                Runs = g.Sum(b => b.Runs),
                Done = g.Sum(b => b.Done),
                Failed = g.Sum(b => b.Failed),
                Interrupted = g.Sum(b => b.Interrupted),
                Retried = g.Sum(b => b.Retried),
                Unavailable = g.Sum(b => b.Unavailable),
                Timeouts = g.Sum(b => b.Timeouts),
                Errors = g.Sum(b => b.Errors),
                RunSeconds = g.Sum(b => b.RunSeconds),
                DoneJobSeconds = g.Sum(b => b.DoneJobSeconds),
                Nodes = g.Sum(b => b.Nodes),
                NodeSeconds = g.Sum(b => b.NodeSeconds),
            })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.EngineId, r => new EngineThroughputBucket
        {
            EngineId = r.EngineId, Runs = r.Runs, Done = r.Done, Failed = r.Failed, Interrupted = r.Interrupted,
            Retried = r.Retried, Unavailable = r.Unavailable, Timeouts = r.Timeouts, Errors = r.Errors,
            RunSeconds = r.RunSeconds, DoneJobSeconds = r.DoneJobSeconds, Nodes = r.Nodes, NodeSeconds = r.NodeSeconds,
        }, StringComparer.Ordinal);
    }

    private static void AddPending(Dictionary<string, EngineThroughputBucket> window, IReadOnlyList<EngineThroughputBucket> pending,
        DateTime since)
    {
        foreach (var b in pending.Where(b => b.BucketStart >= since))
        {
            if (!window.TryGetValue(b.EngineId, out var row))
                window[b.EngineId] = row = new EngineThroughputBucket { EngineId = b.EngineId };
            EngineThroughputRecorder.Add(row, b);
        }
    }

    private static EngineThroughputWindowDto WindowOf(Dictionary<string, EngineThroughputBucket> window, string engineId)
        => window.TryGetValue(engineId, out var b) ? ToWindow(b) : ToWindow(new EngineThroughputBucket());

    private static EngineThroughputWindowDto Total(Dictionary<string, EngineThroughputBucket> window)
    {
        var sum = new EngineThroughputBucket();
        foreach (var b in window.Values) EngineThroughputRecorder.Add(sum, b);
        return ToWindow(sum);
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
