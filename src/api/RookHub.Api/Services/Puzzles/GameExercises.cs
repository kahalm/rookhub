using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Puzzles;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Tactics;

namespace RookHub.Api.Services.Puzzles;

/// <summary>
/// „Empfohlene Übungen“ am Ende der Partie-Zusammenfassung (0.960.0, Wunsch 2026-11-13). Aus den Daten der Partie, nicht von
/// Claude: jedes gespielte oder verpasste Matt (beider Seiten, auch wenn der Gegner es ebenfalls übersah) ist eine Stelle; je
/// Stelle bis zu <see cref="PerGroup"/> Lichess-Aufgaben mit demselben Mattbild (<see cref="MatePattern"/>) — erst gleiche strenge
/// Signatur, dann gleiche Familie, und nur wenn es davon keine gibt, Aufgaben desselben Themas (<c>mateInN</c>). Seit 0.961.0 ebenso
/// für taktische Fehler (<see cref="TacticMotifs"/>: Gabel, Fesselung, Spieß, Abzug …; streng = Thema + Figuren). Keine Stelle →
/// keine Übungen; eine Stelle ohne Bezug zur Partie wird nie erfunden („erzwinge keine motive/aufgaben“).
///
/// <para><b>Mehr Motive</b> (0.976.0, Befund an Vereinspartie 251: nur EINE Gruppe, erwartet „5 Gabeln, 5x Matt, 5 mal
/// Grundlinie“): (1) <b>Mattdrohung</b> (<see cref="MateThreat"/>) — der beste Zug an einer Fehlerstelle droht Matt in einem Zug
/// oder eine naheliegende Antwort läuft hinein (<see cref="MateThreats"/>), oder eine gerechnete Zusatzlinie des Fehler-Vergleichs
/// endet matt (<see cref="SideLinesOf"/>); (2) <b>Matt am Schluss</b> (<see cref="ForcedMate"/>) — die Partie endet ohne Matt, aber
/// mit erzwungenem Matt auf dem Brett (aufgegeben); (3) Taktikstellen über <see cref="TacticMotifs.DetectInGame"/> (Grundreihe,
/// hängende Figur, Desperado-Gabel, Motiv nach forcierender Einleitung) und auch dann, wenn die beste Linie nicht +2,0 erreicht,
/// aber mindestens so viel besser ist als jeder andere Zug (<see cref="MinTacticGap"/> — die Taktik, die eine Partie rettet);
/// (4) je Stelle werden die <see cref="PerGroup"/> LÖSBAREN Aufgaben aus dem Thema aufgefüllt (benanntes Mattbild
/// <see cref="MatePattern.ThemeOf"/>, <c>mateInN</c>, Motiv-Thema), wenn Signatur und Familie nicht reichen.</para>
///
/// <para>Spielstärke: die der Seite, die das Motiv hatte (<see cref="EbookSuggestions.StrengthAsync"/>, wie die Literatur; ein Band
/// zählt mit seiner Mitte), sonst die der anderen Seite, sonst <see cref="DefaultRating"/>. Aufgaben im Fenster ±<see cref="Window"/>,
/// beliebt (<see cref="PuzzleSignatureWorker.MinPopularity"/>), oft gespielt (<see cref="MinPlays"/>), Tipps nicht als irreführend
/// markiert. Die Auswahl ist je Partie und Stelle fest gesät, also bei jedem Aufruf dieselbe (solange sich der Bestand nicht ändert).</para>
///
/// <para><b>Bücher zuerst</b> (0.962.0): mit einem Betrachter (<see cref="EbookViewer"/>) kommen vor den Lichess-Aufgaben Diagramme aus
/// seinen Büchern mit derselben Signatur (<see cref="BookExercises"/>); zusammen höchstens <see cref="PerGroup"/>. Ohne Betrachter
/// (<c>null</c>) nur Lichess.</para>
/// </summary>
public sealed class GameExercises(AppDbContext db, EbookSuggestions strength)
{
    private readonly BookExercises _books = new(db);

    public const int PerGroup = 6;
    public const int MaxGroups = 8;
    /// <summary>Matts bis zu so vielen Zügen zählen als Motiv (Lichess-Themen gehen bis mateIn5).</summary>
    public const int MaxMateIn = 5;
    public const int Window = 250;
    public const int MinPlays = 200;
    public const int DefaultRating = 1500;
    private const int Pool = 40;
    private const int Closest = 12;

    public sealed record Position(int Ply, string Fen, string GameMoveUci, string? CandidatesJson);

    /// <summary>Eine Motivstelle der Partie. Matt: <see cref="Signature"/> (fehlt, wenn die Engine-Linie nicht bis zum Matt reicht) und
    /// <see cref="MateTheme"/> (benanntes Mattbild, sonst <c>null</c>); Taktik (0.961.0): <see cref="Motif"/>. <see cref="Weight"/> ordnet
    /// die Stellen (verpasstes Matt vorn, Taktik und Mattdrohung nach Verlust, gespieltes Matt zuletzt).</summary>
    public sealed record Occurrence(int Ply, bool White, string Reason, int MateIn, MatePattern.Signature? Signature,
        TacticMotifs.Motif? Motif = null, double Weight = 0, string? MateTheme = null)
    {
        /// <summary>Thema der Stelle als Lichess-Name: das Motiv (<c>fork</c> …) bzw. das benannte Mattbild (<c>backRankMate</c>,
        /// <c>smotheredMate</c>); <c>null</c> bei einem Matt ohne Namen (dann nur <c>mateInN</c>).</summary>
        public string? Theme => Motif?.Theme ?? MateTheme;
    }

    /// <summary>Eine Nebenlinie, die matt endet (0.976.0): an der Stelle <paramref name="Ply"/> hat die Seite <paramref name="White"/>
    /// ein Matt in <paramref name="MateIn"/> ab <paramref name="Fen"/> (sie am Zug) über <paramref name="Pv"/>.</summary>
    public sealed record SideLine(int Ply, bool White, string Fen, IReadOnlyList<string> Pv, int MateIn);

    /// <summary>Zeile eines Fehler-Vergleichs, wie <see cref="SideLinesOf"/> sie liest (<see cref="MoveComparisonLine"/> + Stelle des Vergleichs).</summary>
    /// <param name="BestUci">Der beste Zug des Vergleichs.</param>
    /// <param name="ReplyUci">Die Antwort der Zeile (<see cref="MoveComparisonLine.ReplyUci"/>).</param>
    public sealed record ComparisonRow(int? Ply, string ComparisonFen, MoveComparisonLineKind Kind, string Fen, string? ResultJson,
        string? BestUci = null, string? ReplyUci = null);

    public const string MissedMate = "missedMate";
    public const string PlayedMate = "playedMate";
    /// <summary>Die Partie endet ohne Matt, aber mit erzwungenem Matt in höchstens <see cref="MaxMateIn"/> auf dem Brett (0.976.0).</summary>
    public const string ForcedMate = "forcedMate";
    /// <summary>Matt nur als Drohung (0.976.0): hinter dem besten Zug einer Fehlerstelle (<see cref="MateThreats"/>) oder in einer
    /// gerechneten Zusatzlinie des Fehler-Vergleichs (<see cref="SideLinesOf"/>) — Mattbild wie beim verpassten Matt.</summary>
    public const string MateThreat = "mateThreat";
    /// <summary>Die Seite am Zug hatte eine Taktik (beste Linie gewinnt, Motiv erkannt) und spielte einen Fehler.</summary>
    public const string MissedTactic = "missedTactic";
    /// <summary>Nach einem Fehler hatte der GEGNER eine Taktik (Motiv in seiner besten Linie) — Stelle auf seiner Seite.</summary>
    public const string AllowedTactic = "allowedTactic";
    /// <summary>Ab diesem Vorteil (Bauerneinheiten × 100, Sicht der Seite am Zug) gilt die beste Linie als gewinnende Taktik.</summary>
    public const int MinTacticCp = 200;
    /// <summary>… oder (0.976.0) wenn sie mindestens so viel besser ist als der ZWEITBESTE Zug — die Taktik, die Material zurückholt
    /// oder die Partie rettet, ohne dass die Stellung danach +2,0 erreicht (Vereinspartie 251: 45.Ta8+ Lf8 46.Lxc3+ mit −0,1 statt −7,4).</summary>
    public const int MinTacticGap = 200;

    /// <summary>
    /// Die Matt-Stellen der Partie (rein, öffentlich für Tests): <b>verpasst</b> = die Seite am Zug hatte laut Analyse Matt in höchstens
    /// <see cref="MaxMateIn"/>, nach ihrem Zug steht der Gegner aber nicht mehr matt (bzw. die Partie endet ohne Matt) — Bild aus der
    /// Engine-Linie; <b>gespielt</b> = die Partie endet mit Matt — Bild aus der Schlussstellung, Stelle = Beginn der Mattfolge laut
    /// Analyse; <b>am Schluss erzwungen</b> (0.976.0) = die Partie endet ohne Matt, die Endstellung steht aber auf Matt in höchstens
    /// <see cref="MaxMateIn"/> — Bild aus der Engine-Linie der Endstellung. Stellungen ohne Bewertung entscheiden nichts. Dazu die
    /// taktischen Stellen und Mattdrohungen (<see cref="TacticOccurrences"/>); <paramref name="sideLines"/> = Nebenlinien mit Matt
    /// aus den Fehler-Vergleichen (<see cref="SideLinesOf"/>).
    /// </summary>
    public static List<Occurrence> Occurrences(IReadOnlyList<Position> positions, IReadOnlyList<SideLine>? sideLines = null)
    {
        var result = new List<Occurrence>();
        if (positions.Count == 0) return result;
        var ordered = positions.OrderBy(p => p.Ply).ToList();
        var cands = ordered.ToDictionary(p => p.Ply, p => TacticHarvest.Parse(p.CandidatesJson));
        // Die Endstellung steht seit 0.689.0 als eigene Zeile ohne Zug in der Analyse — der letzte Zug ist die letzte Zeile MIT Zug.
        var last = ordered.LastOrDefault(p => !string.IsNullOrEmpty(p.GameMoveUci));
        if (last is null) return result;
        var final = Games.TacticsBoard.TryParse(last.Fen)?.Apply(last.GameMoveUci);
        var playedMate = final is not null && MatePattern.IsMate(final);

        int? MateOf(int ply) => cands.TryGetValue(ply, out var c) && c.Count > 0 ? c[0].Mate : null;
        bool HasEval(int ply) => cands.TryGetValue(ply, out var c) && c.Count > 0;

        foreach (var p in ordered)
        {
            if (string.IsNullOrEmpty(p.GameMoveUci)) continue;
            var c = cands[p.Ply];
            if (c.Count == 0 || c[0].Mate is not (> 0 and <= MaxMateIn) || c[0].Mate is not int n) continue;
            if (p.Ply == last.Ply && playedMate) continue;           // gespielt — eigene Stelle unten
            if (HasEval(p.Ply + 1)) { if (MateOf(p.Ply + 1) is < 0) continue; }   // der Gegner steht weiter matt: behalten
            else if (p.Ply != last.Ply) continue;                    // unbewertet: nichts behaupten
            var pv = c[0].Pv.Count > 0 ? c[0].Pv : new List<string> { c[0].Uci };
            result.Add(MateAt(p.Ply, FenFields.WhiteToMove(p.Fen), MissedMate, n, MatePattern.Play(p.Fen, pv), 1000));
        }

        if (playedMate)
        {
            var start = last.Ply;
            while (MateOf(start - 2) is > 0 && MateOf(start - 1) is < 0) start -= 2;
            var mateIn = MateOf(start) is int m and > 0 ? m : (last.Ply - start) / 2 + 1;
            result.Add(MateAt(start, FenFields.WhiteToMove(last.Fen), PlayedMate, mateIn, final, 0));
        }
        else if (ordered.FirstOrDefault(p => p.Ply == last.Ply + 1 && string.IsNullOrEmpty(p.GameMoveUci)) is { } end
            && cands[end.Ply] is { Count: > 0 } ec && ec[0].Mate is int em && Math.Abs(em) is >= 1 and <= MaxMateIn)
        {
            // Aufgegeben (oder abgebrochen) mit Matt auf dem Brett: em > 0 = die Seite am Zug setzt matt, em < 0 = sie wird mattgesetzt
            // (dann hat der Gegner mit dem letzten Zug die Mattfolge gehalten oder begonnen). Stelle = Beginn der Mattfolge.
            var mover = FenFields.WhiteToMove(end.Fen);
            var start = em > 0 ? end.Ply : last.Ply;
            while (MateOf(start - 2) is > 0 && MateOf(start - 1) is < 0) start -= 2;
            var pv = ec[0].Pv.Count > 0 ? ec[0].Pv : new List<string> { ec[0].Uci };
            result.Add(MateAt(start, em > 0 ? mover : !mover, ForcedMate, MateOf(start) is int m and > 0 ? m : Math.Abs(em),
                MatePattern.Play(end.Fen, pv), 0));
        }
        result.AddRange(TacticOccurrences(ordered, cands, sideLines ?? []));
        return result;
    }

    /// <summary>Matt-Stelle mit Bild und benanntem Thema aus der Mattstellung <paramref name="end"/> (<c>null</c> oder kein Matt → ohne Bild).</summary>
    private static Occurrence MateAt(int ply, bool white, string reason, int mateIn, Games.TacticsBoard? end, double weight) =>
        new(ply, white, reason, mateIn, end is null ? null : MatePattern.Of(end), Weight: weight, MateTheme: end is null ? null : MatePattern.ThemeOf(end));

    /// <summary>
    /// Taktische Stellen (0.961.0): je Fehler der Partie (<see cref="Games.GameMistakes.Find"/>, Fehler/Patzer/verpasste Chance — keine
    /// Ungenauigkeit) die Stellung davor (die beste Linie der ziehenden Seite = verpasste Taktik) und die Stellung danach (die beste Linie
    /// des Gegners = zugelassene Taktik). Zählt nur, wenn die Linie klar gewinnt (≥ <see cref="MinTacticCp"/> oder, seit 0.976.0, um
    /// <see cref="MinTacticGap"/> besser als der zweitbeste Zug; kein Matt — das ist oben erfasst) UND
    /// <see cref="TacticMotifs.DetectInGame"/> ein Motiv erkennt; ein positioneller Fehler ohne Motiv gibt nichts.
    ///
    /// <para><b>Mattdrohungen</b> (0.976.0) an denselben beiden Stellungen, je Stellung und Seite höchstens eine: hinter dem besten Zug
    /// (<see cref="MateThreats.After"/>, Matt in 1), sonst die kürzeste matt endende Nebenlinie aus <paramref name="sideLines"/>
    /// (auch an Stellen, die hier kein Fehler sind — der Vergleich wurde ja für einen Zug der Partie gerechnet).</para>
    /// </summary>
    private static List<Occurrence> TacticOccurrences(List<Position> ordered, Dictionary<int, List<TacticHarvest.Cand>> cands, IReadOnlyList<SideLine> sideLines)
    {
        var result = new List<Occurrence>();
        var byPly = ordered.ToDictionary(p => p.Ply);
        var entities = ordered.Select(p => new GameAnalysisPosition
        {
            Ply = p.Ply, Fen = p.Fen, GameMoveUci = p.GameMoveUci, GameMoveSan = "", CandidatesJson = p.CandidatesJson,
        }).ToList();
        // Halbzüge der Partie = letzter Zug + 1 (die Endstellung ohne Zug zählt nicht mit).
        var plyCount = ordered.LastOrDefault(p => !string.IsNullOrEmpty(p.GameMoveUci)) is { } lastMove ? lastMove.Ply + 1 : 0;

        // Hat der Zug VOR der Stellung geschlagen? Dann ist das Zurückschlagen dort keine hängende Figur.
        (int Square, int Value)? RecaptureAt(int ply)
        {
            if (!byPly.TryGetValue(ply - 1, out var prev) || prev.GameMoveUci is not { Length: >= 4 } uci
                || Games.TacticsBoard.SquareIndex(uci[2..4]) is not int to || Games.TacticsBoard.TryParse(prev.Fen) is not { } board
                || board.At(to) is not char taken || Games.TacticsBoard.IsWhite(taken) == board.WhiteToMove) return null;
            return (to, TacticMotifs.Value(taken));
        }

        TacticMotifs.Motif? TacticAt(int ply)
        {
            if (!byPly.TryGetValue(ply, out var pos) || !cands.TryGetValue(ply, out var c) || c.Count == 0 || c[0].Mate is not null) return null;
            var cp = c[0].Cp ?? 0;
            // Zweitbester Zug läuft ins Matt → der Abstand ist beliebig groß.
            var gap = c.Count > 1 ? (c[1].Mate is null ? cp - (c[1].Cp ?? 0) : c[1].Mate < 0 ? int.MaxValue : 0) : 0;
            if (cp < MinTacticCp && gap < MinTacticGap) return null;
            var pv = c[0].Pv.Count > 0 ? c[0].Pv : new List<string> { c[0].Uci };
            return TacticMotifs.DetectInGame(pos.Fen, pv, RecaptureAt(ply)).FirstOrDefault();
        }

        // Je Stellung und Seite die beste Mattdrohung: Rang 0 = hinter dem besten Zug (Brett), sonst 1 + Mattlänge der Nebenlinie.
        var threats = new Dictionary<(int Ply, bool White), (int Rank, Occurrence O)>();
        void Offer(int rank, Occurrence o)
        {
            if (o.Signature is null) return;
            if (!threats.TryGetValue((o.Ply, o.White), out var have) || rank < have.Rank) threats[(o.Ply, o.White)] = (rank, o);
        }
        var lossAt = new Dictionary<int, double>();

        void ThreatAt(int ply, double loss)
        {
            lossAt[ply] = Math.Max(loss, lossAt.GetValueOrDefault(ply));
            if (!byPly.TryGetValue(ply, out var pos) || !cands.TryGetValue(ply, out var c) || c.Count == 0 || c[0].Mate is not null) return;
            var pv = c[0].Pv.Count > 0 ? c[0].Pv : new List<string> { c[0].Uci };
            if (MateThreats.After(pos.Fen, pv[0], pv.Count > 1 ? pv[1] : null) is { } t)
                Offer(0, MateAt(ply, FenFields.WhiteToMove(pos.Fen), MateThreat, 1, t.End, loss + ThreatBonus));
        }

        foreach (var f in Games.GameMistakes.Find(entities, plyCount).Where(f => f.Class is "mistake" or "blunder" or "miss"))
        {
            var loss = f.WinBefore - f.WinAfter;
            if (TacticAt(f.Ply) is { } missed)
                result.Add(new Occurrence(f.Ply, f.White, MissedTactic, 0, null, missed, loss));
            if (TacticAt(f.Ply + 1) is { } allowed)
                result.Add(new Occurrence(f.Ply + 1, !f.White, AllowedTactic, 0, null, allowed, loss));
            ThreatAt(f.Ply, loss);
            ThreatAt(f.Ply + 1, loss);
        }
        foreach (var l in sideLines)
        {
            if (!byPly.ContainsKey(l.Ply) || l.MateIn is < 1 or > MaxMateIn || MatePattern.Play(l.Fen, l.Pv) is not { } end || !MatePattern.IsMate(end)) continue;
            Offer(1 + l.MateIn, MateAt(l.Ply, l.White, MateThreat, l.MateIn, end, lossAt.GetValueOrDefault(l.Ply) + ThreatBonus));
        }
        result.AddRange(threats.Values.Select(t => t.O));
        return result;
    }

    /// <summary>Eine Mattdrohung steht vor der Taktik desselben Fehlers (gleicher Verlust).</summary>
    private const double ThreatBonus = 0.5;

    /// <summary>
    /// Die matt endenden Nebenlinien der Fehler-Vergleiche einer Analyse (rein, 0.976.0). Gelesen werden die gerechneten Zeilen
    /// <see cref="MoveComparisonLineKind.Reply"/> (nur mit naheliegender Antwort, <see cref="MateThreats.IsNaturalReply"/>) und
    /// <see cref="MoveComparisonLineKind.BestMoveTry"/> (Stellung nach dem BESTEN Zug und einer Antwort des Gegners — setzt dort die
    /// Seite der Fehlerstelle in höchstens <see cref="MaxMateIn"/> matt, lief die Antwort ins Matt: Stelle = der Vergleich, Seite = wer
    /// dort am Zug war) und <see cref="MoveComparisonLineKind.Defence"/> (eine andere
    /// Verteidigung in der Widerlegung des Fehlzugs — setzt der Widerlegende matt: Stelle = die Stellung NACH dem Fehlzug, seine Seite).
    /// Eine Zeile zählt nur, wenn der Vergleich zur Stellung der Analyse passt und in ihrer Stellung die erwartete Seite am Zug ist.
    /// </summary>
    public static List<SideLine> SideLinesOf(IEnumerable<ComparisonRow> rows, IReadOnlyList<Position> positions)
    {
        var result = new List<SideLine>();
        var fenAt = new Dictionary<int, string>();
        foreach (var p in positions) fenAt[p.Ply] = p.Fen;
        static string Board(string fen) => string.Join(' ', fen.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
        foreach (var r in rows)
        {
            if (r.Ply is not int ply || string.IsNullOrEmpty(r.Fen) || !fenAt.TryGetValue(ply, out var fen) || Board(fen) != Board(r.ComparisonFen)) continue;
            var hero = FenFields.WhiteToMove(r.ComparisonFen);
            var mover = FenFields.WhiteToMove(r.Fen);
            bool expected;
            int at;
            switch (r.Kind)
            {
                // Antwort-Test: die Antwort stammt aus der Rangfolge nach dem FEHLZUG — nach dem besten Zug zählt sie nur, wenn sie
                // dort naheliegt (sonst wäre „stellt die Dame ein und wird matt“ eine Mattdrohung).
                case MoveComparisonLineKind.Reply:
                    if (r.BestUci is null || !MateThreats.IsNaturalReply(r.ComparisonFen, r.BestUci, r.ReplyUci)) continue;
                    expected = hero; at = ply; break;
                case MoveComparisonLineKind.BestMoveTry: expected = hero; at = ply; break;
                case MoveComparisonLineKind.Defence: expected = !hero; at = ply + 1; break;
                default: continue;
            }
            if (mover != expected) continue;
            if (BrokerCandidates.Parse(r.ResultJson, r.Fen) is not { Count: > 0 } c || c[0].Mate is not (>= 1 and <= MaxMateIn) || c[0].Mate is not int n) continue;
            result.Add(new SideLine(at, mover, r.Fen, c[0].Pv is { Count: > 0 } pv ? pv : [c[0].Uci], n));
        }
        return result;
    }

    /// <summary>Gleiches Bild/Motiv mehrfach in der Partie → eine Gruppe; Reihenfolge nach Gewicht (verpasstes Matt, dann Taktik nach
    /// Bewertungsverlust, gespieltes Matt zuletzt), dann nach Zug; höchstens <see cref="MaxGroups"/>.</summary>
    public static List<(Occurrence First, List<int> Also)> Group(IEnumerable<Occurrence> occurrences) =>
        occurrences.OrderByDescending(o => o.Weight).ThenBy(o => o.Ply)
            .GroupBy(o => o.Signature?.Strict ?? o.Motif?.Strict ?? $"{o.Reason}:{o.MateIn}:{o.Ply}")
            .Select(g => (g.First(), g.Skip(1).Select(x => x.Ply).Where(x => x != g.First().Ply).Distinct().OrderBy(x => x).ToList()))
            .Take(MaxGroups).ToList();

    /// <summary>Übungen einer Vereinspartie (Hauptanalyse, wie die Bewertungen der Partieseite). Rechte prüft der Aufrufer.</summary>
    public async Task<GameExercisesDto> ForClubGameAsync(int clubGameId, CancellationToken ct = default, EbookViewer? viewer = null)
    {
        var analysisId = await db.GameAnalyses.AsNoTracking()
            .Where(a => a.LeagueClubGameId == clubGameId && a.Origin == GameAnalysisOrigin.Club && a.EngineId == null)
            .OrderByDescending(a => a.Id).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        return await ForAnalysisAsync(analysisId, clubGameId.ToString(System.Globalization.CultureInfo.InvariantCulture), ct, viewer);
    }

    /// <summary>Übungen einer eigenen Partie (ihre Analyse). Rechte prüft der Aufrufer.</summary>
    public async Task<GameExercisesDto> ForSavedGameAsync(int savedGameId, CancellationToken ct = default, EbookViewer? viewer = null)
    {
        var analysisId = await db.SavedGames.AsNoTracking().Where(g => g.Id == savedGameId).Select(g => g.GameAnalysisId).FirstOrDefaultAsync(ct);
        return await ForAnalysisAsync(analysisId, $"o{savedGameId}", ct, viewer);
    }

    /// <summary>Eigene Partie des Besitzers; <c>null</c> = unbekannt oder fremd (404 wie die Zusammenfassung).</summary>
    public async Task<GameExercisesDto?> ForOwnGameAsync(int userId, int savedGameId, CancellationToken ct = default, EbookViewer? viewer = null) =>
        await db.SavedGames.AsNoTracking().AnyAsync(g => g.Id == savedGameId && g.UserId == userId, ct)
            ? await ForSavedGameAsync(savedGameId, ct, viewer) : null;

    /// <summary>Eigene Partie über ihren Teilen-Link <c>/g/{token}</c>; <c>null</c> = unbekannter Link.</summary>
    public async Task<GameExercisesDto?> ForSharedOwnGameAsync(string token, CancellationToken ct = default, EbookViewer? viewer = null)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var id = await db.SavedGames.AsNoTracking().Where(g => g.ShareToken == token).Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);
        return id is int i ? await ForSavedGameAsync(i, ct, viewer) : null;
    }

    /// <summary>Vereinspartie über ihren eigenen Link <c>/cg/{token}</c>; <c>null</c> = ungültig oder widerrufen.</summary>
    public async Task<GameExercisesDto?> ForClubLinkAsync(string token, CancellationToken ct = default, EbookViewer? viewer = null)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var id = await db.LeagueClubGames.AsNoTracking().Where(g => g.ShareToken == token).Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);
        return id is int i ? await ForClubGameAsync(i, ct, viewer) : null;
    }

    /// <param name="gameKey">Kennung der Partie wie bei der Literatur (<c>237</c> bzw. <c>o143</c>) — Stärke und Saat.</param>
    /// <param name="viewer">Wessen Bücher als Beispiele dienen dürfen; <c>null</c> = keine (nur Lichess).</param>
    public async Task<GameExercisesDto> ForAnalysisAsync(int? analysisId, string gameKey, CancellationToken ct = default, EbookViewer? viewer = null)
    {
        var empty = new GameExercisesDto(new List<GameExerciseGroupDto>());
        if (analysisId is not int id) return empty;
        var rawPositions = await db.GameAnalysisPositions.AsNoTracking().Where(p => p.GameAnalysisId == id)
            .Select(p => new { p.Ply, p.Fen, p.GameMoveUci, p.CandidatesJson, p.GameMoveSan }).ToListAsync(ct);
        var positions = rawPositions.Select(p => new Position(p.Ply, p.Fen, p.GameMoveUci, p.CandidatesJson)).ToList();
        // Partiezug je Halbzug (SAN) für den Vermerk „Warum diese Aufgabe“ im Kurs (1.001.0); die Endstellung hat keinen Zug.
        var sanAt = new Dictionary<int, string>();
        foreach (var p in rawPositions)
            if (!string.IsNullOrEmpty(p.GameMoveUci) && !string.IsNullOrWhiteSpace(p.GameMoveSan)) sanAt[p.Ply] = p.GameMoveSan.Trim();
        // Nur Zusatzlinien mit Mattbewertung — der Rest der Vergleiche wird hier nicht gebraucht.
        var rows = await db.MoveComparisonLines.AsNoTracking()
            .Where(l => l.Comparison!.GameAnalysisId == id && l.State == MoveComparisonLineState.Done && l.ResultJson != null
                && l.ResultJson.Contains("\"mate\"")
                && (l.Kind == MoveComparisonLineKind.Reply || l.Kind == MoveComparisonLineKind.Defence || l.Kind == MoveComparisonLineKind.BestMoveTry))
            .OrderBy(l => l.Id)
            .Select(l => new ComparisonRow(l.Comparison!.Ply, l.Comparison.Fen, l.Kind, l.Fen, l.ResultJson, l.Comparison.BestUci, l.ReplyUci)).ToListAsync(ct);
        var groups = Group(Occurrences(positions, SideLinesOf(rows, positions)));
        if (groups.Count == 0) return empty;

        var s = await strength.StrengthAsync(gameKey, ct);
        int? Of(PlayerStrength? p) => p?.Elo ?? BandMid(p?.Band);
        var used = new HashSet<int>();
        var usedDiagrams = new HashSet<int>();
        var result = new List<GameExerciseGroupDto>();
        foreach (var (o, also) in groups)
        {
            var target = (o.White ? Of(s?.White) ?? Of(s?.Black) : Of(s?.Black) ?? Of(s?.White)) ?? DefaultRating;
            var seed = (int)(MatePattern.Hash($"{gameKey}:{o.Ply}") & 0x7fffffff);
            var picked = viewer is { } v ? await _books.PickAsync(o, target, v, usedDiagrams, seed, ct) : [];
            await PickAsync(picked, o, target, used, seed, ct);
            if (picked.Count == 0) continue;
            result.Add(new GameExerciseGroupDto(o.Ply, o.White, o.Reason, o.Motif is null ? o.MateIn : null, also, picked, o.Theme,
                o.Motif?.Strict, sanAt.GetValueOrDefault(o.Ply), sanAt.GetValueOrDefault(o.Ply - 1)));
        }
        return new GameExercisesDto(result);
    }

    /// <summary>„1700–1799“ → 1750.</summary>
    internal static int? BandMid(string? band) =>
        band is { Length: >= 4 } && int.TryParse(band[..4], out var low) ? low + 50 : null;

    /// <summary>Lichess-Aufgaben hinter die schon gewählten Buchbeispiele in <paramref name="picked"/>, bis <see cref="PerGroup"/> LÖSBARE
    /// beisammen sind (ein „Beispiel im Buch“ ohne bestätigte Lösung zählt seit 0.976.0 nicht mit — im Kurs gibt es dort nichts zu
    /// lösen): gleiche strenge Signatur, gleiche Familie, und was dann noch fehlt, aus dem Thema (<see cref="ThemeTags"/>).</summary>
    private async Task PickAsync(List<GameExerciseDto> picked, Occurrence o, int target, HashSet<int> used, int seed, CancellationToken ct)
    {
        if (o.Signature is { } sig)
        {
            var strict = sig.StrictHash;
            await AddAsync(picked, "strict", db.PuzzleSignatures.AsNoTracking()
                .Where(x => x.Strict == strict && x.Kind == PuzzleSignatureKind.Mate).Select(x => new Row { PuzzleId = x.PuzzleId, Rating = x.Rating }), target, used, seed, ct);
            if (Solvable(picked) < PerGroup)
            {
                var family = sig.FamilyHash;
                await AddAsync(picked, "family", db.PuzzleSignatures.AsNoTracking()
                    .Where(x => x.Family == family && x.Kind == PuzzleSignatureKind.Mate).Select(x => new Row { PuzzleId = x.PuzzleId, Rating = x.Rating }), target, used, seed, ct);
            }
        }
        if (o.Motif is { } motif && TacticMotifs.HasSignatures(motif.Theme))
        {
            var kind = TacticMotifs.KindOf(motif.Theme);
            var strictHash = motif.StrictHash;
            await AddAsync(picked, "strict", db.PuzzleSignatures.AsNoTracking()
                .Where(x => x.Strict == strictHash && x.Kind == kind).Select(x => new Row { PuzzleId = x.PuzzleId, Rating = x.Rating }), target, used, seed, ct);
            if (Solvable(picked) < PerGroup)
            {
                var familyHash = motif.FamilyHash;
                await AddAsync(picked, "family", db.PuzzleSignatures.AsNoTracking()
                    .Where(x => x.Family == familyHash && x.Kind == kind).Select(x => new Row { PuzzleId = x.PuzzleId, Rating = x.Rating }), target, used, seed, ct);
            }
        }
        foreach (var name in ThemeTags(o))
        {
            if (Solvable(picked) >= PerGroup) break;
            var tagId = await db.Tags.AsNoTracking().Where(t => t.Name == name).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
            if (tagId is int tid)
                await AddAsync(picked, "theme", db.PuzzleTags.AsNoTracking().Where(t => t.TagId == tid).Select(t => new Row { PuzzleId = t.PuzzleId, Rating = t.Rating }),
                    target, used, seed, ct);
        }
    }

    /// <summary>Lichess-Themen, aus denen eine Stelle aufgefüllt wird, in dieser Reihenfolge: das Motiv bzw. das benannte Mattbild
    /// (<c>backRankMate</c>, <c>smotheredMate</c>), bei einem Matt danach <c>mateInN</c>.</summary>
    public static List<string> ThemeTags(Occurrence o)
    {
        var names = new List<string>();
        if (o.Theme is { } theme) names.Add(theme);
        if (o.Motif is null) names.Add($"mateIn{Math.Clamp(o.MateIn, 1, MaxMateIn)}");
        return names;
    }

    /// <summary>Lösbare Aufgaben in der Auswahl: jede Lichess-Aufgabe, ein Buchdiagramm nur mit bestätigter Lösung und bestimmter Löserseite.</summary>
    private static int Solvable(List<GameExerciseDto> picked) => picked.Count(e => e.Book is null || e.Book.Solvable);

    /// <summary>Mit Objekt-Initialisierer statt Konstruktor: nur so übersetzt EF die nachgestellten Bedingungen auf <c>Rating</c> (MariaDB-Test).</summary>
    private sealed class Row { public int PuzzleId { get; init; } public int Rating { get; init; } }

    /// <summary>Aus <paramref name="source"/> (Index Signatur/Thema + Rating) die nächstgelegenen um <paramref name="target"/>, je
    /// <see cref="Pool"/> darüber und darunter, gefiltert über die Aufgabe selbst, dann fest gesät gemischt.</summary>
    private async Task AddAsync(List<GameExerciseDto> into, string match, IQueryable<Row> source, int target, HashSet<int> used, int seed, CancellationToken ct)
    {
        var need = PerGroup - Solvable(into);
        if (need <= 0) return;
        var above = await source.Where(r => r.Rating >= target && r.Rating <= target + Window)
            .OrderBy(r => r.Rating).ThenBy(r => r.PuzzleId).Select(r => r.PuzzleId).Take(Pool).ToListAsync(ct);
        var below = await source.Where(r => r.Rating < target && r.Rating >= target - Window)
            .OrderByDescending(r => r.Rating).ThenByDescending(r => r.PuzzleId).Select(r => r.PuzzleId).Take(Pool).ToListAsync(ct);
        var ids = above.Concat(below).Where(i => !used.Contains(i)).Distinct().ToList();
        if (ids.Count == 0) return;
        var puzzles = await db.Puzzles.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.NbPlays >= MinPlays && p.Popularity >= PuzzleSignatureWorker.MinPopularity && !p.HintsFlagged)
            .Select(p => new { p.Id, p.Fen, p.Moves, p.Rating, p.Themes }).ToListAsync(ct);
        var rng = new Random(seed ^ match.Length);
        var chosen = puzzles.OrderBy(p => Math.Abs(p.Rating - target)).ThenBy(p => p.Id).Take(Closest)
            .OrderBy(_ => rng.Next()).Take(need).OrderBy(p => p.Rating).ThenBy(p => p.Id);
        foreach (var p in chosen)
        {
            used.Add(p.Id);
            into.Add(new GameExerciseDto(p.Id, p.Fen, p.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(), p.Rating,
                (p.Themes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(), match));
        }
    }
}
