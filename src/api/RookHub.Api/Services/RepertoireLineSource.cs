using System.Text.RegularExpressions;
using Chess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// EINE Quelle für „alle lesbaren Repertoire-Linien eines Users, geparst" — inklusive PGN-Parser
/// (mit Varianten und <c>[FEN]</c>-Header) und User-Cache.
///
/// Vorher lag das als privates Innenleben in <see cref="RepertoirePositionLookupService"/>. Mit der
/// Ähnlichkeitssuche (<see cref="RepertoireSimilarityService"/>) gäbe es sonst einen ZWEITEN Parser
/// und eine zweite Cache-Ebene über denselben Daten — mit der Garantie, dass die beiden bei der
/// nächsten Parser-Korrektur auseinanderlaufen (z. B. der <c>[FEN]</c>-Header, dessen Fehlen in
/// v0.340.0 still falsche Stellungen indiziert hat, oder die gameIndex-Zählung, an der Client und
/// Server hängen).
///
/// Cache: per User, 10 min absolut / 5 min sliding; invalidiert von <see cref="RepertoireService"/>
/// (über <see cref="RepertoirePositionLookupService.Invalidate"/>) bei Upload/Delete/Update/Share.
/// </summary>
public class RepertoireLineSource
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public RepertoireLineSource(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    // Sicherheits-Deckel: verhindert, dass ein pathologisch großes Repertoire den Index-Aufbau/-Speicher
    // sprengt. Bei realen Repertoire-Größen nie erreicht.
    private const int MaxGamesPerUser = 20000;
    /// <summary>Obergrenze der je Linie gemeldeten Stellungen (der Walk läuft weiter, meldet aber nichts mehr).</summary>
    public const int MaxPositionsPerLine = 400;

    private static string GamesCacheKey(int userId) => $"rep:posgames:{userId}";

    /// <summary>Geparste Linien eines Users verwerfen (nach PGN-Upload/-Delete/-Update/Freigabe).</summary>
    public void Invalidate(int userId) => _cache.Remove(GamesCacheKey(userId));

    /// <summary>Ein Zug im PGN samt der VOR ihm abzweigenden Varianten.</summary>
    public sealed record PgnMove(string San, List<List<PgnMove>> Variations);

    /// <summary>
    /// Eine Fortsetzung AN einer Stellung: der Zug, mit dem das Repertoire dort tatsächlich
    /// weitergeht — der Hauptzug ODER der erste Zug einer an dieser Stelle abzweigenden Variante.
    ///
    /// Mitgeführt werden Ausgangs- und Zielfeld (0…63, a1 = 0, Index = Reihe*8 + Linie) plus die
    /// Umwandlungsfigur, NICHT nur die SAN-Zeichenkette: <c>Nbd2</c> und <c>Nd2</c> sind derselbe
    /// Zug, nur anders disambiguiert — ein Textvergleich verfehlt solche Paare still (dieselbe
    /// Fehlerklasse, die im August den Linien-Hash getroffen hat).
    /// </summary>
    public readonly record struct LineContinuation(
        string San, int From, int To, char? Promotion, SimPieceType Piece, bool IsMainline);

    /// <summary>Eine besuchte Stellung: FEN, Ply (<c>-1</c> = nur in einer Variante) und die dort
    /// im Repertoire gespielten Fortsetzungen (leer, wenn nicht angefordert).</summary>
    public readonly record struct PositionVisit(string Fen, int Ply, IReadOnlyList<LineContinuation> Continuations);

    private static readonly IReadOnlyList<LineContinuation> NoContinuations = Array.Empty<LineContinuation>();

    /// <summary>Eine geparste Repertoire-Linie samt Herkunft — gemeinsame Basis von Stellungs-Index,
    /// Baummodus und Ähnlichkeitssuche, damit alle dieselben Linien/gameIndex-Zuordnungen sehen.</summary>
    public sealed record RepGame(
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
    public async Task<List<RepGame>> GetGamesAsync(int userId, CancellationToken ct)
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

    // ─── Brett-Walk ───────────────────────────────────────────────────────

    /// <summary>Brett in der Startstellung DIESER Linie. Chessable-Linien starten oft mitten in der
    /// Partie ([FEN]-Header); ohne das säuft der erste Zug ab und die Linie fehlt still (bzw. es
    /// landen FALSCHE Stellungen im Ergebnis). Ist die FEN unbrauchbar, wird die Linie übersprungen
    /// (<c>null</c>) statt aus der Grundstellung gespielt.</summary>
    public static ChessBoard? BoardFor(string? startFen)
    {
        if (string.IsNullOrWhiteSpace(startFen)) return new ChessBoard();
        try { return ChessBoard.LoadFromFen(startFen); }
        catch { return null; }
    }

    /// <summary>
    /// Spielt eine Zugliste (rekursiv über alle Varianten) durch und meldet jede erreichte Stellung
    /// an <paramref name="visit"/>: <c>(fen, ply)</c>, wobei <c>ply</c> auf der Hauptlinie die Anzahl
    /// Halbzüge ist und <c>-1</c>, wenn die Stellung nur in einer Variante vorkommt.
    /// Gibt <paramref name="visit"/> <c>false</c> zurück, werden keine weiteren Stellungen mehr
    /// gemeldet — der Walk läuft aber zu Ende, damit die <c>Cancel()</c>-Bilanz des Bretts stimmt.
    /// Das Brett steht danach wieder auf der Ausgangsstellung.
    /// </summary>
    public static void WalkPositions(ChessBoard board, List<PgnMove> moves, int startPly, Func<string, int, bool> visit)
        => WalkPositions(board, moves, startPly, v => visit(v.Fen, v.Ply), withContinuations: false);

    /// <summary>
    /// Wie oben, meldet aber je Stellung zusätzlich die dort im Repertoire gespielten Fortsetzungen
    /// (<see cref="LineContinuation"/>) — Hauptzug plus die an dieser Stelle abzweigenden Varianten.
    /// <paramref name="withContinuations"/> steuert das bewusst: die Auflösung SAN → from/to kostet
    /// je Stellung einen Zuggenerator-Lauf und wird nur bezahlt, wenn die Anfrage einen Zug enthält.
    /// </summary>
    public static void WalkPositions(ChessBoard board, List<PgnMove> moves, int startPly,
        Func<PositionVisit, bool> visit, bool withContinuations)
        => new Walker(visit, withContinuations).Walk(board, moves, startPly, isMainline: true);

    /// <summary>
    /// Die Fortsetzungen an der Stellung, die das Brett GERADE zeigt: <c>moves[index]</c> (Hauptzug)
    /// plus der jeweils erste Zug der an dieser Stelle abzweigenden Varianten. Nicht auflösbare SANs
    /// (illegal, mehrdeutig, Chessable-Schreibweise <c>a8Q</c>) fallen still heraus — sie würden im
    /// Walk ohnehin die Linie beenden.
    /// </summary>
    public static IReadOnlyList<LineContinuation> ContinuationsAt(ChessBoard board, List<PgnMove> moves, int index)
    {
        if (index < 0 || index >= moves.Count) return NoContinuations;
        var list = new List<LineContinuation>(1 + moves[index].Variations.Count);
        AddContinuation(board, moves[index].San, isMainline: true, list);
        // Varianten hängen an dem Zug, VOR dem sie abzweigen — ihr erster Zug startet also in
        // genau dieser Stellung (dieselbe Zuordnung wie im Walk).
        foreach (var variation in moves[index].Variations)
            if (variation.Count > 0) AddContinuation(board, variation[0].San, isMainline: false, list);
        return list;
    }

    private static void AddContinuation(ChessBoard board, string san, bool isMainline, List<LineContinuation> into)
    {
        Move? parsed;
        try { parsed = board.TryParseFromSan(san, out var m) ? m : null; }
        catch { parsed = null; }
        if (parsed == null) return;
        var type = SimPieceTypeOf(parsed.Piece?.Type.AsChar);
        if (type == null) return;
        into.Add(new LineContinuation(
            San: san,
            From: SquareIndex(parsed.OriginalPosition),
            To: SquareIndex(parsed.NewPosition),
            Promotion: parsed.Promotion == null ? null : char.ToLowerInvariant(parsed.Promotion.Type.AsChar),
            Piece: type.Value,
            IsMainline: isMainline));
    }

    /// <summary>Feldindex wie in <see cref="PositionSimilarity"/>: Reihe*8 + Linie (a1 = 0, h8 = 63).</summary>
    private static int SquareIndex(Position p) => p.Y * 8 + p.X;

    private static SimPieceType? SimPieceTypeOf(char? fenChar) => char.ToLowerInvariant(fenChar ?? ' ') switch
    {
        'p' => SimPieceType.Pawn,
        'n' => SimPieceType.Knight,
        'b' => SimPieceType.Bishop,
        'r' => SimPieceType.Rook,
        'q' => SimPieceType.Queen,
        'k' => SimPieceType.King,
        _ => null,
    };

    private sealed class Walker
    {
        private readonly Func<PositionVisit, bool> _visit;
        private readonly bool _withContinuations;
        private bool _collecting = true;

        public Walker(Func<PositionVisit, bool> visit, bool withContinuations)
        {
            _visit = visit;
            _withContinuations = withContinuations;
        }

        public void Walk(ChessBoard board, List<PgnMove> moves, int startPly, bool isMainline)
        {
            int movesMade = 0;
            int ply = startPly;
            for (int i = 0; i < moves.Count; i++)
            {
                var move = moves[i];
                // Varianten zweigen VOR diesem Zug ab (ply -1 = nur in Variante).
                foreach (var variation in move.Variations)
                    Walk(board, variation, ply, isMainline: false);

                bool ok;
                try { ok = board.Move(move.San); }
                catch { ok = false; }
                if (!ok) break;
                movesMade++;
                ply++;
                if (!_collecting) continue;   // weiterlaufen (Cancel!), aber nichts mehr melden
                var continuations = _withContinuations ? ContinuationsAt(board, moves, i + 1) : NoContinuations;
                _collecting = _visit(new PositionVisit(board.ToFen(), isMainline ? ply : -1, continuations));
            }
            for (int i = 0; i < movesMade; i++) board.Cancel();
        }
    }

    // ─── PGN Parser (header-aware, mit Varianten) ─────────────────────────
    // Eigenständig gehalten (statt RepertoireAnalyzeService-Interna offenzulegen); deckt dieselben
    // Fälle ab wie der Client-Parser `parsePgnText`, plus [White]/[Black]/[FEN]-Header pro Partie.

    private sealed record ParsedGame(string Chapter, string LineName, string? StartFen, List<PgnMove> Moves);

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
