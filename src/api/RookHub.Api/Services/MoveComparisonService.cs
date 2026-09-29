using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>Absage beim Anlegen eines Vergleichs — <see cref="Reason"/> ist der Code, den die Seite übersetzt.</summary>
public sealed class MoveComparisonException(string reason, string message) : Exception(message)
{
    public string Reason { get; } = reason;
}

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

    private const double MateScore = 100_000;

    private readonly AppDbContext _db;
    private readonly AnalysisJobService _jobs;
    private readonly IClaudeJsonClient _llm;
    private readonly MoveComparisonExplainJobs _running;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MoveComparisonService> _logger;

    public MoveComparisonService(AppDbContext db, AnalysisJobService jobs, IClaudeJsonClient llm,
        MoveComparisonExplainJobs running, IServiceScopeFactory scopes, ILogger<MoveComparisonService> logger)
    {
        _db = db;
        _jobs = jobs;
        _llm = llm;
        _running = running;
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>Begründungen nur mit einem Modell auf eigener Hardware (über Claude kostete jeder Vergleich Geld).</summary>
    public bool ExplanationsAvailable => _llm.IsConfigured && _llm.IsLocal;

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
        var rows = await _db.MoveComparisons.AsNoTracking().Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt).Take(20)
            .Select(c => new
            {
                c.Id, c.Fen, c.Title, c.Status, c.CreatedAt, c.BestUci,
                Moves = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate).OrderBy(l => l.Ordinal)
                    .Select(l => l.CandidateUci).ToList(),
            })
            .ToListAsync(ct);
        return rows.Select(r => new MoveComparisonSummaryDto(r.Id, r.Fen, r.Title, StatusText(r.Status), r.CreatedAt,
            r.Moves.Select(m => SanOf(r.Fen, m) ?? m).ToList(), r.BestUci is null ? null : SanOf(r.Fen, r.BestUci))).ToList();
    }

    public async Task<MoveComparisonDto?> GetAsync(int userId, int id, CancellationToken ct = default)
    {
        var c = await _db.MoveComparisons.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        return c is null ? null : await BuildDtoAsync(c, ct);
    }

    // ── Anlegen / Löschen ──────────────────────────────────────────────────────────────────────────

    public async Task<MoveComparisonDto> CreateAsync(int userId, CreateMoveComparisonRequest req, CancellationToken ct = default)
    {
        var fen = (req.Fen ?? string.Empty).Trim();
        if (fen.Length is 0 or > 120 || !AnalysisJobService.IsLegalFen(fen))
            throw new MoveComparisonException("invalid-fen", "Invalid FEN");
        var legal = ChessBoard.LoadFromFen(fen).Moves();
        if (legal.Length == 0) throw new MoveComparisonException("game-over", "No legal moves in this position");

        var ucis = new List<string>();
        foreach (var raw in req.Moves ?? [])
        {
            var move = FindMove(legal, raw) ?? throw new MoveComparisonException("invalid-move", $"Not a legal move: {raw}");
            var uci = GamePlies.ToUci(move);
            if (!ucis.Contains(uci)) ucis.Add(uci);
        }
        if (ucis.Count < MinCandidates) throw new MoveComparisonException("too-few-moves", $"At least {MinCandidates} moves");
        if (ucis.Count > MaxCandidates) throw new MoveComparisonException("too-many-moves", $"At most {MaxCandidates} moves");
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
            EngineOwnerUserId = owner == userId ? null : owner, CreatedAt = now, UpdatedAt = now,
        };
        for (var i = 0; i < ucis.Count; i++)
        {
            var after = PlayUci(fen, ucis[i])!;
            var line = new MoveComparisonLine { Kind = MoveComparisonLineKind.Candidate, CandidateUci = ucis[i], Ordinal = i, Fen = after };
            if (Terminal(after) is not null) line.State = MoveComparisonLineState.Done;   // setzt matt/patt — nichts zu rechnen
            comparison.Lines.Add(line);
        }
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
        var c = await _db.MoveComparisons.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (c is null) return false;
        foreach (var jobId in c.Lines.Where(l => l.AnalysisJobId != null).Select(l => l.AnalysisJobId!.Value))
            await _jobs.DeleteAsync(userId, jobId, ct);
        _db.MoveComparisons.Remove(c);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private Task<int> OpenCountAsync(int userId, CancellationToken ct)
        => _db.MoveComparisons.CountAsync(c => c.UserId == userId
            && c.Status != MoveComparisonStatus.Done && c.Status != MoveComparisonStatus.Failed, ct);

    /// <summary>Hält höchstens <see cref="MaxKeptPerUser"/> fertige Vergleiche je Nutzer — der älteste geht zuerst.</summary>
    private async Task TrimAsync(int userId, CancellationToken ct)
    {
        var old = await _db.MoveComparisons.Where(c => c.UserId == userId
                && (c.Status == MoveComparisonStatus.Done || c.Status == MoveComparisonStatus.Failed))
            .OrderByDescending(c => c.CreatedAt).Skip(MaxKeptPerUser - 1).ToListAsync(ct);
        if (old.Count > 0) _db.MoveComparisons.RemoveRange(old);
    }

    private async Task<int> CreateJobAsync(MoveComparison c, string fen, int multiPv, CancellationToken ct)
    {
        var job = await _jobs.CreateAsync(c.UserId, new CreateAnalysisJobRequest
        {
            Fen = fen, TargetDepth = c.Depth, MultiPv = multiPv, Title = JobTitle,
        }, ct, remember: false, engineOwnerUserId: c.EngineOwnerUserId ?? c.UserId);
        return job.Id;
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
            c.Status = ExplanationsAvailable && WeakerCandidates(c).Any() ? MoveComparisonStatus.Explaining : MoveComparisonStatus.Done;
            changed = true;
        }
        if (c.Status == MoveComparisonStatus.Explaining && !ExplanationsAvailable)
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
        var ranked = Ranked(c);
        if (ranked.Count == 0)
        {
            c.Status = MoveComparisonStatus.Failed;
            c.Error = "No candidate move could be calculated";
            return;
        }
        var best = ranked[0].Line;
        c.BestUci = best.CandidateUci;
        var bestOver = Terminal(best.Fen) is not null;
        foreach (var (weak, _) in ranked.Skip(1))
        {
            var replies = Pvs(weak.Fen, weak.ResultJson).Take(RepliesTested).ToList();
            for (var k = 0; k < replies.Count; k++)
            {
                var line = new MoveComparisonLine
                {
                    Kind = MoveComparisonLineKind.Reply, CandidateUci = weak.CandidateUci, ReplyUci = replies[k].Uci, Ordinal = k,
                };
                var after = bestOver ? null : PlayUci(best.Fen, replies[k].Uci);
                if (after is null) line.State = MoveComparisonLineState.Illegal;
                else
                {
                    line.Fen = after;
                    if (Terminal(after) is not null) line.State = MoveComparisonLineState.Done;
                }
                c.Lines.Add(line);
            }
        }
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

    /// <summary>Gerechnete Kandidaten, der beste (für die Seite am Zug) zuerst.</summary>
    private static List<(MoveComparisonLine Line, double Score)> Ranked(MoveComparison c)
    {
        var moverWhite = WhiteToMove(c.Fen);
        return c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate && l.State == MoveComparisonLineState.Done)
            .Select(l => (Line: l, Ev: EvalOf(l.Fen, l.ResultJson)))
            .Where(x => x.Ev is not null)
            .Select(x => (x.Line, Score: moverWhite ? x.Ev!.Score : -x.Ev!.Score))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Line.Ordinal)
            .ToList();
    }

    private static IEnumerable<MoveComparisonLine> WeakerCandidates(MoveComparison c)
        => c.BestUci is null ? [] : c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate
            && l.State == MoveComparisonLineState.Done && l.CandidateUci != c.BestUci);

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

    /// <summary>Je schwächerem Kandidaten eine Begründung schreiben lassen, dann fertig — auch wenn das Modell keine
    /// brauchbare liefert (sonst stünde der Vergleich für immer auf „wird erklärt").</summary>
    public async Task ExplainAsync(int id, CancellationToken ct)
    {
        var c = await _db.MoveComparisons.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null || c.Status != MoveComparisonStatus.Explaining) return;
        var system = SystemPrompt(c.Language);
        var pairs = WeakerCandidates(c).Select(w => (Line: w, Facts: PairFactsOf(c, w))).Where(p => p.Facts is not null).ToList();
        using var gate = new SemaphoreSlim(Parallel);
        var texts = await Task.WhenAll(pairs.Select(async p =>
        {
            await gate.WaitAsync(ct);
            try { return await ExplainPairAsync(p.Facts!, system, ct); }
            finally { gate.Release(); }
        }));
        for (var i = 0; i < pairs.Count; i++) pairs[i].Line.Explanation = texts[i];
        c.Status = MoveComparisonStatus.Done;
        c.Model = _llm.TranslationModel is { Length: > 80 } m ? m[..80] : _llm.TranslationModel;
        c.UpdatedAt = DateTime.UtcNow;
        c.FinishedAt = c.UpdatedAt;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Zugvergleich {Id}: {Saved} von {Total} Begruendungen", id, texts.Count(t => t != null), pairs.Count);
    }

    private async Task<string?> ExplainPairAsync(PairFacts facts, string system, CancellationToken ct)
    {
        var prompt = UserPrompt(facts);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var json = await _llm.CompleteJsonAsync("move-comparison", system,
                attempt == 0 ? prompt : prompt + "\n\nIMPORTANT: your previous answer mentioned a move that is not in the lines above. Mention ONLY moves that appear in the lines above.",
                Schema, 1500, ct);
            var text = TextOf(json);
            if (text != null && GameMoveExplanationService.MentionsOnly(text, facts.AllowedMoves())) return text;
            if (text != null) _logger.LogInformation("Zugvergleich: Begruendung verworfen (nennt einen fremden Zug): {Text}", text);
        }
        return null;
    }

    private static readonly JsonNode Schema = JsonNode.Parse(
        """{"type":"object","properties":{"explanation":{"type":"string"}},"required":["explanation"],"additionalProperties":false}""")!;

    private static string? TextOf(string? json)
    {
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.TryGetProperty("explanation", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()?.Trim() : null;
            return string.IsNullOrWhiteSpace(text) || text.Length > 1500 ? null : text;
        }
        catch (JsonException) { return null; }
    }

    // ── Fakten ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Eine Antwort nach dem besten Zug: geht sie, wie steht es dann (Sicht des Lesers), was antwortet er.</summary>
    internal sealed record ReplyTest(string ReplySan, MoveComparisonLineState State, string? Eval, IReadOnlyList<string> Answer);

    /// <summary>Alles, was das Modell für „warum ist <see cref="BestSan"/> besser als <see cref="WeakSan"/>" bekommt —
    /// Bewertungen aus Sicht des LESERS (der Seite am Zug), Linien als SAN.</summary>
    internal sealed record PairFacts(
        string Fen, bool MoverWhite, int Depth,
        string BestSan, string BestFen, string? BestEval, IReadOnlyList<Pv> BestReplies,
        string WeakSan, string WeakFen, string? WeakEval, IReadOnlyList<Pv> WeakReplies,
        IReadOnlyList<ReplyTest> Tests)
    {
        public IEnumerable<string> AllowedMoves() => new[] { BestSan, WeakSan }
            .Concat(BestReplies.SelectMany(p => p.Line)).Concat(WeakReplies.SelectMany(p => p.Line))
            .Concat(Tests.Select(t => t.ReplySan)).Concat(Tests.SelectMany(t => t.Answer));
    }

    private static PairFacts? PairFactsOf(MoveComparison c, MoveComparisonLine weak)
    {
        var best = c.Lines.FirstOrDefault(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == c.BestUci);
        if (best is null) return null;
        var moverWhite = WhiteToMove(c.Fen);
        var bestSan = SanOf(c.Fen, best.CandidateUci);
        var weakSan = SanOf(c.Fen, weak.CandidateUci);
        if (bestSan is null || weakSan is null) return null;
        var weakReplies = Pvs(weak.Fen, weak.ResultJson);
        var tests = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Reply && l.CandidateUci == weak.CandidateUci)
            .OrderBy(l => l.Ordinal)
            .Select(l =>
            {
                var san = weakReplies.FirstOrDefault(p => p.Uci == l.ReplyUci)?.San ?? l.ReplyUci ?? "?";
                if (l.State == MoveComparisonLineState.Illegal) return new ReplyTest(san, l.State, null, []);
                var pvs = Pvs(l.Fen, l.ResultJson);
                var ev = EvalOf(l.Fen, l.ResultJson);
                return new ReplyTest(san, l.State, ev is null ? null : MoverText(ev, moverWhite), pvs.FirstOrDefault()?.Line ?? []);
            })
            .ToList();
        return new PairFacts(c.Fen, moverWhite, c.Depth,
            bestSan, best.Fen, EvalOf(best.Fen, best.ResultJson) is { } be ? MoverText(be, moverWhite) : null, Pvs(best.Fen, best.ResultJson),
            weakSan, weak.Fen, EvalOf(weak.Fen, weak.ResultJson) is { } we ? MoverText(we, moverWhite) : null, weakReplies,
            tests);
    }

    internal static string SystemPrompt(string lang) =>
        $"""
        You are a friendly, precise chess coach. The reader is to move and considered two candidate moves; the engine
        rates one of them higher. Explain WHY it is better. Write in {GameMoveExplanationService.LanguageName(lang)}, three
        to five short sentences, at most 90 words:
        1. What the opponent's best answer to the weaker move achieves — name it and take what it does from the
           "Concretely" facts.
        2. Why that answer does not work (as well) against the better move: either it is not possible at all, or the
           reader's reply from the given line punishes it — name that reply and what it achieves, again from the facts.
        3. What it means in plain words, from the evaluations (e.g. the better move keeps a clear advantage, the weaker one
           only leads to an equal position). You may add the evaluations in pawns with a decimal POINT and one decimal, in
           every language (e.g. +0.8 against -0.4).
        If the facts say the two moves are almost equal, say that first and keep it short.
        Never give the evaluation itself as the reason: "the engine rates it higher" or "it keeps the advantage" only
        restate THAT it is better, not WHY. Claim an attack, a threat, a capture or a material gain ONLY if a "Concretely"
        fact says so — if none does, describe what the moves of the lines do (which piece goes where), without inventing
        tactics. Avoid empty phrases like "more active" or "better coordination" unless you say exactly what.
        Use ONLY the facts and lines given — never calculate your own variations and never mention a move that is not in
        the given lines. Name at most the first two moves of a line — never recite a whole engine line. Write moves
        exactly as given, in English algebraic notation (e.g. Nf3, Bxh7+, O-O); every other word in
        {GameMoveExplanationService.LanguageName(lang)}, no English words. Address the reader as "you", informally where the
        language distinguishes (German "du", French "tu", …); the other side is "your opponent". Do not mention which
        colour the reader plays. No centipawns, no percentages. Return the JSON object {"{"}"explanation": "..."{"}"}.
        """;

    internal static string UserPrompt(PairFacts f)
    {
        var you = f.MoverWhite ? "White" : "Black";
        var number = MoveNumber(f.Fen) + (f.MoverWhite ? "." : "...");
        var best = number + f.BestSan;
        var weak = number + f.WeakSan;
        var lines = new List<string>
        {
            $"Position (FEN): {f.Fen}",
            $"The reader plays {you} and is to move. The engine compared the reader's candidate moves at depth {f.Depth}.",
            $"Better move: {best} — evaluation from the reader's view afterwards: {f.BestEval ?? "?"}{Level(f.BestEval)}.",
            $"Weaker move: {weak} — evaluation from the reader's view afterwards: {f.WeakEval ?? "?"}{Level(f.WeakEval)}.",
        };
        if (ExplanationFacts.Parse(f.BestEval) is { Mate: null } b && ExplanationFacts.Parse(f.WeakEval) is { Mate: null } w
            && b.Level == w.Level && b.Pawns - w.Pawns < 0.3)
            lines.Add($"The two moves are almost equal (difference {(b.Pawns - w.Pawns).ToString("0.0", CultureInfo.InvariantCulture)} pawns).");

        if (f.WeakReplies.Count > 0)
        {
            lines.Add($"After {weak}, the opponent's best answers (evaluation from the reader's view, engine line):");
            foreach (var p in f.WeakReplies)
                lines.Add($"- {Numbered(f.WeakFen, [p.San])} ({p.MoverEval(f.MoverWhite)}): {Numbered(f.WeakFen, p.Line)}");
            var top = f.WeakReplies[0].Line;
            var facts = ExplanationFacts.FirstMoveAttacks(f.WeakFen, top)
                .Concat(ExplanationFacts.LineEvents(f.Fen, top, gainerWhite: !f.MoverWhite, lead: f.WeakSan)).ToList();
            if (facts.Count > 0)
                lines.Add($"Concretely, after {weak} and the opponent's best answer line: {string.Join("; ", facts)}.");
        }

        if (f.Tests.Count > 0)
        {
            lines.Add($"The same answers against {best}:");
            foreach (var t in f.Tests)
            {
                var reply = Numbered(f.BestFen, [t.ReplySan]);
                if (t.State == MoveComparisonLineState.Illegal) { lines.Add($"- {reply}: not possible after {best}."); continue; }
                if (t.State == MoveComparisonLineState.Failed || t.Eval is null) { lines.Add($"- {reply}: could not be calculated."); continue; }
                var answer = t.Answer.Count > 0 && AfterMove(f.BestFen, t.ReplySan) is { } afterReply
                    ? $" The reader's best reply, with the engine line: {Numbered(afterReply, t.Answer)}." : "";
                lines.Add($"- {reply}: evaluation from the reader's view {t.Eval}{Level(t.Eval)}.{answer}");
                if (t.Answer.Count > 0 && AfterMove(f.BestFen, t.ReplySan) is { } fenAfter)
                {
                    var facts = ExplanationFacts.FirstMoveAttacks(fenAfter, t.Answer)
                        .Concat(ExplanationFacts.LineEvents(f.BestFen, t.Answer, gainerWhite: f.MoverWhite, lead: t.ReplySan)).ToList();
                    if (facts.Count > 0) lines.Add($"  Concretely, after {best} {t.ReplySan} and the reader's reply line: {string.Join("; ", facts)}.");
                }
            }
        }
        if (f.BestReplies.Count > 0)
        {
            var p = f.BestReplies[0];
            lines.Add($"The opponent's best answer to {best} itself: {Numbered(f.BestFen, [p.San])} ({p.MoverEval(f.MoverWhite)}): {Numbered(f.BestFen, p.Line)}");
        }
        lines.Add($"Reader: plays {you} — \"you\" is the reader, {(f.MoverWhite ? "Black" : "White")} is \"your opponent\".");
        return string.Join('\n', lines);
    }

    private static string Level(string? moverEval)
        => ExplanationFacts.Parse(moverEval) is { } e ? $" ({ExplanationFacts.LevelName(e)})" : "";

    private static string MoveNumber(string fen)
        => fen.Split(' ') is { Length: >= 6 } parts && int.TryParse(parts[5], out var n) ? n.ToString(CultureInfo.InvariantCulture) : "?";

    private static string Numbered(string fen, IReadOnlyList<string> sans) => GameRecapService.Numbered(fen, sans);

    // ── Zeile lesen ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Eine Linie der Engine: erster Zug (UCI + SAN), Bewertung aus WEISS-Sicht, Linie als SAN.</summary>
    internal sealed record Pv(string Uci, string San, int? CpWhite, int? MateWhite, IReadOnlyList<string> Line)
    {
        public string MoverEval(bool moverWhite) => GameMistakes.EvalText(CpWhite, MateWhite, moverWhite);
    }

    /// <summary>Bewertung einer Stellung aus Weiß-Sicht: Rangwert (Matt ± <see cref="MateScore"/>) und Text wie auf dem
    /// Analysebrett.</summary>
    internal sealed record Ev(double Score, string Text, int? CpWhite, int? MateWhite, bool Checkmate);

    internal static List<Pv> Pvs(string fen, string? resultJson)
    {
        var result = new List<Pv>();
        if (string.IsNullOrEmpty(fen) || BrokerCandidates.Parse(resultJson, fen) is not { } cands) return result;
        var sign = WhiteToMove(fen) ? 1 : -1;   // Parse liefert die Sicht der Seite am Zug — zurück nach Weiß
        foreach (var cand in cands)
        {
            var line = GameMistakes.LineSans(fen, cand.Pv is { Count: > 0 } pv ? pv : [cand.Uci], LinePlies);
            if (line.Count == 0) continue;
            result.Add(new Pv(cand.Uci, line[0], cand.Cp * sign, cand.Mate * sign, line));
        }
        return result;
    }

    internal static Ev? EvalOf(string fen, string? resultJson)
    {
        if (Terminal(fen) is { } t) return t;
        var pv = Pvs(fen, resultJson).FirstOrDefault();
        if (pv is null) return null;
        if (pv.MateWhite is int m)
            return new Ev(m > 0 ? MateScore - m : -MateScore - m, "#" + m.ToString(CultureInfo.InvariantCulture), null, m, false);
        var cp = pv.CpWhite ?? 0;
        var v = cp / 100.0;
        return new Ev(cp, (v > 0 ? "+" : "") + v.ToString("0.00", CultureInfo.InvariantCulture), cp, null, false);
    }

    /// <summary>Matt oder Patt auf dem Brett (keine legalen Züge) — dann gibt es nichts zu rechnen.</summary>
    internal static Ev? Terminal(string fen)
    {
        if (string.IsNullOrEmpty(fen)) return null;
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            if (board.Moves().Length > 0) return null;
            var white = WhiteToMove(fen);
            var mated = white ? board.WhiteKingChecked : board.BlackKingChecked;
            if (!mated) return new Ev(0, "0.00", 0, null, false);
            return white ? new Ev(-MateScore, "#-0", null, null, true) : new Ev(MateScore, "#0", null, null, true);
        }
        catch { return null; }
    }

    /// <summary>Aus Sicht des Lesers, wie „Warum war das ein Fehler?": „+1.35", „mate in 3", „gets mated in 2", „mate".</summary>
    private static string MoverText(Ev e, bool moverWhite)
    {
        if (e.Checkmate) return (e.Score > 0) == moverWhite ? "mate" : "gets mated in 0";
        return GameMistakes.EvalText(e.CpWhite, e.MateWhite, moverWhite);
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
        (string? Json, int Depth) Current(MoveComparisonLine l)
            => l.AnalysisJobId is int id && jobs.TryGetValue(id, out var j) ? j : (l.ResultJson, l.ReachedDepth);

        var moverWhite = WhiteToMove(c.Fen);
        var best = c.Lines.FirstOrDefault(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == c.BestUci);
        var candidates = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate)
            .Select(l =>
            {
                var (json, depth) = Current(l);
                var ev = EvalOf(l.Fen, json);
                var replies = Pvs(l.Fen, json);
                var tests = c.Lines.Where(r => r.Kind == MoveComparisonLineKind.Reply && r.CandidateUci == l.CandidateUci)
                    .OrderBy(r => r.Ordinal)
                    .Select(r =>
                    {
                        var (rj, rd) = Current(r);
                        var san = replies.FirstOrDefault(p => p.Uci == r.ReplyUci)?.San ?? r.ReplyUci ?? "?";
                        var answer = r.State == MoveComparisonLineState.Illegal ? [] : Pvs(r.Fen, rj).FirstOrDefault()?.Line ?? [];
                        return new MoveComparisonTestDto(r.ReplyUci ?? "", san, StateText(r.State), rd,
                            r.State == MoveComparisonLineState.Illegal ? null : EvalOf(r.Fen, rj)?.Text, answer);
                    })
                    .ToList();
                var explanation = l.Explanation is null ? null : PieceLetters.Convert(l.Explanation, "en", c.Language);
                return (Dto: new MoveComparisonCandidateDto(l.CandidateUci, SanOf(c.Fen, l.CandidateUci) ?? l.CandidateUci,
                        StateText(l.State), depth, ev?.Text, l.CandidateUci == c.BestUci,
                        replies.Select(p => new MoveComparisonReplyDto(p.Uci, p.San, EvalText(p), p.Line)).ToList(), tests, explanation),
                    Score: ev is null ? double.MinValue : moverWhite ? ev.Score : -ev.Score, l.Ordinal, Ranked: l.State == MoveComparisonLineState.Done);
            })
            .ToList();
        // Erst gerechnet, dann nach Stärke — solange noch gerechnet wird, bleibt die Reihenfolge der Auswahl (sonst
        // sprängen die Zeilen mit jedem Zwischenstand).
        var ordered = c.BestUci is null
            ? candidates.OrderBy(x => x.Ordinal)
            : candidates.OrderByDescending(x => x.Ranked).ThenByDescending(x => x.Score).ThenBy(x => x.Ordinal);
        return new MoveComparisonDto(c.Id, c.Fen, c.Title, c.Depth, StatusText(c.Status), c.Error, moverWhite, c.BestUci,
            c.Language, ExplanationsAvailable, c.Lines.Count(l => l.State == MoveComparisonLineState.Pending), c.Lines.Count,
            c.CreatedAt, c.FinishedAt, ordered.Select(x => x.Dto).ToList());
    }

    private static string EvalText(Pv p)
    {
        if (p.MateWhite is int m) return "#" + m.ToString(CultureInfo.InvariantCulture);
        var v = (p.CpWhite ?? 0) / 100.0;
        return (v > 0 ? "+" : "") + v.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string StatusText(MoveComparisonStatus s) => s switch
    {
        MoveComparisonStatus.Candidates => "candidates",
        MoveComparisonStatus.Replies => "replies",
        MoveComparisonStatus.Explaining => "explaining",
        MoveComparisonStatus.Done => "done",
        _ => "failed",
    };

    private static string StateText(MoveComparisonLineState s) => s switch
    {
        MoveComparisonLineState.Pending => "pending",
        MoveComparisonLineState.Done => "done",
        MoveComparisonLineState.Illegal => "illegal",
        _ => "failed",
    };

    // ── Brett ──────────────────────────────────────────────────────────────────────────────────────

    private static bool WhiteToMove(string fen) => !(fen.Split(' ') is { Length: >= 2 } parts && parts[1] == "b");

    /// <summary>Ein legaler Zug zu einer UCI-Angabe — auch als König-schlägt-Turm (so schreibt der Broker Rochaden).</summary>
    internal static Move? FindMove(Move[] legal, string? raw)
    {
        var uci = (raw ?? "").Trim().ToLowerInvariant();
        if (uci.Length is < 4 or > 5) return null;
        var move = Array.Find(legal, m => GamePlies.ToUci(m) == uci);
        if (move != null) return move;
        var castle = uci switch { "e1h1" => "e1g1", "e1a1" => "e1c1", "e8h8" => "e8g8", "e8a8" => "e8c8", _ => null };
        return castle is null ? null : Array.Find(legal, m => GamePlies.ToUci(m) == castle && m.Parameter?.ShortStr is "O-O" or "O-O-O");
    }

    /// <summary>Die Stellung nach dem Zug, <c>null</c>, wenn er dort nicht geht.</summary>
    internal static string? PlayUci(string fen, string uci)
    {
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            var move = FindMove(board.Moves(), uci);
            if (move is null) return null;
            board.Move(move);
            return board.ToFen();
        }
        catch { return null; }
    }

    private static string? AfterMove(string fen, string san)
    {
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            var move = Array.Find(board.Moves(generateSan: true), m => m.San == san);
            if (move is null) return null;
            board.Move(move);
            return board.ToFen();
        }
        catch { return null; }
    }

    internal static string? SanOf(string fen, string uci)
    {
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            var legal = board.Moves(generateSan: true);
            return FindMove(legal, uci)?.San;
        }
        catch { return null; }
    }
}

/// <summary>Welche Vergleiche gerade erklärt werden — damit die Pumpe keinen zweiten Lauf startet. Nur Arbeitsspeicher:
/// nach einem Neustart startet die Pumpe den Lauf neu.</summary>
public sealed class MoveComparisonExplainJobs
{
    private readonly ConcurrentDictionary<int, byte> _running = new();
    public bool TryStart(int id) => _running.TryAdd(id, 0);
    public void Finish(int id) => _running.TryRemove(id, out _);
    public bool IsRunning(int id) => _running.ContainsKey(id);
}
