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
/// warten darauf und lesen dann den Cache; im ganzen Prozess laufen höchstens <see cref="PositionSetBuildGate"/>-viele
/// Neuaufbauten gleichzeitig (sonst <see cref="PositionSetBusyException"/>); ein Set liest höchstens
/// <see cref="MaxPgnBytesPerSet"/> PGN, und ein KONTO hält über alle Arten zusammen höchstens
/// <see cref="MaxPositionsPerAccount"/> Stellungen (darüber: Teil-Set, <c>RepertoireTruncated</c> in der Antwort);
/// der Cache ist ein eigener (<see cref="CacheServiceKey"/>) mit <see cref="CacheSizeLimit"/> Stellungen, und ein Set,
/// das er abweist, wird nach einer Verdichtung erneut eingestellt (<see cref="Put"/>).</para>
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
    /// rund 190 Byte je Stellung — 2 Mio. sind knapp 400 MB, vier volle Konten.</summary>
    public const long CacheSizeLimit = 2_000_000;
    /// <summary>Höchstens so viele Stellungen je KONTO, über alle Arten zusammen (rund 95 MB und 10 s Aufbau bei dichtem
    /// PGN): ein Set bekommt, was die gecachten Sets der anderen Arten des Kontos übrig lassen, darüber bleibt es ein
    /// Teil-Set. Ein Viertel des Caches — ein Konto allein kann ihn so nicht so weit füllen, dass er sein eigenes Set
    /// abweist. Ein großes echtes Repertoire hat einige zehntausend.</summary>
    public const int MaxPositionsPerAccount = 500_000;
    /// <summary>Höchstens so viel PGN je Set, gemessen an <see cref="RepertoireFile.FileSize"/> (Bytes, in jedem
    /// Schreibweg gepflegt und nie kleiner als die Zeichenzahl — die Texte bleiben dafür in der Datenbank). Summe der
    /// Dateien in Id-Reihenfolge; eine Datei, die nicht mehr passt, bleibt draußen. Ohne Deckel lud ein Neuaufbau bis
    /// 500 × 1000 × 10 MB; mit Abstand über 16 MB, weil Chessable-Importe ohne Grenze an eine Datei anhängen und
    /// Kommentare mehrsprachig tragen. Gelesen wird in Portionen (<see cref="PgnBatchBytes"/>), nicht alles auf einmal.</summary>
    public const long MaxPgnBytesPerSet = 64L * 1024 * 1024;
    /// <summary>So viel PGN holt eine Abfrage höchstens auf einmal (eine größere Datei kommt allein); ist das Konto beim
    /// Stellungsdeckel angekommen, bleiben die übrigen Portionen in der Datenbank.</summary>
    public const long PgnBatchBytes = 16L * 1024 * 1024;
    /// <summary><c>refresh</c> baut je Konto höchstens einmal in diesem Zeitraum neu; der Knopf „Aktualisieren" der
    /// Erweiterung braucht nicht mehr — Upload, Löschen und Ändern verwerfen den Cache ohnehin.</summary>
    public static readonly TimeSpan RefreshCooldown = TimeSpan.FromMinutes(1);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RepertoireAnalyzeService>? _logger;
    private readonly PositionSetBuildGate _buildGate;

    /// <summary>Sperren und gecachte Arten je Konto — am Cache-Objekt aufgehängt, damit sie genau so weit geteilt sind
    /// wie der Cache selbst (in der API ein Singleton, in Tests je Test ein eigener).</summary>
    private sealed class CacheBook
    {
        public readonly ConcurrentDictionary<int, SemaphoreSlim> Gates = new();
        /// <summary>Welche Arten ein Konto je in den Cache gestellt hat — auch Zahlen außerhalb von
        /// <see cref="RepertoireKind"/> (der Enum-Konverter nimmt sie an). Aufgeräumt wird beim Nachsehen.</summary>
        public readonly ConcurrentDictionary<int, ConcurrentDictionary<int, byte>> Kinds = new();
    }

    private static readonly ConditionalWeakTable<IMemoryCache, CacheBook> Books = new();

    private CacheBook Book => Books.GetValue(_cache, _ => new CacheBook());

    public RepertoireAnalyzeService(AppDbContext db, [FromKeyedServices(CacheServiceKey)] IMemoryCache cache,
        ILogger<RepertoireAnalyzeService>? logger = null, PositionSetBuildGate? buildGate = null)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
        _buildGate = buildGate ?? PositionSetBuildGate.Shared;
    }

    /// <summary>Wie viele Sets DIESE Instanz (= eine Anfrage) gebaut hat — für Tests.</summary>
    internal int SetsBuilt { get; private set; }

    /// <summary>Stellungsdeckel je Konto (<see cref="MaxPositionsPerAccount"/>) — Tests setzen ihn klein.</summary>
    internal int PositionsPerAccount { get; init; } = MaxPositionsPerAccount;

    private static string CacheKey(int userId, RepertoireKind kind) =>
        $"ext:posset:{userId}:{(int)kind}";

    private static string RefreshKey(int userId) => $"ext:posset:refresh:{userId}";

    /// <summary>Cache-Eintrag eines Users invalidieren (z. B. nach PGN-Upload/-Delete) — alle Arten, auch die außerhalb
    /// von <see cref="RepertoireKind"/>, die das Konto gecacht hat.</summary>
    public void Invalidate(int userId)
    {
        foreach (var k in Enum.GetValues<RepertoireKind>())
            _cache.Remove(CacheKey(userId, k));
        if (Book.Kinds.TryGetValue(userId, out var kinds))
            foreach (var k in kinds.Keys)
                _cache.Remove(CacheKey(userId, (RepertoireKind)k));
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
            CachedPositionSet set;
            try { set = await GetPositionSetAsync(userId, kind); }
            // Alle Plätze für Neuaufbauten belegt: diesmal ohne Buchzüge — der Rückblick selbst soll daran nicht scheitern,
            // und eine halbe Auswahl der Arten setzte die Abweichung an die falsche Stelle.
            catch (PositionSetBusyException) { return result; }
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
    /// <exception cref="PositionSetBusyException">Es muss gebaut werden, aber alle Plätze der
    /// <see cref="PositionSetBuildGate"/> bleiben belegt (bei <paramref name="refresh"/> gilt dann der alte Eintrag, falls
    /// es einen gibt).</exception>
    private async Task<CachedPositionSet> GetPositionSetAsync(int userId, RepertoireKind kind, bool refresh = false)
    {
        var key = CacheKey(userId, kind);
        if (!refresh && _cache.TryGetValue<CachedPositionSet>(key, out var cached) && cached != null)
            return cached;

        // Je Konto EIN Neuaufbau zur Zeit: wer wartet, findet danach den Cache gefüllt.
        var gate = Book.Gates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var rebuild = refresh && !_cache.TryGetValue(RefreshKey(userId), out _);
            _cache.TryGetValue(key, out cached);
            if (!rebuild && cached != null) return cached;

            // Im ganzen Prozess nur wenige Neuaufbauten zugleich (Reihenfolge immer Konto → Prozess, also kein Deadlock).
            if (!await _buildGate.TryEnterAsync())
                return cached ?? throw new PositionSetBusyException();
            try
            {
                if (rebuild)
                {
                    _cache.Remove(key);   // zuerst: macht Platz für die Sperrmarke, auch in einem randvollen Cache
                    Put(RefreshKey(userId), true, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = RefreshCooldown,
                        Priority = CacheItemPriority.NeverRemove,   // eine Verdichtung (Put) gibt den Refresh nicht frei
                        Size = 1,
                    });
                }
                var entry = await BuildAsync(userId, kind, AccountBudget(userId, kind));
                Store(userId, kind, entry);
                return entry;
            }
            finally
            {
                _buildGate.Exit();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Wie viele Stellungen das Set der Art <paramref name="kind"/> noch haben darf: der Deckel je Konto minus
    /// die gecachten Sets seiner ANDEREN Arten. Läuft unter der Sperre des Kontos.</summary>
    private int AccountBudget(int userId, RepertoireKind kind)
    {
        long used = 0;
        if (Book.Kinds.TryGetValue(userId, out var kinds))
            foreach (var k in kinds.Keys)
            {
                if (k == (int)kind) continue;
                if (_cache.TryGetValue<CachedPositionSet>(CacheKey(userId, (RepertoireKind)k), out var other) && other != null)
                    used += other.Positions.Count;
                else kinds.TryRemove(k, out _);   // abgelaufen, verdrängt oder verworfen
            }
        return (int)Math.Clamp(PositionsPerAccount - used, 1, PositionsPerAccount);
    }

    /// <summary>Stufen der Verdichtung, wenn der Cache ein Set abweist (Anteil der Einträge, älteste zuerst).</summary>
    private static readonly double[] CompactionSteps = { 0.25, 0.5, 1.0 };

    private void Store(int userId, RepertoireKind kind, CachedPositionSet entry)
    {
        Book.Kinds.GetOrAdd(userId, _ => new ConcurrentDictionary<int, byte>()).TryAdd((int)kind, 0);
        var stored = Put(CacheKey(userId, kind), entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15),
            SlidingExpiration = TimeSpan.FromMinutes(5),
            Size = Math.Max(1, entry.Positions.Count),
        });
        if (!stored)
            _logger?.LogWarning("Positions-Set passt nicht in den Cache: Konto {UserId}, Art {Kind}, {Positions} Stellungen",
                userId, kind, entry.Positions.Count);
    }

    /// <summary>
    /// In den Cache stellen — und nachsehen, ob der Eintrag dort ANGEKOMMEN ist. Ein <see cref="MemoryCache"/> mit
    /// Größengrenze weist einen Eintrag ab, der nicht mehr hineinpasst, und verdichtet selbst nur oberhalb seines
    /// Niedrigwassers (95 %): lag der Füllstand zwischen „Grenze minus Eintrag" und dem Niedrigwasser, blieb das Set
    /// draußen und JEDE weitere Anfrage baute neu, auch ohne <c>refresh</c> (Codereview N8-005, Nacharbeit). Dann wird
    /// hier verdichtet (abgelaufene und am längsten nicht gelesene Einträge zuerst) und erneut eingestellt.
    /// </summary>
    /// <returns><c>false</c> = auch nach voller Verdichtung kein Platz.</returns>
    private bool Put(string key, object value, MemoryCacheEntryOptions options)
    {
        _cache.Set(key, value, options);
        if (_cache.TryGetValue(key, out _)) return true;
        if (_cache is not MemoryCache memory) return false;
        foreach (var share in CompactionSteps)
        {
            memory.Compact(share);
            _cache.Set(key, value, options);
            if (_cache.TryGetValue(key, out _)) return true;
        }
        return false;
    }

    private async Task<CachedPositionSet> BuildAsync(int userId, RepertoireKind kind, int maxPositions)
    {
        SetsBuilt++;
        var files = _db.RepertoireFiles
            .Where(f => f.Repertoire.UserId == userId && f.Repertoire.Kind == kind && f.Repertoire.UseForExtension);

        // Erst die Größen (die Texte bleiben in der Datenbank), dann nur die Dateien, die in den Deckel passen.
        var sizes = await files.OrderBy(f => f.Id).Select(f => new { f.Id, f.FileSize }).ToListAsync();
        var picked = new List<(int Id, long Bytes)>(sizes.Count);
        long bytes = 0;
        foreach (var s in sizes)
        {
            var size = Math.Max(0, s.FileSize);
            if (bytes + size > MaxPgnBytesPerSet) continue;
            bytes += size;
            picked.Add((s.Id, size));
        }

        // Portionsweise lesen und nachspielen — ist der Stellungsdeckel erreicht, bleibt der Rest in der Datenbank.
        var positions = NewPositionSet();
        var capped = false;
        foreach (var batch in Batches(picked))
        {
            var texts = await files.Where(f => batch.Contains(f.Id)).OrderBy(f => f.Id)
                .Select(f => f.PgnContent).ToListAsync();
            capped = AddPositions(positions, texts.Select(RepertoirePgnCleanup.WithoutHidden), maxPositions);   // ohne ausgeblendete Altlasten
            if (capped) break;
        }

        var truncated = capped || picked.Count < sizes.Count;
        if (truncated)
            _logger?.LogWarning(
                "Positions-Set gekappt: Konto {UserId}, Art {Kind}, {Used}/{Files} Dateien, {Positions} Stellungen",
                userId, kind, picked.Count, sizes.Count, positions.Count);
        // FileCount = alle markierten Dateien der Art (wie vor dem Deckel): „verbunden, aber keine Eröffnungen" meldet
        // die Erweiterung nur, wenn es wirklich keine gibt — ein Teil-Set sagt RepertoireTruncated.
        return new CachedPositionSet(positions, sizes.Count, truncated);
    }

    /// <summary>Ids in Portionen bis <see cref="PgnBatchBytes"/> (und höchstens 500 je IN-Liste).</summary>
    private static IEnumerable<List<int>> Batches(List<(int Id, long Bytes)> files)
    {
        var batch = new List<int>();
        long bytes = 0;
        foreach (var (id, size) in files)
        {
            if (batch.Count > 0 && (bytes + size > PgnBatchBytes || batch.Count >= 500))
            {
                yield return batch;
                batch = new List<int>();
                bytes = 0;
            }
            batch.Add(id);
            bytes += size;
        }
        if (batch.Count > 0) yield return batch;
    }

    private sealed record CachedPositionSet(HashSet<string> Positions, int FileCount, bool Truncated);

    // ─── PGN → Position Set ────────────────────────────────────────────────
    // Port der JS-Implementierung in repcheck.user.js: tokenize → parse mit
    // Varianten → walk mit Cancel() statt Reparse.

    /// <param name="capped"><c>true</c> = bei <paramref name="maxPositions"/> Stellungen abgebrochen (Teil-Set).</param>
    internal static HashSet<string> BuildPositionSet(List<string> pgnTexts, out bool capped,
        int maxPositions = MaxPositionsPerAccount)
    {
        var positions = NewPositionSet();
        capped = AddPositions(positions, pgnTexts, maxPositions);
        return positions;
    }

    /// <summary>Ein leeres Set — nur die Ausgangsstellung, sonst landet Zug 1 (Weiss) sofort als Abweichung.</summary>
    private static HashSet<string> NewPositionSet() =>
        new(StringComparer.Ordinal) { NormalizeFen(new ChessBoard().ToFen()) };

    /// <summary>Die Stellungen der PGNs in <paramref name="positions"/> aufnehmen.</summary>
    /// <returns><c>true</c> = bei <paramref name="maxPositions"/> Stellungen abgebrochen, obwohl noch Züge kamen.</returns>
    private static bool AddPositions(HashSet<string> positions, IEnumerable<string> pgnTexts, int maxPositions)
    {
        foreach (var text in pgnTexts)
        {
            try
            {
                foreach (var game in ParsePgn(text))
                {
                    if (positions.Count >= maxPositions) return true;
                    // Unbrauchbare [FEN] → Linie überspringen statt aus der Grundstellung zu spielen
                    // (das würde falsche Stellungen als „im Repertoire" markieren).
                    ChessBoard board;
                    if (game.StartFen == null) board = new ChessBoard();
                    else { try { board = ChessBoard.LoadFromFen(game.StartFen); } catch { continue; } }
                    positions.Add(NormalizeFen(board.ToFen()));   // Startstellung der Linie zählt mit
                    if (WalkMoves(board, game.Moves, positions, maxPositions)) return true;
                }
            }
            catch
            {
                // Einzelne kaputte PGN nicht den ganzen Build kippen lassen.
            }
        }
        return false;
    }

    /// <returns><c>true</c> = beim Deckel abgebrochen.</returns>
    private static bool WalkMoves(ChessBoard board, List<PgnMove> moves, HashSet<string> positions, int maxPositions)
    {
        int movesMade = 0;
        var full = false;
        foreach (var move in moves)
        {
            // Varianten zweigen VOR diesem Zug ab.
            foreach (var variation in move.Variations)
                if (WalkMoves(board, variation, positions, maxPositions)) { full = true; break; }

            if (full || positions.Count >= maxPositions) { full = true; break; }
            bool ok;
            try { ok = board.Move(move.San); }
            catch { ok = false; }
            if (!ok) break;
            movesMade++;
            positions.Add(NormalizeFen(board.ToFen()));
        }
        for (int i = 0; i < movesMade; i++) board.Cancel();
        return full;
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
