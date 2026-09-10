using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Wem ein Durchlauf gehört: einem Konto ODER — ohne Anmeldung — der vom Browser vergebenen
/// Sitzungskennung (dasselbe Muster wie bei den anonymen Puzzle-Versuchen,
/// <c>BookPuzzleAttempt.AnonymousSessionId</c>).
///
/// <para>Bewusst EIN Typ durch den ganzen Dienst statt eines zweiten, anonymen Pfades: hier hängt
/// die eiserne Regel dran (die Fortsetzung verlässt den Server nicht), und zwei Umsetzungen davon
/// laufen irgendwann auseinander. Der Aufrufer entscheidet nur, WER fragt.</para>
/// </summary>
public readonly record struct GuessOwner(int? UserId, string? AnonymousSessionId)
{
    public static GuessOwner ForUser(int userId) => new(userId, null);

    /// <summary>Anonymer Besitzer. Die Kennung ist ungeprüft Client-Eingabe — sie MUSS vorher gegen
    /// <see cref="ValidationConstants.SessionIdPattern"/> laufen (Controller), sonst wäre ein kurzer,
    /// erratbarer Wert der Weg in fremde Durchläufe.</summary>
    public static GuessOwner ForAnonymous(string sessionId) => new(null, sessionId);

    public bool IsAnonymous => UserId is null;
}

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

    public async Task<GuessSessionDto> StartAsync(GuessOwner owner, CreateGuessSessionRequest req, CancellationToken ct = default)
    {
        // Spielbar ist eine EIGENE Analyse oder eine aus dem kuratierten Bestand
        // (<c>GameAnalysis.IsPublic</c>) — letztere auch ohne Anmeldung. Zwei Abfragen statt einer
        // mit `||`: die anonyme darf gar nicht erst nach einem Besitzer fragen.
        var analysis = await (owner.UserId is { } auid
                ? _db.GameAnalyses.AsNoTracking()
                    .FirstOrDefaultAsync(g => g.Id == req.GameAnalysisId && (g.UserId == auid || g.IsPublic), ct)
                : _db.GameAnalyses.AsNoTracking()
                    .FirstOrDefaultAsync(g => g.Id == req.GameAnalysisId && g.IsPublic, ct))
            ?? throw new KeyNotFoundException("Analysis not found.");

        var analyzed = await _db.GameAnalysisPositions
            .CountAsync(p => p.GameAnalysisId == analysis.Id && p.CandidatesJson != null, ct);
        if (analyzed == 0)
            throw new InvalidOperationException("Diese Partie ist noch nicht analysiert.");

        var guessWhite = req.GuessWhite ?? await WinnerSideAsync(analysis, ct);
        // Auf die Partie eingrenzen, BEVOR ausgerichtet wird: ohne Deckel liefe `start++` bei
        // int.MaxValue in den negativen Bereich (unchecked) und die Sitzung startete mit einem
        // sinnlosen StartPly, gegen den auch der Fortschritt gezaehlt wuerde.
        var start = Math.Clamp(req.StartPly ?? DefaultSkipPlies, 0, GameAnalysisDefaults.MaxPlies);
        // Auf den ersten Halbzug der geratenen Seite ausrichten (Weiß = gerade Plies).
        if (start % 2 == 0 != guessWhite) start++;

        await TrimSessionsAsync(owner, ct);

        var session = new GuessSession
        {
            UserId = owner.UserId,
            AnonymousSessionId = owner.AnonymousSessionId,
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

    public async Task<GuessSessionDto?> GetAsync(GuessOwner owner, int sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(owner, sessionId, ct);
        return session is null ? null : await BuildDtoAsync(session, ct);
    }

    public async Task<List<GuessSessionDto>> ListAsync(GuessOwner owner, CancellationToken ct = default)
    {
        var sessions = await OwnedBy(owner)
            .Include(s => s.Moves)
            .OrderByDescending(s => s.StartedAt)
            .Take(100)
            .ToListAsync(ct);

        // Kopfdaten und Halbzuege der beteiligten Partien EINMAL holen. Zuvor stellte jede Sitzung
        // zwei eigene Abfragen (Partie-Kopf + Zaehlung) — bei 100 Laeufen 200 Rundreisen, und der
        // Partie-Kopf zog jedes Mal das LONGTEXT-`Pgn` mit.
        var analysisIds = sessions.Select(s => s.GameAnalysisId).Distinct().ToList();
        var heads = await _db.GameAnalyses.AsNoTracking()
            .Where(g => analysisIds.Contains(g.Id))
            .Select(g => new { g.Id, Head = new AnalysisHead(g.Title, g.White, g.Black) })
            .ToDictionaryAsync(x => x.Id, x => x.Head, ct);
        var pliesByAnalysis = (await _db.GameAnalysisPositions.AsNoTracking()
                .Where(p => analysisIds.Contains(p.GameAnalysisId))
                .Select(p => new { p.GameAnalysisId, p.Ply })
                .ToListAsync(ct))
            .GroupBy(x => x.GameAnalysisId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Ply).ToArray());

        var result = new List<GuessSessionDto>(sessions.Count);
        foreach (var s in sessions)
        {
            var plies = pliesByAnalysis.TryGetValue(s.GameAnalysisId, out var arr) ? arr : Array.Empty<int>();
            var total = plies.Count(ply => ply >= s.StartPly && (ply % 2 == 0) == s.GuessWhite);
            result.Add(await BuildDtoAsync(s, ct, withPosition: false,
                head: heads.TryGetValue(s.GameAnalysisId, out var h) ? h : default, totalGuesses: total));
        }
        return result;
    }

    // ===== Raten ============================================================

    /// <summary>Einen Zug raten. <paramref name="uci"/> leer = passen (0 Punkte, keine Strafe).</summary>
    public async Task<GuessResultDto> GuessAsync(GuessOwner owner, int sessionId, GuessMoveRequest req,
        CancellationToken ct = default)
    {
        var session = await LoadAsync(owner, sessionId, ct)
            ?? throw new KeyNotFoundException("Session not found.");
        if (session.Status == GuessSessionStatus.Done)
            throw new InvalidOperationException("Diese Punktepartie ist bereits beendet.");

        var position = await _db.GameAnalysisPositions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.GameAnalysisId == session.GameAnalysisId && p.Ply == session.CurrentPly, ct)
            ?? throw new InvalidOperationException("Zu dieser Sitzung gibt es keine Stellung mehr.");

        // Die Sitzung darf einer Partie davonlaufen, die noch gerechnet wird (spielbar ist sie ab der
        // ERSTEN fertigen Stellung). Eine Stellung ohne Kandidatenliste hat keinen Bezugspunkt: sie
        // hier zu werten hiesse, sie ohne Punkte zu verbrennen und nie wieder zu zeigen — der Nutzer
        // arbeitete sich mit 0 durch die halbe Partie. Also stehenbleiben und darauf hinweisen.
        if (position.CandidatesJson is null)
            throw new InvalidOperationException("Diese Stellung wird noch gerechnet — gleich nochmal versuchen.");

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
    public async Task<List<GuessReviewMoveDto>?> ReviewAsync(GuessOwner owner, int sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(owner, sessionId, ct);
        if (session is null) return null;

        var plies = session.Moves.Select(m => m.Ply).ToList();
        var positions = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.GameAnalysisId == session.GameAnalysisId && plies.Contains(p.Ply))
            .ToDictionaryAsync(p => p.Ply, ct);

        var rows = new List<GuessReviewMoveDto>();
        foreach (var m in session.Moves.OrderBy(m => m.Ply))
        {
            positions.TryGetValue(m.Ply, out var p);
            var row = new GuessReviewMoveDto
            {
                Ply = m.Ply,
                MoveNumber = m.Ply / 2 + 1,
                White = m.Ply % 2 == 0,
                GameSan = p?.GameMoveSan ?? string.Empty,
                PlayedSan = m.PlayedUci is null || p is null ? null : SanOf(p.Fen, m.PlayedUci),
                Grade = m.Grade is GuessGrade g ? CamelCase(g.ToString()) : null,
                Points = m.Grade is GuessGrade g2 ? GuessGrades.PointsFor(g2) : 0,
                DiffCp = m.DiffCp,
                SecondsSpent = m.SecondsSpent,
            };

            // Der beste Zug der Engine samt Bewertung — und die des Partiezuges zum Vergleich.
            // Beide aus DERSELBEN Liste, also aus Sicht der Seite am Zug. Ausgeliefert wird nichts
            // Neues: die Sitzung ist an dieser Stelle vorbei, es gibt keine Loesung mehr zu schuetzen.
            if (p?.CandidatesJson is not null)
            {
                var candidates = BrokerCandidates.FromJson(p.CandidatesJson);
                if (candidates.Count > 0)
                {
                    row.BestSan = SanOf(p.Fen, candidates[0].Uci);
                    row.BestEval = candidates[0].Eval.Text;
                }
                foreach (var c in candidates)
                    if (string.Equals(c.Uci, p.GameMoveUci, StringComparison.OrdinalIgnoreCase))
                    {
                        row.GameEval = c.Eval.Text;
                        break;
                    }
            }
            rows.Add(row);
        }
        return rows;
    }

    public async Task<bool> DeleteAsync(GuessOwner owner, int sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(owner, sessionId, ct);
        if (session is null) return false;
        _db.GuessMoves.RemoveRange(session.Moves);
        _db.GuessSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ===== Innereien ========================================================

    private const int MaxSecondsPerMove = 3600;
    private const int MaxSecondsPerSession = 24 * 3600;

    /// <summary>Wie viele Durchläufe ein Besitzer behält. Die Übersicht zeigt ohnehin nur 100;
    /// entscheidend ist aber, dass <c>POST</c> auch OHNE Anmeldung Zeilen anlegt — ohne Deckel
    /// wächst die Tabelle mit jedem Aufruf, den der Rate-Limiter durchlässt. Weggeräumt werden nur
    /// BEENDETE Durchläufe (der laufende ist die Arbeit des Nutzers) und immer der älteste zuerst,
    /// dieselbe Regel wie <c>AnalysisJobService.MaxJobsPerUser</c>.</summary>
    public const int MaxSessionsPerOwner = 50;

    private async Task TrimSessionsAsync(GuessOwner owner, CancellationToken ct)
    {
        var count = await OwnedBy(owner).CountAsync(ct);
        if (count < MaxSessionsPerOwner) return;

        var stale = await OwnedBy(owner)
            .Where(s => s.Status == GuessSessionStatus.Done)
            .OrderBy(s => s.StartedAt)
            .Take(count - MaxSessionsPerOwner + 1)
            .Include(s => s.Moves)
            .ToListAsync(ct);
        if (stale.Count == 0) return;   // alles läuft noch → nichts wegräumen

        _db.GuessMoves.RemoveRange(stale.SelectMany(s => s.Moves));
        _db.GuessSessions.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Wurde dieser Halbzug uebersprungen, statt abgefragt zu werden — und warum? Betrifft nur die
    /// GERATENE Seite ab dem Startzug; alles davor und die Gegenseite waren nie eine Aufgabe.
    ///
    /// <para><see cref="AdvanceToPlayableAsync"/> ueberspringt eine Stellung, in der der Partiezug
    /// nicht unter den Kandidaten steht: ohne ihn gibt es keinen Bezugspunkt, und eine 0 dafuer
    /// waere unfair. Das ist richtig, war aber unsichtbar — das Brett spielte wortlos weiter.</para>
    /// </summary>
    private static string? SkipReason(GuessSession session, int ply, bool analyzed, HashSet<int> answered)
    {
        if ((ply % 2 == 0) != session.GuessWhite) return null;   // Zug der Gegenseite
        if (ply < session.StartPly || answered.Contains(ply)) return null;
        return analyzed ? "notScorable" : "pending";
    }

    /// <summary>Ab dieser Bauerndifferenz gilt eine Stellung als entschieden — darunter sagt sie
    /// nichts darüber, wer die Partie gewonnen hat.</summary>
    private const double DecisivePawns = 1.5;

    /// <summary>
    /// Welche Seite übernimmt der Nutzer, wenn er es NICHT sagt? Die des GEWINNERS. Im kuratierten
    /// Bestand ist genau das der Sinn der Übung — man rät die Züge des Spielers, der die Partie
    /// gewonnen hat, und deshalb fragt die Auswahl dort nicht mehr nach der Seite.
    ///
    /// <para>Vorrang hat das ERGEBNIS der Partie. Fehlt es, entscheidet die BEWERTUNG der letzten
    /// gerechneten Stellung: eine aufgegebene Partie steht dort klar auf einer Seite. Das ist keine
    /// Notlösung für Sonderfälle, sondern der Normalfall bei unseren Meisterpartien — die kommen aus
    /// einem Buch, das in die Kopfzeile nur <c>*</c> schreibt (Capablancas <i>Chess Fundamentals</i>
    /// nennt die Ergebnisse nur im Fließtext, und der ist nicht auswertbar).</para>
    ///
    /// <para>Sagt auch die Bewertung nichts Deutliches (Remis, oder erst die Eröffnung gerechnet),
    /// bleibt es bei Weiß — eine Seite muss es sein, und Raten hilft hier niemandem.</para>
    /// </summary>
    private async Task<bool> WinnerSideAsync(GameAnalysis analysis, CancellationToken ct)
    {
        switch (analysis.Result?.Trim())
        {
            case "1-0": return true;
            case "0-1": return false;
        }

        var last = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.GameAnalysisId == analysis.Id && p.CandidatesJson != null)
            .OrderByDescending(p => p.Ply)
            .Select(p => new { p.Ply, p.CandidatesJson })
            .FirstOrDefaultAsync(ct);
        if (last is null) return true;

        var candidates = BrokerCandidates.FromJson(last.CandidatesJson);
        if (candidates.Count == 0) return true;

        // Die Kandidaten-Bewertung gilt aus Sicht der Seite AM ZUG (BrokerCandidates dreht sie beim
        // Einlesen entsprechend) — und am Zug ist bei geradem Halbzug Weiß.
        var pawns = candidates[0].Eval.Pawns;
        if (Math.Abs(pawns) < DecisivePawns) return true;
        var whiteToMove = last.Ply % 2 == 0;
        return pawns > 0 ? whiteToMove : !whiteToMove;
    }

    /// <summary>
    /// Die Zug-Kommentare der Partie, je Halbzug (Schluessel wie <c>GameAnalysisPosition.Ply</c> —
    /// <see cref="PgnParser.ExtractMoveComments"/> zaehlt genauso). Bei einer Meisterpartie sind das
    /// die eigentliche Lehre; ohne sie ist eine annotierte Partie hier stumm.
    ///
    /// <para><b>Gelesen statt gespeichert.</b> Das Quell-PGN steht ohnehin an der Analyse, und
    /// eine eigene Spalte je Stellung braeuchte eine Migration UND einen Nachtrag fuer den
    /// Bestand — fuer Text, der sich nie aendert. Der Aufwand hier ist ein Feld einer Zeile (die
    /// Partien liegen im einstelligen KB-Bereich) und faellt nur an, wo der Verlauf gebaut wird
    /// (also nicht in der Uebersicht).</para>
    ///
    /// <para>Die eiserne Regel bleibt gewahrt, weil der VERLAUF nur gespielte Zuege enthaelt: der
    /// Kommentar zum noch zu ratenden Zug wird nie nachgeschlagen.</para>
    /// </summary>
    private async Task<Dictionary<int, string>> CommentsAsync(int analysisId, CancellationToken ct)
    {
        var pgn = await _db.GameAnalyses.AsNoTracking()
            .Where(g => g.Id == analysisId)
            .Select(g => g.Pgn)
            .FirstOrDefaultAsync(ct);
        // Ohne geschweifte Klammer gibt es nichts zu holen — dann auch nicht parsen.
        if (string.IsNullOrEmpty(pgn) || !pgn.Contains('{')) return new Dictionary<int, string>();

        var game = PgnParser.SplitGames(pgn).FirstOrDefault();
        if (game.MoveText is null) return new Dictionary<int, string>();
        return PgnParser.ExtractMoveComments(game.MoveText) ?? new Dictionary<int, string>();
    }

    private Task<GuessSession?> LoadAsync(GuessOwner owner, int sessionId, CancellationToken ct) =>
        OwnedBy(owner).Include(s => s.Moves).FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    /// <summary>Die Durchläufe EINES Besitzers. Die Fallunterscheidung steht bewusst in C# und
    /// nicht als <c>?:</c> in der Abfrage — so wird daraus ein einfaches Gleich statt eines CASE,
    /// und die Abfrage trifft ihren Index.</summary>
    private IQueryable<GuessSession> OwnedBy(GuessOwner owner) =>
        owner.UserId is { } uid
            ? _db.GuessSessions.Where(s => s.UserId == uid)
            : _db.GuessSessions.Where(s => s.AnonymousSessionId == owner.AnonymousSessionId);

    /// <summary>
    /// Rückt <c>CurrentPly</c> auf den nächsten Halbzug vor, der sich WERTEN lässt: eine Stellung
    /// ohne Kandidatenliste (noch nicht gerechnet, oder von der Engine aufgegeben) oder eine, in der
    /// der Partiezug nicht in der Liste steht, wird übersprungen statt mit 0 abgestraft — ohne
    /// Bezugspunkt wäre Raten unfair. Ist keine mehr da, ist die Sitzung durch.
    /// </summary>
    private async Task AdvanceToPlayableAsync(GuessSession session, CancellationToken ct)
    {
        // Nur die Halbzuege der GERATENEN Seite und nur die drei Spalten, die hier gelesen werden —
        // `Fen`/`EvalText` blieben sonst bei jedem Rateversuch fuer den ganzen Partierest mit dabei.
        var positions = await _db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.GameAnalysisId == session.GameAnalysisId && p.Ply >= session.CurrentPly
                        && (p.Ply % 2 == 0) == session.GuessWhite)
            .OrderBy(p => p.Ply)
            .Select(p => new { p.Ply, p.CandidatesJson, p.GameMoveUci })
            .ToListAsync(ct);

        foreach (var p in positions)
        {
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

    /// <summary>Was die Sitzungs-Anzeige von der Partie braucht — bewusst OHNE <c>Pgn</c> (LONGTEXT).</summary>
    private readonly record struct AnalysisHead(string? Title, string? White, string? Black);

    /// <param name="head">Vorab geladene Kopfdaten (Listen-Pfad); <c>null</c> = selbst nachschlagen.</param>
    /// <param name="totalGuesses">Vorab gezaehlte Halbzuege (Listen-Pfad); <c>null</c> = selbst zaehlen.</param>
    private async Task<GuessSessionDto> BuildDtoAsync(GuessSession session, CancellationToken ct,
        bool withPosition = true, AnalysisHead? head = null, int? totalGuesses = null)
    {
        var analysis = head ?? await _db.GameAnalyses.AsNoTracking()
            .Where(g => g.Id == session.GameAnalysisId)
            .Select(g => new AnalysisHead(g.Title, g.White, g.Black))
            .FirstOrDefaultAsync(ct);

        var moves = session.Moves ?? new List<GuessMove>();
        var points = moves.Where(m => m.Grade is not null).Sum(m => GuessGrades.PointsFor(m.Grade!.Value));
        var hits = moves.Count(m => m.Grade is GuessGrade.GameMove or GuessGrade.OnlyMove);

        var dto = new GuessSessionDto
        {
            Id = session.Id,
            GameAnalysisId = session.GameAnalysisId,
            Title = analysis.Title,
            White = analysis.White,
            Black = analysis.Black,
            GuessWhite = session.GuessWhite,
            StartPly = session.StartPly,
            Status = session.Status == GuessSessionStatus.Done ? "done" : "running",
            Points = points,
            MaxPoints = moves.Count(m => m.Grade is not null) * GuessGrades.MaxPointsPerMove,
            MovesPlayed = moves.Count,
            GameMoveHits = hits,
            SecondsSpent = session.SecondsSpent,
            TotalGuesses = totalGuesses ?? await CountGuessablePliesAsync(session, ct),
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

            // Die Partie bis zur aktuellen Aufgabe zum Durchblättern. Je Zug wird die Stellung DANACH
            // geliefert — das ist die `Fen` des FOLGENDEN Halbzugs (eine Stellungszeile hält immer
            // die Stellung VOR ihrem Zug). Für den letzten Eintrag ist das die AUFGABENSTELLUNG; die
            // Liste hört damit genau vor der Lösung auf und kann sie nicht verraten.
            if (session.CurrentPly > 0)
            {
                var played = await _db.GameAnalysisPositions.AsNoTracking()
                    .Where(p => p.GameAnalysisId == session.GameAnalysisId && p.Ply <= session.CurrentPly)
                    .OrderBy(p => p.Ply)
                    // `Analyzed` statt der Kandidatenliste selbst: die ist LONGTEXT und wuerde hier
                    // fuer JEDEN gespielten Halbzug mitgelesen, nur um ein Ja/Nein zu bekommen.
                    .Select(p => new { p.Ply, p.Fen, p.GameMoveSan, p.GameMoveUci, Analyzed = p.CandidatesJson != null })
                    .ToListAsync(ct);
                var answered = moves.Select(m => m.Ply).ToHashSet();
                if (played.Count > 0)
                {
                    var comments = await CommentsAsync(session.GameAnalysisId, ct);
                    dto.StartFen = played[0].Fen;
                    for (var i = 0; i + 1 < played.Count && played[i].Ply < session.CurrentPly; i++)
                        dto.History.Add(new GuessHistoryMoveDto
                        {
                            Ply = played[i].Ply,
                            MoveNumber = played[i].Ply / 2 + 1,
                            White = played[i].Ply % 2 == 0,
                            San = played[i].GameMoveSan,
                            Uci = played[i].GameMoveUci,
                            Fen = played[i + 1].Fen,
                            Comment = comments.GetValueOrDefault(played[i].Ply),
                            Skipped = SkipReason(session, played[i].Ply, played[i].Analyzed, answered),
                        });
                }
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
        // Sicht drehen (dort ist der Gegner am Zug) und ueber den gemeinsamen Formatierer ausgeben —
        // die vorige Fassung rechnete mit `Pawns` und machte aus „Matt in 3" ein „+997.00".
        return candidates[0].Eval.Negated.Text;
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
