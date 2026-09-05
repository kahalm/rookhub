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
    /// <summary>Obergrenze der Linien: das Lichess-External-Engine-Protokoll erlaubt <c>work.multiPv</c> nur
    /// 1..5 (der Broker weist mehr beim Deserialisieren ab) — derselbe Deckel wie im Live-Pfad
    /// (<c>EngineController.BuildWork</c>). Ein höherer Wert würde jeden Lauf in die Wiederholung schicken.</summary>
    public const int MaxMultiPv = 5;
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
    /// <param name="remember">Stellung zusätzlich unter „Gemerkte Stellungen" ablegen. Vorgabe <c>true</c>
    /// (der Nutzer hat sie von Hand eingereiht, dort will er sie wiederfinden). Maschinell erzeugte
    /// Aufträge — die Partie-Analyse legt je Halbzug einen an — setzen <c>false</c>: eine 80-Halbzug-Partie
    /// spülte sonst 80 Zeilen in die Merkliste und lüde bei JEDEM Auftrag die ganze Liste erneut.</param>
    public async Task<AnalysisJobDto> CreateAsync(int userId, CreateAnalysisJobRequest req, CancellationToken ct = default,
        bool remember = true)
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
        await TrimAsync(userId, 1, ct);
        // Analysierte Stellungen gehören auch in „Gemerkte Stellungen" (dort zeigt die Übersicht den
        // Auftrag mit Status/Tiefe/Bewertung an) — aber nur EINMAL je Stellung: hat der User sie schon
        // gemerkt (Chessable-Remember oder früherer Auftrag), bleibt der vorhandene Eintrag.
        if (remember) await EnsureRememberedAsync(userId, fen, title, ct);
        await _db.SaveChangesAsync(ct);
        return ToDto(job);
    }

    /// <summary>Mehrere Stellungen mit EINER Tiefe/Linienzahl vormerken. Je Stellung wird höchstens ein Auftrag
    /// angelegt: illegale FENs, Stellungen mit bereits vorhandenem (nicht gescheitertem) Auftrag und alles
    /// über dem Deckel offener Aufträge landen mit Grund in <c>Skipped</c> — der Rest wird angelegt.
    /// Wirft <see cref="ArgumentException"/> bei ungültigen Grenzen, <see cref="InvalidOperationException"/> ohne Hintergrund-Engine.</summary>
    public async Task<AnalysisJobBatchResult> CreateManyAsync(int userId, CreateAnalysisJobsBatchRequest req, CancellationToken ct = default)
    {
        if (req.TargetDepth is < 1 or > MaxDepth) throw new ArgumentException($"Depth must be 1..{MaxDepth}");
        if (req.MultiPv is < 1 or > MaxMultiPv) throw new ArgumentException($"Lines must be 1..{MaxMultiPv}");
        var fens = req.Fens ?? [];   // "fens": null hebelt den Initializer aus → 400 statt NullReference/500
        if (fens.Count is 0 or > 200) throw new ArgumentException("1..200 positions");

        var engineId = string.IsNullOrWhiteSpace(req.EngineId) ? null : req.EngineId.Trim();
        if (engineId is null)
        {
            var cred = await _db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
            engineId = cred?.BackgroundEngineId;
            if (string.IsNullOrEmpty(engineId)) throw new InvalidOperationException("No background engine configured");
        }
        if (engineId.Length > 64) throw new ArgumentException("Invalid engine id");

        var existing = await _db.AnalysisJobs.Where(j => j.UserId == userId && j.Status != AnalysisJobStatus.Failed)
            .Select(j => j.Fen).ToListAsync(ct);
        var taken = existing.Select(RepertoireAnalyzeService.NormalizeFen).ToHashSet();
        var open = await _db.AnalysisJobs.CountAsync(j => j.UserId == userId
            && j.Status != AnalysisJobStatus.Done && j.Status != AnalysisJobStatus.Failed, ct);

        // Gemerkte Stellungen EINMAL laden (normalisiert) statt je FEN erneut: bei 200 Stellungen und
        // 1.000 gemerkten Zeilen wären das sonst 200 Abfragen über die ganze Liste — und die im selben
        // Batch angelegten fehlten darin ohnehin.
        var remembered = (await _db.RememberedPositions.Where(p => p.UserId == userId).Select(p => p.Fen).ToListAsync(ct))
            .Select(RepertoireAnalyzeService.NormalizeFen).ToHashSet();

        var created = new List<AnalysisJob>(); var skipped = new List<AnalysisJobBatchSkipped>();
        var now = DateTime.UtcNow;
        foreach (var raw in fens)
        {
            var fen = (raw ?? string.Empty).Trim();
            if (fen.Length is 0 or > 120 || !IsLegalFen(fen)) { skipped.Add(new(fen, "invalid")); continue; }
            var norm = RepertoireAnalyzeService.NormalizeFen(fen);
            if (!taken.Add(norm)) { skipped.Add(new(fen, "duplicate")); continue; }   // auch Dubletten INNERHALB des Batches
            if (open >= MaxOpenJobsPerUser) { skipped.Add(new(fen, "limit")); continue; }
            var job = new AnalysisJob
            {
                UserId = userId, Fen = fen, EngineId = engineId, TargetDepth = req.TargetDepth, MultiPv = req.MultiPv,
                Status = AnalysisJobStatus.Queued, CreatedAt = now, UpdatedAt = now,
            };
            now = now.AddMilliseconds(1);   // FIFO-Reihenfolge = Auswahlreihenfolge
            _db.AnalysisJobs.Add(job); created.Add(job); open++;
            EnsureRemembered(userId, fen, null, remembered);
        }
        if (created.Count > 0) { await TrimAsync(userId, created.Count, ct); await _db.SaveChangesAsync(ct); }
        return new AnalysisJobBatchResult(created.Select(ToDto).ToList(), skipped);
    }

    /// <summary>Gesamtzahl behaltener Aufträge je User: <see cref="MaxOpenJobsPerUser"/> deckelt nur die OFFENEN;
    /// ohne diese Grenze wüchsen Done/Failed unbegrenzt, und jede Liste lüde deren Roh-Zeilen mit.</summary>
    public const int MaxJobsPerUser = 200;

    /// <summary>Älteste abgeschlossene/gescheiterte Aufträge entfernen, sobald der User über <see cref="MaxJobsPerUser"/>
    /// liegt. Offene Aufträge bleiben immer stehen. Aufrufer speichert (Teil der laufenden Transaktion).</summary>
    /// <param name="incoming">Anzahl der in DIESEM Vorgang neu angelegten, noch nicht gespeicherten Aufträge —
    /// sie zählen mit, sonst stünde der User nach dem Speichern über der Grenze.</param>
    private async Task TrimAsync(int userId, int incoming, CancellationToken ct)
    {
        var total = await _db.AnalysisJobs.CountAsync(j => j.UserId == userId, ct) + incoming;
        var over = total - MaxJobsPerUser;
        if (over <= 0) return;
        var old = await _db.AnalysisJobs
            .Where(j => j.UserId == userId && (j.Status == AnalysisJobStatus.Done || j.Status == AnalysisJobStatus.Failed))
            .OrderBy(j => j.FinishedAt ?? j.UpdatedAt)
            .Take(over)
            .ToListAsync(ct);
        if (old.Count > 0) _db.AnalysisJobs.RemoveRange(old);
    }

    /// <summary>Kennzeichnet gemerkte Stellungen, die aus einem Analyseauftrag stammen (interner Link statt Kurs-URL).</summary>
    public const string RememberedSourceUrl = "/analysis/jobs";

    private async Task EnsureRememberedAsync(int userId, string fen, string? title, CancellationToken ct)
    {
        var remembered = (await _db.RememberedPositions.Where(p => p.UserId == userId).Select(p => p.Fen).ToListAsync(ct))
            .Select(RepertoireAnalyzeService.NormalizeFen).ToHashSet();
        EnsureRemembered(userId, fen, title, remembered);
    }

    /// <summary>Stellung vormerken, falls sie (normalisiert) noch nicht in <paramref name="remembered"/> steht.
    /// Das Set wird MITGEFÜHRT, damit ein Batch nicht für jede Stellung erneut die ganze Liste des Nutzers
    /// lädt — und damit Dubletten INNERHALB des Batches ebenfalls greifen.</summary>
    private void EnsureRemembered(int userId, string fen, string? title, HashSet<string> remembered)
    {
        if (!remembered.Add(RepertoireAnalyzeService.NormalizeFen(fen))) return;
        _db.RememberedPositions.Add(new RememberedPosition
        {
            UserId = userId, Fen = fen, CourseName = title, SourceUrl = RememberedSourceUrl, CreatedAt = DateTime.UtcNow,
        });
    }

    /// <summary>Bewertung der Hauptvariante aus der gespeicherten Broker-Zeile („+0.35", „#-3"); null ohne Ergebnis.
    /// Gleiche Formatierung wie das Frontend (mapBrokerLine) — Weiß-Sicht, cp/100 mit 2 Nachkommastellen.</summary>
    public static string? EvalTextOf(string? resultJson)
    {
        if (string.IsNullOrEmpty(resultJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            if (!doc.RootElement.TryGetProperty("pvs", out var pvs) || pvs.ValueKind != JsonValueKind.Array || pvs.GetArrayLength() == 0)
                return null;
            var pv = pvs[0];
            if (pv.TryGetProperty("mate", out var mate) && mate.ValueKind == JsonValueKind.Number)
                return "#" + mate.GetInt32();
            if (pv.TryGetProperty("cp", out var cp) && cp.ValueKind == JsonValueKind.Number)
            {
                var v = cp.GetInt32() / 100.0;
                return (v > 0 ? "+" : "") + v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            }
            return null;
        }
        catch (JsonException) { return null; }
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
        if (req.EngineId is { Length: > 64 }) throw new ArgumentException("Invalid engine id");

        var restart = false;
        if (!string.IsNullOrWhiteSpace(req.EngineId) && req.EngineId.Trim() != job.EngineId)
        {
            // Andere Engine = anderer Prozess mit eigener (kalter) Hashtabelle: der laufende Stream gehört
            // der alten Engine und muss weg. Ergebnis/ReachedDepth bleiben — die neue setzt dort an.
            job.EngineId = req.EngineId.Trim();
            restart = true;
        }
        if (req.Title is not null) job.Title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim();

        if (req.MultiPv is int multiPv && multiPv != job.MultiPv)
        {
            if (multiPv < job.MultiPv)
            {
                // Weniger Linien: die gespeicherten Top-k bleiben exakte Bewertungen — nur kürzen.
                job.ResultJson = TruncatePvs(job.ResultJson, multiPv);
                job.EvalText = EvalTextOf(job.ResultJson);
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
            // Ein Neustart, dessen Zieltiefe UNTER dem Erreichten liegt, könnte die alte Anzeige nie überholen
            // (der Worker übernimmt nur Zeilen ab ReachedDepth) — die Engine-Zeit wäre verschenkt.
            job.TargetDepth = Math.Max(job.TargetDepth, job.ReachedDepth);
            _control?.Interrupt(job.Id);
            job.Status = AnalysisJobStatus.Queued;
            job.FruitlessAttempts = 0;
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
        else if (job.Status is AnalysisJobStatus.Done or AnalysisJobStatus.Failed)
        {
            // Zieltiefe erhöht → weiterrechnen. Auch aus Failed heraus (Engine war weg, Token neu hinterlegt,
            // Stellung deterministisch beendet): dieselbe Regel für beide Endzustände, sonst wäre „Tiefe ↑"
            // je nach Vorgeschichte mal wirksam und mal nicht. Der Fehlversuchs-Zähler beginnt von vorn.
            job.Status = AnalysisJobStatus.Queued;
            job.FruitlessAttempts = 0;
            job.LastError = null;
            job.FinishedAt = null;
        }
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(job);
    }

    /// <summary>Auftrag neu anstoßen: zurück in die Queue, Fehlversuchs-Zähler und Backoff gelöscht, ein
    /// laufender Lauf abgebrochen. Das ERGEBNIS bleibt erhalten — der Lauf setzt ab der erreichten Tiefe fort
    /// (wer wirklich bei null beginnen will, löscht den Auftrag und legt ihn neu an). Bereits fertige Aufträge
    /// (Ziel erreicht) bleiben fertig: sie hätten nichts zu rechnen.</summary>
    public async Task<AnalysisJobDto?> RestartAsync(int userId, int id, CancellationToken ct = default)
    {
        var job = await _db.AnalysisJobs.FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, ct);
        if (job is null) return null;
        if (job.ReachedDepth < job.TargetDepth)
        {
            _control?.Interrupt(job.Id);
            job.Status = AnalysisJobStatus.Queued;
            job.FruitlessAttempts = 0;
            job.LastError = null;
            job.NextAttemptAt = null;
            job.FinishedAt = null;
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
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
    /// <summary>Nächster laufbereiter Auftrag AUF DIESER ENGINE: bevorzugt der zuletzt dort gelaufene (warme
    /// Hashtabelle — typisch „weiter bis Tiefe 50" direkt nach Abschluss), sonst der älteste. Die Engine ist die
    /// knappe Ressource (ein Stockfish-Prozess = eine Suche), nicht der Nutzer: Aufträge auf VERSCHIEDENEN
    /// Engines laufen deshalb parallel.</summary>
    public async Task<AnalysisJob?> PickNextForEngineAsync(string engineId, DateTime now, CancellationToken ct = default)
    {
        var runnable = await _db.AnalysisJobs
            .Where(j => j.EngineId == engineId
                && (j.Status == AnalysisJobStatus.Queued || j.Status == AnalysisJobStatus.Paused)
                && (j.NextAttemptAt == null || j.NextAttemptAt <= now))
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(ct);
        if (runnable.Count == 0) return null;

        var lastRunId = await _db.AnalysisJobs
            .Where(j => j.EngineId == engineId && j.LastRunAt != null)
            .OrderByDescending(j => j.LastRunAt)
            .Select(j => (int?)j.Id)
            .FirstOrDefaultAsync(ct);
        return runnable.FirstOrDefault(j => j.Id == lastRunId) ?? runnable[0];
    }

    /// <summary>Alle Engines mit laufbereiten Aufträgen (für den Worker-Tick).</summary>
    public Task<List<string>> EnginesWithRunnableJobsAsync(DateTime now, CancellationToken ct = default)
        => _db.AnalysisJobs
            .Where(j => (j.Status == AnalysisJobStatus.Queued || j.Status == AnalysisJobStatus.Paused)
                && (j.NextAttemptAt == null || j.NextAttemptAt <= now))
            .Select(j => j.EngineId).Distinct().ToListAsync(ct);

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
        j.ReachedDepth, j.ResultJson, j.SecondsSpent, j.LastError, j.CreatedAt, j.UpdatedAt, j.LastRunAt, j.FinishedAt,
        j.EvalText, j.CurrentDepth, j.CurrentNps);
}
