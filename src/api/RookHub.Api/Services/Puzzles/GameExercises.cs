using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Puzzles;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Services.Tactics;

namespace RookHub.Api.Services.Puzzles;

/// <summary>
/// „Empfohlene Übungen“ am Ende der Partie-Zusammenfassung (0.960.0, Wunsch 2026-11-13). Aus den Daten der Partie, nicht von
/// Claude: jedes gespielte oder verpasste Matt (beider Seiten, auch wenn der Gegner es ebenfalls übersah) ist eine Stelle; je
/// Stelle bis zu <see cref="PerGroup"/> Lichess-Aufgaben mit demselben Mattbild (<see cref="MatePattern"/>) — erst gleiche strenge
/// Signatur, dann gleiche Familie, und nur wenn es davon keine gibt, Aufgaben desselben Themas (<c>mateInN</c>). Seit 0.961.0 ebenso
/// für taktische Fehler (<see cref="TacticMotifs"/>: Gabel, Fesselung, Spieß, Abzug …; streng = Thema + Figuren). Keine Stelle →
/// keine Übungen; aufgefüllt wird nicht („erzwinge keine motive/aufgaben“).
///
/// <para>Spielstärke: die der Seite, die das Motiv hatte (<see cref="EbookSuggestions.StrengthAsync"/>, wie die Literatur; ein Band
/// zählt mit seiner Mitte), sonst die der anderen Seite, sonst <see cref="DefaultRating"/>. Aufgaben im Fenster ±<see cref="Window"/>,
/// beliebt (<see cref="PuzzleSignatureWorker.MinPopularity"/>), oft gespielt (<see cref="MinPlays"/>), Tipps nicht als irreführend
/// markiert. Die Auswahl ist je Partie und Stelle fest gesät, also bei jedem Aufruf dieselbe (solange sich der Bestand nicht ändert).</para>
/// </summary>
public sealed class GameExercises(AppDbContext db, EbookSuggestions strength)
{
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

    /// <summary>Eine Motivstelle der Partie. Matt: <see cref="Signature"/> (fehlt, wenn die Engine-Linie nicht bis zum Matt reicht);
    /// Taktik (0.961.0): <see cref="Motif"/>. <see cref="Weight"/> ordnet die Stellen (verpasstes Matt vorn, Taktik nach Verlust).</summary>
    public sealed record Occurrence(int Ply, bool White, string Reason, int MateIn, MatePattern.Signature? Signature,
        TacticMotifs.Motif? Motif = null, double Weight = 0);

    public const string MissedMate = "missedMate";
    public const string PlayedMate = "playedMate";
    /// <summary>Die Seite am Zug hatte eine Taktik (beste Linie gewinnt, Motiv erkannt) und spielte einen Fehler.</summary>
    public const string MissedTactic = "missedTactic";
    /// <summary>Nach einem Fehler hatte der GEGNER eine Taktik (Motiv in seiner besten Linie) — Stelle auf seiner Seite.</summary>
    public const string AllowedTactic = "allowedTactic";
    /// <summary>Ab diesem Vorteil (Bauerneinheiten × 100, Sicht der Seite am Zug) gilt die beste Linie als gewinnende Taktik.</summary>
    public const int MinTacticCp = 200;
    /// <summary>So viele Halbzüge der Engine-Linie sieht der Motiv-Erkenner an.</summary>
    private const int TacticPlies = 6;

    /// <summary>
    /// Die Matt-Stellen der Partie (rein, öffentlich für Tests): <b>verpasst</b> = die Seite am Zug hatte laut Analyse Matt in höchstens
    /// <see cref="MaxMateIn"/>, nach ihrem Zug steht der Gegner aber nicht mehr matt (bzw. die Partie endet ohne Matt) — Bild aus der
    /// Engine-Linie; <b>gespielt</b> = die Partie endet mit Matt — Bild aus der Schlussstellung, Stelle = Beginn der Mattfolge laut
    /// Analyse. Stellungen ohne Bewertung entscheiden nichts.
    /// </summary>
    public static List<Occurrence> Occurrences(IReadOnlyList<Position> positions)
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
            var end = MatePattern.Play(p.Fen, pv);
            result.Add(new Occurrence(p.Ply, FenFields.WhiteToMove(p.Fen), MissedMate, n, end is null ? null : MatePattern.Of(end), Weight: 1000));
        }

        if (playedMate)
        {
            var start = last.Ply;
            while (MateOf(start - 2) is > 0 && MateOf(start - 1) is < 0) start -= 2;
            var mateIn = MateOf(start) is int m and > 0 ? m : (last.Ply - start) / 2 + 1;
            result.Add(new Occurrence(start, FenFields.WhiteToMove(last.Fen), PlayedMate, mateIn, MatePattern.Of(final!)));
        }
        result.AddRange(TacticOccurrences(ordered, cands));
        return result;
    }

    /// <summary>
    /// Taktische Stellen (0.961.0): je Fehler der Partie (<see cref="Games.GameMistakes.Find"/>, Fehler/Patzer/verpasste Chance — keine
    /// Ungenauigkeit) die Stellung davor (die beste Linie der ziehenden Seite = verpasste Taktik) und die Stellung danach (die beste Linie
    /// des Gegners = zugelassene Taktik). Zählt nur, wenn die Linie klar gewinnt (≥ <see cref="MinTacticCp"/>, kein Matt — das ist oben
    /// erfasst) UND <see cref="TacticMotifs"/> ein Motiv erkennt; ein positioneller Fehler ohne Motiv gibt nichts.
    /// </summary>
    private static List<Occurrence> TacticOccurrences(List<Position> ordered, Dictionary<int, List<TacticHarvest.Cand>> cands)
    {
        var result = new List<Occurrence>();
        var byPly = ordered.ToDictionary(p => p.Ply);
        var entities = ordered.Select(p => new GameAnalysisPosition
        {
            Ply = p.Ply, Fen = p.Fen, GameMoveUci = p.GameMoveUci, GameMoveSan = "", CandidatesJson = p.CandidatesJson,
        }).ToList();
        // Halbzüge der Partie = letzter Zug + 1 (die Endstellung ohne Zug zählt nicht mit).
        var plyCount = ordered.LastOrDefault(p => !string.IsNullOrEmpty(p.GameMoveUci)) is { } lastMove ? lastMove.Ply + 1 : 0;

        TacticMotifs.Motif? TacticAt(int ply)
        {
            if (!byPly.TryGetValue(ply, out var pos) || !cands.TryGetValue(ply, out var c) || c.Count == 0) return null;
            if (c[0].Mate is not null || (c[0].Cp ?? 0) < MinTacticCp) return null;
            var pv = c[0].Pv.Count > 0 ? c[0].Pv.Take(TacticPlies).ToList() : new List<string> { c[0].Uci };
            return TacticMotifs.Detect(pos.Fen, pv).FirstOrDefault();
        }

        foreach (var f in Games.GameMistakes.Find(entities, plyCount).Where(f => f.Class is "mistake" or "blunder" or "miss"))
        {
            var loss = f.WinBefore - f.WinAfter;
            if (TacticAt(f.Ply) is { } missed)
                result.Add(new Occurrence(f.Ply, f.White, MissedTactic, 0, null, missed, loss));
            if (TacticAt(f.Ply + 1) is { } allowed)
                result.Add(new Occurrence(f.Ply + 1, !f.White, AllowedTactic, 0, null, allowed, loss));
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
    public async Task<GameExercisesDto> ForClubGameAsync(int clubGameId, CancellationToken ct = default)
    {
        var analysisId = await db.GameAnalyses.AsNoTracking()
            .Where(a => a.LeagueClubGameId == clubGameId && a.Origin == GameAnalysisOrigin.Club && a.EngineId == null)
            .OrderByDescending(a => a.Id).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        return await ForAnalysisAsync(analysisId, clubGameId.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
    }

    /// <summary>Übungen einer eigenen Partie (ihre Analyse). Rechte prüft der Aufrufer.</summary>
    public async Task<GameExercisesDto> ForSavedGameAsync(int savedGameId, CancellationToken ct = default)
    {
        var analysisId = await db.SavedGames.AsNoTracking().Where(g => g.Id == savedGameId).Select(g => g.GameAnalysisId).FirstOrDefaultAsync(ct);
        return await ForAnalysisAsync(analysisId, $"o{savedGameId}", ct);
    }

    /// <summary>Eigene Partie des Besitzers; <c>null</c> = unbekannt oder fremd (404 wie die Zusammenfassung).</summary>
    public async Task<GameExercisesDto?> ForOwnGameAsync(int userId, int savedGameId, CancellationToken ct = default) =>
        await db.SavedGames.AsNoTracking().AnyAsync(g => g.Id == savedGameId && g.UserId == userId, ct)
            ? await ForSavedGameAsync(savedGameId, ct) : null;

    /// <summary>Eigene Partie über ihren Teilen-Link <c>/g/{token}</c>; <c>null</c> = unbekannter Link.</summary>
    public async Task<GameExercisesDto?> ForSharedOwnGameAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var id = await db.SavedGames.AsNoTracking().Where(g => g.ShareToken == token).Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);
        return id is int i ? await ForSavedGameAsync(i, ct) : null;
    }

    /// <summary>Vereinspartie über ihren eigenen Link <c>/cg/{token}</c>; <c>null</c> = ungültig oder widerrufen.</summary>
    public async Task<GameExercisesDto?> ForClubLinkAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var id = await db.LeagueClubGames.AsNoTracking().Where(g => g.ShareToken == token).Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);
        return id is int i ? await ForClubGameAsync(i, ct) : null;
    }

    /// <param name="gameKey">Kennung der Partie wie bei der Literatur (<c>237</c> bzw. <c>o143</c>) — Stärke und Saat.</param>
    public async Task<GameExercisesDto> ForAnalysisAsync(int? analysisId, string gameKey, CancellationToken ct = default)
    {
        var empty = new GameExercisesDto(new List<GameExerciseGroupDto>());
        if (analysisId is not int id) return empty;
        var positions = await db.GameAnalysisPositions.AsNoTracking().Where(p => p.GameAnalysisId == id)
            .Select(p => new Position(p.Ply, p.Fen, p.GameMoveUci, p.CandidatesJson)).ToListAsync(ct);
        var groups = Group(Occurrences(positions));
        if (groups.Count == 0) return empty;

        var s = await strength.StrengthAsync(gameKey, ct);
        int? Of(PlayerStrength? p) => p?.Elo ?? BandMid(p?.Band);
        var used = new HashSet<int>();
        var result = new List<GameExerciseGroupDto>();
        foreach (var (o, also) in groups)
        {
            var target = (o.White ? Of(s?.White) ?? Of(s?.Black) : Of(s?.Black) ?? Of(s?.White)) ?? DefaultRating;
            var seed = (int)(MatePattern.Hash($"{gameKey}:{o.Ply}") & 0x7fffffff);
            var picked = await PickAsync(o, target, used, seed, ct);
            if (picked.Count == 0) continue;
            result.Add(new GameExerciseGroupDto(o.Ply, o.White, o.Reason, o.Motif is null ? o.MateIn : null, also, picked, o.Motif?.Theme));
        }
        return new GameExercisesDto(result);
    }

    /// <summary>„1700–1799“ → 1750.</summary>
    internal static int? BandMid(string? band) =>
        band is { Length: >= 4 } && int.TryParse(band[..4], out var low) ? low + 50 : null;

    private async Task<List<GameExerciseDto>> PickAsync(Occurrence o, int target, HashSet<int> used, int seed, CancellationToken ct)
    {
        var picked = new List<GameExerciseDto>();
        if (o.Signature is { } sig)
        {
            var strict = sig.StrictHash;
            await AddAsync(picked, "strict", db.PuzzleSignatures.AsNoTracking()
                .Where(x => x.Strict == strict && x.Kind == PuzzleSignatureKind.Mate).Select(x => new Row { PuzzleId = x.PuzzleId, Rating = x.Rating }), target, used, seed, ct);
            if (picked.Count < PerGroup)
            {
                var family = sig.FamilyHash;
                await AddAsync(picked, "family", db.PuzzleSignatures.AsNoTracking()
                    .Where(x => x.Family == family && x.Kind == PuzzleSignatureKind.Mate).Select(x => new Row { PuzzleId = x.PuzzleId, Rating = x.Rating }), target, used, seed, ct);
            }
        }
        if (o.Motif is { } motif)
        {
            var kind = TacticMotifs.KindOf(motif.Theme);
            var strictHash = motif.StrictHash;
            await AddAsync(picked, "strict", db.PuzzleSignatures.AsNoTracking()
                .Where(x => x.Strict == strictHash && x.Kind == kind).Select(x => new Row { PuzzleId = x.PuzzleId, Rating = x.Rating }), target, used, seed, ct);
            if (picked.Count < PerGroup)
            {
                var familyHash = motif.FamilyHash;
                await AddAsync(picked, "family", db.PuzzleSignatures.AsNoTracking()
                    .Where(x => x.Family == familyHash && x.Kind == kind).Select(x => new Row { PuzzleId = x.PuzzleId, Rating = x.Rating }), target, used, seed, ct);
            }
        }
        if (picked.Count == 0)
        {
            var name = o.Motif?.Theme ?? $"mateIn{Math.Clamp(o.MateIn, 1, MaxMateIn)}";
            var tagId = await db.Tags.AsNoTracking().Where(t => t.Name == name).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
            if (tagId is int tid)
                await AddAsync(picked, "theme", db.PuzzleTags.AsNoTracking().Where(t => t.TagId == tid).Select(t => new Row { PuzzleId = t.PuzzleId, Rating = t.Rating }),
                    target, used, seed, ct);
        }
        return picked;
    }

    /// <summary>Mit Objekt-Initialisierer statt Konstruktor: nur so übersetzt EF die nachgestellten Bedingungen auf <c>Rating</c> (MariaDB-Test).</summary>
    private sealed class Row { public int PuzzleId { get; init; } public int Rating { get; init; } }

    /// <summary>Aus <paramref name="source"/> (Index Signatur/Thema + Rating) die nächstgelegenen um <paramref name="target"/>, je
    /// <see cref="Pool"/> darüber und darunter, gefiltert über die Aufgabe selbst, dann fest gesät gemischt.</summary>
    private async Task AddAsync(List<GameExerciseDto> into, string match, IQueryable<Row> source, int target, HashSet<int> used, int seed, CancellationToken ct)
    {
        var need = PerGroup - into.Count;
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
