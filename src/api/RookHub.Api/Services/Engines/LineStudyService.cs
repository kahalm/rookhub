using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Engines;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// „Ohne Engine analysieren" (0.855.0, Wunsch 2026-10-27) — siehe <see cref="LineStudy"/>. Speichert den Zugbaum je Nutzer und
/// Partie (geprüft mit <see cref="AnalysisHistoryService.CheckTree"/>), legt für jede neue Stellung eine
/// <see cref="LineStudyPosition"/> an und gibt die Bewertungen erst nach dem Einschalten der Engine heraus. Die Stellungen rechnet
/// <see cref="StepAsync"/> (vom <see cref="LineStudyWorker"/> getaktet) über die normalen Hintergrund-Aufträge — gedrosselt, und
/// das Ergebnis wandert in die Stellung, damit die Auftragsliste des Nutzers (höchstens 200, älteste fliegen) nichts verliert.
/// </summary>
public partial class LineStudyService(AppDbContext db, AnalysisJobService jobs)
{
    /// <summary>So viele Bäume hält ein Konto (älteste fliegen) — einer je analysierter Partie.</summary>
    public const int MaxStudiesPerUser = 300;
    /// <summary>So viele Aufträge aus Linien laufen je Nutzer gleichzeitig — Platz für eigene Aufträge bleibt.</summary>
    public const int MaxOpenJobsPerUser = 20;
    /// <summary>Gescheiterte Läufe je Stellung, danach bleibt sie ohne Bewertung.</summary>
    public const int MaxAttempts = 3;
    /// <summary>Tiefe und Linienzahl der Hintergrundrechnung: wie der schnelle Durchgang einer Partie-Analyse, mit zwei Linien
    /// (der Vergleich nennt den besseren Zug).</summary>
    public const int TargetDepth = GameAnalysisDefaults.SavedGameFastDepth;
    public const int MultiPv = 2;

    [GeneratedRegex("^[A-Za-z0-9:_-]{1,64}$")]
    private static partial Regex KeyPattern();

    private static readonly JsonSerializerOptions TreeJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<LineStudyDto?> GetAsync(int userId, string? gameKey, CancellationToken ct = default)
    {
        if (gameKey is null || !KeyPattern().IsMatch(gameKey)) return null;
        var study = await db.LineStudies.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId && s.GameKey == gameKey, ct);
        return study is null ? null : await ToDtoAsync(study, ct);
    }

    /// <summary>Anlegen oder ersetzen. <see cref="ArgumentException"/> bei ungültiger Kennung, Stellung oder ungültigem Baum.</summary>
    public async Task<LineStudyDto> SaveAsync(int userId, SaveLineStudyRequest req, CancellationToken ct = default)
    {
        if (req.GameKey is null || !KeyPattern().IsMatch(req.GameKey)) throw new ArgumentException("Invalid game key");
        var startFen = FenListParser.NormalizeFen(req.StartFen) ?? throw new ArgumentException("Invalid FEN");
        var tree = AnalysisHistoryService.CheckTree(startFen, req.Tree, req.Current);
        var title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim() is var t && t.Length > 200 ? t[..200] : t;

        var study = await db.LineStudies.Include(s => s.Positions)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.GameKey == req.GameKey, ct);
        var now = DateTime.UtcNow;
        if (study is null)
        {
            study = new LineStudy { UserId = userId, GameKey = req.GameKey, CreatedAt = now };
            db.LineStudies.Add(study);
            await TrimAsync(userId, ct);
        }
        study.StartFen = startFen;
        study.Title = title ?? study.Title;
        study.TreeJson = JsonSerializer.Serialize(tree.Tree, TreeJson);
        study.Current = tree.Current;
        study.UpdatedAt = now;

        // Jede Stellung des Baums einmal; was aus dem Baum verschwand und noch nicht gerechnet wird, fällt weg.
        var wanted = new Dictionary<string, string>();
        foreach (var fen in tree.Fens.Prepend(startFen))
            if (MarkedPositionService.KeyOf(fen) is { } key) wanted.TryAdd(key, fen);
        foreach (var gone in study.Positions.Where(p => !wanted.ContainsKey(p.PositionKey) && p.Status == LineStudyPositionStatus.Pending).ToList())
            study.Positions.Remove(gone);
        var have = study.Positions.Select(p => p.PositionKey).ToHashSet();
        foreach (var (key, fen) in wanted)
        {
            if (have.Contains(key)) continue;
            // Matt oder Patt: nichts zu rechnen — die Stellung ist sofort „gerechnet", ohne Kandidaten.
            var terminal = TacticsBoard.TryParse(fen) is { } b && b.LegalMoves().Count == 0;
            study.Positions.Add(new LineStudyPosition
            {
                PositionKey = key, Fen = fen, UpdatedAt = now,
                Status = terminal ? LineStudyPositionStatus.Done : LineStudyPositionStatus.Pending,
                CandidatesJson = terminal ? "[]" : null,
            });
        }
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(study, ct);
    }

    /// <summary>Engine einschalten — ab jetzt gibt <see cref="EvalsAsync"/> die Zahlen heraus. <c>null</c> = fremd/unbekannt.</summary>
    public async Task<LineStudyDto?> RevealAsync(int userId, int id, CancellationToken ct = default)
    {
        var study = await db.LineStudies.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (study is null) return null;
        study.RevealedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(study, ct);
    }

    /// <summary>Fortschritt und — erst nach dem Einschalten — die Bewertungen je Stellung. <c>null</c> = fremd/unbekannt.</summary>
    public async Task<LineStudyEvalsDto?> EvalsAsync(int userId, int id, CancellationToken ct = default)
    {
        var study = await db.LineStudies.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (study is null) return null;
        var rows = await db.LineStudyPositions.AsNoTracking().Where(p => p.LineStudyId == id)
            .Select(p => new { p.PositionKey, p.Fen, p.Status, p.CandidatesJson, p.Depth }).ToListAsync(ct);
        var result = new LineStudyEvalsDto
        {
            Revealed = study.RevealedAt is not null,
            Total = rows.Count,
            Analyzed = rows.Count(r => r.Status is LineStudyPositionStatus.Done or LineStudyPositionStatus.Failed),
        };
        if (!result.Revealed) return result;
        var byKey = new Dictionary<string, LineStudyEvalDto>();
        foreach (var r in rows.Where(r => r.Status == LineStudyPositionStatus.Done))
        {
            var ply = GameEvals.PlyOf(0, r.Fen, string.Empty, r.CandidatesJson, r.Depth);
            if (ply is null) continue;
            byKey[r.PositionKey] = new LineStudyEvalDto { Cp = ply.Cp, Mate = ply.Mate, BestUci = ply.BestUci, Depth = r.Depth };
        }
        AnalysisHistoryService.CheckedTree tree;
        try { tree = AnalysisHistoryService.CheckTree(study.StartFen, ParseTree(study.TreeJson), -1); }
        catch (ArgumentException) { return result; }
        var fens = tree.Fens.Prepend(study.StartFen).ToList();
        for (var i = 0; i < fens.Count; i++)
        {
            if (MarkedPositionService.KeyOf(fens[i]) is not { } key || !byKey.TryGetValue(key, out var e)) continue;
            result.Nodes.Add(new LineStudyEvalDto { Index = i - 1, Cp = e.Cp, Mate = e.Mate, BestUci = e.BestUci, Depth = e.Depth });
        }
        return result;
    }

    /// <summary>
    /// Ein Takt des Hintergrunds: fertige Aufträge in ihre Stellung übernehmen (und den Auftrag löschen), gescheiterte erneut
    /// vormerken, dann je Nutzer neue Aufträge bis <see cref="MaxOpenJobsPerUser"/> anlegen — auf der eigenen Hintergrund-Engine,
    /// sonst der Haus-Engine (<see cref="EngineOwnerResolver"/>). Gibt (übernommen, angelegt) zurück.
    /// </summary>
    public async Task<(int Collected, int Queued)> StepAsync(CancellationToken ct = default)
    {
        var collected = 0;
        var queued = await db.LineStudyPositions.Where(p => p.Status == LineStudyPositionStatus.Queued).ToListAsync(ct);
        if (queued.Count > 0)
        {
            var jobIds = queued.Where(p => p.AnalysisJobId != null).Select(p => p.AnalysisJobId!.Value).ToList();
            var jobsById = await db.AnalysisJobs.Where(j => jobIds.Contains(j.Id)).ToDictionaryAsync(j => j.Id, ct);
            foreach (var pos in queued)
            {
                jobsById.TryGetValue(pos.AnalysisJobId ?? 0, out var job);
                if (job is not null && job.Status is not (AnalysisJobStatus.Done or AnalysisJobStatus.Failed)) continue;
                var candidates = job?.Status == AnalysisJobStatus.Done ? BrokerCandidates.Parse(job.ResultJson, pos.Fen) : null;
                if (candidates is { Count: > 0 })
                {
                    pos.CandidatesJson = BrokerCandidates.ToJson(candidates);
                    pos.Depth = job!.ReachedDepth;
                    pos.Status = LineStudyPositionStatus.Done;
                    collected++;
                }
                else
                {
                    pos.Attempts++;
                    pos.Status = pos.Attempts >= MaxAttempts ? LineStudyPositionStatus.Failed : LineStudyPositionStatus.Pending;
                }
                pos.AnalysisJobId = null;
                pos.UpdatedAt = DateTime.UtcNow;
                if (job is not null) db.AnalysisJobs.Remove(job);
            }
            await db.SaveChangesAsync(ct);
        }

        var queuedNow = 0;
        var pendingUsers = await db.LineStudyPositions.Where(p => p.Status == LineStudyPositionStatus.Pending)
            .Select(p => p.LineStudy!.UserId).Distinct().ToListAsync(ct);
        foreach (var userId in pendingUsers)
        {
            var owner = await EngineOwnerResolver.ResolveAsync(db, userId, ct);
            if (owner is null) continue;   // keine Engine erreichbar — die Stellungen warten
            var open = await db.LineStudyPositions.CountAsync(p => p.LineStudy!.UserId == userId && p.Status == LineStudyPositionStatus.Queued, ct);
            var room = MaxOpenJobsPerUser - open;
            if (room <= 0) continue;
            // Der zuletzt bearbeitete Baum zuerst, darin in der Reihenfolge des Anlegens (Hauptlinie vor späteren Varianten).
            var next = await db.LineStudyPositions.Include(p => p.LineStudy)
                .Where(p => p.LineStudy!.UserId == userId && p.Status == LineStudyPositionStatus.Pending)
                .OrderByDescending(p => p.LineStudy!.UpdatedAt).ThenBy(p => p.Id)
                .Take(room).ToListAsync(ct);
            foreach (var pos in next)
            {
                try
                {
                    var job = await jobs.CreateAsync(userId, new CreateAnalysisJobRequest
                    {
                        Fen = pos.Fen, Title = pos.LineStudy!.Title is { } t ? $"Linien: {t}" : "Linien",
                        TargetDepth = TargetDepth, MultiPv = MultiPv,
                    }, ct, remember: false, engineOwnerUserId: owner, background: true);
                    pos.AnalysisJobId = job.Id;
                    pos.Status = LineStudyPositionStatus.Queued;
                    pos.UpdatedAt = DateTime.UtcNow;
                    queuedNow++;
                }
                catch (InvalidOperationException) { break; }   // Deckel der Aufträge oder gerade keine Engine: nächster Takt
                catch (ArgumentException)
                {
                    pos.Status = LineStudyPositionStatus.Failed;   // FEN, die die Engine nicht nimmt
                }
            }
            await db.SaveChangesAsync(ct);
        }
        return (collected, queuedNow);
    }

    private static AnalysisTreeDto ParseTree(string json)
    {
        try { return JsonSerializer.Deserialize<AnalysisTreeDto>(json, TreeJson) ?? new(); }
        catch (JsonException) { return new(); }
    }

    private async Task TrimAsync(int userId, CancellationToken ct)
    {
        var count = await db.LineStudies.CountAsync(s => s.UserId == userId, ct);
        if (count < MaxStudiesPerUser) return;
        var old = await db.LineStudies.Include(s => s.Positions).Where(s => s.UserId == userId)
            .OrderBy(s => s.UpdatedAt).Take(count - MaxStudiesPerUser + 1).ToListAsync(ct);
        db.LineStudyPositions.RemoveRange(old.SelectMany(s => s.Positions));
        db.LineStudies.RemoveRange(old);
    }

    private async Task<LineStudyDto> ToDtoAsync(LineStudy s, CancellationToken ct)
    {
        var counts = await db.LineStudyPositions.AsNoTracking().Where(p => p.LineStudyId == s.Id)
            .GroupBy(p => p.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var tree = ParseTree(s.TreeJson);
        return new LineStudyDto
        {
            Id = s.Id, GameKey = s.GameKey, StartFen = s.StartFen, Title = s.Title, Tree = tree, Current = s.Current,
            Revealed = s.RevealedAt is not null,
            Total = counts.Sum(c => c.Count),
            Analyzed = counts.Where(c => c.Key is LineStudyPositionStatus.Done or LineStudyPositionStatus.Failed).Sum(c => c.Count),
        };
    }
}
