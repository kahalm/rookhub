using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Eine Zeile der Partie-Analyse → Bewertung aus WEISS-Sicht, fuer Kurve, Genauigkeit und Zug-Klassen
/// unter der gespeicherten Partie. Reine Funktion, wie <see cref="GuessScoring"/>: Zeile rein, DTO
/// raus, ohne Datenbank testbar.
///
/// <para><b>Warum hier gedreht wird.</b> Die Kandidatenlisten stehen aus Sicht der Seite AM ZUG
/// (<see cref="BrokerCandidates"/> dreht die Broker-Zeile beim Einlesen) — so braucht sie die
/// Punktepartie. Die Kurve dagegen ist EINE Linie ueber die ganze Partie; mit dem Vorzeichen der Seite
/// am Zug sprang sie nach jedem Halbzug ueber die Mittellinie. Zurueckgedreht wird mit DERSELBEN Regel
/// wie beim Einlesen (zweites FEN-Feld <c>"w"</c> oder nicht) — mit einer anderen Regel stuende eine
/// Stellung mit ungewoehnlicher FEN am Ende doppelt oder gar nicht gedreht da.</para>
/// </summary>
public static class GameEvals
{
    /// <summary>
    /// Die Stellung VOR dem Halbzug <paramref name="ply"/>, in Weiß-Sicht. <c>null</c>, wenn die Zeile
    /// keine Kandidaten hat (noch nicht gerechnet, aufgegeben <c>[]</c> oder unlesbar) — die Kurve
    /// laesst dort eine Luecke, statt eine 0,00 als „ausgeglichen" hinzumalen.
    /// </summary>
    public static GameEvalPlyDto? PlyOf(int ply, string fen, string gameMoveUci, string? candidatesJson, int depth)
    {
        var candidates = BrokerCandidates.FromJson(candidatesJson);
        if (candidates.Count == 0) return null;

        var flip = !WhiteToMove(fen);
        GuessScoring.Eval White(GuessScoring.Eval e) => flip ? e.Negated : e;

        var best = White(candidates[0].Eval);
        var dto = new GameEvalPlyDto
        {
            Ply = ply,
            Cp = best.Cp,
            Mate = best.MateIn,
            Depth = depth,
            BestUci = candidates[0].Uci,
            PlayedUci = gameMoveUci,
        };

        // Der gespielte Zug zaehlt nur, wenn die Engine ihn SELBST unter ihren Kandidaten fuehrt —
        // eine Schaetzung („schlechter als der fuenfte") waere eine Zahl, die niemand gerechnet hat.
        // Der Client nimmt dann die Bewertung der naechsten Stellung.
        var playedIndex = candidates.FindIndex(c => string.Equals(c.Uci, gameMoveUci, StringComparison.OrdinalIgnoreCase));
        if (playedIndex >= 0)
        {
            var played = White(candidates[playedIndex].Eval);
            dto.PlayedCp = played.Cp;
            dto.PlayedMate = played.MateIn;
        }
        if (candidates.Count > 1)
        {
            var second = White(candidates[1].Eval);
            dto.SecondCp = second.Cp;
            dto.SecondMate = second.MateIn;
        }
        var pvs = BrokerCandidates.PvsFromJson(candidatesJson);
        foreach (var c in candidates)
        {
            var e = White(c.Eval);
            dto.Candidates.Add(new GameEvalCandidateDto
            {
                Uci = c.Uci, Cp = e.Cp, Mate = e.MateIn, Pv = pvs.TryGetValue(c.Uci, out var pv) ? pv : null,
            });
        }
        return dto;
    }

    /// <summary>
    /// Bewertung NACH dem letzten Zug. Fuer die Endstellung gibt es keine Zeile (die Analyse rechnet
    /// Stellungen, in denen noch gezogen wird) — ihre Bewertung ist die des gespielten Kandidaten der
    /// LETZTEN Zeile. <c>null</c>, wenn <paramref name="last"/> nicht die letzte Zeile ist (sie ist
    /// noch nicht gerechnet) oder der Partiezug nicht unter den Kandidaten steht.
    /// </summary>
    public static GameEvalScoreDto? FinalOf(GameEvalPlyDto? last, int plyCount)
    {
        if (last is null || last.Ply != plyCount - 1) return null;
        if (last.PlayedCp is null && last.PlayedMate is null) return null;
        return new GameEvalScoreDto { Cp = last.PlayedCp, Mate = last.PlayedMate };
    }

    /// <summary>So viele JUENGSTE Ergebnisse gehen ins Tempo ein. Bei Tiefe 30 kommt etwa alle
    /// zwanzig Sekunden eine Stellung, zwoelf sind also rund vier Minuten — lang genug, dass die
    /// Pumpe (sie holt die Ergebnisse im 20-s-Takt und oft mehrere mit demselben Zeitstempel) nicht
    /// den Ausschlag gibt, und kurz genug, dass eine zweite Partie auf derselben Engine das Tempo
    /// sofort bremst.</summary>
    public const int EtaRecentPositions = 12;

    /// <summary>
    /// Hochgerechnete Restdauer DIESER Partie in Minuten, aus den Zeitstempeln ihrer eigenen
    /// gerechneten Stellungen (<c>GameAnalysisPosition.AnalyzedAt</c>) — Tempo nach <see cref="AnalysisPace"/>:
    /// die juengsten <see cref="EtaRecentPositions"/> Ergebnisse, nur der zusammenhaengende Lauf (eine Pause davor
    /// zaehlt nicht), gemessen bis JETZT, solange der Lauf lebt (haengt die Engine, waechst die Restdauer).
    /// Die Wartezeit VOR dem ersten Ergebnis (Warteschlange, andere Partien des Nutzers) zaehlt nicht mit.
    ///
    /// <para><c>null</c>, wenn nichts mehr offen ist oder es noch kein Tempo gibt (weniger als zwei
    /// Ergebnisse). Nie unter einer Minute: „0 min" bei zwei offenen Stellungen liest sich wie fertig.</para>
    /// </summary>
    public static int? EtaMinutes(IEnumerable<DateTime> analyzedAt, int remaining, DateTime now)
    {
        if (remaining <= 0) return null;
        var run = AnalysisPace.Measure(analyzedAt, now, EtaRecentPositions);
        if (run is not { Count: >= 2 } r) return null;
        return (int)Math.Max(1, Math.Round(remaining * r.SecondsPerPosition / 60));
    }

    /// <summary>Dieselbe Regel wie <see cref="BrokerCandidates.Parse"/>: nur ein <c>"w"</c> im zweiten
    /// FEN-Feld heisst Weiß am Zug.</summary>
    private static bool WhiteToMove(string fen)
        => fen.Split(' ') is { Length: >= 2 } parts && parts[1] == "w";
}
