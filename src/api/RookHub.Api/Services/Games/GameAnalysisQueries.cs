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
/// Lesen und Listen der Partie-Analysen (Teil von <see cref="GameAnalysisService"/>, Zerlegung Z5): eigene und kuratierte
/// Listen, Kopfdaten, Tempo, Konvergenz, Analysen derselben Partie und ihre Bewertungen — dazu der Schalter
/// „kuratierter Bestand“. Keine Aufträge, keine Pumpe.
/// </summary>
internal sealed class GameAnalysisQueries
{
    private readonly AppDbContext _db;

    internal GameAnalysisQueries(AppDbContext db) => _db = db;


    /// <summary>
    /// „Eigene Analysen" — dieselbe Liste auf der Punktepartie-Seite und auf „Partie-Analysen".
    /// Die ueber eine gespeicherte Partie angestossenen (<see cref="GameAnalysisOrigin.SavedGame"/>) kommen
    /// nur mit <paramref name="includeSavedGames"/> mit: auf „Partie-Analysen" gehoeren sie hin — dort steht
    /// der Fortschritt jeder laufenden Rechnung (gewuenscht 2026-09-24) —, auf der Punktepartie-Seite nicht:
    /// dort stuenden sie als Punktepartie, die niemand eingeworfen hat.
    /// </summary>
    internal Task<List<GameAnalysisDto>> ListAsync(int userId, CancellationToken ct = default, bool includeSavedGames = false) =>
        ProjectAsync(_db.GameAnalyses.AsNoTracking()
            .Where(g => g.UserId == userId && g.Origin != GameAnalysisOrigin.Library && g.Origin != GameAnalysisOrigin.Club && g.Origin != GameAnalysisOrigin.League
                && (includeSavedGames || g.Origin != GameAnalysisOrigin.SavedGame)), ct);

    /// <summary>
    /// Kopfdaten EINER Analyse ohne Stellungen — und OHNE Besitzer-Pruefung. Nur fuer Aufrufer, die
    /// den Zugriff selbst geprueft haben: <see cref="SavedGameService"/> reicht so die an einer
    /// geteilten Partie verknuepfte Analyse ihres Besitzers weiter, wenn ein Gast „Partie analysieren"
    /// drueckt (er bekommt die vorhandene statt einer zweiten Rechnung).
    /// </summary>
    internal async Task<GameAnalysisDto?> GetHeadUncheckedAsync(int id, CancellationToken ct = default)
        => (await ProjectAsync(_db.GameAnalyses.AsNoTracking().Where(g => g.Id == id), ct)).FirstOrDefault();

    /// <summary>
    /// Der kuratierte Bestand: Partien, die JEDER als Punktepartie spielen darf — auch ohne
    /// Anmeldung. Geliefert werden nur VOLLSTAENDIG gerechnete: eine halb fertige Partie laesst sich
    /// zwar anspielen, ueberspringt dann aber stillschweigend jede Stellung ohne Kandidatenliste —
    /// man raet also eine Partie mit Loechern, ohne dass irgendwo steht, warum. Sortiert nach Titel,
    /// nicht nach Anlagedatum: der Bestand ist eine Bibliothek und keine Zeitleiste.
    /// </summary>
    /// <param name="line">Eroeffnungszeile als Filter („e4 e5 Nf3"); leer = alles. Der
    /// Stellungsfilter der Punktepartie-Seite reicht sie durch — gesucht wird ueber ein PRAEFIX,
    /// und das trifft den Index auf <see cref="GameAnalysis.OpeningLine"/>.</param>
    internal async Task<List<GameAnalysisDto>> ListPublicAsync(CancellationToken ct = default,
        string? line = null)
    {
        var query = _db.GameAnalyses.AsNoTracking()
            .Where(g => g.IsPublic && g.Positions.Any() && !g.Positions.Any(p => p.CandidatesJson == null));

        var prefix = GuessOpeningTree.Normalize(line);
        if (prefix.Length > 0)
        {
            // Dieselbe Menge wie der Baum (GuessOpeningTree): die Partie, die genau hier endet, plus
            // alles, was danach weitergeht — Platzhalter aus der anonymen Eingabe maskiert.
            var muster = OpeningLines.ContinuationPattern(prefix);
            query = query.Where(g => g.OpeningLine != null && (g.OpeningLine == prefix
                || EF.Functions.Like(g.OpeningLine, muster, OpeningLines.LikeEscape)));
        }

        var rows = await ProjectAsync(query, ct);
        await FillGuessSideAsync(rows, ct);
        return rows.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Traegt in jede Zeile ein, welche Seite man dort uebernimmt — die des Gewinners.
    ///
    /// <para>Die Liste soll das VORHER sagen: im Bestand waehlt man die Seite nicht, und wer erst
    /// nach dem Start sieht, dass er Schwarz spielt, hat die Partie schon offen.</para>
    ///
    /// <para>Bei entschiedenen Partien steht die Antwort im Ergebnis. Die uebrigen brauchen die
    /// Bewertung der letzten gerechneten Stellung — dafuer genuegt die kurze Zeichenkette
    /// (<c>EvalText</c>), nicht die Kandidatenliste: die ist LONGTEXT und wuerde hier je Partie
    /// mitgelesen. Geholt wird sie in EINER Abfrage fuer alle betroffenen Partien; je Partie eine
    /// waere bei einem wachsenden Bestand genau das N+1-Muster, das Listen langsam macht.</para>
    /// </summary>
    private async Task FillGuessSideAsync(List<GameAnalysisDto> rows, CancellationToken ct)
    {
        var undecided = rows
            .Where(r => r.Result?.Trim() is not ("1-0" or "0-1"))
            .Select(r => r.Id)
            .ToList();

        var lastByGame = new Dictionary<int, (int Ply, string? EvalText)>();
        if (undecided.Count > 0)
        {
            var analysed = await _db.GameAnalysisPositions.AsNoTracking()
                .Where(p => undecided.Contains(p.GameAnalysisId) && p.CandidatesJson != null)
                .Select(p => new { p.GameAnalysisId, p.Ply, p.EvalText })
                .ToListAsync(ct);
            foreach (var group in analysed.GroupBy(p => p.GameAnalysisId))
            {
                var last = group.OrderByDescending(p => p.Ply).First();
                lastByGame[group.Key] = (last.Ply, last.EvalText);
            }
        }

        foreach (var row in rows)
        {
            lastByGame.TryGetValue(row.Id, out var last);
            row.GuessWhite = GuessSides.WinnerWhite(row.Result,
                last.Ply == 0 && last.EvalText is null ? null : last.Ply,
                GuessSides.PawnsFromEvalText(last.EvalText));
        }
    }

    /// <summary>Kuratierten Bestand ein-/ausschalten — Besitzer der Analyse oder Admin. Bewusst
    /// beim Besitzer und nicht nur beim Admin: wer die Engine-Zeit gestiftet hat, entscheidet auch,
    /// ob die Partie öffentlich spielbar ist (dieselbe Teilung wie beim Kalkulations-Schalter des
    /// Kurses).</summary>
    internal async Task<bool?> SetPublicAsync(int userId, bool isAdmin, int id, bool isPublic,
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
                g.EngineId, g.TargetNodes, g.Status, g.PlyCount, g.LastError, g.CreatedAt, g.FinishedAt, g.IsPublic,
                Annotated = g.Pgn.Contains("{"),
                Analyzed = g.Positions.Count(p => p.CandidatesJson != null && p.GameMoveUci != ""),
            })
            .ToListAsync(ct);
        return rows.Select(r => new GameAnalysisDto
        {
            Id = r.Id, Title = r.Title, White = r.White, Black = r.Black, Result = r.Result,
            Event = r.Event, TargetDepth = r.TargetDepth, MultiPv = r.MultiPv, EngineId = r.EngineId, TargetNodes = r.TargetNodes,
            Status = r.Status.ToString().ToLowerInvariant(), PlyCount = r.PlyCount,
            AnalyzedPlies = r.Analyzed, LastError = r.LastError, IsPublic = r.IsPublic,
            Annotated = r.Annotated,
            CreatedAt = r.CreatedAt, FinishedAt = r.FinishedAt,
        }).ToList();
    }

    /// <summary>So weit wird fuer das Tempo zurueckgesehen — nach einer Nacht Pause ist das Tempo von gestern
    /// die beste Schaetzung, die es gibt.</summary>
    private const int ThroughputLookbackHours = 24;

    /// <summary>Hoechstens so viele juengste Ergebnisse gehen ins Tempo ein — bei ein paar Stellungen je Minute
    /// rund die letzte Stunde, bei vielen Engines entsprechend weniger.</summary>
    private const int ThroughputMaxPositions = 200;

    /// <summary>
    /// Tempo und Restdauer aus den Zeitstempeln der gerechneten Stellungen — nach <see cref="AnalysisPace"/>:
    /// nur der juengste zusammenhaengende Lauf zaehlt, eine Pause davor nicht (bis 0.521.2 mittelte das hier ueber
    /// die letzte Stunde ab dem ersten Ergebnis darin, samt Pause).
    /// </summary>
    internal async Task<AnalysisThroughputDto> ThroughputAsync(int userId, CancellationToken ct = default)
    {
        var remaining = await _db.GameAnalysisPositions
            .CountAsync(p => p.CandidatesJson == null
                && p.GameAnalysis!.UserId == userId
                && (p.GameAnalysis.Status == GameAnalysisStatus.Pending
                    || p.GameAnalysis.Status == GameAnalysisStatus.Running), ct);

        var now = DateTime.UtcNow;
        var since = now.AddHours(-ThroughputLookbackHours);
        var stamps = await _db.GameAnalysisPositions
            .Where(p => p.AnalyzedAt != null && p.AnalyzedAt >= since && p.GameAnalysis!.UserId == userId)
            .OrderByDescending(p => p.AnalyzedAt)
            .Select(p => p.AnalyzedAt!.Value)
            .Take(ThroughputMaxPositions)
            .ToListAsync(ct);

        var result = new AnalysisThroughputDto { Remaining = remaining };
        if (AnalysisPace.Measure(stamps, now, ThroughputMaxPositions) is { } run)
        {
            result.AnalyzedInWindow = run.Count;
            result.WindowMinutes = (int)Math.Round(run.Seconds / 60);
            result.PerMinute = Math.Round(run.PerMinute, 2);
        }
        result.EtaMinutes = result.PerMinute > 0 && remaining > 0
            ? (int)Math.Max(1, Math.Round(remaining / result.PerMinute))
            : null;
        return result;
    }

    /// <summary>
    /// Die Kopfdaten einer Partie, die dieser Nutzer SPIELEN darf — seine eigene oder eine des
    /// kuratierten Bestands. Ohne die Zugliste.
    ///
    /// <para>Gebraucht, wenn jemand eine Partie aus dem Rohbestand anfordert, die schon im Bestand
    /// liegt: die gehoert dann einem ANDEREN Konto, und <see cref="GetAsync"/> fragt ausdruecklich
    /// nach dem Besitzer. Ohne diesen Weg haette die Anforderung „geht nicht" geantwortet, obwohl
    /// die Partie danebenliegt und jeder sie spielen darf.</para>
    /// </summary>
    internal async Task<GameAnalysisDto?> GetPlayableHeadAsync(int userId, int id, CancellationToken ct = default)
    {
        var rows = await ProjectAsync(
            _db.GameAnalyses.AsNoTracking().Where(g => g.Id == id
                && (g.UserId == userId || g.IsPublic || g.Origin == GameAnalysisOrigin.Library)), ct);
        return rows.FirstOrDefault();
    }

    /// <summary>Konvergenz einer Knotenanalyse: wie schnell der beste Zug und die Bewertung gegen das Ziel konvergieren (nur eigene Analyse;
    /// <c>null</c> = nicht gefunden). Ohne Zwischenstände (Tiefenanalyse, Altbestand) eine leere Auswertung.</summary>
    internal async Task<ConvergenceDto?> ConvergenceAsync(int userId, int id, CancellationToken ct = default)
    {
        var analysis = await _db.GameAnalyses.AsNoTracking()
            .Where(g => g.Id == id && g.UserId == userId)
            .Select(g => new { g.TargetNodes })
            .FirstOrDefaultAsync(ct);
        if (analysis is null) return null;

        var raw = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.GameAnalysisId == id && p.NodeStepsJson != null)
            .Select(p => p.NodeStepsJson!)
            .ToListAsync(ct);
        var report = Convergence.Evaluate(raw.Select(NodeSteps.Parse).Where(l => l.Count > 0).Select(l => (IReadOnlyList<NodeStep>)l));
        return new ConvergenceDto
        {
            TargetNodes = analysis.TargetNodes,
            Positions = report.Positions,
            Rows = report.Rows.Select(r => new ConvergenceRowDto
            {
                Threshold = r.Threshold, Positions = r.Positions, SameMovePercent = Math.Round(r.SameMoveShare * 100, 1),
                MedianCp = r.MedianCp, P90Cp = r.P90Cp, MedianWinPct = Math.Round(r.MedianWinPct, 2), P90WinPct = Math.Round(r.P90WinPct, 2),
                MoveChanges = r.MoveChanges, MateMismatch = r.MateMismatch, MeanNodes = Math.Round(r.MeanNodes),
            }).ToList(),
        };
    }

    internal async Task<GameAnalysisDto?> GetAsync(int userId, int id, CancellationToken ct = default)
    {
        var analysis = await _db.GameAnalyses.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);
        if (analysis is null) return null;

        var positions = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.GameAnalysisId == id && p.GameMoveUci != "")   // Endstellung (0.689.0) hat keinen Zug
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
    /// Alle eigenen Analysen DESSELBEN Spiels (0.682.0, gewünscht 2026-10-06: Lc0 neben Stockfish auf der Partieseite).
    /// Erkannt an der Zugfolge: gleiche Länge, gleiche UCI-Züge in derselben Reihenfolge. Eine Spalte mit fertigem
    /// Zug-Schlüssel gibt es nur für Liga-Partien; deshalb hier zuerst billig nach Länge vorgefiltert und erst für die
    /// wenigen Kandidaten die Züge verglichen. Gescheiterte bleiben draußen, neueste zuerst.
    /// </summary>
    internal async Task<List<GameAnalysisAlternativeDto>> SameGameAsync(int userId, IReadOnlyList<string>? ucis, CancellationToken ct = default,
        bool clubReader = false)
    {
        if (ucis is null || ucis.Count == 0 || ucis.Count > GameAnalysisService.MaxSameGamePlies) return [];
        var wanted = ucis.Select(u => (u ?? string.Empty).Trim().ToLowerInvariant()).ToList();
        var candidates = await _db.GameAnalyses.AsNoTracking()
            // Dazu die zweite Engine der Vereinspartien (0.684.0) für alle, die Vereinspartien sehen — sie gehört dem
            // Engine-Konto, gezeigt wird sie wie die Stockfish-Analyse der Partie allen im Verein.
            .Where(a => (a.UserId == userId || (clubReader && a.Origin == GameAnalysisOrigin.Club && a.EngineId != null))
                && a.PlyCount == wanted.Count && a.Status != GameAnalysisStatus.Failed)
            .Select(a => new { a.Id, a.Title, a.EngineId, a.TargetNodes, a.TargetDepth, a.MultiPv, a.Status, a.PlyCount, a.CreatedAt })
            .ToListAsync(ct);
        if (candidates.Count == 0) return [];

        var ids = candidates.Select(c => c.Id).ToList();
        var positions = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => ids.Contains(p.GameAnalysisId))
            .Select(p => new { p.GameAnalysisId, p.Ply, p.GameMoveUci, Done = p.CandidatesJson != null })
            .ToListAsync(ct);
        var byAnalysis = positions.GroupBy(p => p.GameAnalysisId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Ply).ToList());

        var matches = candidates.Where(c => byAnalysis.TryGetValue(c.Id, out var rows)
            && rows.Where(r => !string.IsNullOrEmpty(r.GameMoveUci)).Select(r => r.GameMoveUci!.ToLowerInvariant()).SequenceEqual(wanted)).ToList();
        if (matches.Count == 0) return [];

        var engineIds = matches.Select(m => m.EngineId).OfType<string>().Distinct().ToList();
        var names = engineIds.Count == 0 ? new Dictionary<string, string>()
            : await _db.ExternalEngineRegistrations.AsNoTracking().Where(r => engineIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        return matches.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Select(m => new GameAnalysisAlternativeDto(m.Id, m.Title, m.EngineId,
                m.EngineId is { } e && names.TryGetValue(e, out var n) ? n : null,
                m.TargetNodes, m.TargetDepth, m.MultiPv, m.Status.ToString().ToLowerInvariant(),
                Math.Min(m.PlyCount, byAnalysis[m.Id].Count(r => r.Done)), m.PlyCount))
            .ToList();
    }

    /// <summary>
    /// Die zweite Analyse (Lc0 o. Ä.) am Teilen-Link (0.819.0, Wunsch 2026-10-21: „sollte gleich sein wie bei /games/117 —
    /// Standard beim Teilen"): die Analysen des BESITZERS mit ausdrücklich gewählter Engine und genau den Zügen der geteilten
    /// Partie. Die Zugfolge kommt aus der verknüpften Analyse der Partie, nicht vom Aufrufer — sonst ließen sich über den
    /// Link beliebige andere Analysen des Besitzers erfragen. Ohne verknüpfte Analyse: keine.
    /// </summary>
    internal async Task<List<GameAnalysisAlternativeDto>> SharedAlternativesAsync(string? token, CancellationToken ct = default)
        => (await SharedAlternativesCoreAsync(token, ct)).List;

    /// <summary>Bewertungen einer dieser Analysen; <c>null</c>, wenn sie nicht zur geteilten Partie gehört.</summary>
    internal async Task<GameEvalsDto?> SharedAlternativeEvalsAsync(string? token, int id, CancellationToken ct = default)
    {
        var (ownerId, list) = await SharedAlternativesCoreAsync(token, ct);
        return ownerId is int owner && list.Any(a => a.Id == id) ? await EvalsOfAsync(owner, id, ct) : null;
    }

    private async Task<(int? OwnerId, List<GameAnalysisAlternativeDto> List)> SharedAlternativesCoreAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return (null, []);
        var game = await _db.SavedGames.AsNoTracking().Where(g => g.ShareToken == token)
            .Select(g => new { g.UserId, g.GameAnalysisId }).FirstOrDefaultAsync(ct);
        if (game?.GameAnalysisId is not int linked) return (null, []);
        var ucis = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.GameAnalysisId == linked && p.GameMoveUci != "")
            .OrderBy(p => p.Ply).Select(p => p.GameMoveUci).ToListAsync(ct);
        var list = (await SameGameAsync(game.UserId, ucis, ct)).Where(a => a.EngineId != null && a.Id != linked).ToList();
        return (game.UserId, list);
    }

    /// <summary>Die Bewertungen einer EIGENEN Analyse im Format der Partiekurve (Weiß-Sicht) — für den Umschalter auf
    /// der Partieseite. Ohne Buchzüge: die markiert schon die Kurve der Seite. <c>null</c> = gibt es nicht oder fremd.</summary>
    internal async Task<GameEvalsDto?> EvalsOfAsync(int userId, int id, CancellationToken ct = default, bool clubReader = false)
    {
        var head = await GameEvalsStore.Heads(_db.GameAnalyses.AsNoTracking().Where(a => a.Id == id
                && (a.UserId == userId || (clubReader && a.Origin == GameAnalysisOrigin.Club && a.EngineId != null))))
            .FirstOrDefaultAsync(ct);
        return head is null ? null : await GameEvalsStore.ReadAsync(_db, head, null, ct);
    }

    // ===== Mapping ==========================================================

    internal static GameAnalysisDto ToDto(GameAnalysis g, int analyzed) => new()
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
        TargetNodes = g.TargetNodes,
        Status = g.Status.ToString().ToLowerInvariant(),
        PlyCount = g.PlyCount,
        AnalyzedPlies = analyzed,
        LastError = g.LastError,
        CreatedAt = g.CreatedAt,
        FinishedAt = g.FinishedAt,
    };
}
