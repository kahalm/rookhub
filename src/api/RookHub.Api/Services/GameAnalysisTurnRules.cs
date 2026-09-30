using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Die Reihenfolge-Regeln der Partie-Pumpe (<see cref="GameAnalysisService.PumpOneAsync"/>) als reine Funktionen:
/// wann eine Partie nachgefuettert werden darf, wie viele Vertiefungs-Auftraege offen stehen duerfen und welche
/// Stellungen die Vertiefung zuerst rechnet. Die Abfragen (offene Stellungen, Engine-Zahl) macht der Dienst; hier
/// steht nur, was er mit den Zahlen tut — so ist jede Regel ohne Datenbank und ohne Pumpe pruefbar.
/// </summary>
internal static class GameAnalysisTurnRules
{
    /// <summary>
    /// Der SCHWANZ (0.543.0): darf eine Partie schon Auftraege bekommen, obwohl vor ihr noch
    /// <paramref name="openPliesAhead"/> Stellungen offen sind? Ja, wenn davor nichts mehr offen ist — oder
    /// weniger, als Engines da sind: sonst stuenden Engines still. Die aelteren Auftraege bleiben vorn in der
    /// Schlange (FIFO im Worker), die aeltere Partie wird also weiterhin ZUERST fertig; nur der Leerlauf am Ende
    /// jeder Partie faellt weg. Gemessen am 2026-09-26 auf Prod mit 16 Engines: jede Partie endete mit 20–30 s,
    /// in denen 15 Engines nichts taten — bei Partien von drei Minuten ein Fuenftel der Zeit.
    ///
    /// <para>Dieselbe Schwelle gilt fuer die Vertiefung: sie wartet, solange der erste Durchgang des Nutzers
    /// mindestens so viele offene Stellungen hat, wie Engines da sind.</para>
    /// </summary>
    internal static bool TailMayAdvance(int openPliesAhead, int engineSlots) =>
        openPliesAhead == 0 || openPliesAhead < engineSlots;

    /// <summary>
    /// Wie viele Vertiefungs-Auftraege je Partie gleichzeitig offen stehen duerfen: so viele, wie der
    /// Engine-Besitzer Hintergrund-Engines hat — mindestens <see cref="GameAnalysisDefaults.MaxOpenRefineJobsPerGame"/>,
    /// hoechstens <see cref="GameAnalysisDefaults.MaxOpenJobsPerGame"/>.
    ///
    /// <para>Vorher galt fest 8. Vertieft wird immer nur EINE Partie zur Zeit (<c>GameAnalysisService.IsOwnersRefineTurnAsync</c>),
    /// also hatte der ganze Engine-Park genau 8 Auftraege: am 27.09. auf Prod rechneten Hintergrund 1–8, waehrend
    /// Hintergrund 9–12 und die vier Server-Engines online in der Liste standen und nichts taten. Die Sorge hinter
    /// der kleinen Zahl — den Deckel je Nutzer (<see cref="AnalysisJobService.MaxOpenJobsPerUser"/>, 150) fuer den
    /// ersten Durchgang einer neuen Partie frei zu halten — traegt bei 16 oder 32 genauso: der erste Durchgang hat
    /// ohnehin Vorrang (Vertiefung = Hintergrundarbeit, und sie wartet, solange er mehr offene Stellungen hat als
    /// Engines da sind).</para>
    /// </summary>
    internal static int RefineJobCap(int engineSlots) =>
        Math.Clamp(engineSlots, GameAnalysisDefaults.MaxOpenRefineJobsPerGame, GameAnalysisDefaults.MaxOpenJobsPerGame);

    /// <summary>
    /// Stellungen, die die Vertiefung ZUERST rechnet: die vor und die nach jedem Zug, den die bisherige Bewertung als
    /// Fehler „?" oder Patzer „??" einstuft (<see cref="GameAccuracy.MoveLoss.IsMistakeOrWorse"/>). Beide, weil die
    /// Klasse an beiden haengt — Gewinnchance vor dem Zug aus dieser Stellung, danach aus der naechsten. Gewuenscht
    /// 27.09.: genau diese Stellen sind die, die man sich ansieht, und ihr Urteil soll zuerst auf Tiefe 30 stehen.
    /// </summary>
    internal static HashSet<int> SuspectPlies(IEnumerable<GameAnalysisPosition> positions, int plyCount)
    {
        var plies = new HashSet<int>();
        foreach (var move in GameAccuracy.MoveLossesFromPositions(positions, plyCount))
        {
            if (!move.IsMistakeOrWorse) continue;
            plies.Add(move.Ply);
            plies.Add(move.Ply + 1);
        }
        return plies;
    }
}
