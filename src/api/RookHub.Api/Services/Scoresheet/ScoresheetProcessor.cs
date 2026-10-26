using System.Text.Json;
using System.Text.Json.Serialization;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Scoresheet;
using RookHub.Api.DTOs.League;
using static RookHub.Api.Services.Scoresheet.ScoresheetScanRows;

namespace RookHub.Api.Services.Scoresheet;

/// <summary>
/// Pipeline der Einlesung (Zerlegung Z6 aus <see cref="ScoresheetScanService"/>): Schlange des Workers, Lesen durch das
/// Modell (samt Drehen und Nachlesen), Übernahme einer Lesung von außen, Engine-Prüfung, Partie anlegen, Scheitern, Glocke.
/// </summary>
internal sealed class ScoresheetProcessor
{
    private readonly AppDbContext _db;
    private readonly IScoresheetVisionClient _vision;
    private readonly SavedGameService _games;
    private readonly NotificationService _notifications;
    private readonly ILogger<ScoresheetScanService> _logger;
    private readonly ScoresheetBudget _budget;
    private readonly ScoresheetReader _reader;
    private readonly IScoresheetEngine? _engine;
    private readonly ScoresheetReadMode _startMode;
    private readonly ScoresheetQuota _quota;

    internal ScoresheetProcessor(AppDbContext db, IScoresheetVisionClient vision, SavedGameService games,
        NotificationService notifications, ILogger<ScoresheetScanService> logger, ScoresheetBudget budget,
        ScoresheetReader reader, IScoresheetEngine? engine, ScoresheetReadMode startMode, ScoresheetQuota quota)
    {
        _db = db;
        _vision = vision;
        _games = games;
        _notifications = notifications;
        _logger = logger;
        _budget = budget;
        _reader = reader;
        _engine = engine;
        _startMode = startMode;
        _quota = quota;
    }

    /// <summary>Wartende Einlesungen für den Leser von außen (<see cref="External"/>), älteste zuerst — ohne Fotos.</summary>
    public async Task<List<ExternalPendingScanDto>> PendingForExternalAsync(CancellationToken ct = default) =>
        await _db.ScoresheetScans.AsNoTracking()
            .Where(s => s.Status == ScoresheetScanStatus.Pending && s.FileName != DiscardedMark)   // „Photo.Length" übersetzt MariaDB nicht
            .OrderBy(s => s.CreatedAt)
            .Select(s => new ExternalPendingScanDto
            {
                Id = s.Id, Purpose = s.Purpose ?? "own", UserId = s.UserId, Anonymous = s.UserId == null,
                PageCount = s.PageCount, NotationLanguage = s.NotationLanguage, OwnerSide = s.OwnerSide, CreatedAt = s.CreatedAt,
                ViewCount = s.Views.Count,
            }).ToListAsync(ct);

    /// <summary>Foto einer Seite (ab 1) einer Einlesung — für den Leser von außen; <paramref name="view"/> ab 1 = ein weiteres
    /// Foto derselben Seite (0.736.0).</summary>
    public async Task<(byte[] Data, string ContentType)?> PhotoForExternalAsync(int scanId, int page, CancellationToken ct = default,
        int view = 0)
    {
        if (view > 0) return await ViewPhotoAsync(_db, scanId, page, view, ct);
        if (page <= 1)
            return await _db.ScoresheetScans.AsNoTracking().Where(s => s.Id == scanId)
                .Select(s => new { s.Photo, s.ContentType }).FirstOrDefaultAsync(ct) is { Photo.Length: > 0 } p ? (p.Photo, p.ContentType) : null;
        return await _db.ScoresheetScanPages.AsNoTracking().Where(x => x.ScoresheetScanId == scanId && x.Page == page)
            .Select(x => new { x.Photo, x.ContentType }).FirstOrDefaultAsync(ct) is { } q ? (q.Photo, q.ContentType) : null;
    }

    /// <summary>
    /// Die Lesung einer WARTENDEN Einlesung von außen übernehmen (<see cref="External"/>): <paramref name="transcriptionJson"/>
    /// in der Form der Modell-Antwort, Kästen in Pixeln des aufrechten, auf <see cref="ScoresheetScanService.ModelEdge"/> verkleinerten Fotos.
    /// Danach genau der Weg wie nach dem Lesen durch das Modell (<see cref="FinishAsync"/>). Reasons: <c>notFound</c>,
    /// <c>notPending</c>, <c>invalidTranscription</c>.
    /// </summary>
    public async Task<(ScoresheetScanDto? Scan, string? Reason)> ProcessReadingAsync(int scanId, string transcriptionJson,
        CancellationToken ct = default)
    {
        var scan = await _db.ScoresheetScans.FirstOrDefaultAsync(s => s.Id == scanId, ct);
        if (scan == null) return (null, "notFound");
        if (scan.Status is not (ScoresheetScanStatus.Pending or ScoresheetScanStatus.Running)) return (null, "notPending");
        var photos = await PhotosAsync(scan, ct);
        var sizes = photos.Select(p => ScoresheetImage.Prepare(p, ScoresheetScanService.ModelEdge) is { } j ? ScoresheetImage.Size(j) : null).ToList();
        var json = ScoresheetTranscription.WithImageSize(transcriptionJson,
            sizes.All(x => x != null) ? sizes.Select(x => x!.Value).ToList() : new List<(int Width, int Height)>());
        var t = ScoresheetTranscription.Parse(json);
        if (t == null) return (null, "invalidTranscription");

        scan.Status = ScoresheetScanStatus.Running;
        scan.StartedAt ??= DateTime.UtcNow;
        scan.Attempts++;
        scan.Model = ScoresheetScanService.ManualModel;
        scan.Rounds = 1;
        if (t.Moves.Count == 0) { await FailAsync(scan, "noMoves", ct, json); return (ToDto(scan), null); }
        var language = ScoresheetReader.EffectiveLanguage(scan.NotationLanguage, t.NotationLanguage);
        var r = ScoresheetResolver.Resolve(t.Scanned(), new ScoresheetResolver.Options(ScoresheetNotation.Find(language)));
        await FinishAsync(scan, new ScoresheetReader.ReadOutcome(t, r, json, language, 1, null), ct);
        _logger.LogInformation("Formular-Einlesung {ScanId}: Lesung von außen übernommen", scan.Id);
        return (ToDto(scan), null);
    }

    /// <summary>Der Leser von außen fängt an (0.721.2, Wunsch 2026-10-08: „erst wartet, dann reading"): wartend → läuft, damit
    /// die Seite „wird gelesen" statt „wartet" zeigt. Gründe: <c>notFound</c>, <c>notPending</c>.</summary>
    public async Task<string?> StartExternalAsync(int scanId, CancellationToken ct = default)
    {
        var scan = await _db.ScoresheetScans.FirstOrDefaultAsync(s => s.Id == scanId, ct);
        if (scan == null) return "notFound";
        if (scan.Status == ScoresheetScanStatus.Running) return null;
        if (scan.Status != ScoresheetScanStatus.Pending) return "notPending";
        scan.Status = ScoresheetScanStatus.Running;
        scan.StartedAt = DateTime.UtcNow;
        scan.Model = ScoresheetScanService.ManualModel;
        await _db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Eine wartende Einlesung von außen als gescheitert abschließen (Foto unbrauchbar, keine Partie darauf,
    /// verdächtiger Inhalt) — mit Glocke wie ein gescheitertes Lesen. Erlaubte Gründe: <c>unreadable</c>, <c>noMoves</c>,
    /// <c>failed</c>.</summary>
    public async Task<string?> FailExternalAsync(int scanId, string? reason, CancellationToken ct = default)
    {
        if (reason is not ("unreadable" or "noMoves" or "failed")) return "invalidReason";
        var scan = await _db.ScoresheetScans.FirstOrDefaultAsync(s => s.Id == scanId, ct);
        if (scan == null) return "notFound";
        if (scan.Status is not (ScoresheetScanStatus.Pending or ScoresheetScanStatus.Running)) return "notPending";
        scan.Model = ScoresheetScanService.ManualModel;
        await FailAsync(scan, reason, ct);
        return null;
    }

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
            if (scan.Attempts >= ScoresheetScanService.MaxAttempts)
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
    /// <param name="ct">Bricht die Einlesung ab — beim Herunterfahren ODER am Laufzeit-Deckel des Workers.</param>
    /// <param name="shutdown">Der Token des Herunterfahrens: nur er lässt die Einlesung auf <c>Running</c> stehen (sie
    /// kommt beim nächsten Start zurück). Jeder andere Abbruch ist der Laufzeit-Deckel → gescheitert mit
    /// <c>timeout</c>, Glocke, und der laufende Aufruf wird mit seinem ungünstigsten Fall verbucht.</param>
    public async Task ProcessAsync(int scanId, CancellationToken ct, CancellationToken shutdown = default)
    {
        var scan = await _db.ScoresheetScans.FirstOrDefaultAsync(s => s.Id == scanId, ct);
        if (scan == null || scan.Status != ScoresheetScanStatus.Running) return;

        // Alle Seiten in Reihenfolge, jede aufrecht und auf die Kante des Modells verkleinert.
        var photos = await PhotosAsync(scan, ct);
        var jpegs = photos.Select(p => ScoresheetImage.Prepare(p, ScoresheetScanService.ModelEdge)).ToList();
        if (jpegs.Any(j => j == null)) { await FailAsync(scan, "unreadable", ct); return; }

        // Ein Lesedurchgang; null = am Laufzeit-Deckel abgebrochen (die Einlesung ist dann schon gescheitert).
        async Task<ScoresheetReader.ReadOutcome?> ReadAsync(IReadOnlyList<byte[]> pages)
        {
            try
            {
                return await _reader.ReadPagesAsync(pages, scan.NotationLanguage, ct,
                    beforeCall: token => _quota.AllowanceAsync(scan.UserId, token),
                    afterCall: async (input, output, token) =>
                    {
                        // SOFORT verbuchen: stürzt der Worker danach ab, ist das Geld trotzdem ausgegeben.
                        scan.InputTokens += input;
                        scan.OutputTokens += output;
                        scan.CostMicroUsd += _budget.CostMicroUsd(input, output);
                        await _db.SaveChangesAsync(token);
                    },
                    startMode: _startMode);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested && !shutdown.IsCancellationRequested)
            {
                // Laufzeit-Deckel mitten in einem Aufruf: was er gekostet hat, meldet die API nicht mehr. Verbucht wird
                // der ungünstigste Fall — lieber zu viel als eine Kostenbremse, die abgebrochene Aufrufe übersieht.
                if (_reader.InFlightMaxTokens is int max)
                {
                    scan.InputTokens += ScoresheetBudget.ReserveInputTokens;
                    scan.OutputTokens += max;
                    scan.CostMicroUsd += _budget.WorstCaseMicroUsd(max);
                }
                scan.Model = _vision.Model;
                await FailAsync(scan, "timeout", CancellationToken.None);
                return null;
            }
        }

        if (await ReadAsync(jpegs!) is not { } outcome) return;
        scan.Model = _vision.Model;
        scan.Rounds = outcome.Rounds;
        if (outcome.Error != null) { await FailAsync(scan, outcome.Error, ct, outcome.Json); return; }

        // Quer oder kopfüber fotografiert (ScoresheetOrientation, 0.672.6)? Dann die Fotos aufrecht drehen und noch einmal
        // lesen — aufrecht liest das Modell sicherer, und der Ausschnitt je Zug liegt nicht mehr quer. Übernommen wird die
        // zweite Lesung nur, wenn sie aufgeht; sonst bleiben erste Lesung und Fotos, wie sie waren.
        var turns = Enumerable.Range(1, photos.Count).Select(p => ScoresheetOrientation.Detect(outcome.Transcription!, p)).ToList();
        if (turns.Any(d => d != 0))
        {
            var upright = photos.Select((p, i) => turns[i] == 0 ? p : ScoresheetImage.Rotate(p, turns[i])).ToList();
            var uprightJpegs = upright.Select(p => p == null ? null : ScoresheetImage.Prepare(p, ScoresheetScanService.ModelEdge)).ToList();
            if (uprightJpegs.All(j => j != null))
            {
                _logger.LogInformation("Formular-Einlesung {ScanId}: Foto gedreht ({Turns}°) — lese aufrecht neu",
                    scan.Id, string.Join("/", turns));
                if (await ReadAsync(uprightJpegs!) is not { } second) return;
                if (second.Error == null && second.Resolution!.Plies.Count > 0)
                {
                    outcome = second;
                    scan.Rounds += second.Rounds;
                    scan.Photo = upright[0]!;
                    scan.ContentType = "image/jpeg";
                    if (photos.Count > 1)
                    {
                        var pages = await _db.ScoresheetScanPages.Where(p => p.ScoresheetScanId == scan.Id)
                            .OrderBy(p => p.Page).ToListAsync(ct);
                        for (var i = 0; i < pages.Count && i + 1 < upright.Count; i++)
                            if (turns[i + 1] != 0) { pages[i].Photo = upright[i + 1]!; pages[i].ContentType = "image/jpeg"; }
                    }
                }
                else
                {
                    _logger.LogWarning("Formular-Einlesung {ScanId}: aufrechte Lesung ging nicht auf ({Error}) — erste bleibt",
                        scan.Id, second.Error ?? "noMoves");
                }
            }
        }

        await FinishAsync(scan, outcome, ct);
    }

    /// <summary>Nach dem Lesen — gleich, ob das Modell gelesen hat (<see cref="ProcessAsync"/>) oder die Lesung von außen
    /// kam (<see cref="ProcessReadingAsync"/>): Engine-Prüfung, dann Liga-Einlesung fertig bzw. Partie in „Meine Partien"
    /// samt Glocke.</summary>
    private async Task FinishAsync(ScoresheetScan scan, ScoresheetReader.ReadOutcome outcome, CancellationToken ct)
    {
        var t = outcome.Transcription!;
        var r = outcome.Resolution!;
        if (r.Plies.Count == 0) { await FailAsync(scan, "noMoves", ct, outcome.Json); return; }
        r = await CheckWithEngineAsync(scan.Id, t, r, outcome.Language, ct);

        if (scan.Purpose == ScoresheetScan.PurposeLeague)
        {
            // Vereins-Datenbank: keine Partie in „Meine Partien" — LeagueHub korrigiert und übernimmt selbst. Keine
            // Glocke: die Seite fragt nach, und ein Link aus RookHub nach LeagueHub wäre eine fremde Adresse.
            MarkDone(scan, outcome, r);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Formular-Einlesung {ScanId} (Vereins-Datenbank) fertig: {Plies} Halbzüge, {Uncertain} unsicher",
                scan.Id, r.Plies.Count, r.Plies.Count(p => p.Uncertain));
            return;
        }

        var comments = ScoresheetComments.For(r);
        if (scan.UserId is not int owner) { await FailAsync(scan, "failed", ct, outcome.Json); return; }   // ohne Konto nur Liga
        var side = scan.OwnerSide is "white" or "black" ? scan.OwnerSide : await ScoresheetOwnerSide.GuessOwnerSideAsync(_db, owner, t, ct);
        var game = await _games.CreateGeneratedAsync(owner, SavedGameService.ScoresheetSource,
            r.Plies.Select(p => p.San).ToList(), comments,
            new GameHeaderInput(Blank(t.Event), Blank(t.Site), Blank(t.DateIso) ?? Blank(t.Date), Blank(t.Round),
                Blank(t.White), Blank(t.Black), t.Result), side);

        scan.SavedGameId = game.Id;
        MarkDone(scan, outcome, r);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Formular-Einlesung {ScanId} fertig: Partie {GameId}, {Plies} Halbzüge, {Uncertain} unsicher, {Unresolved} offen, {Rounds} Durchgänge",
            scan.Id, game.Id, r.Plies.Count, r.Plies.Count(p => p.Uncertain), r.Unresolved.Count, outcome.Rounds);
        // Glocke (und Web-Push, wo eingerichtet): das Lesen dauert Minuten, und wer die Seite verlassen hat,
        // erfährt sonst nicht, dass die Partie da ist. Direkt auf die Korrekturseite — dort ist die Arbeit.
        await NotifyAsync(owner, NotificationType.ScoresheetRead, new Dictionary<string, string>
        {
            ["white"] = game.White ?? "?",
            ["black"] = game.Black ?? "?",
            ["moves"] = r.Plies.Count.ToString(),
            ["uncertain"] = r.Plies.Count(p => p.Uncertain).ToString(),
            ["unresolved"] = r.Unresolved.Count.ToString(),
        }, $"/games/{game.Id}/edit");
    }

    /// <summary>
    /// Engine-Prüfung der Lesung (<see cref="ScoresheetPlausibility"/>): ein Zickzack in der Bewertung — ein Halbzug nach
    /// dem anderen verliert Gewinnchance — ist meist ein falsch gelesener legaler Zug. Ohne Engine, mit abgeschalteter
    /// Prüfung oder bei einem Fehler bleibt die Lesung, wie sie ist: die Prüfung ist eine Zugabe, kein Teil des Lesens.
    /// </summary>
    internal async Task<ScoresheetResolution> CheckWithEngineAsync(int scanId, ScoresheetTranscription t, ScoresheetResolution r,
        string? language, CancellationToken ct)
    {
        if (_engine == null) return r;
        try
        {
            var options = new ScoresheetResolver.Options(ScoresheetNotation.Find(language));
            var o = await ScoresheetPlausibility.ImproveAsync(t.Scanned(), options, r, _engine, ct: ct);
            if (o.Replaced.Count > 0 || o.Flagged.Count > 0)
                _logger.LogInformation(
                    "Formular-Einlesung {ScanId}: Engine-Prüfung ersetzte {Replaced} Zug/Züge, zweifelt an {Flagged} (Halbzüge {ReplacedPlies} / {FlaggedPlies})",
                    scanId, o.Replaced.Count, o.Flagged.Count, string.Join(",", o.Replaced), string.Join(",", o.Flagged));
            return o.Resolution;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Formular-Einlesung {ScanId}: Engine-Prüfung gescheitert, Lesung bleibt unverändert", scanId);
            return r;
        }
    }


    /// <summary>Alle Seiten in Reihenfolge: Seite 1 an der Einlesung, Seiten 2+ aus <c>ScoresheetScanPages</c>.</summary>
    private async Task<List<byte[]>> PhotosAsync(ScoresheetScan scan, CancellationToken ct)
    {
        var photos = new List<byte[]> { scan.Photo };
        if (scan.PageCount > 1)
            photos.AddRange(await _db.ScoresheetScanPages.AsNoTracking().Where(p => p.ScoresheetScanId == scan.Id)
                .OrderBy(p => p.Page).Select(p => p.Photo).ToListAsync(ct));
        return photos;
    }

    /// <summary>Lesung und Auflösung an der Einlesung ablegen und sie als fertig markieren (gespeichert wird danach).</summary>
    private static void MarkDone(ScoresheetScan scan, ScoresheetReader.ReadOutcome outcome, ScoresheetResolution r)
    {
        scan.TranscriptionJson = outcome.Json;
        scan.ResolutionJson = JsonSerializer.Serialize(new StoredResolution
        {
            Language = outcome.Language, Plies = r.Plies, Unresolved = r.Unresolved, UnresolvedFrom = r.StuckAt,
        }, Json);
        scan.Status = ScoresheetScanStatus.Done;
        scan.Error = null;
        scan.FinishedAt = DateTime.UtcNow;
    }

    private async Task FailAsync(ScoresheetScan scan, string reason, CancellationToken ct, string? json = null)
    {
        scan.Status = ScoresheetScanStatus.Failed;
        scan.Error = reason;
        scan.TranscriptionJson = json ?? scan.TranscriptionJson;
        scan.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Formular-Einlesung {ScanId} gescheitert: {Reason}", scan.Id, reason);
        if (scan.Purpose == ScoresheetScan.PurposeLeague || scan.UserId is not int owner) return;   // LeagueHub fragt selbst nach
        await NotifyAsync(owner, NotificationType.ScoresheetFailed,
            new Dictionary<string, string> { ["reason"] = reason }, "/games/scoresheet");
    }

    /// <summary>Benachrichtigen, ohne dass ein Fehler dabei die Einlesung scheitern lässt.</summary>
    private async Task NotifyAsync(int userId, string type, Dictionary<string, string> data, string link)
    {
        try { await _notifications.CreateAsync(userId, type, data, link); }
        catch (Exception ex) { _logger.LogWarning(ex, "Benachrichtigung {Type} an User {UserId} fehlgeschlagen", type, userId); }
    }
}
