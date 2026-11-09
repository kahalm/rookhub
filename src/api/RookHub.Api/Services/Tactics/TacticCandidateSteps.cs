using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Tactics;

/// <summary>
/// Die reinen Schritte eines <see cref="TacticCandidate"/> der Taktik-Ernte (aus <see cref="TacticHarvestService"/>
/// herausgezogen, 2026-11-09): Kandidat anlegen, Lösung nach einem Engine-Ergebnis verlängern oder beenden, Zweitprüfung
/// (lc0) Stufe für Stufe. Ohne Datenbank — der Dienst lädt, ruft diese Schritte und speichert.
/// </summary>
internal static class TacticCandidateSteps
{
    /// <summary>Kandidat anlegen: erster Zug (normiert), gefunden ja/nein, nächster Schritt aus der Hauptvariante.</summary>
    internal static TacticCandidate? Start(GameAnalysis a, string prevFen, string blunderUci, int ply, string fen, string gameUci,
        TacticHarvest.Found found, DateTime now, string? variant = null)
    {
        var first = TacticHarvest.Play(fen, found.Best.Uci);
        if (first is null || TacticHarvest.Play(prevFen, blunderUci) is null) return null;
        var bestUci = GamePlies.ToUci(first.Value.Move);
        var played = TacticHarvest.Play(fen, gameUci);
        var c = new TacticCandidate
        {
            GameAnalysisId = a.Id, Ply = ply, Origin = a.Origin, PrevFen = prevFen, BlunderUci = blunderUci, Fen = fen,
            SolverWhite = FenFields.WhiteToMove(fen), GameMoveUci = gameUci,
            Found = played is not null && played.Value.Fen == first.Value.Fen, Kind = found.Kind, Moves = bestUci,
            EvalText = TacticHarvest.EvalText(found.Best), Status = TacticCandidateStatus.Verifying, CreatedAt = now, UpdatedAt = now,
            Variant = variant,
        };
        // Lc0-Fund: Stockfish sieht den Vorteil oft nicht (das ist der Punkt) — die Lösung bleibt der eine erste Zug, die Zweitprüfung
        // (wieder Lc0) bestätigt ihn
        if (variant == "lc0") Finish(c);
        else Continue(c, first.Value.Fen, first.Value.Over, found.Best.Pv);
        return c;
    }

    /// <summary>Nach einem bestätigten Zug des Lösers: weiter mit der Antwort aus der Hauptvariante, oder fertig.</summary>
    private static void Continue(TacticCandidate c, string fenAfterSolver, bool over, IReadOnlyList<string> pv)
    {
        var solverMoves = (c.Moves.Split(' ').Length + 1) / 2;
        var reply = pv.Count > 1 ? pv[1] : null;
        var next = !over && solverMoves < TacticHarvest.MaxSolverMoves && reply is not null ? TacticHarvest.Play(fenAfterSolver, reply) : null;
        if (next is null || next.Value.Over)
        {
            Finish(c);
            return;
        }
        c.PendingReplyUci = GamePlies.ToUci(next.Value.Move);
        c.NextFen = next.Value.Fen;
        c.AnalysisJobId = null;
        c.Attempts = 0;
    }

    internal static void Finish(TacticCandidate c)
    {
        c.Status = TacticCandidateStatus.Done;
        c.PendingReplyUci = null;
        c.NextFen = null;
        c.AnalysisJobId = null;
        var solution = c.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        c.Themes = string.Join(',', TacticHarvest.Themes(c.Fen, solution, c.Kind));
        c.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Ergebnis der Stellung <see cref="TacticCandidate.NextFen"/> (Löser am Zug): eindeutig → weiter, sonst fertig.</summary>
    internal static void Step(TacticCandidate c, IReadOnlyList<TacticHarvest.Cand> cands)
    {
        var mate = c.Kind == "mate";
        if (cands.Count == 0 || c.NextFen is null || c.PendingReplyUci is null || !TacticHarvest.IsUnique(cands, mate, TacticHarvest.RulesFor(c.Variant))
            || (mate && cands[0].Mate is not > 0)
            || (!mate && cands[0].Mate is null && (cands[0].Cp ?? 0) < TacticHarvest.RulesFor(c.Variant).MinAdvantageCp))
        {
            Finish(c);
            return;
        }
        var played = TacticHarvest.Play(c.NextFen, cands[0].Uci);
        if (played is null)
        {
            Finish(c);
            return;
        }
        c.Moves = $"{c.Moves} {c.PendingReplyUci} {GamePlies.ToUci(played.Value.Move)}";
        c.UpdatedAt = DateTime.UtcNow;
        Continue(c, played.Value.Fen, played.Value.Over, cands[0].Pv);
    }

    /// <summary>Stellung der Zweitprüfung in Stufe <paramref name="stage"/>: 0 = Aufgabenstellung, 1 = vor dem Fehler, ab 2 die
    /// Stellung vor dem späteren Löserzug (nach den Zügen davor samt Antworten). <c>null</c> = nicht spielbar.</summary>
    internal static string? SecondFen(TacticCandidate c, int stage)
    {
        if (stage == 0) return c.Fen;
        if (stage == 1) return c.PrevFen;
        var fen = c.Fen;
        var tokens = c.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < 2 * (stage - 1); i++)
        {
            if (i >= tokens.Length || TacticHarvest.Play(fen, tokens[i]) is not { } p) return null;
            fen = p.Fen;
        }
        return fen;
    }

    internal static void Dispute(TacticCandidate c, string reason)
    {
        c.Status = TacticCandidateStatus.Disputed;
        c.SecondAgrees = false;
        c.SecondJobId = null;
        c.RejectReason = reason;
        c.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Ergebnis der Zweitprüfung für <see cref="TacticCandidate.SecondStage"/>. Stufe 0: bester Zug und Eindeutigkeit wie die
    /// Erstprüfung. Stufe 1: ist der Zug des Gegners auch für die Zweitprüfung ein Fehler (sonst Kompensation, keine Taktik)?
    /// Ab Stufe 2: die späteren Löserzüge — stimmt einer nicht, endet die Lösung davor (bei Matt-Aufgaben stattdessen
    /// <c>Disputed</c>, sie wären abgeschnitten keine Matt-Aufgabe mehr). Alle Stufen durch → <see cref="TacticCandidate.SecondAgrees"/>.
    /// </summary>
    internal static void SecondStep(TacticCandidate c, IReadOnlyList<TacticHarvest.Cand> cands)
    {
        var tokens = c.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var solverMoves = (tokens.Length + 1) / 2;
        var mate = c.Kind == "mate";
        c.SecondJobId = null;
        c.SecondAttempts = 0;
        c.UpdatedAt = DateTime.UtcNow;
        if (c.SecondStage == 0)
        {
            if (!TacticHarvest.Agree(c.Fen, cands, tokens[0], mate, TacticHarvest.RulesFor(c.Variant))) { Dispute(c, "lc0Move"); return; }
            c.SecondBest = cands[0].Uci;
            c.SecondEval = TacticHarvest.EvalText(cands[0]);
            c.SecondHereJson = TacticHarvest.Dump(cands);
        }
        else if (c.SecondStage == 1)
        {
            if (TacticHarvest.Detect(cands, TacticHarvest.LoadDump(c.SecondHereJson), TacticHarvest.RulesFor(c.Variant)) is null) { Dispute(c, "lc0NoBlunder"); return; }
        }
        else
        {
            var j = c.SecondStage - 1;   // Index des Löserzugs (0-basiert), Halbzug-Index 2*j
            var fen = SecondFen(c, c.SecondStage);
            if (fen is null) { Dispute(c, "lc0Fen"); return; }
            if (!TacticHarvest.Agree(fen, cands, tokens[2 * j], mate, TacticHarvest.RulesFor(c.Variant)))
            {
                if (mate) { Dispute(c, "lc0Line"); return; }
                c.Moves = string.Join(' ', tokens.Take(2 * j - 1));
                c.Themes = string.Join(',', TacticHarvest.Themes(c.Fen, c.Moves.Split(' '), c.Kind));
                c.SecondStage = solverMoves + 1;
                c.SecondAgrees = true;
                return;
            }
        }
        c.SecondStage++;
        if (c.SecondStage > solverMoves) c.SecondAgrees = true;
    }
}
