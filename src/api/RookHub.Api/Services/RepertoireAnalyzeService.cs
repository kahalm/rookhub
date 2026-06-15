using System.Text.RegularExpressions;
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

        var positions = BuildPositionSet(pgnTexts);
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
                    var board = new ChessBoard();
                    WalkMoves(board, game, positions);
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

    // ─── PGN Parser (mit Varianten) ────────────────────────────────────────
    // Bewusst klein gehalten; deckt die Faelle ab, die `parsePgnText` im Client behandelt.

    private sealed record PgnMove(string San, List<List<PgnMove>> Variations);

    private static readonly Regex CommentRegex = new(@"\{[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex LineCommentRegex = new(@";[^\n]*", RegexOptions.Compiled);
    private static readonly Regex NagRegex = new(@"\$\d+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex MoveNumberRegex = new(@"^\d+\.+$", RegexOptions.Compiled);
    private static readonly Regex EventHeaderSplit = new(@"(?=\[Event\s)", RegexOptions.Compiled);
    private static readonly HashSet<string> ResultTokens = new() { "1-0", "0-1", "1/2-1/2", "*" };

    private static List<List<PgnMove>> ParsePgn(string text)
    {
        var games = new List<List<PgnMove>>();
        foreach (var section in EventHeaderSplit.Split(text))
        {
            var movetext = ExtractMovetext(section);
            if (string.IsNullOrWhiteSpace(movetext)) continue;
            var tokens = Tokenize(movetext);
            var (moves, _) = ParseMoveTokens(tokens, 0);
            if (moves.Count > 0) games.Add(moves);
        }
        if (games.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            var tokens = Tokenize(text);
            var (moves, _) = ParseMoveTokens(tokens, 0);
            if (moves.Count > 0) games.Add(moves);
        }
        return games;
    }

    private static string ExtractMovetext(string section)
    {
        // Headers sind Zeilen, die mit '[' beginnen und mit ']' enden; danach kommt der Movetext.
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
                pos++; // skip '('
                var (varMoves, endPos) = ParseMoveTokens(tokens, pos);
                pos = endPos + 1; // skip ')'
                if (moves.Count > 0) moves[^1].Variations.Add(varMoves);
                continue;
            }
            if (IsMoveToken(token))
            {
                // Suffix-Annotationen am SAN entfernen, damit chess-lib parsen kann.
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
