using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
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
/// <para><b>Deckel</b> (Codereview 2026-09-29, N8-005): Der Neuaufbau liest alle markierten PGNs des Kontos und spielt
/// jeden Halbzug nach — vorher verwarf JEDE Anfrage mit <c>refresh</c> den Cache, parallele Anfragen bauten dasselbe Set
/// mehrfach gleichzeitig, und der Cache hatte keine Größengrenze. Jetzt: <c>refresh</c> baut höchstens einmal je Konto
/// und <see cref="RefreshCooldown"/> neu (sonst gilt der Cache); je Konto läuft höchstens EIN Neuaufbau, weitere Anfragen
/// warten darauf und lesen dann den Cache; ein Set liest höchstens <see cref="MaxPgnCharsPerSet"/> Zeichen PGN und hält
/// höchstens <see cref="MaxPositionsPerSet"/> Stellungen (darüber: Teil-Set, <c>RepertoireTruncated</c> in der Antwort);
/// der Cache ist ein eigener (<see cref="CacheServiceKey"/>) mit <see cref="CacheSizeLimit"/> Stellungen.</para>
///
/// Der PGN-Parser liegt seit 0.499.6 in <see cref="PgnMoveTree"/> — er stand hier und in
/// <see cref="RepertoireLineSource"/> als wörtliche Kopie.
/// </summary>
public class RepertoireAnalyzeService
{
    /// <summary>DI-Schlüssel des eigenen Caches mit Größengrenze (Program.cs). Der allgemeine <see cref="IMemoryCache"/>
    /// hat keine: dort eine Grenze zu setzen hieße, jedem anderen Eintrag im Prozess eine Größe zu geben.</summary>
    public const string CacheServiceKey = "repertoire-position-sets";
    /// <summary>Größengrenze des eigenen Caches in Stellungen (Size eines Eintrags = Stellungen seines Sets). Gemessen
    /// rund 190 Byte je Stellung — 2 Mio. sind knapp 400 MB, vier volle Sets.</summary>
    public const long CacheSizeLimit = 2_000_000;
    /// <summary>Höchstens so viele Stellungen je Set (rund 95 MB und 10 s Aufbau bei dichtem PGN); darüber bleibt es ein
    /// Teil-Set. Ein großes echtes Repertoire hat einige zehntausend.</summary>
    public const int MaxPositionsPerSet = 500_000;
    /// <summary>Höchstens so viele Zeichen PGN je Set (Summe der Dateien, in Id-Reihenfolge; eine Datei, die nicht mehr
    /// passt, bleibt draußen). Ohne Deckel lud ein Neuaufbau bis 500 × 1000 × 10 MB aus der Datenbank.</summary>
    public const long MaxPgnCharsPerSet = 16L * 1024 * 1024;
    /// <summary><c>refresh</c> baut je Konto höchstens einmal in diesem Zeitraum neu; der Knopf „Aktualisieren" der
    /// Erweiterung braucht nicht mehr — Upload, Löschen und Ändern verwerfen den Cache ohnehin.</summary>
    public static readonly TimeSpan RefreshCooldown = TimeSpan.FromMinutes(1);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RepertoireAnalyzeService>? _logger;

    /// <summary>Sperren je Konto — am Cache-Objekt aufgehängt, damit sie genau so weit geteilt sind wie der Cache
    /// selbst (in der API ein Singleton, in Tests je Test ein eigener).</summary>
    private static readonly ConditionalWeakTable<IMemoryCache, ConcurrentDictionary<int, SemaphoreSlim>> BuildGates = new();

    public RepertoireAnalyzeService(AppDbContext db, [FromKeyedServices(CacheServiceKey)] IMemoryCache cache,
        ILogger<RepertoireAnalyzeService>? logger = null)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Wie viele Sets DIESE Instanz (= eine Anfrage) gebaut hat — für Tests.</summary>
    internal int SetsBuilt { get; private set; }

    private static string CacheKey(int userId, RepertoireKind kind) =>
        $"ext:posset:{userId}:{(int)kind}";

    private static string RefreshKey(int userId) => $"ext:posset:refresh:{userId}";

    /// <summary>Cache-Eintrag eines Users invalidieren (z. B. nach PGN-Upload/-Delete).</summary>
    public void Invalidate(int userId)
    {
        foreach (var k in Enum.GetValues<RepertoireKind>())
            _cache.Remove(CacheKey(userId, k));
    }

    public async Task<AnalyzeGameResponseDto> AnalyzeAsync(int userId, AnalyzeGameRequestDto dto)
    {
        var set = await GetPositionSetAsync(userId, dto.Kind, dto.Refresh);
        var positions = set.Positions;

        var response = new AnalyzeGameResponseDto
        {
            RepertoireFileCount = set.FileCount,
            RepertoireTruncated = set.Truncated,
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

    /// <summary>
    /// „Buchzüge" einer Partie für den Partie-Rückblick (seit 0.522.0): die Halbzüge, deren Stellung DANACH in einem
    /// Repertoire des Nutzers steht, das er für die Erweiterung markiert hat (<c>UseForExtension</c>) — über ALLE
    /// Repertoire-Arten, mit Zugumstellungen. Gezählt wird wie in <see cref="AnalyzeAsync"/>: Buch sind die
    /// Repertoire-Züge VOR der Abweichung (dem ersten Zug nach dem letzten Repertoire-Zug); ein Zwischenzug, der das
    /// Repertoire kurz verlässt, ist selbst kein Buchzug.
    /// </summary>
    /// <param name="fensAfterPly">Je Halbzug die Stellung DANACH (Index = Halbzug, 0-basiert).</param>
    public async Task<List<int>> BookPliesAsync(int userId, IReadOnlyList<string> fensAfterPly)
    {
        var result = new List<int>();
        if (fensAfterPly.Count == 0) return result;
        var sets = new List<HashSet<string>>();
        foreach (var kind in Enum.GetValues<RepertoireKind>())
        {
            var set = await GetPositionSetAsync(userId, kind);
            if (set.FileCount > 0) sets.Add(set.Positions);
        }
        if (sets.Count == 0) return result;

        var inRep = fensAfterPly.Select(f => sets.Any(s => s.Contains(NormalizeFen(f)))).ToList();
        var lastIn = inRep.LastIndexOf(true);
        for (var i = 0; i <= lastIn; i++)
            if (inRep[i]) result.Add(i);
        return result;
    }

    /// <param name="refresh">Cache verwerfen und neu bauen — höchstens einmal je Konto und <see cref="RefreshCooldown"/>,
    /// sonst gilt der Cache.</param>
    private async Task<CachedPositionSet> GetPositionSetAsync(int userId, RepertoireKind kind, bool refresh = false)
    {
        var key = CacheKey(userId, kind);
        if (!refresh && _cache.TryGetValue<CachedPositionSet>(key, out var cached) && cached != null)
            return cached;

        // Je Konto EIN Neuaufbau zur Zeit: wer wartet, findet danach den Cache gefüllt.
        var gate = BuildGates.GetValue(_cache, _ => new ConcurrentDictionary<int, SemaphoreSlim>())
            .GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (refresh && !_cache.TryGetValue(RefreshKey(userId), out _))
            {
                _cache.Set(RefreshKey(userId), true, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = RefreshCooldown,
                    Priority = CacheItemPriority.High,
                    Size = 1,
                });
                _cache.Remove(key);
            }
            if (_cache.TryGetValue(key, out cached) && cached != null)
                return cached;

            var entry = await BuildAsync(userId, kind);
            _cache.Set(key, entry, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15),
                SlidingExpiration = TimeSpan.FromMinutes(5),
                Size = Math.Max(1, entry.Positions.Count),
            });
            return entry;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CachedPositionSet> BuildAsync(int userId, RepertoireKind kind)
    {
        SetsBuilt++;
        var files = _db.RepertoireFiles
            .Where(f => f.Repertoire.UserId == userId && f.Repertoire.Kind == kind && f.Repertoire.UseForExtension);

        // Erst die Längen (die Texte bleiben in der Datenbank), dann nur die Dateien, die in den Deckel passen.
        var sizes = await files.OrderBy(f => f.Id).Select(f => new { f.Id, f.PgnContent.Length }).ToListAsync();
        var ids = new List<int>(sizes.Count);
        long chars = 0;
        foreach (var s in sizes)
        {
            if (chars + s.Length > MaxPgnCharsPerSet) continue;
            chars += s.Length;
            ids.Add(s.Id);
        }
        var pgnTexts = new List<string>(ids.Count);
        foreach (var chunk in ids.Chunk(500))
            pgnTexts.AddRange(await files.Where(f => chunk.Contains(f.Id)).OrderBy(f => f.Id)
                .Select(f => f.PgnContent).ToListAsync());

        var positions = BuildPositionSet(pgnTexts.Select(RepertoirePgnCleanup.WithoutHidden).ToList(),   // ohne ausgeblendete Altlasten
            out var positionsCapped);
        var truncated = positionsCapped || ids.Count < sizes.Count;
        if (truncated)
            _logger?.LogWarning(
                "Positions-Set gekappt: Konto {UserId}, Art {Kind}, {Used}/{Files} Dateien, {Positions} Stellungen",
                userId, kind, ids.Count, sizes.Count, positions.Count);
        return new CachedPositionSet(positions, pgnTexts.Count, truncated);
    }

    private sealed record CachedPositionSet(HashSet<string> Positions, int FileCount, bool Truncated);

    // ─── PGN → Position Set ────────────────────────────────────────────────
    // Port der JS-Implementierung in repcheck.user.js: tokenize → parse mit
    // Varianten → walk mit Cancel() statt Reparse.

    /// <param name="capped"><c>true</c> = bei <paramref name="maxPositions"/> Stellungen abgebrochen (Teil-Set).</param>
    internal static HashSet<string> BuildPositionSet(List<string> pgnTexts, out bool capped,
        int maxPositions = MaxPositionsPerSet)
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
                    if (positions.Count >= maxPositions) break;
                    // Unbrauchbare [FEN] → Linie überspringen statt aus der Grundstellung zu spielen
                    // (das würde falsche Stellungen als „im Repertoire" markieren).
                    ChessBoard board;
                    if (game.StartFen == null) board = new ChessBoard();
                    else { try { board = ChessBoard.LoadFromFen(game.StartFen); } catch { continue; } }
                    positions.Add(NormalizeFen(board.ToFen()));   // Startstellung der Linie zählt mit
                    WalkMoves(board, game.Moves, positions, maxPositions);
                }
            }
            catch
            {
                // Einzelne kaputte PGN nicht den ganzen Build kippen lassen.
            }
            if (positions.Count >= maxPositions) break;
        }
        capped = positions.Count >= maxPositions;
        return positions;
    }

    private static void WalkMoves(ChessBoard board, List<PgnMove> moves, HashSet<string> positions, int maxPositions)
    {
        int movesMade = 0;
        foreach (var move in moves)
        {
            // Varianten zweigen VOR diesem Zug ab.
            foreach (var variation in move.Variations)
                WalkMoves(board, variation, positions, maxPositions);

            if (positions.Count >= maxPositions) break;
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
