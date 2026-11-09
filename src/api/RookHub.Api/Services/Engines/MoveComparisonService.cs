using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.DTOs.Engines;
using static RookHub.Api.Services.Engines.MoveComparisonBoard;
using static RookHub.Api.Services.Engines.MoveComparisonEval;
using static RookHub.Api.Services.Engines.MoveComparisonSteps;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// „Züge vergleichen" (0.602.0, Wunsch 2026-09-29): „soll diese durchrechnen und schaun, warum Zug 1 besser ist als
/// Zug 2 — vor allem im Vergleich: was sind bei Zug 2 die besten Züge, und warum gehen die bei Zug 1 nicht (so gut)".
///
/// <para><b>Zwei Durchgänge über die Hintergrund-Aufträge</b> (<see cref="AnalysisJobService"/>, eigene oder Haus-Engine
/// wie die Punktepartie, <see cref="EngineOwnerResolver"/>): (1) je Kandidat die Stellung DANACH mit
/// <see cref="CandidateLines"/> Linien — das sind zugleich die besten Antworten des Gegners; daraus der beste Kandidat.
/// (2) Je SCHWÄCHEREM Kandidaten dessen <see cref="RepliesTested"/> beste Antworten, gespielt nach dem BESTEN: geht die
/// Antwort dort gar nicht, oder was antwortet man selbst, und wie steht es dann? Genau das ist das „warum".</para>
/// <para><b>Danach schreibt das Modell auf eigener Hardware</b> je schwächerem Kandidaten zwei bis fünf Sätze, nur aus
/// GEPRÜFTEN Fakten (<see cref="ExplanationFacts"/>: was der erste Zug angreift, Schach, Matt, Materialbilanz) und nur mit
/// Zügen aus den Linien (<see cref="GameMoveExplanationService.MentionsOnly"/>) — dieselben Regeln wie „Warum war das ein
/// Fehler?". Ohne Modell endet der Vergleich ohne Text; die gerechneten Linien stehen trotzdem da.</para>
/// <para>Weitergetrieben wird ausschließlich von der Pumpe (<see cref="MoveComparisonPumpService"/>, ein Schreiber): sie
/// sammelt fertige Aufträge ein, kopiert das Ergebnis in die Zeile und LÖSCHT den Auftrag — in der Auftragsliste haben
/// die Teilrechnungen nichts verloren, und der Trimmer der Auftragsliste nähme sie sonst irgendwann mit.</para>
/// </summary>
public sealed class MoveComparisonService
{
    public const int MinCandidates = 2;
    public const int MaxCandidates = 4;
    /// <summary>Linien je Kandidat = so viele beste Antworten des Gegners werden gezeigt.</summary>
    public const int CandidateLines = 3;
    /// <summary>So viele dieser Antworten je schwächerem Kandidaten werden gegen den besten gerechnet.</summary>
    public const int RepliesTested = 3;
    public const int DefaultDepth = 22;
    public const int MinDepth = 12;
    public const int MaxDepth = 30;
    /// <summary>Auf der Haus-Engine höchstens so tief — es ist fremde Rechenzeit (die Punktepartie nimmt dort fest 20).</summary>
    public const int HouseMaxDepth = 24;
    public const int MaxOpenPerUser = 3;
    /// <summary>So viele fertige Vergleiche bleiben je Nutzer, ältere werden beim Anlegen weggeräumt.</summary>
    public const int MaxKeptPerUser = 50;
    /// <summary>Halbzüge je gezeigter Linie.</summary>
    public const int LinePlies = 10;
    /// <summary>So viele Begründungen gleichzeitig ans Modell.</summary>
    public const int Parallel = 3;
    public const string JobTitle = "Zugvergleich";


    private readonly AppDbContext _db;
    private readonly AnalysisJobService _jobs;
    private readonly IClaudeJsonClient _llm;
    private readonly MoveComparisonExplainJobs _running;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MoveComparisonService> _logger;
    private readonly ClubFableExplanations? _fable;
    private readonly MoveComparisonExplainer _explainer;

    /// <param name="fable">Begründungen der Vereinspartien ab dem Stichtag über Claude Fable (0.911.0); <c>null</c> = nur lokal.</param>
    public MoveComparisonService(AppDbContext db, AnalysisJobService jobs, IClaudeJsonClient llm,
        MoveComparisonExplainJobs running, IServiceScopeFactory scopes, ILogger<MoveComparisonService> logger,
        ClubFableExplanations? fable = null)
    {
        _fable = fable;
        _db = db;
        _jobs = jobs;
        _llm = llm;
        _explainer = new MoveComparisonExplainer(llm, fable, logger);
        _running = running;
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>Begründungen nur mit einem Modell auf eigener Hardware (über Claude kostete jeder Vergleich Geld).</summary>
    public bool ExplanationsAvailable => _llm.IsConfigured && _llm.IsLocal;

    /// <summary>Kann für DIESEN Vergleich jemand begründen? Lokal immer, Fable nur für die Vergleiche der Vereinspartien (ob die
    /// Partie nach dem Stichtag liegt, entscheidet erst <see cref="ExplainAsync"/> — davor reicht „könnte").</summary>
    private bool CanExplain(MoveComparison c) => ExplanationsAvailable || (c.GameAnalysisId != null && _fable?.Available == true);

    // ── Lesen ──────────────────────────────────────────────────────────────────────────────────────

    public async Task<MoveComparisonStatusDto> StatusAsync(int userId, CancellationToken ct = default)
    {
        var owner = await EngineOwnerResolver.ResolveAsync(_db, userId, ct);
        var open = await OpenCountAsync(userId, ct);
        return new MoveComparisonStatusDto(owner is not null, owner == userId, ExplanationsAvailable, MaxCandidates,
            DefaultDepth, owner == userId ? MaxDepth : HouseMaxDepth, open, MaxOpenPerUser);
    }

    public async Task<List<MoveComparisonSummaryDto>> ListAsync(int userId, CancellationToken ct = default)
    {
        var rows = await _db.MoveComparisons.AsNoTracking().Where(c => c.UserId == userId && c.GameAnalysisId == null)
            .OrderByDescending(c => c.CreatedAt).Take(20)
            .Select(c => new
            {
                c.Id, c.Fen, c.Title, c.Status, c.CreatedAt, c.BestUci,
                Moves = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate).OrderBy(l => l.Ordinal)
                    .Select(l => l.CandidateUci).ToList(),
            })
            .ToListAsync(ct);
        return rows.Select(r => new MoveComparisonSummaryDto(r.Id, r.Fen, r.Title, MoveComparisonView.StatusText(r.Status), r.CreatedAt,
            r.Moves.Select(m => SanOf(r.Fen, m) ?? m).ToList(), r.BestUci is null ? null : SanOf(r.Fen, r.BestUci))).ToList();
    }

    public async Task<MoveComparisonDto?> GetAsync(int userId, int id, CancellationToken ct = default)
    {
        var c = await _db.MoveComparisons.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId && x.GameAnalysisId == null, ct);
        return c is null ? null : await BuildDtoAsync(c, ct);
    }

    // ── Anlegen / Löschen ──────────────────────────────────────────────────────────────────────────

    public async Task<MoveComparisonDto> CreateAsync(int userId, CreateMoveComparisonRequest req, CancellationToken ct = default)
    {
        var fen = (req.Fen ?? string.Empty).Trim();
        var ucis = CandidateUcis(fen, req.Moves);
        if (await OpenCountAsync(userId, ct) >= MaxOpenPerUser)
            throw new MoveComparisonException("too-many-open", "Too many comparisons still running");
        var owner = await EngineOwnerResolver.ResolveAsync(_db, userId, ct)
            ?? throw new MoveComparisonException("no-engine", "No engine available");
        var depth = Math.Clamp(req.Depth ?? DefaultDepth, MinDepth, owner == userId ? MaxDepth : HouseMaxDepth);

        var now = DateTime.UtcNow;
        var title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim();
        var comparison = new MoveComparison
        {
            UserId = userId, Fen = fen, Title = title is { Length: > 200 } ? title[..200] : title, Depth = depth,
            Language = GameMoveExplanationService.NormalizeLanguage(req.Lang), Status = MoveComparisonStatus.Candidates,
            EngineOwnerUserId = owner == userId ? null : owner, CreatedAt = now, UpdatedAt = now, Source = MoveComparisonSource.Engine,
        };
        foreach (var line in PlanCandidates(fen, ucis)) comparison.Lines.Add(line);
        _db.MoveComparisons.Add(comparison);
        await TrimAsync(userId, ct);
        await _db.SaveChangesAsync(ct);

        var created = new List<int>();
        try
        {
            foreach (var line in comparison.Lines.Where(l => l.State == MoveComparisonLineState.Pending))
            {
                line.AnalysisJobId = await CreateJobAsync(comparison, line.Fen, CandidateLines, ct);
                created.Add(line.AnalysisJobId.Value);
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Kein halber Vergleich: was schon angelegt ist, kommt wieder weg.
            foreach (var jobId in created) await _jobs.DeleteAsync(userId, jobId, CancellationToken.None);
            _db.MoveComparisons.Remove(comparison);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new MoveComparisonException(ex.Message.StartsWith("Too many", StringComparison.Ordinal) ? "too-many-jobs" : "no-engine", ex.Message);
        }
        _logger.LogInformation("Zugvergleich {Id} angelegt: {Moves} in {Fen}, Tiefe {Depth}", comparison.Id,
            string.Join(' ', ucis), fen, depth);
        await PumpOneAsync(comparison.Id, ct);   // eine Stellung, die matt setzt, braucht keinen Auftrag
        return (await GetAsync(userId, comparison.Id, ct))!;
    }

    public async Task<bool> DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var c = await _db.MoveComparisons.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId && x.GameAnalysisId == null, ct);
        if (c is null) return false;
        foreach (var jobId in c.Lines.Where(l => l.AnalysisJobId != null).Select(l => l.AnalysisJobId!.Value))
            await _jobs.DeleteAsync(userId, jobId, ct);
        _db.MoveComparisons.Remove(c);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private Task<int> OpenCountAsync(int userId, CancellationToken ct)
        => _db.MoveComparisons.CountAsync(c => c.UserId == userId && c.GameAnalysisId == null
            && c.Status != MoveComparisonStatus.Done && c.Status != MoveComparisonStatus.Failed, ct);

    /// <summary>Hält höchstens <see cref="MaxKeptPerUser"/> fertige Vergleiche je Nutzer — der älteste geht zuerst.</summary>
    private async Task TrimAsync(int userId, CancellationToken ct)
    {
        var old = await _db.MoveComparisons.Where(c => c.UserId == userId && c.GameAnalysisId == null
                && (c.Status == MoveComparisonStatus.Done || c.Status == MoveComparisonStatus.Failed))
            .OrderByDescending(c => c.CreatedAt).Skip(MaxKeptPerUser - 1).ToListAsync(ct);
        if (old.Count > 0) _db.MoveComparisons.RemoveRange(old);
    }

    /// <summary>Ein Auftrag des Vergleichs. Vergleiche einer Vereinspartie (<see cref="MoveComparison.GameAnalysisId"/>) rechnen
    /// als Stapel-Hintergrundarbeit wie die Analyse der Vereinspartien selbst (<see cref="AnalysisJob.Background"/> +
    /// <see cref="AnalysisJob.Batch"/>): nach allem, worauf jemand wartet, und in den Sperrzeiten der Haus-Engine nicht. Wartet
    /// eine Zusammenfassung der Partie darauf, nur Hintergrund, kein Stapel (<see cref="ClubMistakeComparisons.SummaryWantedAsync"/>).</summary>
    private async Task<int> CreateJobAsync(MoveComparison c, string fen, int multiPv, CancellationToken ct)
    {
        var club = c.GameAnalysisId != null;
        var wanted = club && await ClubMistakeComparisons.SummaryWantedAsync(_db, c.GameAnalysisId!.Value, ct);
        var batch = club && !wanted;
        JobPriority? priority = wanted ? JobPriority.Waiting
            : club ? JobPriorities.ForClub(await _db.GameAnalyses.Where(a => a.Id == c.GameAnalysisId)
                .Select(a => a.CreatedAt).FirstAsync(ct), DateTime.UtcNow)
            : null;
        var job = await _jobs.CreateAsync(c.UserId, new CreateAnalysisJobRequest
        {
            Fen = fen, TargetDepth = c.Depth, MultiPv = multiPv, Title = JobTitle,
        }, ct, remember: false, engineOwnerUserId: c.EngineOwnerUserId ?? c.UserId, background: club, batch: batch,
            priority: priority);
        return job.Id;
    }

    /// <summary>
    /// Einen wartenden Vergleich (<see cref="MoveComparisonStatus.Waiting"/>, Vereinspartien) starten: je offenem Kandidaten
    /// ein Auftrag, Status <see cref="MoveComparisonStatus.Candidates"/>. Scheitert ein Auftrag (Deckel, keine Engine), kommen
    /// die schon angelegten wieder weg, der Vergleich bleibt wartend und die Ausnahme geht an den Aufrufer.
    /// </summary>
    internal async Task StartWaitingAsync(MoveComparison c, CancellationToken ct)
    {
        // Aus der Analyse vorbefüllt (MoveComparisonFromAnalysis): Kandidaten fertig, die Antwort-Tests stehen schon da — nur
        // die offenen davon rechnen (eine Linie je Test), weiter ab „Antworten".
        var replies = c.Lines.Any(l => l.Kind == MoveComparisonLineKind.Reply);
        var created = new List<int>();
        try
        {
            foreach (var line in c.Lines.Where(l => l.Kind == (replies ? MoveComparisonLineKind.Reply : MoveComparisonLineKind.Candidate)
                && l.State == MoveComparisonLineState.Pending && l.AnalysisJobId == null))
            {
                line.AnalysisJobId = await CreateJobAsync(c, line.Fen, replies ? 1 : CandidateLines, ct);
                created.Add(line.AnalysisJobId.Value);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            foreach (var jobId in created) await _jobs.DeleteAsync(c.UserId, jobId, CancellationToken.None);
            foreach (var line in c.Lines) line.AnalysisJobId = null;
            throw;
        }
        c.Status = replies ? MoveComparisonStatus.Replies : MoveComparisonStatus.Candidates;
        c.Source ??= MoveComparisonSource.Engine;
        c.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await PumpOneAsync(c.Id, ct);   // eine Stellung, die matt setzt, braucht keinen Auftrag
    }

    /// <summary>
    /// Altbestand (<see cref="MoveComparison.Source"/> <c>null</c>): ein Vergleich einer Partie-Analyse, der noch wartet oder
    /// seine Kandidaten rechnet, bekommt, was die vertiefte Analyse schon weiß (<see cref="MoveComparisonFromAnalysis.Apply"/>);
    /// die Aufträge der übernommenen Zeilen kommen weg. Danach weiter wie gewohnt (Pumpe). → ob etwas übernommen wurde.
    /// </summary>
    internal async Task<bool> ApplyAnalysisAsync(MoveComparison c, IEnumerable<GameAnalysisPosition> positions, int minDepth, CancellationToken ct)
    {
        var jobs = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate && l.State == MoveComparisonLineState.Pending)
            .ToDictionary(l => l, l => l.AnalysisJobId);
        var filled = MoveComparisonFromAnalysis.Apply(c, positions, minDepth);
        foreach (var line in filled)
            if (jobs.TryGetValue(line, out var jobId) && jobId is int id)
            {
                await _jobs.DeleteAsync(c.UserId, id, ct);   // hält einen laufenden Auftrag an
                line.AnalysisJobId = null;
            }
        c.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        if (filled.Count > 0)
            _logger.LogInformation("Zugvergleich {Id} (Analyse {AnalysisId}, Halbzug {Ply}): {Count} Kandidaten aus der Analyse, Herkunft {Source}, Status {Status}",
                c.Id, c.GameAnalysisId, c.Ply, filled.Count, c.Source, c.Status);
        return filled.Count > 0;
    }

    /// <summary>Die Vergleiche zu den Fehlern einer Analyse (Vereinspartie), nach Halbzug — für jeden, der die Partie sieht
    /// (die Prüfung macht der Aufrufer).</summary>
    public async Task<List<ClubMistakeComparisonDto>> ListForAnalysisAsync(int analysisId, CancellationToken ct = default)
    {
        var rows = await _db.MoveComparisons.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.GameAnalysisId == analysisId && x.Ply != null).OrderBy(x => x.Ply).ToListAsync(ct);
        var result = new List<ClubMistakeComparisonDto>();
        foreach (var c in rows)
        {
            // Angelegt als [bester Zug der Analyse, Partiezug] (ClubMistakeComparisons.EnqueueAsync).
            var cands = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate).OrderBy(l => l.Ordinal).ToList();
            if (cands.Count < 2) continue;
            result.Add(new ClubMistakeComparisonDto(c.Ply!.Value, cands[1].CandidateUci, cands[0].CandidateUci, await BuildDtoAsync(c, ct)));
        }
        return result;
    }

    /// <summary>Ein Vergleich zu einem Fehler dieser Analyse; <c>null</c> = gibt es nicht (oder gehört zu einer anderen).</summary>
    public async Task<MoveComparisonDto?> GetForAnalysisAsync(int analysisId, int id, CancellationToken ct = default)
    {
        var c = await _db.MoveComparisons.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id && x.GameAnalysisId == analysisId, ct);
        return c is null ? null : await BuildDtoAsync(c, ct);
    }

    // ── Pumpe ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Alle offenen Vergleiche einen Schritt weiterbringen.</summary>
    public async Task<int> PumpAllAsync(CancellationToken ct = default)
    {
        var ids = await _db.MoveComparisons
            .Where(c => c.Status == MoveComparisonStatus.Candidates || c.Status == MoveComparisonStatus.Replies
                || c.Status == MoveComparisonStatus.Explaining)
            .OrderBy(c => c.Id).Select(c => c.Id).ToListAsync(ct);
        var moved = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try { if (await PumpOneAsync(id, ct)) moved++; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { _logger.LogWarning(ex, "Zugvergleich {Id}: Durchlauf uebersprungen", id); }
        }
        return moved;
    }

    /// <summary>Ein Vergleich: fertige Aufträge einsammeln, dann so weit wie möglich weiter (Kandidaten → Antworten →
    /// Begründungen → fertig).</summary>
    public async Task<bool> PumpOneAsync(int id, CancellationToken ct = default)
    {
        var c = await _db.MoveComparisons.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return false;
        var changed = await CollectAsync(c, ct);

        if (c.Status == MoveComparisonStatus.Candidates
            && c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate).All(l => l.State != MoveComparisonLineState.Pending))
        {
            await StartRepliesAsync(c, ct);
            changed = true;
        }
        if (c.Status == MoveComparisonStatus.Replies
            && c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Reply).All(l => l.State != MoveComparisonLineState.Pending))
        {
            c.Status = CanExplain(c) && WeakerCandidates(c).Any() ? MoveComparisonStatus.Explaining : MoveComparisonStatus.Done;
            changed = true;
        }
        if (c.Status == MoveComparisonStatus.Explaining && !CanExplain(c))
        {
            c.Status = MoveComparisonStatus.Done;
            changed = true;
        }
        if (changed)
        {
            c.UpdatedAt = DateTime.UtcNow;
            if (c.Status is MoveComparisonStatus.Done or MoveComparisonStatus.Failed) c.FinishedAt ??= c.UpdatedAt;
            await _db.SaveChangesAsync(ct);
        }
        if (c.Status == MoveComparisonStatus.Explaining) StartExplaining(c.Id);
        return changed;
    }

    /// <summary>Fertige Aufträge übernehmen: Ergebnis in die Zeile, Auftrag weg.</summary>
    private async Task<bool> CollectAsync(MoveComparison c, CancellationToken ct)
    {
        var open = c.Lines.Where(l => l.State == MoveComparisonLineState.Pending && l.AnalysisJobId != null).ToList();
        if (open.Count == 0) return false;
        var ids = open.Select(l => l.AnalysisJobId!.Value).ToList();
        var jobs = await _db.AnalysisJobs.Where(j => ids.Contains(j.Id)).ToDictionaryAsync(j => j.Id, ct);
        var changed = false;
        foreach (var line in open)
        {
            if (!jobs.TryGetValue(line.AnalysisJobId!.Value, out var job))
            {
                // Auftrag verschwunden (von Hand gelöscht, Konto-Trimmer) — mit dem, was da ist, weiter.
                line.State = line.ResultJson != null ? MoveComparisonLineState.Done : MoveComparisonLineState.Failed;
                line.AnalysisJobId = null;
                changed = true;
                continue;
            }
            if (job.Status is not (AnalysisJobStatus.Done or AnalysisJobStatus.Failed)) continue;
            line.ResultJson = job.ResultJson;
            line.ReachedDepth = job.ReachedDepth;
            line.State = !string.IsNullOrEmpty(job.ResultJson) ? MoveComparisonLineState.Done : MoveComparisonLineState.Failed;
            line.AnalysisJobId = null;
            _db.AnalysisJobs.Remove(job);
            changed = true;
        }
        return changed;
    }

    /// <summary>Kandidaten fertig: den besten bestimmen und je schwächerem Kandidaten dessen beste Antworten nach dem
    /// besten Zug einreihen.</summary>
    private async Task StartRepliesAsync(MoveComparison c, CancellationToken ct)
    {
        if (!AddReplyLines(c)) return;
        // Vereinspartie: die Antwort, die schon die Hauptvariante nach dem besten Zug ist, braucht keinen Auftrag (2026-11-09).
        if (c.GameAnalysisId != null) MoveComparisonFromAnalysis.DeriveRepliesFromBest(c);
        c.Status = MoveComparisonStatus.Replies;
        await _db.SaveChangesAsync(ct);
        foreach (var line in c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Reply && l.State == MoveComparisonLineState.Pending))
        {
            try { line.AnalysisJobId = await CreateJobAsync(c, line.Fen, 1, ct); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                _logger.LogWarning("Zugvergleich {Id}: Antwort {Reply} nicht eingereiht: {Message}", c.Id, line.ReplyUci, ex.Message);
                line.State = MoveComparisonLineState.Failed;
            }
        }
    }

    // ── Begründungen ───────────────────────────────────────────────────────────────────────────────

    private void StartExplaining(int id)
    {
        if (!_running.TryStart(id)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<MoveComparisonService>().ExplainAsync(id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Zugvergleich {Id}: Begruendungen gescheitert", id);
            }
            finally
            {
                _running.Finish(id);
            }
        });
    }

    /// <summary>Je schwächerem Kandidaten eine Begründung schreiben lassen (<see cref="MoveComparisonExplainer"/>), dann fertig.</summary>
    public async Task ExplainAsync(int id, CancellationToken ct)
    {
        var c = await _db.MoveComparisons.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null || c.Status != MoveComparisonStatus.Explaining) return;
        await _explainer.ExplainAsync(_db, c, ct);
    }

    // ── DTO ────────────────────────────────────────────────────────────────────────────────────────


    private async Task<MoveComparisonDto> BuildDtoAsync(MoveComparison c, CancellationToken ct)
    {
        // Solange gerechnet wird, zeigt die Seite den Zwischenstand des laufenden Auftrags.
        var jobIds = c.Lines.Where(l => l.AnalysisJobId != null).Select(l => l.AnalysisJobId!.Value).ToList();
        var jobs = jobIds.Count == 0 ? new Dictionary<int, (string? Json, int Depth)>()
            : await _db.AnalysisJobs.AsNoTracking().Where(j => jobIds.Contains(j.Id))
                .Select(j => new { j.Id, j.ResultJson, j.ReachedDepth, j.CurrentDepth })
                .ToDictionaryAsync(j => j.Id, j => (j.ResultJson, Math.Max(j.ReachedDepth, j.CurrentDepth)), ct);
        return MoveComparisonView.Build(c, jobs, ExplanationsAvailable);
    }

    // ── Brett ──────────────────────────────────────────────────────────────────────────────────────

    // Kurzwege für Aufrufer außerhalb des Bereichs (Games, League, Tactics) — die Logik liegt in MoveComparisonBoard.
    internal static Move? FindMove(Move[] legal, string? raw) => MoveComparisonBoard.FindMove(legal, raw);
    internal static string? PlayUci(string fen, string uci) => MoveComparisonBoard.PlayUci(fen, uci);
    internal static string? SanOf(string fen, string uci) => MoveComparisonBoard.SanOf(fen, uci);
}
