using System.Text.Json;
using System.Text.Json.Serialization;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Partieformular einlesen" (0.529.0): Foto hochladen → Claude liest → der Server macht daraus eine legale
/// Partie (<see cref="ScoresheetResolver"/>) → sie liegt in „Meine Partien", das Foto bleibt daneben liegen.
///
/// <para>Das Lesen dauert (Nachdenken + ggf. Nachfrage) typisch eine halbe bis zwei Minuten und läuft deshalb im
/// Hintergrund (<see cref="ScoresheetScanWorker"/>); der Zustand steht in der Datenbank, ein Neustart verliert
/// nichts. Geht die Lesung ab einer Stelle nicht mehr legal auf, fragt der Dienst beim Modell NACH — mit der
/// Stellung, den legalen Zügen dort und dem Hinweis, dass oft ein früherer Eintrag der falsche ist
/// (<see cref="MaxRounds"/> Durchgänge insgesamt). Was dann noch offen ist, steht am Ende der Partie als
/// Kommentar und auf der Korrekturseite.</para>
/// </summary>
public class ScoresheetScanService
{
    private readonly AppDbContext _db;
    private readonly IScoresheetVisionClient _vision;
    private readonly SavedGameService _games;
    private readonly ILogger<ScoresheetScanService> _logger;
    private readonly int _dailyLimit;
    private readonly ScoresheetBudget _budget;
    private readonly ScoresheetReader _reader;

    /// <summary>Größter angenommener Upload.</summary>
    public const int MaxUploadBytes = 30 * 1024 * 1024;

    /// <summary>Größer wird ein Foto nicht abgelegt — darüber wird es verkleinert gespeichert (3000 px).</summary>
    public const int MaxStoredBytes = 12 * 1024 * 1024;

    /// <summary>Längste Bildseite, die das Modell bekommt.</summary>
    public const int ModelEdge = 2000;

    /// <summary>Längste Bildseite eines verkleinert abgelegten Fotos.</summary>
    public const int StoredEdge = 3000;

    /// <summary>Lese-Durchgänge insgesamt (1 + Nachfragen).</summary>
    public const int MaxRounds = 3;

    /// <summary>So oft setzt der Worker an einer Einlesung an (ein Absturz mittendrin zählt mit).</summary>
    public const int MaxAttempts = 3;

    /// <summary>So viele Einlesungen dürfen je Nutzer gleichzeitig warten oder laufen.</summary>
    public const int MaxOpenPerUser = 3;

    /// <summary>Vorgabe für <c>Scoresheet:DailyLimit</c> — jede Einlesung kostet echtes Geld beim Modell.</summary>
    public const int DefaultDailyLimit = 20;

    public ScoresheetScanService(AppDbContext db, IScoresheetVisionClient vision, SavedGameService games,
        ILogger<ScoresheetScanService> logger, IConfiguration? config = null)
    {
        _db = db;
        _vision = vision;
        _games = games;
        _logger = logger;
        _dailyLimit = int.TryParse(config?["Scoresheet:DailyLimit"], out var l) && l > 0 ? l : DefaultDailyLimit;
        _budget = new ScoresheetBudget(config, ClaudeScoresheetVisionClient.MaxTokens);
        _reader = new ScoresheetReader(vision);
    }

    /// <summary>Die Kostenbremse (für Tests und die Statusanzeige).</summary>
    public ScoresheetBudget Budget => _budget;

    /// <summary>Was ein Nutzer heute und in 30 Tagen verbraucht hat, was alle zusammen heute — und ob er Admin ist.</summary>
    private async Task<(long UserToday, long UserMonth, long GlobalToday, bool IsAdmin)> SpentAsync(int userId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var day = now.AddDays(-1);
        var month = now.AddDays(-30);
        var userToday = await _db.ScoresheetScans.Where(s => s.UserId == userId && s.CreatedAt >= day)
            .SumAsync(s => (long?)s.CostMicroUsd, ct) ?? 0;
        var userMonth = await _db.ScoresheetScans.Where(s => s.UserId == userId && s.CreatedAt >= month)
            .SumAsync(s => (long?)s.CostMicroUsd, ct) ?? 0;
        var globalToday = await _db.ScoresheetScans.Where(s => s.CreatedAt >= day)
            .SumAsync(s => (long?)s.CostMicroUsd, ct) ?? 0;
        var isAdmin = await _db.AppUsers.Where(u => u.Id == userId).Select(u => u.IsAdmin).FirstOrDefaultAsync(ct);
        return (userToday, userMonth, globalToday, isAdmin);
    }

    /// <summary>Darf für diesen Nutzer jetzt noch ein Modell-Aufruf starten? <c>null</c> = ja.</summary>
    internal async Task<string?> BudgetBlockAsync(int userId, CancellationToken ct = default)
    {
        var (today, month, global, admin) = await SpentAsync(userId, ct);
        return _budget.Check(today, month, global, admin);
    }

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Hochladen + Stand ─────────────────────────────────────────────

    public async Task<ScoresheetStatusDto> StatusAsync(int userId)
    {
        var since = DateTime.UtcNow.AddDays(-1);
        var (today, month, global, admin) = await SpentAsync(userId);
        var dayShare = _budget.UserDailyMicroUsd > 0 ? (double)today / _budget.UserDailyMicroUsd : 1;
        var monthShare = _budget.UserMonthlyMicroUsd > 0 ? (double)month / _budget.UserMonthlyMicroUsd : 1;
        return new ScoresheetStatusDto
        {
            Available = _vision.IsConfigured,
            DailyLimit = _dailyLimit,
            UsedToday = await _db.ScoresheetScans.CountAsync(s => s.UserId == userId && s.CreatedAt >= since),
            BudgetUsedPercent = admin ? 0 : (int)Math.Clamp(Math.Round(Math.Max(dayShare, monthShare) * 100), 0, 100),
            Blocked = _budget.Check(today, month, global, admin),
            Unlimited = admin,
            Languages = ScoresheetNotation.Languages.Select(l => new ScoresheetLanguageDto
            {
                Code = l.Code,
                Name = l.Name,
                Pieces = $"{l.King} {l.Queen} {l.Rook} {l.Bishop} {l.Knight}",
            }).ToList(),
        };
    }

    /// <summary>Nimmt ein Foto an und reiht es ein. Absage als Grund-Code (<c>notConfigured</c>,
    /// <c>unsupportedImage</c>, <c>tooLarge</c>, <c>dailyLimit</c>, <c>tooManyOpen</c>, <c>invalidLanguage</c>).</summary>
    public async Task<(ScoresheetScanDto? Scan, string? Reason)> CreateAsync(int userId, byte[] data, string? contentType,
        string? fileName, string? language)
    {
        if (!_vision.IsConfigured) return (null, "notConfigured");
        var lang = string.IsNullOrWhiteSpace(language) ? "auto" : language.Trim().ToLowerInvariant();
        if (!ScoresheetNotation.IsKnown(lang)) return (null, "invalidLanguage");
        if (data.Length == 0 || data.Length > MaxUploadBytes) return (null, "tooLarge");
        if (contentType != null && !ScoresheetImage.AcceptedTypes.Contains(contentType)) return (null, "unsupportedImage");
        if (!ScoresheetImage.CanDecode(data)) return (null, "unsupportedImage");

        var (today, month, global, admin) = await SpentAsync(userId);
        var since = DateTime.UtcNow.AddDays(-1);
        if (!admin && await _db.ScoresheetScans.CountAsync(s => s.UserId == userId && s.CreatedAt >= since) >= _dailyLimit)
            return (null, "dailyLimit");
        if (_budget.Check(today, month, global, admin) is { } blocked) return (null, blocked);
        if (await _db.ScoresheetScans.CountAsync(s => s.UserId == userId
                && (s.Status == ScoresheetScanStatus.Pending || s.Status == ScoresheetScanStatus.Running)) >= MaxOpenPerUser)
            return (null, "tooManyOpen");

        var photo = data;
        var type = contentType ?? "image/jpeg";
        if (photo.Length > MaxStoredBytes)
        {
            photo = ScoresheetImage.Prepare(data, StoredEdge, 90) ?? data;
            type = "image/jpeg";
            if (photo.Length > MaxStoredBytes) return (null, "tooLarge");
        }

        var scan = new ScoresheetScan
        {
            UserId = userId,
            Photo = photo,
            ContentType = type,
            FileName = CleanFileName(fileName),
            NotationLanguage = lang,
            Status = ScoresheetScanStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };
        _db.ScoresheetScans.Add(scan);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Formular-Einlesung {ScanId} von User {UserId} angenommen ({Bytes} Bytes, Sprache {Language})",
            scan.Id, userId, photo.Length, lang);
        return (ToDto(scan), null);
    }

    public async Task<ScoresheetScanDto?> GetAsync(int userId, int id)
    {
        var scan = await ScanHeads().FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        return scan == null ? null : ToDto(scan);
    }

    /// <summary>Die letzten Einlesungen des Nutzers (ohne Foto) — für die Seite, falls man sie verlassen hat.</summary>
    public async Task<List<ScoresheetScanDto>> ListAsync(int userId, int take = 20)
    {
        var scans = await ScanHeads().Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt).Take(Math.Clamp(take, 1, 50)).ToListAsync();
        var gameIds = scans.Where(s => s.SavedGameId != null).Select(s => s.SavedGameId!.Value).ToList();
        var names = await _db.SavedGames.Where(g => gameIds.Contains(g.Id))
            .Select(g => new { g.Id, g.White, g.Black }).ToDictionaryAsync(g => g.Id);
        return scans.Select(s =>
        {
            var dto = ToDto(s);
            if (s.SavedGameId is int gid && names.TryGetValue(gid, out var n)) { dto.White = n.White; dto.Black = n.Black; }
            return dto;
        }).ToList();
    }

    /// <summary>Alles außer dem Foto (das ist das Schwergewicht der Zeile).</summary>
    private IQueryable<ScoresheetScan> ScanHeads() => _db.ScoresheetScans.AsNoTracking().Select(s => new ScoresheetScan
    {
        Id = s.Id, UserId = s.UserId, SavedGameId = s.SavedGameId, ContentType = s.ContentType, FileName = s.FileName,
        NotationLanguage = s.NotationLanguage, Status = s.Status, Error = s.Error, ResolutionJson = s.ResolutionJson,
        Model = s.Model, Attempts = s.Attempts, Rounds = s.Rounds, CreatedAt = s.CreatedAt, StartedAt = s.StartedAt,
        FinishedAt = s.FinishedAt,
    });

    /// <summary>Das Foto einer eigenen Partie; <c>null</c>, wenn es keins gibt oder die Partie fremd ist.</summary>
    public async Task<(byte[] Data, string ContentType, string FileName)?> PhotoForGameAsync(int userId, int gameId)
    {
        var p = await _db.ScoresheetScans.AsNoTracking()
            .Where(s => s.SavedGameId == gameId && s.UserId == userId)
            .Select(s => new { s.Photo, s.ContentType, s.FileName, s.Id })
            .FirstOrDefaultAsync();
        if (p == null) return null;
        var ext = p.ContentType switch { "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg" };
        return (p.Photo, p.ContentType, p.FileName ?? $"partieformular-{p.Id}{ext}");
    }

    // ── Lesen (Worker) ────────────────────────────────────────────────

    /// <summary>Beim Start: was beim letzten Herunterfahren mitten im Lesen war, kommt zurück in die Schlange.</summary>
    public async Task<int> RequeueInterruptedAsync(CancellationToken ct)
    {
        var running = await _db.ScoresheetScans.Where(s => s.Status == ScoresheetScanStatus.Running).ToListAsync(ct);
        foreach (var s in running) s.Status = ScoresheetScanStatus.Pending;
        await _db.SaveChangesAsync(ct);
        return running.Count;
    }

    /// <summary>Die älteste wartende Einlesung übernehmen (Status → Running); <c>null</c> = nichts zu tun.</summary>
    public async Task<int?> ClaimNextAsync(CancellationToken ct)
    {
        while (true)
        {
            var next = await _db.ScoresheetScans
                .Where(s => s.Status == ScoresheetScanStatus.Pending)
                .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
                .Select(s => new { s.Id, s.Attempts })
                .FirstOrDefaultAsync(ct);
            if (next == null) return null;

            var scan = await _db.ScoresheetScans.FirstAsync(s => s.Id == next.Id, ct);
            if (scan.Attempts >= MaxAttempts)
            {
                // Dreimal mittendrin gestorben — ein viertes Mal wäre eine Endlosschleife auf Kosten des Kontos.
                scan.Status = ScoresheetScanStatus.Failed;
                scan.Error = "failed";
                scan.FinishedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                continue;
            }
            scan.Status = ScoresheetScanStatus.Running;
            scan.Attempts++;
            scan.StartedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return scan.Id;
        }
    }

    /// <summary>Liest eine übernommene Einlesung und legt die Partie an. Scheitert sie, steht der Grund an der Zeile.</summary>
    public async Task ProcessAsync(int scanId, CancellationToken ct)
    {
        var scan = await _db.ScoresheetScans.FirstOrDefaultAsync(s => s.Id == scanId, ct);
        if (scan == null || scan.Status != ScoresheetScanStatus.Running) return;

        var jpeg = ScoresheetImage.Prepare(scan.Photo, ModelEdge);
        if (jpeg == null) { await FailAsync(scan, "unreadable", ct); return; }

        var outcome = await _reader.ReadAsync(jpeg, scan.NotationLanguage, ct,
            beforeCall: token => BudgetBlockAsync(scan.UserId, token),
            afterCall: async (input, output, token) =>
            {
                // SOFORT verbuchen: stürzt der Worker danach ab, ist das Geld trotzdem ausgegeben.
                scan.InputTokens += input;
                scan.OutputTokens += output;
                scan.CostMicroUsd += _budget.CostMicroUsd(input, output);
                await _db.SaveChangesAsync(token);
            });
        scan.Model = _vision.Model;
        scan.Rounds = outcome.Rounds;
        if (outcome.Error != null) { await FailAsync(scan, outcome.Error, ct, outcome.Json); return; }

        var t = outcome.Transcription!;
        var r = outcome.Resolution!;
        if (r.Plies.Count == 0) { await FailAsync(scan, "noMoves", ct, outcome.Json); return; }

        var comments = CommentsFor(r);
        var game = await _games.CreateGeneratedAsync(scan.UserId, SavedGameService.ScoresheetSource,
            r.Plies.Select(p => p.San).ToList(), comments,
            new GameHeaderInput(Blank(t.Event), Blank(t.Site), Blank(t.DateIso) ?? Blank(t.Date), Blank(t.Round),
                Blank(t.White), Blank(t.Black), t.Result));

        scan.SavedGameId = game.Id;
        scan.TranscriptionJson = outcome.Json;
        scan.ResolutionJson = JsonSerializer.Serialize(new StoredResolution
        {
            Language = outcome.Language,
            Plies = r.Plies,
            Unresolved = r.Unresolved,
            UnresolvedFrom = r.StuckAt,
        }, Json);
        scan.Status = ScoresheetScanStatus.Done;
        scan.Error = null;
        scan.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Formular-Einlesung {ScanId} fertig: Partie {GameId}, {Plies} Halbzüge, {Uncertain} unsicher, {Unresolved} offen, {Rounds} Durchgänge",
            scan.Id, game.Id, r.Plies.Count, r.Plies.Count(p => p.Uncertain), r.Unresolved.Count, outcome.Rounds);
    }

    /// <summary>
    /// Kommentare im PGN: wo der Zug vom Formular abweicht (Lesefehler repariert, Eintrag erschlossen), steht,
    /// was DASTAND — dieselbe Auskunft, die man beim Nachspielen braucht, um dem Zug zu misstrauen. Die nicht
    /// aufgelösten Einträge hängen am letzten Zug.
    /// </summary>
    internal static Dictionary<int, string> CommentsFor(ScoresheetResolution r)
    {
        var comments = new Dictionary<int, string>();
        for (var i = 0; i < r.Plies.Count; i++)
        {
            var p = r.Plies[i];
            if (p.Match is ScoresheetResolver.Matches.Fuzzy or ScoresheetResolver.Matches.Guess)
                comments[i] = $"sheet: {(string.IsNullOrWhiteSpace(p.Written) ? "?" : p.Written)}";
            else if (p.Match == ScoresheetResolver.Matches.Inserted)
                comments[i] = "sheet: —"; // stand nicht auf dem Formular
        }
        // Doppelt notierte Einträge hängen am Zug davor.
        foreach (var skip in r.Skipped)
        {
            var at = Math.Max(0, skip.AfterPly - 1);
            if (at >= r.Plies.Count) continue;
            var text = "sheet, extra: " + skip.Written;
            comments[at] = comments.TryGetValue(at, out var c0) ? c0 + " | " + text : text;
        }
        if (r.Unresolved.Count > 0 && r.Plies.Count > 0)
        {
            var text = "sheet, not resolved: " + string.Join(' ', r.Unresolved);
            var last = r.Plies.Count - 1;
            comments[last] = comments.TryGetValue(last, out var c) ? c + " | " + text : text;
        }
        return comments;
    }

    private async Task FailAsync(ScoresheetScan scan, string reason, CancellationToken ct, string? json = null)
    {
        scan.Status = ScoresheetScanStatus.Failed;
        scan.Error = reason;
        scan.TranscriptionJson = json ?? scan.TranscriptionJson;
        scan.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Formular-Einlesung {ScanId} gescheitert: {Reason}", scan.Id, reason);
    }

    // ── Korrekturseite ────────────────────────────────────────────────

    /// <summary>Formular-Einträge + Stand je Halbzug einer eigenen, eingelesenen Partie.</summary>
    public async Task<ScoresheetEditStateDto?> EditStateAsync(int userId, int gameId)
    {
        var scan = await _db.ScoresheetScans.AsNoTracking()
            .Where(s => s.SavedGameId == gameId && s.UserId == userId)
            .Select(s => new { s.Id, s.NotationLanguage, s.TranscriptionJson, s.ResolutionJson })
            .FirstOrDefaultAsync();
        if (scan == null) return null;
        var stored = Deserialize(scan.ResolutionJson);
        var t = ScoresheetTranscription.Parse(scan.TranscriptionJson);
        return new ScoresheetEditStateDto
        {
            ScanId = scan.Id,
            NotationLanguage = stored?.Language ?? scan.NotationLanguage,
            Written = t?.Moves.Select(m => m.Written).ToList() ?? new(),
            Plies = stored?.Plies ?? new(),
            Unresolved = stored?.Unresolved ?? new(),
            UnresolvedFrom = stored?.UnresolvedFrom,
        };
    }

    /// <summary>
    /// Den Rest ab einer festgelegten Stelle neu aufbereiten: die Züge in <paramref name="prefix"/> stehen fest
    /// (bestätigt, gewählt oder selbst eingegeben), die Formular-Einträge ab <paramref name="writtenFrom"/> werden
    /// neu aufgelöst — samt Unsicherheiten und Ausgängen. Kein Modell-Aufruf: das ist schnell und kostet nichts.
    /// </summary>
    /// <returns><c>null</c>, wenn es keine Einlesung zur Partie gibt; <see cref="ArgumentException"/> bei einem
    /// illegalen Präfix.</returns>
    public async Task<ScoresheetResolveResultDto?> ResolveRestAsync(int userId, int gameId, IReadOnlyList<string> prefix,
        int writtenFrom)
    {
        var scan = await _db.ScoresheetScans.AsNoTracking()
            .Where(s => s.SavedGameId == gameId && s.UserId == userId)
            .Select(s => new { s.NotationLanguage, s.TranscriptionJson, s.ResolutionJson })
            .FirstOrDefaultAsync();
        if (scan == null) return null;
        var t = ScoresheetTranscription.Parse(scan.TranscriptionJson);
        if (t == null) return null;
        var scanned = t.Scanned();
        var language = Deserialize(scan.ResolutionJson)?.Language ?? scan.NotationLanguage;

        var legalPrefix = SavedGameService.LegalSans(prefix);
        var from = Math.Clamp(writtenFrom, 0, scanned.Count);
        var r = ScoresheetResolver.Resolve(scanned, new ScoresheetResolver.Options(ScoresheetNotation.Find(language)),
            legalPrefix, from);
        return new ScoresheetResolveResultDto { Plies = r.Plies, Unresolved = r.Unresolved, UnresolvedFrom = r.StuckAt };
    }

    /// <summary>Nach dem Speichern der Korrekturseite: deren Stand je Halbzug ablegen (Anzeige-Zustand).</summary>
    public async Task SaveEditStateAsync(int userId, int gameId, List<ScoresheetPly> plies)
    {
        var scan = await _db.ScoresheetScans.FirstOrDefaultAsync(s => s.SavedGameId == gameId && s.UserId == userId);
        if (scan == null) return;
        var stored = Deserialize(scan.ResolutionJson) ?? new StoredResolution { Language = scan.NotationLanguage };
        stored.Plies = plies.Take(600).ToList();
        // Was nach dem letzten gespeicherten Zug noch offen ist, bestimmt die Seite nicht mehr — das Speichern
        // übernimmt nur legale Züge; offene Einträge dahinter bleiben stehen, wie sie waren.
        scan.ResolutionJson = JsonSerializer.Serialize(stored, Json);
        await _db.SaveChangesAsync();
    }

    private static StoredResolution? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<StoredResolution>(json, Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Was an der Einlesung als Auflösung liegt.</summary>
    internal sealed class StoredResolution
    {
        public string? Language { get; set; }
        public List<ScoresheetPly> Plies { get; set; } = new();
        public List<string> Unresolved { get; set; } = new();
        public int? UnresolvedFrom { get; set; }
    }

    // ── Helfer ────────────────────────────────────────────────────────

    /// <summary>Einlesungen löschen, ohne ihr Foto zu laden (das Schwergewicht der Zeile). Schon geladene werden
    /// direkt entfernt — ein Platzhalter mit derselben Id wäre für EF ein zweites Objekt und wirft. Der Platzhalter
    /// trägt seine Fremdschlüssel: ohne sie kennt EF die Abhängigkeit nicht und löscht womöglich zuerst die Partie —
    /// MariaDB räumt die Einlesung dann per Cascade selbst weg, und das DELETE danach trifft keine Zeile mehr
    /// (gefunden vom Integrationstest).</summary>
    public static void RemoveWithoutLoading(AppDbContext db, IEnumerable<(int Id, int UserId, int? SavedGameId)> scans)
    {
        foreach (var (id, userId, gameId) in scans)
        {
            var tracked = db.ScoresheetScans.Local.FirstOrDefault(s => s.Id == id);
            if (tracked != null) { db.ScoresheetScans.Remove(tracked); continue; }
            var stub = new ScoresheetScan { Id = id, UserId = userId, SavedGameId = gameId };
            db.ScoresheetScans.Attach(stub);
            db.ScoresheetScans.Remove(stub);
        }
    }

    /// <summary>Die Schlüssel der Einlesungen, die <see cref="RemoveWithoutLoading"/> braucht.</summary>
    public static async Task<List<(int Id, int UserId, int? SavedGameId)>> KeysAsync(IQueryable<ScoresheetScan> query)
    {
        var rows = await query.Select(s => new { s.Id, s.UserId, s.SavedGameId }).ToListAsync();
        return rows.Select(x => (x.Id, x.UserId, x.SavedGameId)).ToList();
    }

    private static ScoresheetScanDto ToDto(ScoresheetScan s)
    {
        var stored = Deserialize(s.ResolutionJson);
        return new ScoresheetScanDto
        {
            Id = s.Id,
            Status = s.Status.ToString().ToLowerInvariant(),
            Error = s.Error,
            SavedGameId = s.SavedGameId,
            NotationLanguage = s.NotationLanguage,
            FileName = s.FileName,
            CreatedAt = s.CreatedAt,
            FinishedAt = s.FinishedAt,
            Rounds = s.Rounds,
            MoveCount = stored?.Plies.Count ?? 0,
            UncertainCount = stored?.Plies.Count(p => p.Uncertain && !p.Confirmed) ?? 0,
            UnresolvedCount = stored?.Unresolved.Count ?? 0,
        };
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? CleanFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = Path.GetFileName(name.Trim()).Replace("\"", "").Replace("\r", "").Replace("\n", "");
        return n.Length == 0 ? null : n.Length > 200 ? n[..200] : n;
    }
}

/// <summary>Die Antwort des Modells, gelesen (Schema aus <see cref="ScoresheetPrompt.Schema"/>).</summary>
public sealed class ScoresheetTranscription
{
    public string? NotationLanguage { get; set; }
    public string? Event { get; set; }
    public string? Site { get; set; }
    public string? Date { get; set; }
    public string? DateIso { get; set; }
    public string? Round { get; set; }
    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Result { get; set; }
    public List<Entry> Moves { get; set; } = new();
    public string? Remarks { get; set; }

    public sealed class Entry
    {
        public int MoveNumber { get; set; }
        public string? Color { get; set; }
        public string Written { get; set; } = string.Empty;
        public string? San { get; set; }
        public List<string>? Alternatives { get; set; }
        public string? Confidence { get; set; }
        public string? Note { get; set; }

        public ScannedPly ToScanned() => new(Written ?? string.Empty, string.IsNullOrWhiteSpace(San) ? null : San,
            Alternatives?.Where(a => !string.IsNullOrWhiteSpace(a)).Take(3).ToList(), Confidence);
    }

    public List<ScannedPly> Scanned() => Moves.Select(m => m.ToScanned()).ToList();

    /// <summary><c>null</c> bei kaputtem JSON.</summary>
    public static ScoresheetTranscription? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var t = JsonSerializer.Deserialize<ScoresheetTranscription>(json, ScoresheetScanService.Json);
            if (t == null) return null;
            t.Moves = t.Moves.Take(600).ToList();
            return t;
        }
        catch (JsonException) { return null; }
    }
}
