using Chess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Serverseitige Repertoire-Analyse fuer die Browser-Extension. Statt das vollstaendige PGN
/// an den Client zu schicken, sendet der Client die Zugliste der aktuell betrachteten Partie
/// und der Server vergleicht sie gegen ein gecachtes, normalisiertes Positions-Set des Users.
///
/// Transpositions: das Set enthaelt alle in der Repertoire-PGN erreichbaren Stellungen als
/// normalisierte FEN-Strings (Brett + Seite + Rochaderechte + en-passant). Damit erkennen
/// wir Zugumstellungen, die dieselbe Zielstellung ueber eine andere Reihenfolge erreichen.
///
/// Cache: per User + RepertoireKind, 15 min absolute / 5 min sliding TTL. Invalidiert von
/// <see cref="RepertoireService"/> bei Upload/Delete/Update-Operationen.
///
/// Der PGN-Parser liegt seit 0.499.6 in <see cref="PgnMoveTree"/> — er stand hier und in
/// <see cref="RepertoireLineSource"/> als wörtliche Kopie.
/// </summary>
public class RepertoireAnalyzeService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public RepertoireAnalyzeService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private static string CacheKey(int userId, RepertoireKind kind) =>
        $"ext:posset:{userId}:{(int)kind}";

    /// <summary>Cache-Eintrag eines Users invalidieren (z. B. nach PGN-Upload/-Delete).</summary>
    public void Invalidate(int userId)
    {
        foreach (var k in Enum.GetValues<RepertoireKind>())
            _cache.Remove(CacheKey(userId, k));
    }

    public async Task<AnalyzeGameResponseDto> AnalyzeAsync(int userId, AnalyzeGameRequestDto dto)
    {
        if (dto.Refresh) _cache.Remove(CacheKey(userId, dto.Kind));

        var (positions, fileCount) = await GetPositionSetAsync(userId, dto.Kind);

        var response = new AnalyzeGameResponseDto
        {
            RepertoireFileCount = fileCount,
        };

        if (dto.Moves.Count == 0) return response;

        // Walk the game with Gera.Chess, collect per-ply in-rep flags.
        var board = new ChessBoard();
        var inRep = new List<bool>(dto.Moves.Count);
        for (int i = 0; i < dto.Moves.Count; i++)
        {
            var san = dto.Moves[i];
            bool moved;
            try { moved = board.Move(san); }
            catch { moved = false; }
            if (!moved)
            {
                response.IllegalMoveAt = i;
                break;
            }
            inRep.Add(positions.Contains(NormalizeFen(board.ToFen())));
        }

        // Last in-repertoire ply (transposition-aware: gaps are temporary excursions).
        int lastIn = -1;
        for (int i = inRep.Count - 1; i >= 0; i--)
            if (inRep[i]) { lastIn = i; break; }

        int deviation = -1;
        for (int i = 0; i < inRep.Count; i++)
        {
            if (inRep[i]) response.InRepertoire.Add(i);
            else if (i <= lastIn) response.Gaps.Add(i);
            else if (deviation == -1) deviation = i;
        }
        response.Deviation = deviation;

        if (deviation >= 0) response.FenBeforeDeviation = FenBeforeMove(dto.Moves, deviation);
        return response;
    }

    private async Task<(HashSet<string> Positions, int FileCount)> GetPositionSetAsync(int userId, RepertoireKind kind)
    {
        var key = CacheKey(userId, kind);
        if (_cache.TryGetValue<CachedPositionSet>(key, out var cached) && cached != null)
            return (cached.Positions, cached.FileCount);

        var pgnTexts = await _db.RepertoireFiles
            .Where(f => f.Repertoire.UserId == userId && f.Repertoire.Kind == kind && f.Repertoire.UseForExtension)
            .Select(f => f.PgnContent)
            .ToListAsync();

        var positions = BuildPositionSet(pgnTexts.Select(RepertoirePgnCleanup.WithoutHidden).ToList());   // ohne ausgeblendete Altlasten
        var entry = new CachedPositionSet(positions, pgnTexts.Count);

        _cache.Set(key, entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15),
            SlidingExpiration = TimeSpan.FromMinutes(5),
        });
        return (positions, pgnTexts.Count);
    }

    private sealed record CachedPositionSet(HashSet<string> Positions, int FileCount);

    // ─── PGN → Position Set ────────────────────────────────────────────────
    // Port der JS-Implementierung in repcheck.user.js: tokenize → parse mit
    // Varianten → walk mit Cancel() statt Reparse.

    private static HashSet<string> BuildPositionSet(List<string> pgnTexts)
    {
        var positions = new HashSet<string>(StringComparer.Ordinal);
        // Ausgangsstellung gehoert dazu — sonst landet Zug 1 (Weiss) sofort als Abweichung.
        positions.Add(NormalizeFen(new ChessBoard().ToFen()));
        foreach (var text in pgnTexts)
        {
            try
            {
                foreach (var game in ParsePgn(text))
                {
                    // Unbrauchbare [FEN] → Linie überspringen statt aus der Grundstellung zu spielen
                    // (das würde falsche Stellungen als „im Repertoire" markieren).
                    ChessBoard board;
                    if (game.StartFen == null) board = new ChessBoard();
                    else { try { board = ChessBoard.LoadFromFen(game.StartFen); } catch { continue; } }
                    positions.Add(NormalizeFen(board.ToFen()));   // Startstellung der Linie zählt mit
                    WalkMoves(board, game.Moves, positions);
                }
            }
            catch
            {
                // Einzelne kaputte PGN nicht den ganzen Build kippen lassen.
            }
        }
        return positions;
    }

    private static void WalkMoves(ChessBoard board, List<PgnMove> moves, HashSet<string> positions)
    {
        int movesMade = 0;
        foreach (var move in moves)
        {
            // Varianten zweigen VOR diesem Zug ab.
            foreach (var variation in move.Variations)
                WalkMoves(board, variation, positions);

            bool ok;
            try { ok = board.Move(move.San); }
            catch { ok = false; }
            if (!ok) break;
            movesMade++;
            positions.Add(NormalizeFen(board.ToFen()));
        }
        for (int i = 0; i < movesMade; i++) board.Cancel();
    }

    public static string NormalizeFen(string fen)
    {
        // Halbzug- und Vollzugzaehler weglassen: fuer Repertoire-Matching irrelevant.
        var parts = fen.Split(' ');
        return parts.Length >= 4 ? string.Join(' ', parts.Take(4)) : fen;
    }

    private static string FenBeforeMove(List<string> moves, int idx)
    {
        var board = new ChessBoard();
        for (int i = 0; i < idx && i < moves.Count; i++)
        {
            try { if (!board.Move(moves[i])) break; }
            catch { break; }
        }
        return board.ToFen();
    }

    // ─── PGN → Linien ──────────────────────────────────────────────

    /// <summary>Eine Linie: Startstellung (<c>null</c> = Grundstellung) + Zugbaum.</summary>
    private sealed record ParsedLine(string? StartFen, List<PgnMove> Moves);

    /// <summary>Die ZUG-TRAGENDEN Abschnitte eines PGN. Zug-lose interessieren hier nicht (sie
    /// tragen keine Stellung bei) — anders als bei <see cref="RepertoireLineSource"/>, wo ihre
    /// Position den <c>gameIndex</c> hält; deshalb filtert jeder Aufrufer selbst.</summary>
    private static List<ParsedLine> ParsePgn(string text)
    {
        var games = PgnMoveTree.ParseSections(text)
            .Where(s => s.Moves.Count > 0)
            .Select(s => new ParsedLine(s.StartFen, s.Moves))
            .ToList();
        if (games.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            // Kein einziger Abschnitt mit Zügen: ein PGN ganz ohne Header (oder mit Header UND
            // Movetext in derselben Zeile) — dann den GANZEN Text als Movetext lesen.
            var (moves, _) = PgnMoveTree.ParseMoveTokens(PgnMoveTree.Tokenize(text), 0);
            if (moves.Count > 0) games.Add(new ParsedLine(null, moves));
        }
        return games;
    }
}
