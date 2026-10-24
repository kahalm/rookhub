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
/// Wer ist mit dem Nachlegen dran (Teil der Pumpe von <see cref="GameAnalysisService"/>, Zerlegung Z5): die
/// Datenbank-Seite der Reihenfolge — offene Stellungen älterer Partien, nutzbare Engines, Freigabe der Haus-Engine.
/// Die Regeln selbst stehen als reine Funktionen in <see cref="GameAnalysisTurnRules"/>.
/// </summary>
internal sealed class GameAnalysisTurns
{
    private readonly AppDbContext _db;
    private readonly QuietHours? _quiet;

    internal GameAnalysisTurns(AppDbContext db, QuietHours? quiet)
    {
        _db = db;
        _quiet = quiet;
    }

    /// <summary>
    /// Ist DIESE Partie gerade dran? Je Nutzer wird immer nur EINE Partie weitergefuettert: die
    /// aelteste, die noch ungerechnete Stellungen hat.
    ///
    /// <para><b>Warum nicht mehrere gleichzeitig.</b> Frueher fuetterte die Pumpe jede unfertige
    /// Partie bis zum Block-Limit; bei fuenfzig offenen Auftraegen je Nutzer liefen damit vier bis
    /// fuenf Partien nebeneinander, und alle wurden gleich langsam fertig. Das ist die schlechteste
    /// aller Aufteilungen: die Engine-Zeit ist dieselbe, aber man wartet auf JEDE Partie das
    /// Fuenffache, statt nach einem Fuenftel der Zeit die erste spielen zu koennen. Die Punktepartie
    /// braucht eine FERTIGE Partie — eine halb gerechnete ist nichts wert.</para>
    ///
    /// <para>Gemessen wird an den STELLUNGEN und nicht am Status: eine Partie, deren Zeilen alle
    /// eine Kandidatenliste haben, ist durch, auch wenn ihr Status noch nicht nachgezogen ist.
    /// Gescheiterte Partien blockieren nicht — die Pumpe fasst sie gar nicht erst an.</para>
    ///
    /// <para>Je NUTZER, nicht global: zwei Leute sollen sich nicht gegenseitig ausbremsen. Die
    /// Engine selbst serialisiert ohnehin, sie rechnet je Engine genau eine Suche.</para>
    ///
    /// <para>Laufende Auftraege einer anderen Partie werden NICHT abgebrochen. Sie sind bezahlte
    /// Rechenzeit; sie laufen aus, und nachgelegt wird nur noch bei der Partie, die dran ist.</para>
    /// </summary>
    internal async Task<bool> IsOwnersTurnAsync(GameAnalysis analysis, CancellationToken ct)
    {
        // Meisterpartien reihen sich nicht in die Partien des Besitzers ein: ihre Auftraege sind durchweg
        // Hintergrundarbeit und weichen ohnehin — auf den Besitzer zu warten hiesse, dass eine einzige haengende
        // eigene Partie den ganzen Stapel anhaelt.
        if (GameAnalysisOrigins.IsBatch(analysis.Origin)) return true;
        var olderOpen = await OpenPliesOfOlderGamesAsync(analysis, ct);
        // Nichts Aelteres offen: dran, ohne die Engines zu zaehlen (TailMayAdvance gaebe dasselbe).
        if (olderOpen == 0) return true;
        // Der SCHWANZ (0.543.0) — Regel und Messung stehen an GameAnalysisTurnRules.TailMayAdvance.
        return GameAnalysisTurnRules.TailMayAdvance(olderOpen, await EngineSlotsAsync(analysis, ct));
    }

    /// <summary>Offene (ungerechnete) Stellungen aller unfertigen Partien dieses Nutzers, die VOR
    /// <paramref name="analysis"/> an der Reihe sind (aelter nach CreatedAt, dann Id). Knoten-Analysen (lc0 & Co.) laufen
    /// auf ihrer eigenen Engine und bilden eine eigene Spur: sie warten nicht auf Tiefen-Partien und halten sie nicht auf —
    /// sonst stuende die Stockfish-Analyse derselben Partie stundenlang hinter der langsamen lc0-Rechnung.</summary>
    private Task<int> OpenPliesOfOlderGamesAsync(GameAnalysis analysis, CancellationToken ct)
    {
        var nodesLane = analysis.TargetNodes != null;
        return _db.GameAnalyses
            .Where(g => g.UserId == analysis.UserId && g.Id != analysis.Id
                && (g.TargetNodes != null) == nodesLane
                && g.Origin != GameAnalysisOrigin.Library && g.Origin != GameAnalysisOrigin.Club && g.Origin != GameAnalysisOrigin.League
                && (g.Status == GameAnalysisStatus.Pending || g.Status == GameAnalysisStatus.Running)
                && (g.CreatedAt < analysis.CreatedAt || (g.CreatedAt == analysis.CreatedAt && g.Id < analysis.Id)))
            .SumAsync(g => g.Positions.Count(p => p.CandidatesJson == null), ct);
    }

    /// <summary>Offene Stellungen des ERSTEN Durchgangs ueber alle unfertigen Partien des Nutzers.</summary>
    private Task<int> OpenFirstPassPliesAsync(int userId, CancellationToken ct)
        => _db.GameAnalyses
            .Where(g => g.UserId == userId && g.Origin != GameAnalysisOrigin.Library && g.Origin != GameAnalysisOrigin.Club && g.Origin != GameAnalysisOrigin.League
                && (g.Status == GameAnalysisStatus.Pending || g.Status == GameAnalysisStatus.Running))
            .SumAsync(g => g.Positions.Count(p => p.CandidatesJson == null), ct);

    /// <summary>Wie viele Suchen fuer diese Partie nebeneinander laufen koennen: die Hintergrund-Engines
    /// des ENGINE-BESITZERS (Haus-Engine: das Haus-Konto), bei fest gewaehlter Engine genau eine.
    /// Mindestens 1 — ohne Engine scheitert das Einreihen ohnehin mit eigener Meldung.</summary>
    internal async Task<int> EngineSlotsAsync(GameAnalysis analysis, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(analysis.EngineId)) return 1;
        // Nur die Engines, die gerade Arbeit nehmen (0.679.0): bei Teillast eines Clients stehen einige still, und
        // Suchen fuer sie oeffneten Auftraege, die erst nach einem 503 woanders landen.
        return Math.Max(1, (await UsableEnginesAsync(analysis, ct)).Count);
    }

    /// <summary>Die Hintergrund-Engines, die fuer diese Analyse JETZT Arbeit bekommen duerfen — nach dem Zeitplan, den
    /// ihr Client meldet, sonst nach den Sperrzeiten von RookHub (nur fuer den Stapel). Siehe
    /// <see cref="EngineAvailability"/>.</summary>
    internal async Task<List<string>> UsableEnginesAsync(GameAnalysis analysis, CancellationToken ct)
    {
        var ownerId = analysis.EngineOwnerUserId ?? analysis.UserId;
        IReadOnlyList<string> engines;
        if (!string.IsNullOrWhiteSpace(analysis.EngineId))
        {
            engines = [analysis.EngineId];
        }
        else
        {
            var cred = await _db.LichessEngineCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == ownerId, ct);
            engines = cred?.BackgroundEngines ?? [];
        }
        return await EngineAvailability.UsableAsync(_db, ownerId, engines, GameAnalysisOrigins.IsBatch(analysis.Origin),
            _quiet, _quiet?.Now ?? DateTimeOffset.UtcNow, ct);
    }

    /// <summary>
    /// Ist diese Partie mit der Vertiefung dran? Erst wenn KEINE Partie des Nutzers mehr im ersten Durchgang steckt —
    /// der schnelle Durchgang einer neuen Partie geht immer vor —, und dann die aelteste, die noch vertieft wird
    /// (dieselbe Regel „eine nach der anderen" wie bei <see cref="IsOwnersTurnAsync"/>), MIT demselben Schwanz.
    /// </summary>
    internal async Task<bool> IsOwnersRefineTurnAsync(GameAnalysis analysis, CancellationToken ct)
    {
        // Dieselbe Schwanz-Regel wie im ersten Durchgang (0.543.0): solange der erste Durchgang mehr offene
        // Stellungen hat als Engines da sind, wartet die Vertiefung; darunter darf sie die freien Engines
        // nehmen — ihre Auftraege sind ohnehin Hintergrundarbeit, die der Worker nur ohne normalen Auftrag zieht.
        var firstPassOpen = await OpenFirstPassPliesAsync(analysis.UserId, ct);
        if (firstPassOpen > 0 && !GameAnalysisTurnRules.TailMayAdvance(firstPassOpen, await EngineSlotsAsync(analysis, ct)))
            return false;
        // Und der Schwanz unter den Vertiefungen selbst (0.647.0). Bis dahin war nur die aelteste dran — am 04.10.2026
        // hing Analyse 5540 eine Stunde an ihrer LETZTEN Stellung (Tiefe 30, fuenf Linien), und die geteilte Partie
        // dahinter bekam keinen einzigen Auftrag, waehrend 15 der 16 Engines Meisterpartien rechneten.
        var olderUnrefined = await UnrefinedPliesOfOlderGamesAsync(analysis, ct);
        return olderUnrefined == 0
            || GameAnalysisTurnRules.TailMayAdvance(olderUnrefined, await EngineSlotsAsync(analysis, ct));
    }

    /// <summary>Noch nicht vertiefte Stellungen der AELTEREN Partien des Nutzers, die gerade vertieft werden —
    /// dieselbe Reihenfolge (<c>CreatedAt</c>, dann <c>Id</c>) wie <see cref="OpenPliesOfOlderGamesAsync"/>.</summary>
    private Task<int> UnrefinedPliesOfOlderGamesAsync(GameAnalysis analysis, CancellationToken ct)
        => _db.GameAnalyses
            .Where(g => g.UserId == analysis.UserId && g.Id != analysis.Id
                && g.Status == GameAnalysisStatus.Done && g.RefineDepth != null && g.RefinedAt == null
                && (g.CreatedAt < analysis.CreatedAt || (g.CreatedAt == analysis.CreatedAt && g.Id < analysis.Id)))
            .SumAsync(g => g.Positions.Count(p => !p.Refined), ct);

    /// <summary>Rechnet diese Analyse auf einer FREMDEN Haus-Engine, deren Freigabe inzwischen weg ist (Haekchen
    /// zurueckgenommen, Admin-Rolle verloren, Hintergrund-Liste leer)? Geprueft wird vor JEDEM Nachlegen, nicht nur beim
    /// Einwurf — sonst fuetterten Pumpe, Vertiefung und Neustart die Maschine weiter (Codereview 2026-09-29, A4-004).
    /// Eigene Engine und Stapel (<see cref="GameAnalysis.EngineOwnerUserId"/> = <c>null</c>) fragen nichts ab.</summary>
    internal async Task<bool> HouseEngineWithdrawnAsync(GameAnalysis analysis, CancellationToken ct)
        => analysis.EngineOwnerUserId is int owner
           && !await EngineOwnerResolver.IsHouseEngineSharedAsync(_db, owner, ct);
}
