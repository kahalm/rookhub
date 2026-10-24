using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Library;
using RookHub.Api.DTOs.Games;
using RookHub.Api.DTOs.Engines;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Anlegen von Partie-Analysen (Teil von <see cref="GameAnalysisService"/>, Zerlegung Z5): von Hand, als Stapel
/// (Meister-, Vereins-, Liga-Partien), als Einwurf der Punktepartie-Seite (Haus-Engine) — dazu das Nachziehen
/// korrigierter Vereinspartien. Die erste Fuhre reiht <see cref="GameAnalysisPump"/> sofort ein.
/// </summary>
internal sealed class GameAnalysisIntake
{
    private readonly AppDbContext _db;
    private readonly AnalysisJobService _jobs;
    private readonly CommentSetService _comments;
    private readonly ILogger _logger;
    private readonly GameAnalysisPump _pump;
    private readonly GameAnalysisQueries _queries;
    private readonly GameAnalysisDeletion _deletion;

    internal GameAnalysisIntake(AppDbContext db, AnalysisJobService jobs, CommentSetService comments, ILogger logger,
        GameAnalysisPump pump, GameAnalysisQueries queries, GameAnalysisDeletion deletion)
    {
        _db = db;
        _jobs = jobs;
        _comments = comments;
        _logger = logger;
        _pump = pump;
        _queries = queries;
        _deletion = deletion;
    }

    // ===== Anlegen ==========================================================

    internal async Task<GameAnalysisDto> CreateAsync(int userId, CreateGameAnalysisRequest req, CancellationToken ct = default,
        GameAnalysisOrigin origin = GameAnalysisOrigin.Manual, int? engineOwnerUserId = null,
        int? libraryGameId = null, int? leagueClubGameId = null, string? movesHash = null)
    {
        var depth = req.TargetDepth ?? GameAnalysisDefaults.TargetDepth;
        if (depth is < 1 or > AnalysisJobService.MaxDepth)
            throw new ArgumentException($"Depth must be 1..{AnalysisJobService.MaxDepth}");
        var multiPv = req.MultiPv ?? GameAnalysisDefaults.MultiPv;
        if (multiPv is < 1 or > AnalysisJobService.MaxMultiPv)
            throw new ArgumentException($"Lines must be 1..{AnalysisJobService.MaxMultiPv}");

        var engineId = string.IsNullOrWhiteSpace(req.EngineId) ? null : req.EngineId.Trim();
        if (req.TargetNodes is not null)
        {
            // Knotenziel nur mit ausdrücklich gewählter Engine (sonst rechnete eine Tiefen-Engine mit Knotenlimit) und nur
            // von Hand angelegt; die Engine muss dem Engine-Besitzer gehören (A4-003) — VOR dem Anlegen, nicht erst beim
            // ersten Stellungs-Auftrag, damit der Nutzer die Absage liest.
            if (engineId is null) throw new ArgumentException("Nodes need an engine");
            // Vereinspartien bekommen ihre zweite Engine automatisch (ClubSecondEngineScheduler, 0.684.0).
            if (origin is not (GameAnalysisOrigin.Manual or GameAnalysisOrigin.Club))
                throw new ArgumentException("Nodes only for manual or club analyses");
            if (req.TargetNodes is < AnalysisJobService.MinTargetNodes or > AnalysisJobService.MaxTargetNodes)
                throw new ArgumentException(
                    $"Nodes must be {AnalysisJobService.MinTargetNodes}..{AnalysisJobService.MaxTargetNodes}");
            await _jobs.VerifyEngineAsync(engineOwnerUserId ?? userId, engineId, ct);
        }

        var parsed = GamePlies.Parse(req.Pgn, GameAnalysisDefaults.MaxPlies)
            ?? throw new ArgumentException("PGN enthält keine spielbare Partie");
        var (header, plies) = parsed;

        var title = string.IsNullOrWhiteSpace(req.Title)
            ? GameAnalysisText.BuildTitle(header)
            : req.Title.Trim();
        // Zwei Analysen DERSELBEN Partie (Tiefe auf Stockfish, Knoten auf lc0) sollen in der Liste unterscheidbar sein.
        var suffix = req.TargetNodes is { } nodes ? $" ({GameAnalysisText.NodesLabel(nodes)} nodes)" : string.Empty;
        if (title.Length + suffix.Length > 200) title = title[..(200 - suffix.Length)];
        title += suffix;

        var analysis = new GameAnalysis
        {
            UserId = userId,
            Title = title,
            Pgn = req.Pgn!,
            White = header.White,
            Black = header.Black,
            Result = header.Result,
            Event = header.Event,
            StartFen = header.StartFen,
            TargetDepth = depth,
            MultiPv = multiPv,
            EngineId = engineId,
            TargetNodes = req.TargetNodes,
            EngineOwnerUserId = engineOwnerUserId == userId ? null : engineOwnerUserId,
            Origin = origin,
            LibraryGameId = libraryGameId,
            LeagueClubGameId = leagueClubGameId,
            MovesHash = movesHash,
            // Nur aus der Grundstellung — sonst stuende der erste Zug der Partie als Fortsetzung
            // an der Wurzel des Eroeffnungsbaums, wo es ihn gar nicht gibt (siehe LibraryGameReader).
            OpeningLine = LibraryGameReader.StartsFromInitialPosition(header.StartFen)
                ? GameAnalysisText.OpeningLineOf(plies)
                : null,
            PlyCount = plies.Count,
            Status = GameAnalysisStatus.Pending,
        };
        foreach (var p in plies)
            analysis.Positions.Add(new GameAnalysisPosition
            {
                Ply = p.Index, Fen = p.Fen, GameMoveUci = p.Uci, GameMoveSan = p.San,
            });
        // Die Endstellung (0.689.0, Wunsch 2026-10-06: „die letzte Stellung hat keine Linien"): eine Zeile mehr mit
        // Ply = PlyCount und OHNE Partiezug — nur wenn dort noch gezogen werden kann (Aufgabe, Remis-Angebot, Zeit).
        // Leser, die je Zeile einen Partiezug brauchen, lassen sie weg (`GameMoveUci != ""` bzw. `Ply < PlyCount`).
        if (plies.Count > 0 && GameAnalysisText.FinalPositionOf(plies[^1]) is { } finalFen)
            analysis.Positions.Add(new GameAnalysisPosition { Ply = plies.Count, Fen = finalFen });

        _db.GameAnalyses.Add(analysis);
        await _db.SaveChangesAsync(ct);

        // Die Kommentare in ihre Sprachen zerlegen — hier und nicht auf Vorrat: der Rohbestand hat
        // 94 898 kommentierte Partien, und gespielt wird nur, was eingereiht wurde. Ein Fehlschlag
        // darf das Einreihen nicht verhindern, der Rueckfall auf das PGN traegt weiter.
        try { await _comments.EnsureSourceAsync(analysis.Id, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Kommentar-Saetze fuer {Id} nicht angelegt", analysis.Id); }

        // Sofort die erste Fuhre einreihen, damit der Nutzer nicht auf den nächsten Pump-Lauf wartet.
        await _pump.PumpOneAsync(analysis.Id, ct);
        return await _queries.GetAsync(userId, analysis.Id, ct) ?? GameAnalysisQueries.ToDto(analysis, 0);
    }

    // ===== Stapel: Meister- und Vereinspartien ================================

    /// <summary>
    /// Eine Meisterpartie fuer den Stapel anlegen (<see cref="GameAnalysisOrigin.Library"/>, angestossen vom
    /// <see cref="MasterAnalysisScheduler"/>): dieselbe Tiefe und dieselben Linien wie eine vom Nutzer angeforderte
    /// Bibliothekspartie (<see cref="GameAnalysisDefaults.GuessTargetDepth"/>, fuenf Linien) — wer dieselbe Partie
    /// spaeter oeffnet, bekommt genau das, was er angefordert haette. Gerechnet wird auf den Hintergrund-Engines des
    /// Besitzers, ausschliesslich als Hintergrundarbeit. Kein Deckel je Nutzer: der Stapel haelt selbst nur so viel
    /// offen, wie Engines da sind. Wirft <see cref="ArgumentException"/> bei einem unspielbaren PGN.
    /// </summary>
    internal Task<GameAnalysisDto> CreateLibraryBatchAsync(int ownerUserId, int libraryGameId, string pgn, string title,
        CancellationToken ct = default)
        => CreateAsync(ownerUserId, new CreateGameAnalysisRequest
        {
            Pgn = pgn,
            Title = title,
            TargetDepth = GameAnalysisDefaults.GuessTargetDepth,
            MultiPv = GameAnalysisDefaults.MultiPv,
        }, ct, GameAnalysisOrigin.Library, engineOwnerUserId: ownerUserId, libraryGameId: libraryGameId);

    /// <summary>
    /// Eine Vereinspartie fuer den Stapel anlegen (<see cref="GameAnalysisOrigin.Club"/>) — genau wie
    /// <see cref="CreateLibraryBatchAsync"/>, nur mit dem Verweis auf die Partie der Vereins-Datenbank. Der Titel kommt
    /// aus dem Kopf des PGN (Weiss – Schwarz). Wirft <see cref="ArgumentException"/> bei einem unspielbaren PGN.
    /// </summary>
    internal Task<GameAnalysisDto> CreateClubBatchAsync(int ownerUserId, int leagueClubGameId, string pgn,
        CancellationToken ct = default)
        => CreateAsync(ownerUserId, new CreateGameAnalysisRequest
        {
            Pgn = pgn,
            TargetDepth = GameAnalysisDefaults.GuessTargetDepth,
            MultiPv = GameAnalysisDefaults.MultiPv,
        }, ct, GameAnalysisOrigin.Club, engineOwnerUserId: ownerUserId, leagueClubGameId: leagueClubGameId);

    /// <summary>Eine Liga-Partie aus einem Spielerprofil für den Stapel anlegen (<see cref="GameAnalysisOrigin.League"/>,
    /// 0.665.0) — wie <see cref="CreateClubBatchAsync"/>, erkannt über den Zug-Schlüssel.</summary>
    internal Task<GameAnalysisDto> CreateLeagueBatchAsync(int ownerUserId, string pgn, string movesHash, CancellationToken ct = default)
        => CreateAsync(ownerUserId, new CreateGameAnalysisRequest
        {
            Pgn = pgn,
            TargetDepth = GameAnalysisDefaults.GuessTargetDepth,
            MultiPv = GameAnalysisDefaults.MultiPv,
        }, ct, GameAnalysisOrigin.League, engineOwnerUserId: ownerUserId, movesHash: movesHash);

    /// <summary>
    /// Den Kopf der Analysen einer Vereinspartie nachziehen, nachdem sie korrigiert wurde (<c>LeagueClubService.UpdateAsync</c>):
    /// Namen, Ergebnis, Veranstaltung, Titel und das PGN selbst. Die Zuege aendert eine Korrektur nie, die Stellungen
    /// bleiben also gueltig — aber ein Name, der zu „Schwaz" wurde, darf in der Analyse nicht stehen bleiben.
    /// </summary>
    internal async Task SyncClubGameAsync(LeagueClubGame game, CancellationToken ct = default)
    {
        var analyses = await _db.GameAnalyses.Where(g => g.LeagueClubGameId == game.Id).ToListAsync(ct);
        if (analyses.Count == 0) return;
        var header = GamePlies.Parse(game.Pgn, GameAnalysisDefaults.MaxPlies)?.Header;
        foreach (var a in analyses)
        {
            a.Pgn = game.Pgn;
            a.White = header?.White ?? game.White;
            a.Black = header?.Black ?? game.Black;
            a.Result = header?.Result ?? game.Result;
            a.Event = header?.Event;
            var title = header is { } h ? GameAnalysisText.BuildTitle(h) : GameAnalysisText.BuildTitle(game.White, game.Black, null);
            if (a.TargetNodes is { } nodes) title += $" ({GameAnalysisText.NodesLabel(nodes)} nodes)";
            a.Title = title.Length > 200 ? title[..200] : title;
            a.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Die Analysen einer Vereinspartie auf KORRIGIERTE Züge umbauen (0.694.1, Wunsch 2026-10-06: „er sollte nur die
    /// geänderten Stellungen neu analysieren"). Vorher wurde jede Analyse gelöscht und die ganze Partie neu gerechnet —
    /// auch die Stellungen vor der ersten Korrektur, die gleich geblieben sind. Jetzt bleibt die Analyse (gleiche Id, die
    /// Kopien in „Meine Partien" bleiben verknüpft), ihre Stellungen werden durch die der neuen Zugfolge ersetzt, und jede
    /// neue Stellung, die schon gerechnet ist (gleiche Stellung, ohne Zugzähler — auch durch Zugumstellung), übernimmt das
    /// Ergebnis samt Vertiefung und Knotenstufen. Nur der Rest wird neu eingereiht (Status Pending, die Pumpe macht weiter).
    /// Was an Halbzügen hängt, gilt nicht mehr: offene Aufträge, Fehler-Erklärungen, geerntete Taktiken, Genauigkeit.
    /// → Zahl der übernommenen Stellungen über alle Analysen.
    /// </summary>
    internal async Task<int> RebaseClubGameAsync(LeagueClubGame game, CancellationToken ct = default)
    {
        var parsed = GamePlies.Parse(game.Pgn, GameAnalysisDefaults.MaxPlies);
        if (parsed is not { } ok) { await _deletion.DeleteForClubGameAsync(game.Id, ct); return 0; }
        var (header, plies) = ok;
        var analyses = await _db.GameAnalyses.Include(g => g.Positions)
            .Where(g => g.LeagueClubGameId == game.Id).ToListAsync(ct);
        static string Key(string fen) => string.Join(' ', fen.Split(' ').Take(4));
        var reused = 0;
        foreach (var a in analyses)
        {
            foreach (var jobId in a.Positions.Where(p => p.AnalysisJobId != null).Select(p => p.AnalysisJobId!.Value).ToList())
            {
                try { await _jobs.DeleteAsync(a.UserId, jobId, ct); }
                catch (Exception ex) { _logger.LogDebug(ex, "GameAnalysis: Auftrag {JobId} liess sich nicht loeschen", jobId); }
            }
            var done = a.Positions
                .Where(p => p.CandidatesJson != null && p.CandidatesJson != "[]" && p.AnalysisJobId == null)
                .GroupBy(p => Key(p.Fen)).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Depth).First());
            var fresh = plies.Select(p => new GameAnalysisPosition { Ply = p.Index, Fen = p.Fen, GameMoveUci = p.Uci, GameMoveSan = p.San })
                .ToList();
            if (plies.Count > 0 && GameAnalysisText.FinalPositionOf(plies[^1]) is { } finalFen)
                fresh.Add(new GameAnalysisPosition { Ply = plies.Count, Fen = finalFen });
            foreach (var n in fresh)
            {
                if (!done.TryGetValue(Key(n.Fen), out var o)) continue;
                n.CandidatesJson = o.CandidatesJson; n.Depth = o.Depth; n.EvalText = o.EvalText; n.AnalyzedAt = o.AnalyzedAt;
                n.Refined = o.Refined; n.NodeStepsJson = o.NodeStepsJson;
                reused++;
            }
            _db.GameAnalysisPositions.RemoveRange(a.Positions);
            a.Positions = fresh;
            a.PlyCount = plies.Count;
            a.OpeningLine = LibraryGameReader.StartsFromInitialPosition(header.StartFen) ? GameAnalysisText.OpeningLineOf(plies) : null;
            if (a.MovesHash != null) a.MovesHash = game.MovesHash;
            a.Status = GameAnalysisStatus.Pending;
            a.FinishedAt = null;
            a.AccuracyWhite = null;
            a.AccuracyBlack = null;
            a.LastError = null;
            a.TacticsScannedAt = null;
            if (a.RefineDepth != null && fresh.Any(p => !p.Refined)) a.RefinedAt = null;
            _db.GameMoveExplanations.RemoveRange(await _db.GameMoveExplanations.Where(x => x.GameAnalysisId == a.Id).ToListAsync(ct));
            _db.TacticCandidates.RemoveRange(await _db.TacticCandidates.Where(x => x.GameAnalysisId == a.Id).ToListAsync(ct));
            _logger.LogInformation("GameAnalysis {Id}: auf korrigierte Vereinspartie {ClubGameId} umgebaut — {Reused} von {Total} Stellungen übernommen",
                a.Id, game.Id, fresh.Count(p => p.CandidatesJson != null), fresh.Count);
        }
        await _db.SaveChangesAsync(ct);
        await SyncClubGameAsync(game, ct);
        return reused;
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
    /// <param name="libraryGameId">Aus welcher Zeile des Rohbestands die Partie angefordert wurde;
    /// <c>null</c> = selbst eingeworfen.</param>
    /// <param name="origin"><see cref="GameAnalysisOrigin.SavedGame"/> fuer „Partie analysieren" an
    /// einer gespeicherten Partie: dort ist die Bewertung das Ergebnis, deshalb rechnet sie mit
    /// <see cref="GameAnalysisDefaults.SavedGameTargetDepth"/> (30, fuenf Linien) in der Vertiefung statt 20. Gedeckelt wird gemeinsam;
    /// das Etikett entscheidet ausserdem, in welcher Liste die Analyse erscheint.</param>
    internal async Task<GuessUploadResult> CreateForGuessAsync(int userId, CreateGuessGameRequest req,
        CancellationToken ct = default, int? libraryGameId = null,
        GameAnalysisOrigin origin = GameAnalysisOrigin.Guess)
    {
        var open = await OpenGuessGamesAsync(userId, ct);
        if (open >= GameAnalysisDefaults.MaxOpenGuessGamesPerUser)
            return new GuessUploadResult(null, GuessUploadReason.TooManyOpen);

        var engineOwner = await ResolveGuessEngineOwnerAsync(userId, ct);
        if (engineOwner is null)
            return new GuessUploadResult(null, GuessUploadReason.NoEngine);

        try
        {
            // „Partie analysieren" rechnet ZWEIMAL (seit 0.523.0, gewuenscht 2026-09-24): erst schnell (Tiefe 20, eine
            // Linie — Kurve, Genauigkeit und Fehler stehen nach wenigen Minuten), dann im Hintergrund die Vertiefung
            // (Tiefe 30 seit 0.555.2, fuenf Linien — Zweitbester fuer Great/Brilliant, Computer-Linien, gleichwertige Zuege).
            var savedGame = origin == GameAnalysisOrigin.SavedGame;
            var dto = await CreateAsync(userId, new CreateGameAnalysisRequest
            {
                Pgn = req.Pgn,
                Title = req.Title,
                TargetDepth = savedGame ? GameAnalysisDefaults.SavedGameFastDepth : GameAnalysisDefaults.GuessTargetDepth,
                MultiPv = savedGame ? GameAnalysisDefaults.SavedGameFastMultiPv : GameAnalysisDefaults.MultiPv,
            }, ct, origin, engineOwner.Value, libraryGameId);
            if (savedGame)
            {
                var entity = await _db.GameAnalyses.FirstAsync(g => g.Id == dto.Id, ct);
                entity.RefineDepth = GameAnalysisDefaults.SavedGameTargetDepth;
                entity.RefineMultiPv = GameAnalysisDefaults.MultiPv;
                await _db.SaveChangesAsync(ct);
            }
            return new GuessUploadResult(dto, null);
        }
        catch (ArgumentException)
        {
            // Kein spielbares PGN — die einzige Absage, die am eingeworfenen Text liegt.
            return new GuessUploadResult(null, GuessUploadReason.InvalidPgn);
        }
    }

    /// <summary>Wie viele eingeworfene Partien dieses Nutzers noch rechnen — die von der
    /// Punktepartie-Seite UND die aus den gespeicherten Partien: beide laufen auf fremder Rechenzeit,
    /// und mit getrennten Deckeln kaeme man ueber den zweiten Weg am ersten vorbei. Gescheiterte
    /// zaehlen NICHT mit: sonst sperrte eine tote Engine den Nutzer dauerhaft aus, obwohl nichts mehr
    /// laeuft — er soll sie loeschen koennen und weitermachen.</summary>
    private Task<int> OpenGuessGamesAsync(int userId, CancellationToken ct) =>
        _db.GameAnalyses.CountAsync(g => g.UserId == userId
            && (g.Origin == GameAnalysisOrigin.Guess || g.Origin == GameAnalysisOrigin.SavedGame)
            && (g.Status == GameAnalysisStatus.Pending || g.Status == GameAnalysisStatus.Running), ct);

    /// <summary>Wer rechnet: erst die EIGENE Hintergrund-Engine, sonst die Haus-Engine, sonst niemand
    /// (<c>null</c>) — siehe <see cref="EngineOwnerResolver"/>.</summary>
    private Task<int?> ResolveGuessEngineOwnerAsync(int userId, CancellationToken ct)
        => EngineOwnerResolver.ResolveAsync(_db, userId, ct);

    /// <summary>Was die Punktepartie-Seite ueber das Einwerfen wissen muss, BEVOR jemand ein PGN
    /// hineinkopiert: gibt es ueberhaupt eine Engine, und wie viele Partien sind noch frei.</summary>
    internal async Task<GuessUploadStatusDto> GuessUploadStatusAsync(int userId, CancellationToken ct = default)
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
}
