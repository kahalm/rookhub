using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Die Punktepartie: Der Nutzer übernimmt eine Seite einer analysierten Partie und rät Zug für Zug.
/// Gewertet wird gegen den TATSÄCHLICHEN Partiezug (<see cref="GuessScoring"/>), die Engine urteilt
/// nur über die Alternativen — die Kandidatenlisten stehen fertig in der
/// <see cref="GameAnalysis"/>, hier läuft keine Engine mehr.
///
/// <para><b>Die eiserne Regel:</b> Die Fortsetzung verlässt den Server nicht. Ausgeliefert wird
/// immer nur die aktuelle Stellung; der Partiezug kommt erst als ANTWORT auf den Rateversuch.
/// Dieselbe Disziplin wie im Kalkulations-Modus (dort: <c>BookPuzzle.Moves</c> bleibt drin).</para>
/// </summary>
public class GuessSessionService
{
    private readonly AppDbContext _db;

    public GuessSessionService(AppDbContext db) => _db = db;

    /// <summary>Vorgabe, wie viele Halbzüge Eröffnung gezeigt statt geraten werden. Grober
    /// Platzhalter, bis die Eröffnungsstatistik angebunden ist (chessgames startet dort, wo eine
    /// Stellung unter 1000 Datenbankpartien fällt) — Raten ab Zug 1 prüft Buchwissen, nicht Spielstärke.</summary>
    public const int DefaultSkipPlies = 8;

    // ===== Sitzung starten ==================================================

    public async Task<GuessSessionDto> StartAsync(int userId, CreateGuessSessionRequest req, CancellationToken ct = default)
    {
        var analysis = await _db.GameAnalyses.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == req.GameAnalysisId && g.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Analysis not found.");

        var analyzed = await _db.GameAnalysisPositions
            .CountAsync(p => p.GameAnalysisId == analysis.Id && p.CandidatesJson != null, ct);
        if (analyzed == 0)
            throw new InvalidOperationException("Diese Partie ist noch nicht analysiert.");

        var guessWhite = req.GuessWhite ?? true;
        var start = req.StartPly ?? DefaultSkipPlies;
        // Auf den ersten Halbzug der geratenen Seite ausrichten (Weiß = gerade Plies).
        if (start < 0) start = 0;
        if (start % 2 == 0 != guessWhite) start++;

        var session = new GuessSession
        {
            UserId = userId,
            GameAnalysisId = analysis.Id,
            GuessWhite = guessWhite,
            StartPly = start,
            CurrentPly = start,
        };
        _db.GuessSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        await AdvanceToPlayableAsync(session, ct);
        await _db.SaveChangesAsync(ct);
        return await BuildDtoAsync(session, ct);
    }

    public async Task<GuessSessionDto?> GetAsync(int userId, int sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(userId, sessionId, ct);
        return session is null ? null : await BuildDtoAsync(session, ct);
    }

    public async Task<List<GuessSessionDto>> ListAsync(int userId, CancellationToken ct = default)
    {
        var sessions = await _db.GuessSessions
            .Include(s => s.Moves)
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.StartedAt)
            .Take(100)
            .ToListAsync(ct);

        var result = new List<GuessSessionDto>(sessions.Count);
        foreach (var s in sessions) result.Add(await BuildDtoAsync(s, ct, withPosition: false));
        return result;
    }

    // ===== Raten ============================================================

    /// <summary>Einen Zug raten. <paramref name="uci"/> leer = passen (0 Punkte, keine Strafe).</summary>
    public async Task<GuessResultDto> GuessAsync(int userId, int sessionId, GuessMoveRequest req,
        CancellationToken ct = default)
    {
        var session = await LoadAsync(userId, sessionId, ct)
            ?? throw new KeyNotFoundException("Session not found.");
        if (session.Status == GuessSessionStatus.Done)
            throw new InvalidOperationException("Diese Punktepartie ist bereits beendet.");

        var position = await _db.GameAnalysisPositions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.GameAnalysisId == session.GameAnalysisId && p.Ply == session.CurrentPly, ct)
            ?? throw new InvalidOperationException("Zu dieser Sitzung gibt es keine Stellung mehr.");

        var playedUci = string.IsNullOrWhiteSpace(req.Uci) ? null : req.Uci.Trim().ToLowerInvariant();
        GuessGrade? grade = null;
        int? diffCp = null;
        string? playedSan = null;

        if (playedUci is not null)
        {
            playedSan = SanOf(position.Fen, playedUci);
            if (playedSan is null)
                throw new ArgumentException("Dieser Zug ist in der Stellung nicht möglich.");

            var candidates = BrokerCandidates.FromJson(position.CandidatesJson);
            var scored = GuessScoring.Evaluate(candidates, playedUci, position.GameMoveUci);
            if (scored is GuessScoring.GuessResult r)
            {
                grade = r.Grade;
                diffCp = (int)Math.Round((r.PlayedPawns - r.GamePawns) * 100);
            }
            // scored == null → Stellung nicht wertbar (Partiezug fehlt in der Liste): kein Grade,
            // keine Punkte, aber auch kein Abzug. Der Zug wird trotzdem protokolliert.
        }

        var move = new GuessMove
        {
            GuessSessionId = session.Id,
            Ply = position.Ply,
            PlayedUci = playedUci,
            Grade = grade,
            DiffCp = diffCp,
            SecondsSpent = Math.Clamp(req.AddSeconds ?? 0, 0, MaxSecondsPerMove),
        };
        // NUR über die Navigation anhängen — die Sitzung ist getrackt. Zusätzlich `_db.GuessMoves.Add`
        // legte die Zeile ein ZWEITES Mal an (im Test: 6 Züge nach 3 Rateversuchen).
        session.Moves.Add(move);
        session.SecondsSpent = Math.Min(session.SecondsSpent + move.SecondsSpent, MaxSecondsPerSession);

        // Partiezug + Antwort des Gegners nachspielen.
        var reply = await _db.GameAnalysisPositions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.GameAnalysisId == session.GameAnalysisId && p.Ply == position.Ply + 1, ct);

        session.CurrentPly = position.Ply + 2;   // eigener Zug + Gegenzug
        await AdvanceToPlayableAsync(session, ct);
        await _db.SaveChangesAsync(ct);

        var evalText = await EvalTextAfterAsync(session, position.Ply + 1, ct);

        return new GuessResultDto
        {
            Grade = grade is GuessGrade g ? CamelCase(g.ToString()) : null,
            Points = grade is GuessGrade g2 ? GuessGrades.PointsFor(g2) : 0,
            PlayedSan = playedSan,
            GameMoveSan = position.GameMoveSan,
            GameMoveUci = position.GameMoveUci,
            ReplySan = reply?.GameMoveSan,
            ReplyUci = reply?.GameMoveUci,
            DiffCp = diffCp,
            EvalText = evalText,
            Session = await BuildDtoAsync(session, ct),
        };
    }

    /// <summary>Rückblick nach dem Ende: jeder Halbzug mit dem, was gespielt und was geraten wurde.</summary>
    public async Task<List<GuessReviewMoveDto>?> ReviewAsync(int userId, int sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(userId, sessionId, ct);
        if (session is null) return null;

        var plies = session.Moves.Select(m => m.Ply).ToList();
        var positions = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.GameAnalysisId == session.GameAnalysisId && plies.Contains(p.Ply))
            .ToDictionaryAsync(p => p.Ply, ct);

        return session.Moves.OrderBy(m => m.Ply).Select(m => new GuessReviewMoveDto
        {
            Ply = m.Ply,
            MoveNumber = m.Ply / 2 + 1,
            White = m.Ply % 2 == 0,
            GameSan = positions.TryGetValue(m.Ply, out var p) ? p.GameMoveSan : string.Empty,
            PlayedSan = m.PlayedUci is null || !positions.ContainsKey(m.Ply)
                ? null
                : SanOf(positions[m.Ply].Fen, m.PlayedUci),
            Grade = m.Grade is GuessGrade g ? CamelCase(g.ToString()) : null,
            Points = m.Grade is GuessGrade g2 ? GuessGrades.PointsFor(g2) : 0,
            DiffCp = m.DiffCp,
            SecondsSpent = m.SecondsSpent,
        }).ToList();
    }

    public async Task<bool> DeleteAsync(int userId, int sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(userId, sessionId, ct);
        if (session is null) return false;
        _db.GuessMoves.RemoveRange(session.Moves);
        _db.GuessSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ===== Innereien ========================================================

    private const int MaxSecondsPerMove = 3600;
    private const int MaxSecondsPerSession = 24 * 3600;

    private Task<GuessSession?> LoadAsync(int userId, int sessionId, CancellationToken ct) =>
        _db.GuessSessions.Include(s => s.Moves)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

    /// <summary>
    /// Rückt <c>CurrentPly</c> auf den nächsten Halbzug vor, der sich WERTEN lässt: eine Stellung
    /// ohne Kandidatenliste (noch nicht gerechnet, oder von der Engine aufgegeben) oder eine, in der
    /// der Partiezug nicht in der Liste steht, wird übersprungen statt mit 0 abgestraft — ohne
    /// Bezugspunkt wäre Raten unfair. Ist keine mehr da, ist die Sitzung durch.
    /// </summary>
    private async Task AdvanceToPlayableAsync(GuessSession session, CancellationToken ct)
    {
        var positions = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.GameAnalysisId == session.GameAnalysisId && p.Ply >= session.CurrentPly)
            .OrderBy(p => p.Ply)
            .ToListAsync(ct);

        foreach (var p in positions)
        {
            if (p.Ply % 2 == 0 != session.GuessWhite) continue;   // Zug der Gegenseite
            if (p.CandidatesJson is null) break;                  // noch nicht gerechnet → hier warten
            var candidates = BrokerCandidates.FromJson(p.CandidatesJson);
            if (candidates.Any(c => string.Equals(c.Uci, p.GameMoveUci, StringComparison.OrdinalIgnoreCase)))
            {
                session.CurrentPly = p.Ply;
                return;
            }
        }

        // Nichts Wertbares mehr: fertig, wenn hinter dem letzten Halbzug; sonst wartet die Analyse.
        var lastPly = await _db.GameAnalysisPositions
            .Where(p => p.GameAnalysisId == session.GameAnalysisId)
            .MaxAsync(p => (int?)p.Ply, ct) ?? -1;
        if (session.CurrentPly > lastPly)
        {
            session.Status = GuessSessionStatus.Done;
            session.FinishedAt ??= DateTime.UtcNow;
        }
    }

    private async Task<GuessSessionDto> BuildDtoAsync(GuessSession session, CancellationToken ct, bool withPosition = true)
    {
        var analysis = await _db.GameAnalyses.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == session.GameAnalysisId, ct);

        var moves = session.Moves ?? new List<GuessMove>();
        var points = moves.Where(m => m.Grade is not null).Sum(m => GuessGrades.PointsFor(m.Grade!.Value));
        var hits = moves.Count(m => m.Grade is GuessGrade.GameMove or GuessGrade.OnlyMove);

        var dto = new GuessSessionDto
        {
            Id = session.Id,
            GameAnalysisId = session.GameAnalysisId,
            Title = analysis?.Title,
            White = analysis?.White,
            Black = analysis?.Black,
            GuessWhite = session.GuessWhite,
            StartPly = session.StartPly,
            Status = session.Status == GuessSessionStatus.Done ? "done" : "running",
            Points = points,
            MaxPoints = moves.Count(m => m.Grade is not null) * GuessGrades.MaxPointsPerMove,
            MovesPlayed = moves.Count,
            GameMoveHits = hits,
            SecondsSpent = session.SecondsSpent,
            TotalGuesses = await CountGuessablePliesAsync(session, ct),
        };

        if (withPosition && session.Status == GuessSessionStatus.Running)
        {
            var pos = await _db.GameAnalysisPositions.AsNoTracking()
                .FirstOrDefaultAsync(p => p.GameAnalysisId == session.GameAnalysisId && p.Ply == session.CurrentPly, ct);
            if (pos is not null)
            {
                var previous = await _db.GameAnalysisPositions.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.GameAnalysisId == session.GameAnalysisId && p.Ply == pos.Ply - 1, ct);
                dto.Position = new GuessPositionDto
                {
                    Ply = pos.Ply,
                    MoveNumber = pos.Ply / 2 + 1,
                    WhiteToMove = pos.Ply % 2 == 0,
                    Fen = pos.Fen,
                    LastMoveUci = previous?.GameMoveUci,
                };
            }
        }
        return dto;
    }

    private Task<int> CountGuessablePliesAsync(GuessSession session, CancellationToken ct) =>
        _db.GameAnalysisPositions
            .Where(p => p.GameAnalysisId == session.GameAnalysisId && p.Ply >= session.StartPly)
            .CountAsync(p => (p.Ply % 2 == 0) == session.GuessWhite, ct);

    /// <summary>Bewertung nach dem Partiezug, aus Sicht der geratenen Seite (die Stellung danach
    /// gehört dem Gegner, deshalb wird das Vorzeichen gedreht).</summary>
    private async Task<string?> EvalTextAfterAsync(GuessSession session, int ply, CancellationToken ct)
    {
        var next = await _db.GameAnalysisPositions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.GameAnalysisId == session.GameAnalysisId && p.Ply == ply, ct);
        var candidates = BrokerCandidates.FromJson(next?.CandidatesJson);
        if (candidates.Count == 0) return null;
        var pawns = -candidates[0].Eval.Pawns;   // Sicht drehen: dort ist der Gegner am Zug
        return (pawns > 0 ? "+" : "") + pawns.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>SAN eines UCI-Zuges in einer Stellung; <c>null</c>, wenn der Zug dort nicht geht.</summary>
    private static string? SanOf(string fen, string uci)
    {
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            var move = Array.Find(board.Moves(generateSan: true), m => GamePlies.ToUci(m) == uci);
            return move is null ? null : (string.IsNullOrEmpty(move.San) ? uci : move.San);
        }
        catch { return null; }
    }

    private static string CamelCase(string s) => char.ToLowerInvariant(s[0]) + s[1..];
}
