using System.Text.Json;
using System.Text.Json.Nodes;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>Griff des Workers für den Service: einen Auftrag, der gerade rechnet, sofort stoppen
/// (Löschen, Linien ↑ = Neustart). Ohne registrierte Implementierung (Tests) passiert nichts.</summary>
public interface IAnalysisJobControl
{
    void Interrupt(int jobId);
}

/// <summary>
/// Verwaltung der Hintergrund-Analyseaufträge (siehe <see cref="AnalysisJob"/>): anlegen, listen,
/// anpassen, löschen — und die Auswahl des nächsten Auftrags für den Worker („sticky": der zuletzt
/// gelaufene Auftrag zuerst, weil seine Hashtabelle in der Hintergrund-Engine noch warm ist).
/// </summary>
public class AnalysisJobService
{
    public const int MaxDepth = 60;
    public const int MaxMultiPv = 10;
    /// <summary>Offene Aufträge je User (Queued/Paused/Running) — gegen Endlos-Listen.</summary>
    public const int MaxOpenJobsPerUser = 50;

    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly IAnalysisJobControl? _control;

    public AnalysisJobService(AppDbContext db, EncryptionService encryption, IAnalysisJobControl? control = null)
    {
        _db = db;
        _encryption = encryption;
        _control = control;
    }

    public async Task<List<AnalysisJobDto>> ListAsync(int userId, CancellationToken ct = default)
    {
        var jobs = await _db.AnalysisJobs.Where(j => j.UserId == userId)
            .OrderByDescending(j => j.CreatedAt).ToListAsync(ct);
        return jobs.Select(ToDto).ToList();
    }

    /// <summary>Anlegen. Wirft <see cref="ArgumentException"/> bei ungültiger Eingabe und
    /// <see cref="InvalidOperationException"/>, wenn keine Hintergrund-Engine bestimmt ist.</summary>
    public async Task<AnalysisJobDto> CreateAsync(int userId, CreateAnalysisJobRequest req, CancellationToken ct = default)
    {
        var fen = (req.Fen ?? string.Empty).Trim();
        if (fen.Length is 0 or > 120 || !IsLegalFen(fen))
            throw new ArgumentException("Invalid FEN");
        if (req.TargetDepth is < 1 or > MaxDepth)
            throw new ArgumentException($"Depth must be 1..{MaxDepth}");
        if (req.MultiPv is < 1 or > MaxMultiPv)
            throw new ArgumentException($"Lines must be 1..{MaxMultiPv}");
        var title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim();
        if (title is { Length: > 200 })
            throw new ArgumentException("Title too long");

        var engineId = string.IsNullOrWhiteSpace(req.EngineId) ? null : req.EngineId.Trim();
        if (engineId is null)
        {
            var cred = await _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
            engineId = cred?.BackgroundEngineId;
            if (string.IsNullOrEmpty(engineId))
                throw new InvalidOperationException("No background engine configured");
        }
        if (engineId.Length > 64)
            throw new ArgumentException("Invalid engine id");

        var open = await _db.AnalysisJobs.CountAsync(j => j.UserId == userId
            && j.Status != AnalysisJobStatus.Done && j.Status != AnalysisJobStatus.Failed, ct);
        if (open >= MaxOpenJobsPerUser)
            throw new InvalidOperationException("Too many open analysis jobs");

        var now = DateTime.UtcNow;
        var job = new AnalysisJob
        {
            UserId = userId, Fen = fen, Title = title, EngineId = engineId,
            TargetDepth = req.TargetDepth, MultiPv = req.MultiPv,
            Status = AnalysisJobStatus.Queued, CreatedAt = now, UpdatedAt = now,
        };
        _db.AnalysisJobs.Add(job);
        await _db.SaveChangesAsync(ct);
        return ToDto(job);
    }

    /// <summary>
    /// Anpassen — die vier Regeln: Tiefe ↑ über das Erreichte setzt einen fertigen Auftrag zurück in die
    /// Queue; Tiefe ↓ auf/unter das Erreichte macht ihn fertig; Linien ↓ kürzt das gespeicherte Ergebnis
    /// ohne Neustart; Linien ↑ startet die Suche neu (der Worker übernimmt Zeilen erst ab der erreichten
    /// Tiefe — das alte Ergebnis bleibt bis dahin sichtbar). null = nicht vorhanden/nicht eigener.
    /// </summary>
    public async Task<AnalysisJobDto?> UpdateAsync(int userId, int id, UpdateAnalysisJobRequest req, CancellationToken ct = default)
    {
        var job = await _db.AnalysisJobs.FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, ct);
        if (job is null) return null;
        if (req.TargetDepth is int d && (d < 1 || d > MaxDepth)) throw new ArgumentException($"Depth must be 1..{MaxDepth}");
        if (req.MultiPv is int m && (m < 1 || m > MaxMultiPv)) throw new ArgumentException($"Lines must be 1..{MaxMultiPv}");
        if (req.Title is { Length: > 200 }) throw new ArgumentException("Title too long");

        var restart = false;
        if (req.Title is not null) job.Title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim();

        if (req.MultiPv is int multiPv && multiPv != job.MultiPv)
        {
            if (multiPv < job.MultiPv)
            {
                // Weniger Linien: die gespeicherten Top-k bleiben exakte Bewertungen — nur kürzen.
                job.ResultJson = TruncatePvs(job.ResultJson, multiPv);
            }
            else
            {
                // Mehr Linien: die zusätzlichen hatte die Engine nie exakt bewertet → Suche neu, ab Tiefe 0;
                // ReachedDepth/ResultJson bleiben als Anzeige, bis die neue Suche sie überholt.
                restart = true;
            }
            job.MultiPv = multiPv;
        }
        if (req.TargetDepth is int target) job.TargetDepth = target;

        if (restart)
        {
            _control?.Interrupt(job.Id);
            job.Status = AnalysisJobStatus.Queued;
            job.FinishedAt = null;
        }
        else if (job.ReachedDepth >= job.TargetDepth)
        {
            if (job.Status is AnalysisJobStatus.Queued or AnalysisJobStatus.Paused or AnalysisJobStatus.Running)
            {
                if (job.Status == AnalysisJobStatus.Running) _control?.Interrupt(job.Id);
                job.Status = AnalysisJobStatus.Done;
                job.FinishedAt ??= DateTime.UtcNow;
            }
        }
        else if (job.Status == AnalysisJobStatus.Done)
        {
            job.Status = AnalysisJobStatus.Queued;   // Tiefe erhöht → weiterrechnen
            job.FinishedAt = null;
        }
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(job);
    }

    public async Task<bool> DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var job = await _db.AnalysisJobs.FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, ct);
        if (job is null) return false;
        _control?.Interrupt(job.Id);
        _db.AnalysisJobs.Remove(job);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Nächster laufbereiter Auftrag eines Users: bevorzugt der ZULETZT gelaufene (warme
    /// Hashtabelle — typisch „weiter bis Tiefe 50" direkt nach Abschluss), sonst der älteste. Aufträge
    /// mit Backoff (<see cref="AnalysisJob.NextAttemptAt"/> in der Zukunft) werden übersprungen.</summary>
    public async Task<AnalysisJob?> PickNextAsync(int userId, DateTime now, CancellationToken ct = default)
    {
        var runnable = await _db.AnalysisJobs
            .Where(j => j.UserId == userId
                && (j.Status == AnalysisJobStatus.Queued || j.Status == AnalysisJobStatus.Paused)
                && (j.NextAttemptAt == null || j.NextAttemptAt <= now))
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(ct);
        if (runnable.Count == 0) return null;

        var lastRunId = await _db.AnalysisJobs
            .Where(j => j.UserId == userId && j.LastRunAt != null)
            .OrderByDescending(j => j.LastRunAt)
            .Select(j => (int?)j.Id)
            .FirstOrDefaultAsync(ct);
        return runnable.FirstOrDefault(j => j.Id == lastRunId) ?? runnable[0];
    }

    /// <summary>Alle Users mit laufbereiten Aufträgen (für den Worker-Tick).</summary>
    public Task<List<int>> UsersWithRunnableJobsAsync(DateTime now, CancellationToken ct = default)
        => _db.AnalysisJobs
            .Where(j => (j.Status == AnalysisJobStatus.Queued || j.Status == AnalysisJobStatus.Paused)
                && (j.NextAttemptAt == null || j.NextAttemptAt <= now))
            .Select(j => j.UserId).Distinct().ToListAsync(ct);

    /// <summary>Beim API-Start: was noch „läuft", läuft nicht mehr — auf Paused, damit es wieder aufgenommen wird.</summary>
    public async Task<int> ResetInterruptedAsync(CancellationToken ct = default)
    {
        var running = await _db.AnalysisJobs.Where(j => j.Status == AnalysisJobStatus.Running).ToListAsync(ct);
        foreach (var j in running) { j.Status = AnalysisJobStatus.Paused; j.UpdatedAt = DateTime.UtcNow; }
        if (running.Count > 0) await _db.SaveChangesAsync(ct);
        return running.Count;
    }

    /// <summary>Entschlüsselter Lichess-Token des Users (null = keiner/nicht lesbar).</summary>
    public async Task<string?> TokenAsync(int userId, CancellationToken ct = default)
    {
        var cred = await _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        return cred is null ? null : _encryption.TryDecrypt(cred.EncryptedToken);
    }

    /// <summary>Gespeicherte Ergebnis-Zeile auf die ersten <paramref name="multiPv"/> Linien kürzen.
    /// Unlesbares JSON bleibt unverändert (der Worker ersetzt es mit der nächsten Zeile).</summary>
    public static string? TruncatePvs(string? resultJson, int multiPv)
    {
        if (string.IsNullOrEmpty(resultJson)) return resultJson;
        try
        {
            var node = JsonNode.Parse(resultJson);
            if (node is JsonObject obj && obj["pvs"] is JsonArray pvs && pvs.Count > multiPv)
            {
                while (pvs.Count > multiPv) pvs.RemoveAt(pvs.Count - 1);
                return obj.ToJsonString();
            }
            return resultJson;
        }
        catch (JsonException) { return resultJson; }
    }

    /// <summary>Legale Stellung? Die Engine bräuchte sonst gar nicht anzutreten (Gera.Chess wirft).</summary>
    public static bool IsLegalFen(string fen)
    {
        try { ChessBoard.LoadFromFen(fen); return true; }
        catch { return false; }
    }

    public static AnalysisJobDto ToDto(AnalysisJob j) => new(
        j.Id, j.Fen, j.Title, j.EngineId, j.TargetDepth, j.MultiPv, j.Status.ToString().ToLowerInvariant(),
        j.ReachedDepth, j.ResultJson, j.SecondsSpent, j.LastError, j.CreatedAt, j.UpdatedAt, j.LastRunAt, j.FinishedAt);
}
