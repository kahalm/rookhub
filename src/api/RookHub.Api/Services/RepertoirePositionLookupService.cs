using System.Text.RegularExpressions;
using Chess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.DTOs;

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
/// Cache: per User, 10 min absolute / 5 min sliding. Invalidiert von <see cref="RepertoireService"/>
/// bei Upload/Delete/Update (analog zum Analyse-Cache).
/// </summary>
public class RepertoirePositionLookupService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public RepertoirePositionLookupService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    // Sicherheits-Deckel: verhindert, dass ein pathologisch großes Repertoire den Index-Aufbau/-Speicher
    // sprengt. Bei realen Repertoire-Größen nie erreicht.
    private const int MaxGamesPerUser = 20000;
    private const int MaxPositionsPerLine = 400;
    /// <summary>Knoten-Obergrenze je Repertoire im Baummodus (danach <c>Truncated</c>).</summary>
    private const int MaxTreeNodes = 1500;
    public const int DefaultTreeDepth = 12;
    public const int MaxTreeDepth = 30;

    private static string CacheKey(int userId) => $"rep:poslookup:{userId}";
    private static string GamesCacheKey(int userId) => $"rep:posgames:{userId}";

    /// <summary>Cache-Einträge eines Users invalidieren (nach PGN-Upload/-Delete/-Update).</summary>
    public void Invalidate(int userId)
    {
        _cache.Remove(CacheKey(userId));
        _cache.Remove(GamesCacheKey(userId));
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

    /// <summary>Eine geparste Repertoire-Linie samt Herkunft — gemeinsame Basis von Index (Listenansicht)
    /// und Baummodus, damit beide dieselben Linien/gameIndex-Zuordnungen sehen.</summary>
    private sealed record RepGame(
        int RepertoireId, string RepertoireName, string Kind, bool Shared,
        string Chapter, string LineName, int GameIndex, string? StartFen, List<PgnMove> Moves);

    private static MemoryCacheEntryOptions CacheOptions() => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
        SlidingExpiration = TimeSpan.FromMinutes(5),
    };

    /// <summary>Lädt + parst alle lesbaren Repertoires des Users (gecacht). Reihenfolge (Repertoire.Id,
    /// dann File.Id) muss mit GetCombinedPgnAsync + parsePgnText übereinstimmen, damit gameIndex zwischen
    /// Server und Client dieselbe Linie meint (gameIndex ist pro Repertoire).</summary>
    private async Task<List<RepGame>> GetGamesAsync(int userId, CancellationToken ct)
    {
        var key = GamesCacheKey(userId);
        if (_cache.TryGetValue<List<RepGame>>(key, out var cached) && cached != null)
            return cached;

        var reps = await RepertoireAccess.ReadableBy(_db, userId)
            .OrderBy(r => r.Id)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Kind,
                Owned = r.UserId == userId,
                Pgns = r.Files.OrderBy(f => f.Id).Select(f => f.PgnContent).ToList(),
            })
            .ToListAsync(ct);

        var result = new List<RepGame>();
        int gamesSeen = 0;
        foreach (var rep in reps)
        {
            int gameIndex = 0;
            var kindName = rep.Kind.ToString();
            foreach (var pgn in rep.Pgns)
            {
                List<ParsedGame> games;
                try { games = ParseGames(pgn); }
                catch { continue; } // kaputte Datei nicht alles kippen lassen
                foreach (var game in games)
                {
                    if (gamesSeen++ > MaxGamesPerUser) break;
                    result.Add(new RepGame(rep.Id, rep.Name, kindName, !rep.Owned,
                        game.Chapter, game.LineName, gameIndex, game.StartFen, game.Moves));
                    gameIndex++;
                }
            }
        }

        _cache.Set(key, result, CacheOptions());
        return result;
    }

    private async Task<Dictionary<string, List<Occurrence>>> GetIndexAsync(int userId, CancellationToken ct)
    {
        var key = CacheKey(userId);
        if (_cache.TryGetValue<Dictionary<string, List<Occurrence>>>(key, out var cached) && cached != null)
            return cached;

        var games = await GetGamesAsync(userId, ct);
        var index = new Dictionary<string, List<Occurrence>>(StringComparer.Ordinal);
        foreach (var game in games)
            IndexGame(index, game);

        _cache.Set(key, index, CacheOptions());
        return index;
    }

    /// <summary>Brett in der Startstellung DIESER Linie. Chessable-Linien starten oft mitten in der
    /// Partie ([FEN]-Header); ohne das säuft der erste Zug ab und die Linie fehlt im Index. Ist die
    /// FEN unbrauchbar, wird die Linie übersprungen (null) statt aus der Grundstellung gespielt —
    /// sonst landen FALSCHE Stellungen im Index.</summary>
    private static ChessBoard? BoardFor(string? startFen)
    {
        if (string.IsNullOrWhiteSpace(startFen)) return new ChessBoard();
        try { return ChessBoard.LoadFromFen(startFen); }
        catch { return null; }
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
        try { WalkLine(board, game.Moves, perLine, startPly: 0, isMainline: true); }
        catch { /* defensiv: eine einzelne Linie nie den Index kippen lassen */ }

        foreach (var (norm, ply) in perLine)
        {
            var list = index.TryGetValue(norm, out var l) ? l : (index[norm] = new List<Occurrence>());
            list.Add(new Occurrence(game.RepertoireId, game.RepertoireName, game.Kind, game.Shared,
                game.Chapter, game.LineName, game.GameIndex, ply));
        }
    }

    private static void WalkLine(ChessBoard board, List<PgnMove> moves, Dictionary<string, int> perLine, int startPly, bool isMainline)
    {
        int movesMade = 0;
        int ply = startPly;
        foreach (var move in moves)
        {
            // Varianten zweigen VOR diesem Zug ab (ply -1 = nur in Variante).
            foreach (var variation in move.Variations)
                WalkLine(board, variation, perLine, ply, isMainline: false);

            bool ok;
            try { ok = board.Move(move.San); }
            catch { ok = false; }
            if (!ok) break;
            movesMade++;
            ply++;
            if (perLine.Count >= MaxPositionsPerLine) continue; // weiterlaufen (Cancel!), aber nichts mehr merken
            var norm = NormalizeKey(board.ToFen());
            var thisPly = isMainline ? ply : -1;
            if (!perLine.TryGetValue(norm, out var existing))
                perLine[norm] = thisPly;
            else if (existing < 0 && thisPly >= 0)
                perLine[norm] = thisPly;                     // echten Hauptlinien-Ply gegenüber -1 bevorzugen
            else if (existing >= 0 && thisPly >= 0 && thisPly < existing)
                perLine[norm] = thisPly;                     // frühesten Ply bevorzugen
        }
        for (int i = 0; i < movesMade; i++) board.Cancel();
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
        var games = await GetGamesAsync(userId, ct);
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

    // ─── PGN Parser (header-aware, mit Varianten) ─────────────────────────
    // Eigenständig gehalten (statt RepertoireAnalyzeService-Interna offenzulegen); deckt dieselben
    // Fälle ab wie der Client-Parser `parsePgnText`, plus [White]/[Black]-Header pro Partie.

    private sealed record ParsedGame(string Chapter, string LineName, string? StartFen, List<PgnMove> Moves);
    private sealed record PgnMove(string San, List<List<PgnMove>> Variations);

    private static readonly Regex CommentRegex = new(@"\{[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex LineCommentRegex = new(@";[^\n]*", RegexOptions.Compiled);
    private static readonly Regex NagRegex = new(@"\$\d+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex MoveNumberRegex = new(@"^\d+\.+$", RegexOptions.Compiled);
    private static readonly Regex EventHeaderSplit = new(@"(?=\[Event\s)", RegexOptions.Compiled);
    private static readonly Regex WhiteHeaderRegex = new(@"^\[White\s+""([^""]*)""\]", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex BlackHeaderRegex = new(@"^\[Black\s+""([^""]*)""\]", RegexOptions.Compiled | RegexOptions.Multiline);
    // Chessable-Importe (via piratechess) tragen je Linie die Startstellung der Variante im
    // [FEN]-Header — ohne den beginnt der Walk in der Grundstellung, der erste Zug ist dort
    // illegal und die ganze Linie fehlt still im Index (bzw. indiziert falsche Stellungen).
    private static readonly Regex FenHeaderRegex = new(@"^\[FEN\s+""([^""]*)""\]", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly HashSet<string> ResultTokens = new() { "1-0", "0-1", "1/2-1/2", "*" };

    private static List<ParsedGame> ParseGames(string text)
    {
        var games = new List<ParsedGame>();
        if (string.IsNullOrWhiteSpace(text)) return games;
        foreach (var section in EventHeaderSplit.Split(text))
        {
            if (string.IsNullOrWhiteSpace(section)) continue;
            var movetext = ExtractMovetext(section);
            var moves = movetext.Length == 0 ? new List<PgnMove>() : ParseMoveTokens(Tokenize(movetext), 0).Moves;
            var white = WhiteHeaderRegex.Match(section);
            var black = BlackHeaderRegex.Match(section);
            var fen = FenHeaderRegex.Match(section);
            var lineName = white.Success ? white.Groups[1].Value.Trim() : "";
            var chapter = black.Success ? black.Groups[1].Value.Trim() : "";
            var startFen = fen.Success ? fen.Groups[1].Value.Trim() : null;
            // Auch zug-lose Partien behalten (könnten Kapitel-Intros sein) — sie tragen aber keine
            // Positionen bei und würden nie matchen; wir nehmen sie nur mit, damit gameIndex mit dem
            // Client-Parser übereinstimmt.
            games.Add(new ParsedGame(chapter, lineName, string.IsNullOrWhiteSpace(startFen) ? null : startFen, moves));
        }
        return games;
    }

    private static string ExtractMovetext(string section)
    {
        var lines = section.Split('\n');
        var sb = new System.Text.StringBuilder();
        bool pastHeaders = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']') && !pastHeaders) continue;
            if (line.Length == 0 && !pastHeaders) { pastHeaders = true; continue; }
            if (pastHeaders || !line.StartsWith('['))
            {
                sb.Append(line).Append(' ');
                pastHeaders = true;
            }
        }
        return sb.ToString().Trim();
    }

    private static List<string> Tokenize(string movetext)
    {
        movetext = CommentRegex.Replace(movetext, " ");
        movetext = LineCommentRegex.Replace(movetext, " ");
        movetext = NagRegex.Replace(movetext, " ");
        movetext = WhitespaceRegex.Replace(movetext, " ").Trim();

        var tokens = new List<string>();
        int i = 0;
        while (i < movetext.Length)
        {
            char c = movetext[i];
            if (c == '(') { tokens.Add("("); i++; }
            else if (c == ')') { tokens.Add(")"); i++; }
            else if (c == ' ') { i++; }
            else
            {
                int j = i;
                while (j < movetext.Length && movetext[j] != ' ' && movetext[j] != '(' && movetext[j] != ')') j++;
                tokens.Add(movetext.Substring(i, j - i));
                i = j;
            }
        }
        return tokens;
    }

    private static (List<PgnMove> Moves, int EndPos) ParseMoveTokens(List<string> tokens, int pos)
    {
        var moves = new List<PgnMove>();
        while (pos < tokens.Count)
        {
            var token = tokens[pos];
            if (token == ")") return (moves, pos);
            if (token == "(")
            {
                pos++;
                var (varMoves, endPos) = ParseMoveTokens(tokens, pos);
                pos = endPos + 1;
                if (moves.Count > 0) moves[^1].Variations.Add(varMoves);
                continue;
            }
            if (IsMoveToken(token))
            {
                var clean = token.TrimEnd('!', '?', '+', '#');
                if (clean.Length > 0)
                    moves.Add(new PgnMove(clean, new List<List<PgnMove>>()));
            }
            pos++;
        }
        return (moves, pos);
    }

    private static bool IsMoveToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token == "(" || token == ")") return false;
        if (MoveNumberRegex.IsMatch(token)) return false;
        if (ResultTokens.Contains(token)) return false;
        char c = token[0];
        return (c >= 'a' && c <= 'h') || c == 'K' || c == 'Q' || c == 'R' || c == 'B' || c == 'N' || c == 'O';
    }
}
