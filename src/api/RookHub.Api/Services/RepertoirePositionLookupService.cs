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

    private static string CacheKey(int userId) => $"rep:poslookup:{userId}";

    /// <summary>Cache-Eintrag eines Users invalidieren (nach PGN-Upload/-Delete/-Update).</summary>
    public void Invalidate(int userId) => _cache.Remove(CacheKey(userId));

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

    private async Task<Dictionary<string, List<Occurrence>>> GetIndexAsync(int userId, CancellationToken ct)
    {
        var key = CacheKey(userId);
        if (_cache.TryGetValue<Dictionary<string, List<Occurrence>>>(key, out var cached) && cached != null)
            return cached;

        // Eigene UND mit dem User geteilte Repertoires. Reihenfolge (Repertoire.Id, dann File.Id) muss
        // mit GetCombinedPgnAsync + parsePgnText übereinstimmen, damit gameIndex zwischen Server und
        // Client dieselbe Linie meint (gameIndex ist pro Repertoire, das Mischen owned/geteilt ist egal).
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

        var index = new Dictionary<string, List<Occurrence>>(StringComparer.Ordinal);
        int gamesSeen = 0;
        foreach (var rep in reps)
        {
            int gameIndex = 0;
            var kindName = rep.Kind.ToString();
            foreach (var pgn in rep.Pgns)
            {
                List<ParsedGame> games;
                try { games = ParseGames(pgn); }
                catch { continue; } // kaputte Datei nicht den ganzen Index kippen lassen
                foreach (var game in games)
                {
                    if (gamesSeen++ > MaxGamesPerUser) break;
                    IndexGame(index, rep.Id, rep.Name, kindName, !rep.Owned, game, gameIndex);
                    gameIndex++;
                }
            }
        }

        _cache.Set(key, index, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
            SlidingExpiration = TimeSpan.FromMinutes(5),
        });
        return index;
    }

    private static void IndexGame(Dictionary<string, List<Occurrence>> index,
        int repId, string repName, string kind, bool shared, ParsedGame game, int gameIndex)
    {
        // Pro Linie zuerst die beste (kleinste echte) Ply je Stellung sammeln, dann in den Index legen.
        var perLine = new Dictionary<string, int>(StringComparer.Ordinal);
        var board = new ChessBoard();
        try { WalkLine(board, game.Moves, perLine, startPly: 0, isMainline: true); }
        catch { /* defensiv: eine einzelne Linie nie den Index kippen lassen */ }

        foreach (var (norm, ply) in perLine)
        {
            var list = index.TryGetValue(norm, out var l) ? l : (index[norm] = new List<Occurrence>());
            list.Add(new Occurrence(repId, repName, kind, shared, game.Chapter, game.LineName, gameIndex, ply));
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

    // ─── PGN Parser (header-aware, mit Varianten) ─────────────────────────
    // Eigenständig gehalten (statt RepertoireAnalyzeService-Interna offenzulegen); deckt dieselben
    // Fälle ab wie der Client-Parser `parsePgnText`, plus [White]/[Black]-Header pro Partie.

    private sealed record ParsedGame(string Chapter, string LineName, List<PgnMove> Moves);
    private sealed record PgnMove(string San, List<List<PgnMove>> Variations);

    private static readonly Regex CommentRegex = new(@"\{[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex LineCommentRegex = new(@";[^\n]*", RegexOptions.Compiled);
    private static readonly Regex NagRegex = new(@"\$\d+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex MoveNumberRegex = new(@"^\d+\.+$", RegexOptions.Compiled);
    private static readonly Regex EventHeaderSplit = new(@"(?=\[Event\s)", RegexOptions.Compiled);
    private static readonly Regex WhiteHeaderRegex = new(@"^\[White\s+""([^""]*)""\]", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex BlackHeaderRegex = new(@"^\[Black\s+""([^""]*)""\]", RegexOptions.Compiled | RegexOptions.Multiline);
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
            var lineName = white.Success ? white.Groups[1].Value.Trim() : "";
            var chapter = black.Success ? black.Groups[1].Value.Trim() : "";
            // Auch zug-lose Partien behalten (könnten Kapitel-Intros sein) — sie tragen aber keine
            // Positionen bei und würden nie matchen; wir nehmen sie nur mit, damit gameIndex mit dem
            // Client-Parser übereinstimmt.
            games.Add(new ParsedGame(chapter, lineName, moves));
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
