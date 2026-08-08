using Chess;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.DTOs;
using static RookHub.Api.Services.RepertoireLineSource;

namespace RookHub.Api.Services;

/// <summary>
/// Findet zu einer Stellung (FEN) alle EIGENEN Repertoire-Linien des Users, in denen sie vorkommt
/// (Repertoire → Kapitel → Linie). Genutzt vom „In welchen Repertoires?"-Knopf im Analyse-/Recap-
/// Modus.
///
/// Ansatz analog <see cref="RepertoireAnalyzeService"/>: pro User wird ein gecachter Index
/// <c>normalisierter FEN → Linien-Treffer</c> aufgebaut (alle in den Repertoire-PGNs erreichbaren
/// Stellungen, Varianten inklusive → Zugumstellungen werden erkannt). FEN-Normalisierung
/// (Brett + Seite + Rochade + en-passant) wird aus <see cref="RepertoireAnalyzeService.NormalizeFen"/>
/// wiederverwendet, damit Matching und Extension-Analyse konsistent sind.
///
/// Die geparsten Linien selbst kommen aus <see cref="RepertoireLineSource"/> (gemeinsam mit
/// <see cref="RepertoireSimilarityService"/>).
///
/// Cache: per User, 10 min absolute / 5 min sliding. Invalidiert von <see cref="RepertoireService"/>
/// bei Upload/Delete/Update (analog zum Analyse-Cache).
/// </summary>
public class RepertoirePositionLookupService
{
    private readonly RepertoireLineSource _lines;
    private readonly IMemoryCache _cache;

    public RepertoirePositionLookupService(RepertoireLineSource lines, IMemoryCache cache)
    {
        _lines = lines;
        _cache = cache;
    }

    /// <summary>Knoten-Obergrenze je Repertoire im Baummodus (danach <c>Truncated</c>).</summary>
    private const int MaxTreeNodes = 1500;
    public const int DefaultTreeDepth = 12;
    public const int MaxTreeDepth = 30;

    private static string CacheKey(int userId) => $"rep:poslookup:{userId}";

    /// <summary>Cache-Einträge eines Users invalidieren (nach PGN-Upload/-Delete/-Update).</summary>
    public void Invalidate(int userId)
    {
        _cache.Remove(CacheKey(userId));
        _lines.Invalidate(userId);
    }

    /// <summary>
    /// Match-Schlüssel = Brett + Seite + Rochaderechte (Halbzug-/Vollzugzähler UND en-passant-Feld
    /// weggelassen). Das en-passant-Feld wird bewusst ignoriert: die Client-Stellung stammt aus chess.js,
    /// der Index aus Gera.Chess — beide setzen das ep-Feld nach einem Doppelschritt unterschiedlich
    /// (strikt „nur wenn schlagbar" vs. immer), und für die Frage „kommt diese Stellung im Repertoire
    /// vor?" ist die ep-Möglichkeit ohnehin irrelevant. So matchen auch Zugumstellungen, bei denen die
    /// eine Linie mit einem Doppelschritt endet und die andere nicht.
    /// </summary>
    private static string NormalizeKey(string fen)
    {
        var parts = fen.Split(' ');
        return parts.Length >= 3 ? string.Join(' ', parts.Take(3)) : fen;
    }

    public async Task<PositionLookupResultDto> LookupAsync(int userId, string fen, CancellationToken ct)
    {
        var index = await GetIndexAsync(userId, ct);
        var result = new PositionLookupResultDto();
        var norm = NormalizeKey(fen);
        if (!index.TryGetValue(norm, out var occurrences) || occurrences.Count == 0)
            return result;

        foreach (var repGroup in occurrences
                     .GroupBy(o => o.RepertoireId)
                     .OrderBy(g => g.First().RepertoireName, StringComparer.OrdinalIgnoreCase))
        {
            var first = repGroup.First();
            var rep = new RepertoirePositionMatchDto
            {
                RepertoireId = first.RepertoireId,
                RepertoireName = first.RepertoireName,
                Kind = first.Kind,
                Shared = first.Shared,
            };
            // Ein Eintrag pro Linie (gameIndex); niedrigsten echten Ply bevorzugen.
            foreach (var lineGroup in repGroup.GroupBy(o => o.GameIndex).OrderBy(g => g.Key))
            {
                var best = lineGroup.OrderBy(o => o.Ply < 0 ? int.MaxValue : o.Ply).First();
                rep.Lines.Add(new RepertoireLineMatchDto
                {
                    Chapter = best.Chapter,
                    LineName = best.LineName,
                    GameIndex = best.GameIndex,
                    Ply = best.Ply,
                });
            }
            result.Repertoires.Add(rep);
        }
        return result;
    }

    private sealed record Occurrence(
        int RepertoireId, string RepertoireName, string Kind, bool Shared,
        string Chapter, string LineName, int GameIndex, int Ply);

    private static MemoryCacheEntryOptions CacheOptions() => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
        SlidingExpiration = TimeSpan.FromMinutes(5),
    };

    private async Task<Dictionary<string, List<Occurrence>>> GetIndexAsync(int userId, CancellationToken ct)
    {
        var key = CacheKey(userId);
        if (_cache.TryGetValue<Dictionary<string, List<Occurrence>>>(key, out var cached) && cached != null)
            return cached;

        var games = await _lines.GetGamesAsync(userId, ct);
        var index = new Dictionary<string, List<Occurrence>>(StringComparer.Ordinal);
        foreach (var game in games)
            IndexGame(index, game);

        _cache.Set(key, index, CacheOptions());
        return index;
    }

    private static void IndexGame(Dictionary<string, List<Occurrence>> index, RepGame game)
    {
        // Pro Linie zuerst die beste (kleinste echte) Ply je Stellung sammeln, dann in den Index legen.
        var perLine = new Dictionary<string, int>(StringComparer.Ordinal);
        var board = BoardFor(game.StartFen);
        if (board == null) return;
        // Startstellung nur bei eigener [FEN] mitindizieren: dort ist sie eine echte Repertoire-
        // Stellung (Endspiel-/Mittelspiel-Linie beginnt genau dort). Bei Linien aus der
        // Grundstellung wäre sie reines Rauschen — jedes Repertoire würde auf sie matchen.
        if (game.StartFen != null) perLine[NormalizeKey(board.ToFen())] = 0;
        try
        {
            WalkPositions(board, game.Moves, startPly: 0, (fen, ply) =>
            {
                var norm = NormalizeKey(fen);
                if (!perLine.TryGetValue(norm, out var existing))
                    perLine[norm] = ply;
                else if (existing < 0 && ply >= 0)
                    perLine[norm] = ply;                     // echten Hauptlinien-Ply gegenüber -1 bevorzugen
                else if (existing >= 0 && ply >= 0 && ply < existing)
                    perLine[norm] = ply;                     // frühesten Ply bevorzugen
                return perLine.Count < MaxPositionsPerLine;
            });
        }
        catch { /* defensiv: eine einzelne Linie nie den Index kippen lassen */ }

        foreach (var (norm, ply) in perLine)
        {
            var list = index.TryGetValue(norm, out var l) ? l : (index[norm] = new List<Occurrence>());
            list.Add(new Occurrence(game.RepertoireId, game.RepertoireName, game.Kind, game.Shared,
                game.Chapter, game.LineName, game.GameIndex, ply));
        }
    }

    // ─── Baummodus ────────────────────────────────────────────────────────
    // Dieselben Treffer wie die Listenansicht, nur andersherum aufgeschnitten: statt „welche Linien
    // enthalten die Stellung?" die Frage „wie geht mein Repertoire ab hier weiter?". Alle Vorkommen
    // (Hauptlinien UND Varianten, auch Zugumstellungen) werden zu EINEM Zugbaum je Repertoire
    // zusammengeführt — Varianten sind hier der halbe Sinn der Sache, deshalb läuft der Baum
    // serverseitig: der Client-Parser (`parsePgnText`) wirft Varianten weg.

    /// <summary>Zugbaum ab <paramref name="fen"/> je Repertoire (zusammengeführt über alle Linien).</summary>
    /// <param name="maxDepth">Maximale Halbzug-Tiefe ab der Stellung (1…<see cref="MaxTreeDepth"/>).</param>
    public async Task<PositionTreeResultDto> TreeAsync(int userId, string fen, int maxDepth, CancellationToken ct)
    {
        maxDepth = Math.Clamp(maxDepth <= 0 ? DefaultTreeDepth : maxDepth, 1, MaxTreeDepth);
        var games = await _lines.GetGamesAsync(userId, ct);
        var target = NormalizeKey(fen);
        var result = new PositionTreeResultDto();

        foreach (var repGroup in games
                     .GroupBy(g => g.RepertoireId)
                     .OrderBy(g => g.First().RepertoireName, StringComparer.OrdinalIgnoreCase))
        {
            var builder = new TreeBuilder();
            int occurrences = 0;
            foreach (var game in repGroup)
            {
                var board = BoardFor(game.StartFen);
                if (board == null) continue;   // unbrauchbare [FEN] → Linie überspringen
                // Die Startstellung DIESER Linie kann der Treffer sein (Repertoire-/Varianten-Anfang).
                if (NormalizeKey(board.ToFen()) == target)
                {
                    occurrences++;
                    builder.Collect(game.Moves, 0, builder.Root, maxDepth, game);
                }
                try { occurrences += WalkForTree(board, game.Moves, target, builder, game, maxDepth); }
                catch { /* eine kaputte Linie darf den Baum nicht kippen */ }
            }
            if (occurrences == 0) continue;

            var first = repGroup.First();
            result.Repertoires.Add(new RepertoirePositionTreeDto
            {
                RepertoireId = first.RepertoireId,
                RepertoireName = first.RepertoireName,
                Kind = first.Kind,
                Shared = first.Shared,
                Occurrences = occurrences,
                Truncated = builder.Truncated,
                Moves = builder.Root.Children.Select(ToDto).ToList(),
            });
        }
        return result;
    }

    /// <summary>Sucht in einer Zugliste (rekursiv über Varianten) alle Vorkommen der Zielstellung und
    /// hängt die jeweilige Fortsetzung an den Baum. Rückgabe: Anzahl gefundener Vorkommen.</summary>
    private static int WalkForTree(ChessBoard board, List<PgnMove> moves, string target,
        TreeBuilder builder, RepGame game, int maxDepth)
    {
        int hits = 0;
        int movesMade = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            var move = moves[i];
            // Varianten zweigen VOR diesem Zug ab — dort kann die Stellung ebenfalls vorkommen.
            foreach (var variation in move.Variations)
                hits += WalkForTree(board, variation, target, builder, game, maxDepth);

            bool ok;
            try { ok = board.Move(move.San); }
            catch { ok = false; }
            if (!ok) break;
            movesMade++;

            if (NormalizeKey(board.ToFen()) == target)
            {
                hits++;
                builder.Collect(moves, i + 1, builder.Root, maxDepth, game);
            }
        }
        for (int i = 0; i < movesMade; i++) board.Cancel();
        return hits;
    }

    private static PositionTreeNodeDto ToDto(TreeNode node) => new()
    {
        San = node.San,
        Count = node.Count,
        IsEnd = node.IsEnd,
        // Kapitel/Linie nur, wenn ab hier genau EINE Linie durchläuft — sonst wäre „Trainieren" mehrdeutig.
        Chapter = node.MultipleLines ? null : node.Chapter,
        LineName = node.MultipleLines ? null : node.LineName,
        GameIndex = node.MultipleLines ? null : node.GameIndex,
        Children = node.Children.Select(ToDto).ToList(),
    };

    private sealed class TreeNode
    {
        public string San { get; init; } = string.Empty;
        public int Count { get; set; }
        public bool IsEnd { get; set; }
        public List<TreeNode> Children { get; } = new();          // Einfüge-Reihenfolge: Hauptlinie zuerst
        private readonly Dictionary<string, TreeNode> _bySan = new(StringComparer.Ordinal);

        public string Chapter { get; private set; } = string.Empty;
        public string LineName { get; private set; } = string.Empty;
        public int GameIndex { get; private set; } = -1;
        public bool MultipleLines { get; private set; }

        public TreeNode? Find(string san) => _bySan.TryGetValue(san, out var n) ? n : null;

        public TreeNode Add(string san)
        {
            var child = new TreeNode { San = san };
            _bySan[san] = child;
            Children.Add(child);
            return child;
        }

        /// <summary>Merkt sich die Herkunfts-Linie; ab der zweiten verschiedenen wird der Knoten mehrdeutig.</summary>
        public void Note(int gameIndex, string chapter, string lineName)
        {
            if (GameIndex < 0) { GameIndex = gameIndex; Chapter = chapter; LineName = lineName; }
            else if (GameIndex != gameIndex) MultipleLines = true;
        }
    }

    private sealed class TreeBuilder
    {
        public TreeNode Root { get; } = new();
        public bool Truncated { get; private set; }
        private int _nodes;

        /// <summary>Hängt die Fortsetzung ab <paramref name="startIdx"/> (inkl. der dort abzweigenden
        /// Varianten) unter <paramref name="node"/>. Rein strukturell — die Legalität hat der
        /// Suchlauf bereits geprüft.</summary>
        public void Collect(List<PgnMove> moves, int startIdx, TreeNode node, int depthLeft, RepGame game)
        {
            if (startIdx >= moves.Count) { node.IsEnd = true; return; }   // Linie endet hier
            if (depthLeft <= 0) return;

            var move = moves[startIdx];
            var child = node.Find(move.San);
            if (child == null)
            {
                if (_nodes >= MaxTreeNodes) { Truncated = true; return; }
                _nodes++;
                child = node.Add(move.San);
            }
            child.Count++;
            child.Note(game.GameIndex, game.Chapter, game.LineName);
            Collect(moves, startIdx + 1, child, depthLeft - 1, game);

            // Alternativen an derselben Stelle hängen unter den GLEICHEN Elternknoten (nach der
            // Hauptfortsetzung, damit die Reihenfolge im Baum der PGN-Lesart entspricht).
            foreach (var variation in move.Variations)
                Collect(variation, 0, node, depthLeft, game);
        }
    }
}
