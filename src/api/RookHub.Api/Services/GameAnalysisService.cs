using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Diese ganze Partie einmal durchrechnen": zerlegt ein PGN in Halbzüge und lässt jede Stellung von
/// der Hintergrund-Engine analysieren — über die bestehenden <see cref="AnalysisJob"/>s, also mit
/// demselben Broker-Pfad, demselben Vorrang der Live-Analyse und derselben Fortsetzungs-Logik.
///
/// <para>Die Klammer drumherum macht drei Dinge, die der Einzel-Auftrag nicht kann:</para>
/// <list type="number">
/// <item><b>In Blöcken nachfüttern.</b> Eine 80-Halbzug-Partie hat 80 Stellungen, offen sein dürfen
/// aber nur 50 Aufträge je Nutzer (<see cref="AnalysisJobService.MaxOpenJobsPerUser"/>). Es laufen
/// deshalb höchstens <see cref="GameAnalysisDefaults.MaxOpenJobsPerGame"/> gleichzeitig, der Rest
/// rückt nach — so bleibt daneben Luft für von Hand eingereihte Stellungen.</item>
/// <item><b>Ergebnisse kopieren.</b> <c>MaxJobsPerUser</c> räumt die ältesten FERTIGEN Aufträge weg;
/// ohne Kopie hätte der Trimmer die Analyse nach zweieinhalb Partien aufgefressen.</item>
/// <item><b>Blickrichtung geradeziehen.</b> Der Broker rechnet aus Weiß-Sicht und schreibt Rochaden
/// als König-schlägt-Turm — beides wird beim Einlesen begradigt (<see cref="BrokerCandidates"/>),
/// damit spätere Auswertungen nicht jedes Mal daran denken müssen.</item>
/// </list>
/// </summary>
public class GameAnalysisService
{
    private readonly AppDbContext _db;
    private readonly AnalysisJobService _jobs;
    private readonly ILogger<GameAnalysisService> _logger;

    public GameAnalysisService(AppDbContext db, AnalysisJobService jobs, ILogger<GameAnalysisService> logger)
    {
        _db = db;
        _jobs = jobs;
        _logger = logger;
    }

    // ===== Anlegen ==========================================================

    public async Task<GameAnalysisDto> CreateAsync(int userId, CreateGameAnalysisRequest req, CancellationToken ct = default,
        GameAnalysisOrigin origin = GameAnalysisOrigin.Manual, int? engineOwnerUserId = null)
    {
        var depth = req.TargetDepth ?? GameAnalysisDefaults.TargetDepth;
        if (depth is < 1 or > AnalysisJobService.MaxDepth)
            throw new ArgumentException($"Depth must be 1..{AnalysisJobService.MaxDepth}");
        var multiPv = req.MultiPv ?? GameAnalysisDefaults.MultiPv;
        if (multiPv is < 1 or > AnalysisJobService.MaxMultiPv)
            throw new ArgumentException($"Lines must be 1..{AnalysisJobService.MaxMultiPv}");

        var parsed = GamePlies.Parse(req.Pgn, GameAnalysisDefaults.MaxPlies)
            ?? throw new ArgumentException("PGN enthält keine spielbare Partie");
        var (header, plies) = parsed;

        var title = string.IsNullOrWhiteSpace(req.Title)
            ? BuildTitle(header)
            : req.Title.Trim();

        var analysis = new GameAnalysis
        {
            UserId = userId,
            Title = title.Length > 200 ? title[..200] : title,
            Pgn = req.Pgn!,
            White = header.White,
            Black = header.Black,
            Result = header.Result,
            Event = header.Event,
            StartFen = header.StartFen,
            TargetDepth = depth,
            MultiPv = multiPv,
            EngineId = string.IsNullOrWhiteSpace(req.EngineId) ? null : req.EngineId.Trim(),
            EngineOwnerUserId = engineOwnerUserId == userId ? null : engineOwnerUserId,
            Origin = origin,
            PlyCount = plies.Count,
            Status = GameAnalysisStatus.Pending,
        };
        foreach (var p in plies)
            analysis.Positions.Add(new GameAnalysisPosition
            {
                Ply = p.Index, Fen = p.Fen, GameMoveUci = p.Uci, GameMoveSan = p.San,
            });

        _db.GameAnalyses.Add(analysis);
        await _db.SaveChangesAsync(ct);

        // Sofort die erste Fuhre einreihen, damit der Nutzer nicht auf den nächsten Pump-Lauf wartet.
        await PumpOneAsync(analysis.Id, ct);
        return await GetAsync(userId, analysis.Id, ct) ?? ToDto(analysis, 0);
    }

    // ===== Einwurf auf der Punktepartie-Seite =================================

    /// <summary>
    /// Eine Partie, die auf der Punktepartie-Seite eingeworfen wurde. Unterschied zum Einreihen von
    /// Hand: die Tiefe ist FEST (<see cref="GameAnalysisDefaults.GuessTargetDepth"/>) und steht in
    /// keinem Formular, und wer keine eigene Hintergrund-Engine hinterlegt hat, rechnet auf der
    /// Haus-Engine.
    ///
    /// <para>Beides haengt zusammen: einen Tiefenregler auf fremder Rechenzeit anzubieten hiesse,
    /// Selbstbedienung an einer Maschine zu erlauben, die jemand anderem gehoert — Tiefe 40 kostet
    /// grob das Zehnfache von 30. Die Punktepartie braucht die Tiefe ohnehin nicht: gewertet wird
    /// gegen den tatsaechlich gespielten Zug, die Engine liefert nur die Rangfolge der Alternativen.</para>
    ///
    /// <para>Wirft NICHT: die drei Absagen (zu viele offene Partien, keine Engine, unbrauchbares
    /// PGN) sind erwartete Antworten und keine Ausnahmen — der Aufrufer soll sie dem Nutzer
    /// erklaeren koennen, und dafuer braucht er einen Grund, keinen Text.</para>
    /// </summary>
    public async Task<GuessUploadResult> CreateForGuessAsync(int userId, CreateGuessGameRequest req,
        CancellationToken ct = default)
    {
        var open = await OpenGuessGamesAsync(userId, ct);
        if (open >= GameAnalysisDefaults.MaxOpenGuessGamesPerUser)
            return new GuessUploadResult(null, GuessUploadReason.TooManyOpen);

        var engineOwner = await ResolveGuessEngineOwnerAsync(userId, ct);
        if (engineOwner is null)
            return new GuessUploadResult(null, GuessUploadReason.NoEngine);

        try
        {
            var dto = await CreateAsync(userId, new CreateGameAnalysisRequest
            {
                Pgn = req.Pgn,
                Title = req.Title,
                TargetDepth = GameAnalysisDefaults.GuessTargetDepth,
                MultiPv = GameAnalysisDefaults.MultiPv,
            }, ct, GameAnalysisOrigin.Guess, engineOwner.Value);
            return new GuessUploadResult(dto, null);
        }
        catch (ArgumentException)
        {
            // Kein spielbares PGN — die einzige Absage, die am eingeworfenen Text liegt.
            return new GuessUploadResult(null, GuessUploadReason.InvalidPgn);
        }
    }

    /// <summary>Wie viele eingeworfene Partien dieses Nutzers noch rechnen. Gescheiterte zaehlen
    /// NICHT mit: sonst sperrte eine tote Engine den Nutzer dauerhaft aus, obwohl nichts mehr
    /// laeuft — er soll sie loeschen koennen und weitermachen.</summary>
    private Task<int> OpenGuessGamesAsync(int userId, CancellationToken ct) =>
        _db.GameAnalyses.CountAsync(g => g.UserId == userId
            && g.Origin == GameAnalysisOrigin.Guess
            && (g.Status == GameAnalysisStatus.Pending || g.Status == GameAnalysisStatus.Running), ct);

    /// <summary>
    /// Wer rechnet: erst die EIGENE Hintergrund-Engine, sonst die Haus-Engine, sonst niemand
    /// (<c>null</c>). Die eigene hat Vorrang, weil sie dem Nutzer gehoert — wer eine Maschine
    /// stehen hat, soll sie benutzen und nicht in der Schlange des Hauses stehen.
    /// </summary>
    private async Task<int?> ResolveGuessEngineOwnerAsync(int userId, CancellationToken ct)
    {
        var own = await _db.LichessEngineCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (own is not null && own.BackgroundEngines.Count > 0) return userId;

        // Haus-Engine: die Freigabe steht an den Zugangsdaten, gelten lassen wir sie aber nur bei
        // einem Admin — verliert jemand die Rechte, soll seine Maschine nicht weiter fuer fremde
        // Partien laufen, ohne dass jemand das Haekchen wegnimmt.
        var house = await _db.LichessEngineCredentials.AsNoTracking()
            .Where(c => c.ShareAsHouseEngine && c.BackgroundEngineIds != null && c.User!.IsAdmin)
            .OrderBy(c => c.UserId)
            .ToListAsync(ct);
        return house.FirstOrDefault(c => c.BackgroundEngines.Count > 0)?.UserId;
    }

    /// <summary>Was die Punktepartie-Seite ueber das Einwerfen wissen muss, BEVOR jemand ein PGN
    /// hineinkopiert: gibt es ueberhaupt eine Engine, und wie viele Partien sind noch frei.</summary>
    public async Task<GuessUploadStatusDto> GuessUploadStatusAsync(int userId, CancellationToken ct = default)
    {
        var owner = await ResolveGuessEngineOwnerAsync(userId, ct);
        var open = await OpenGuessGamesAsync(userId, ct);
        return new GuessUploadStatusDto
        {
            EngineAvailable = owner is not null,
            OwnEngine = owner == userId,
            OpenGames = open,
            MaxGames = GameAnalysisDefaults.MaxOpenGuessGamesPerUser,
        };
    }

    private static string BuildTitle(GamePlies.GameHeader h)
    {
        var names = string.Join(" – ", new[] { h.White, h.Black }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (names.Length > 0) return names;
        return string.IsNullOrWhiteSpace(h.Event) ? "Partie" : h.Event!;
    }

    // ===== Lesen / Löschen ==================================================

    public Task<List<GameAnalysisDto>> ListAsync(int userId, CancellationToken ct = default) =>
        ProjectAsync(_db.GameAnalyses.AsNoTracking().Where(g => g.UserId == userId), ct);

    /// <summary>
    /// Der kuratierte Bestand: Partien, die JEDER als Punktepartie spielen darf — auch ohne
    /// Anmeldung. Geliefert werden nur VOLLSTAENDIG gerechnete: eine halb fertige Partie laesst sich
    /// zwar anspielen, ueberspringt dann aber stillschweigend jede Stellung ohne Kandidatenliste —
    /// man raet also eine Partie mit Loechern, ohne dass irgendwo steht, warum. Sortiert nach Titel,
    /// nicht nach Anlagedatum: der Bestand ist eine Bibliothek und keine Zeitleiste.
    /// </summary>
    public async Task<List<GameAnalysisDto>> ListPublicAsync(CancellationToken ct = default)
    {
        var rows = await ProjectAsync(
            _db.GameAnalyses.AsNoTracking()
                .Where(g => g.IsPublic && g.Positions.Any() && !g.Positions.Any(p => p.CandidatesJson == null)),
            ct);
        return rows.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Kuratierten Bestand ein-/ausschalten — Besitzer der Analyse oder Admin. Bewusst
    /// beim Besitzer und nicht nur beim Admin: wer die Engine-Zeit gestiftet hat, entscheidet auch,
    /// ob die Partie öffentlich spielbar ist (dieselbe Teilung wie beim Kalkulations-Schalter des
    /// Kurses).</summary>
    public async Task<bool?> SetPublicAsync(int userId, bool isAdmin, int id, bool isPublic,
        CancellationToken ct = default)
    {
        var analysis = await _db.GameAnalyses.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (analysis is null || (!isAdmin && analysis.UserId != userId)) return null;
        if (analysis.IsPublic != isPublic)
        {
            analysis.IsPublic = isPublic;
            analysis.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return analysis.IsPublic;
    }

    /// <summary>Die Listen-Abbildung an EINER Stelle (eigene Partien und kuratierter Bestand
    /// unterscheiden sich nur im Filter). Geholt werden nur die Spalten der Liste: `Pgn` ist
    /// LONGTEXT und würde sonst für JEDE Partie mitgelesen, obwohl die Übersicht ihn nie anzeigt —
    /// `Annotated` fragt ihn deshalb IN SQL (LIKE), die Spalte selbst bleibt draußen.</summary>
    private async Task<List<GameAnalysisDto>> ProjectAsync(IQueryable<GameAnalysis> query, CancellationToken ct)
    {
        var rows = await query
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => new
            {
                g.Id, g.Title, g.White, g.Black, g.Result, g.Event, g.TargetDepth, g.MultiPv,
                g.EngineId, g.Status, g.PlyCount, g.LastError, g.CreatedAt, g.FinishedAt, g.IsPublic,
                Annotated = g.Pgn.Contains("{"),
                Analyzed = g.Positions.Count(p => p.CandidatesJson != null),
            })
            .ToListAsync(ct);
        return rows.Select(r => new GameAnalysisDto
        {
            Id = r.Id, Title = r.Title, White = r.White, Black = r.Black, Result = r.Result,
            Event = r.Event, TargetDepth = r.TargetDepth, MultiPv = r.MultiPv, EngineId = r.EngineId,
            Status = r.Status.ToString().ToLowerInvariant(), PlyCount = r.PlyCount,
            AnalyzedPlies = r.Analyzed, LastError = r.LastError, IsPublic = r.IsPublic,
            Annotated = r.Annotated,
            CreatedAt = r.CreatedAt, FinishedAt = r.FinishedAt,
        }).ToList();
    }

    public async Task<GameAnalysisDto?> GetAsync(int userId, int id, CancellationToken ct = default)
    {
        var analysis = await _db.GameAnalyses.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);
        if (analysis is null) return null;

        var positions = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.GameAnalysisId == id)
            .OrderBy(p => p.Ply)
            .Select(p => new GameAnalysisPositionDto
            {
                Ply = p.Ply,
                MoveNumber = p.Ply / 2 + 1,
                White = p.Ply % 2 == 0,
                San = p.GameMoveSan,
                Uci = p.GameMoveUci,
                Fen = p.Fen,
                EvalText = p.EvalText,
                Depth = p.Depth,
                Analyzed = p.CandidatesJson != null,
            })
            .ToListAsync(ct);

        var dto = ToDto(analysis, positions.Count(p => p.Analyzed));
        dto.Positions = positions;
        return dto;
    }

    /// <summary>
    /// Die Partie noch einmal anstossen: alles, was nicht gerechnet ist, kommt frisch in die
    /// Warteschlange.
    ///
    /// <para><b>Warum das ein eigener Knopf sein muss.</b> Ein Auftrag klebt an DER Engine, die
    /// beim Anlegen die kuerzeste Schlange hatte — und er wechselt nie wieder. Ist die Engine
    /// danach aus (ein Rechner, den jemand abends ausmacht), versucht der Auftrag es ewig dort und
    /// nirgends sonst; der Broker antwortet mit 503, der Auftrag pausiert eine Minute, und das
    /// wiederholt sich bis in alle Ewigkeit. Am 2026-09-11 hingen so fuenf Partien an ihren letzten
    /// Halbzuegen fest, waehrend fuenf freie Engines danebenstanden. Der Auftrag wird deshalb
    /// GELOESCHT und neu angelegt — erst dadurch waehlt <c>PickBackgroundEngineAsync</c> erneut.</para>
    ///
    /// <para>Aufgegebene Stellungen kommen ebenfalls zurueck (<c>CandidatesJson = "[]"</c> nach
    /// <see cref="GameAnalysisDefaults.MaxPositionAttempts"/> Anlaeufen). Sie zaehlen als
    /// „gerechnet" und sind der Grund, warum eine Partie fertig aussehen kann und in der
    /// Punktepartie trotzdem Loecher hat. Genau die soll ein Reset schliessen.</para>
    ///
    /// <para>Eine pinnte Engine (<see cref="GameAnalysis.EngineId"/>) bleibt, wo sie ist: wer eine
    /// bestimmte Maschine gewaehlt hat, meint sie auch.</para>
    /// </summary>
    /// <returns>Die Partie mit neuem Stand, oder <c>null</c>, wenn es sie nicht (mehr) gibt.</returns>
    public async Task<GameAnalysisDto?> RestartAsync(int userId, int id, CancellationToken ct = default)
    {
        var analysis = await _db.GameAnalyses
            .Include(g => g.Positions)
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);
        if (analysis is null) return null;

        // Steckengebliebene Auftraege weg — sonst behielte die Stellung ihren alten Auftrag auf der
        // alten Engine, und der Reset aenderte genau gar nichts.
        var stuck = analysis.Positions
            .Where(p => p.CandidatesJson == null && p.AnalysisJobId != null)
            .Select(p => p.AnalysisJobId!.Value)
            .ToList();
        foreach (var jobId in stuck)
        {
            try { await _jobs.DeleteAsync(userId, jobId, ct); }
            catch (Exception ex) { _logger.LogDebug(ex, "GameAnalysis {Id}: Auftrag {JobId} liess sich nicht loeschen", id, jobId); }
        }

        var reopened = 0;
        foreach (var pos in analysis.Positions)
        {
            if (pos.CandidatesJson == "[]")
            {
                pos.CandidatesJson = null;
                pos.AnalyzedAt = null;
                pos.EvalText = null;
                pos.Depth = 0;
            }
            if (pos.CandidatesJson != null) continue;

            pos.AnalysisJobId = null;
            pos.FailedAttempts = 0;
            reopened++;
        }

        analysis.Status = GameAnalysisStatus.Pending;
        analysis.LastError = null;
        analysis.FinishedAt = null;
        analysis.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("GameAnalysis {Id}: neu angestossen ({Count} Stellungen, {Jobs} Auftraege verworfen)",
            id, reopened, stuck.Count);

        // Sofort nachfuettern, damit der Knopf etwas tut, das man sieht.
        await PumpOneAsync(analysis.Id, ct);
        return await GetAsync(userId, id, ct);
    }

    public async Task<bool> DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var analysis = await _db.GameAnalyses
            .Include(g => g.Positions)
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);
        if (analysis is null) return false;

        // Noch laufende Aufträge dieser Partie mitnehmen — sonst rechnet die Engine für nichts weiter.
        var openJobIds = analysis.Positions.Where(p => p.AnalysisJobId != null).Select(p => p.AnalysisJobId!.Value).ToList();
        foreach (var jobId in openJobIds)
        {
            try { await _jobs.DeleteAsync(userId, jobId, ct); }
            catch (Exception ex) { _logger.LogDebug(ex, "GameAnalysis: Auftrag {JobId} liess sich nicht loeschen", jobId); }
        }

        // Punktepartien auf dieser Analyse zuerst: die Sitzung zeigt per RESTRICT auf die Analyse
        // (zwei Cascade-Pfade auf GuessMoves waeren sonst unzulaessig, siehe AppDbContext) — ohne
        // dieses Aufraeumen scheitert das Loeschen in MySQL mit einem Fremdschluessel-Fehler (500).
        var sessions = await _db.GuessSessions
            .Where(s => s.GameAnalysisId == analysis.Id)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (sessions.Count > 0)
        {
            _db.GuessMoves.RemoveRange(
                await _db.GuessMoves.Where(m => sessions.Contains(m.GuessSessionId)).ToListAsync(ct));
            _db.GuessSessions.RemoveRange(
                await _db.GuessSessions.Where(s => sessions.Contains(s.Id)).ToListAsync(ct));
        }

        _db.GameAnalysisPositions.RemoveRange(analysis.Positions);
        _db.GameAnalyses.Remove(analysis);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ===== Pumpe ============================================================

    /// <summary>Alle unfertigen Partien einen Schritt weiterbringen (Ergebnisse einsammeln, nachfüttern).</summary>
    public async Task<int> PumpAllAsync(CancellationToken ct = default)
    {
        var ids = await _db.GameAnalyses
            .Where(g => g.Status == GameAnalysisStatus.Pending || g.Status == GameAnalysisStatus.Running)
            .OrderBy(g => g.CreatedAt)
            .Select(g => g.Id)
            .ToListAsync(ct);
        var moved = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            // Je Partie abschirmen: eine einzige stolpernde Analyse darf nicht den ganzen Durchlauf
            // abbrechen — sonst kaeme keine der dahinterliegenden Partien je an die Reihe.
            try { if (await PumpOneAsync(id, ct)) moved++; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { _logger.LogWarning(ex, "GameAnalysis {Id}: Durchlauf uebersprungen", id); }
        }
        return moved;
    }

    /// <summary>Eine Partie: fertige Aufträge übernehmen, danach bis zum Block-Limit nachfüttern.</summary>
    public async Task<bool> PumpOneAsync(int analysisId, CancellationToken ct = default)
    {
        var analysis = await _db.GameAnalyses
            .Include(g => g.Positions)
            .FirstOrDefaultAsync(g => g.Id == analysisId, ct);
        if (analysis is null || analysis.Status is GameAnalysisStatus.Done or GameAnalysisStatus.Failed) return false;

        var changed = await IngestFinishedAsync(analysis, ct);
        changed |= await EnqueueNextAsync(analysis, ct);

        var analyzed = analysis.Positions.Count(p => p.CandidatesJson != null);
        if (analyzed >= analysis.Positions.Count && analysis.Positions.Count > 0)
        {
            analysis.Status = GameAnalysisStatus.Done;
            analysis.FinishedAt = DateTime.UtcNow;
            changed = true;
        }
        else if (analysis.Status == GameAnalysisStatus.Pending && analysis.Positions.Any(p => p.AnalysisJobId != null))
        {
            analysis.Status = GameAnalysisStatus.Running;
            changed = true;
        }

        if (changed)
        {
            analysis.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return changed;
    }

    /// <summary>Fertige Aufträge in die Stellungen kopieren.</summary>
    private async Task<bool> IngestFinishedAsync(GameAnalysis analysis, CancellationToken ct)
    {
        var pending = analysis.Positions.Where(p => p.CandidatesJson == null && p.AnalysisJobId != null).ToList();
        if (pending.Count == 0) return false;

        var jobIds = pending.Select(p => p.AnalysisJobId!.Value).ToList();
        // Getrackt laden: uebernommene Auftraege werden gleich mitgeloescht (siehe unten).
        var jobs = await _db.AnalysisJobs
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, ct);

        // Ein uebernommener Auftrag hat seinen Zweck erfuellt: das Ergebnis liegt kopiert bei der
        // Stellung. Bliebe er stehen, fuellte eine einzige Partie 80 der 200 Zeilen, die
        // AnalysisJobService.MaxJobsPerUser je Nutzer haelt — nach drei Partien haette der Trimmer
        // die von Hand eingereihten Auftraege des Nutzers verdraengt und die Auftragsliste
        // bestuende nur noch aus Partie-Halbzuegen.
        var consumed = new List<AnalysisJob>();

        var changed = false;
        foreach (var pos in pending)
        {
            if (!jobs.TryGetValue(pos.AnalysisJobId!.Value, out var job))
            {
                // Auftrag ist weg (gelöscht/getrimmt) → Stellung erneut einreihen.
                pos.AnalysisJobId = null;
                changed = true;
                continue;
            }

            if (job.Status == AnalysisJobStatus.Done && !string.IsNullOrEmpty(job.ResultJson))
            {
                var candidates = BrokerCandidates.Parse(job.ResultJson, pos.Fen);
                if (candidates is { Count: > 0 })
                {
                    pos.CandidatesJson = BrokerCandidates.ToJson(candidates);
                    pos.EvalText = BrokerCandidates.EvalTextOf(candidates);
                    pos.Depth = job.ReachedDepth;
                    pos.AnalyzedAt = DateTime.UtcNow;
                    changed = true;
                }
                else
                {
                    // Fertig, aber unlesbar → als analysiert MIT LEERER Liste ablegen, sonst hinge die
                    // Partie ewig an dieser Stellung. Die Wertung überspringt solche Stellungen.
                    pos.CandidatesJson = "[]";
                    pos.AnalyzedAt = DateTime.UtcNow;
                    changed = true;
                    _logger.LogWarning("GameAnalysis {Id}: Ergebnis von Auftrag {JobId} unlesbar (Ply {Ply})",
                        analysis.Id, job.Id, pos.Ply);
                }
                pos.AnalysisJobId = null;
                consumed.Add(job);
            }
            else if (job.Status == AnalysisJobStatus.Failed)
            {
                // Der Auftrag gibt auf. Das heisst fast immer „die Engine war gerade nicht zu
                // gebrauchen" und NICHT „diese Stellung geht nicht": eine Stellung der Partie hat
                // immer einen legalen Zug, Matt oder Patt kann sie nicht sein. Frueher stand hier
                // sofort die leere Liste — eine tote Engine loeschte damit stillschweigend
                // Stellungen aus der Partie, die nie wieder gerechnet wurden. Also zaehlen und
                // erneut einreihen; erst nach MaxPositionAttempts ist endgueltig Schluss, damit
                // eine wirklich unloesbare Stellung nicht ewig im Kreis laeuft.
                pos.FailedAttempts++;
                pos.AnalysisJobId = null;
                if (pos.FailedAttempts >= GameAnalysisDefaults.MaxPositionAttempts)
                {
                    pos.CandidatesJson = "[]";
                    pos.AnalyzedAt = DateTime.UtcNow;
                    _logger.LogWarning("GameAnalysis {Id}: Stellung {Ply} nach {Tries} Anlaeufen aufgegeben",
                        analysis.Id, pos.Ply, pos.FailedAttempts);
                }
                // Der gescheiterte Auftrag bleibt stehen — seine Fehlermeldung ist die einzige
                // Erklaerung, warum es an dieser Stellung hakt.
                changed = true;
            }
        }
        if (consumed.Count > 0) _db.AnalysisJobs.RemoveRange(consumed);
        return changed;
    }

    /// <summary>Bis zum Block-Limit neue Aufträge einreihen (in Zugreihenfolge).</summary>
    private async Task<bool> EnqueueNextAsync(GameAnalysis analysis, CancellationToken ct)
    {
        var open = analysis.Positions.Count(p => p.CandidatesJson == null && p.AnalysisJobId != null);
        var room = GameAnalysisDefaults.MaxOpenJobsPerGame - open;
        if (room <= 0) return false;

        var next = analysis.Positions
            .Where(p => p.CandidatesJson == null && p.AnalysisJobId == null)
            .OrderBy(p => p.Ply)
            .Take(room)
            .ToList();
        if (next.Count == 0) return false;

        var changed = false;
        foreach (var pos in next)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var job = await _jobs.CreateAsync(analysis.UserId, new CreateAnalysisJobRequest
                {
                    Fen = pos.Fen,
                    Title = JobTitle(analysis, pos),
                    TargetDepth = analysis.TargetDepth,
                    MultiPv = analysis.MultiPv,
                    EngineId = analysis.EngineId,
                    // Nicht in „Gemerkte Stellungen" spiegeln: eine Partie erzeugt je Halbzug einen
                    // Auftrag — 80 Zeilen je Partie wuerden die Merkliste des Nutzers zuschuetten.
                }, ct, remember: false, engineOwnerUserId: analysis.EngineOwnerUserId);
                pos.AnalysisJobId = job.Id;
                changed = true;
            }
            catch (InvalidOperationException ex)
            {
                // Kein Platz mehr (globaler Deckel) oder keine Hintergrund-Engine.
                if (ex.Message.Contains("engine", StringComparison.OrdinalIgnoreCase))
                {
                    analysis.Status = GameAnalysisStatus.Failed;
                    analysis.LastError = "Keine Hintergrund-Engine konfiguriert";
                    return true;
                }
                break;   // Deckel erreicht → beim nächsten Pump-Lauf weiter
            }
            catch (ArgumentException ex)
            {
                // Diese eine Stellung ist der Engine nicht zumutbar (z. B. unbrauchbare FEN).
                pos.CandidatesJson = "[]";
                pos.AnalyzedAt = DateTime.UtcNow;
                changed = true;
                _logger.LogWarning(ex, "GameAnalysis {Id}: Stellung {Ply} nicht einreihbar", analysis.Id, pos.Ply);
            }
        }
        return changed;
    }

    private static string JobTitle(GameAnalysis analysis, GameAnalysisPosition pos)
    {
        var moveNo = pos.Ply / 2 + 1;
        var side = pos.Ply % 2 == 0 ? "w" : "s";
        var title = $"{analysis.Title} · {moveNo}{side} {pos.GameMoveSan}";
        return title.Length > 200 ? title[..200] : title;
    }

    // ===== Mapping ==========================================================

    private static GameAnalysisDto ToDto(GameAnalysis g, int analyzed) => new()
    {
        Id = g.Id,
        Title = g.Title,
        White = g.White,
        Black = g.Black,
        Result = g.Result,
        Event = g.Event,
        TargetDepth = g.TargetDepth,
        MultiPv = g.MultiPv,
        EngineId = g.EngineId,
        Status = g.Status.ToString().ToLowerInvariant(),
        PlyCount = g.PlyCount,
        AnalyzedPlies = analyzed,
        LastError = g.LastError,
        CreatedAt = g.CreatedAt,
        FinishedAt = g.FinishedAt,
    };
}
