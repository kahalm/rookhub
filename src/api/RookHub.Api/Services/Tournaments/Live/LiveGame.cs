using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Chess;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>Ein Spieler einer übertragenen Partie (aus den PGN-Kopfzeilen) samt seiner Uhr nach seinem letzten Zug.</summary>
public sealed record LivePlayer(string Name, string? Title, int? Elo, string? FideId, string? Team, int? ClockSeconds);

/// <summary>Bewertung aus Weiß-Sicht: Centipawns ODER Matt in n (negativ = Schwarz setzt matt). <c>Source</c>
/// „broadcast" (<c>[%eval]</c> im Strom), „cloud" (Lichess Cloud-Eval) oder „engine". <c>Best</c> (1.030.0) = bester Zug dieser
/// Bewertung als UCI (erster Zug der Hauptvariante; <c>[%eval]</c> kennt keinen → <c>null</c>) — für „Besten Zug zeigen“.
/// <c>SecondCp</c>/<c>SecondMate</c> (1.033.0) = Bewertung der ZWEITEN Linie (MultiPV 2, ebenfalls Weiß-Sicht), <c>OnlyMove</c> =
/// „Nur ein Zug“ (<see cref="LiveOnlyMove"/>, nur <c>true</c> oder weggelassen). Alle drei fehlen im JSON, wenn unbekannt.</summary>
public sealed record LiveEval(int? Cp, int? Mate, string Source, int? Depth = null, string? Best = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SecondCp = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SecondMate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? OnlyMove = null)
{
    /// <summary>Ist die zweite Linie bekannt?</summary>
    [JsonIgnore]
    public bool HasSecond => SecondCp is not null || SecondMate is not null;

    /// <summary>Bewertung der zweiten Linie (Index 1) aus einem <c>pvs</c>-Feld: <c>(cp, mate)</c>, beide <c>null</c> = fehlt
    /// (nur eine Linie, z. B. nur ein legaler Zug oder die Cloud kennt nur eine).</summary>
    public static (int? Cp, int? Mate) SecondOf(System.Text.Json.JsonElement pvs)
    {
        if (pvs.ValueKind != System.Text.Json.JsonValueKind.Array || pvs.GetArrayLength() < 2) return (null, null);
        var pv = pvs[1];
        if (pv.ValueKind != System.Text.Json.JsonValueKind.Object) return (null, null);
        if (pv.TryGetProperty("mate", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.Number) return (null, m.GetInt32());
        if (pv.TryGetProperty("cp", out var cp) && cp.ValueKind == System.Text.Json.JsonValueKind.Number) return (cp.GetInt32(), null);
        return (null, null);
    }

    private static readonly Regex Uci = new("^[a-h][1-8][a-h][1-8][qrbn]?$", RegexOptions.Compiled);

    /// <summary>Erster Zug einer Hauptvariante: <c>"moves"</c> als Text („e2e4 e7e5 …“, Cloud) oder Liste (Broker); nur gültiges UCI.</summary>
    public static string? FirstMove(System.Text.Json.JsonElement pv)
    {
        if (!pv.TryGetProperty("moves", out var mv)) return null;
        var first = mv.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => mv.GetString()?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
            System.Text.Json.JsonValueKind.Array when mv.GetArrayLength() > 0 && mv[0].ValueKind == System.Text.Json.JsonValueKind.String => mv[0].GetString(),
            _ => null,
        };
        return first is not null && Uci.IsMatch(first) ? first : null;
    }
}

/// <summary>
/// Stand einer Partie der Lichess-Übertragung: Stellung nach dem letzten Zug, letzter Zug als UCI, Uhren aus <c>[%clk]</c>,
/// Ergebnis. <see cref="Hash"/> unterscheidet „wirklich geändert" von einer Wiederholung im Strom (Lichess schickt nach dem
/// Verbinden alle Partien noch einmal). <see cref="Seen"/> (1.035.1, nur im Server) = Laufnummer des Feeds, wann Lichess die Partie
/// zuletzt geschickt hat (Strom oder Runden-Export) — größer = jünger; der <see cref="LiveBoardAssembler"/> nimmt bei zwei Partien
/// auf demselben Brettplatz die jüngere.
/// </summary>
public sealed record LiveGame(
    string Id, string? BoardTag, LivePlayer White, LivePlayer Black, string Fen, string? LastMove, int Plies,
    string Result, LiveEval? Eval, string Hash,
    [property: JsonIgnore] long Seen = 0)
{
    public bool Finished => Result is "1-0" or "0-1" or "1/2-1/2";
    public bool WhiteToMove => FenFields.WhiteToMove(Fen);
}

/// <summary>Liest EINE Partie aus dem Übertragungs-PGN (rein, getestet).</summary>
public static partial class LiveGameParser
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    /// <summary>Die Partie aus einem PGN-Block; <c>null</c>, wenn er keine Kopfzeilen hat.</summary>
    public static LiveGame? Parse(string pgn)
    {
        var (headers, moveText) = PgnParser.SplitGames(pgn).FirstOrDefault();
        if (headers is null || headers.Count == 0) return null;
        string? H(string k) => headers.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) && v != "?" ? v.Trim() : null;

        var (sans, clocks, evals) = Scan(moveText ?? "");
        var start = H("FEN") ?? StartFen;
        // Fertige Partie mit Signal-Königszug des Siegers am Ende (DGT-Brett, 1.022.0, <see cref="Games.SignalKingMove"/>): das Brett
        // zeigt die Stellung davor — sonst stünde der König des Siegers mitten im Matt.
        if (H("Result") is "1-0" or "0-1" && Games.SignalKingMove.Detect(sans, H("Result"), start == StartFen ? null : start) is not null)
            sans.RemoveAt(sans.Count - 1);
        string fen = start;
        string? last = null;
        var plies = 0;
        try
        {
            var board = start == StartFen ? new ChessBoard() : ChessBoard.LoadFromFen(start);
            foreach (var san in sans)
            {
                bool ok;
                try { ok = board.Move(san); } catch (Exception) { ok = false; }   // Gera.Chess wirft bei unlesbarem SAN
                if (!ok) break;                                  // ab dem ersten unlesbaren Zug: Stand davor zeigen
                plies++;
            }
            fen = board.ToFen();
            if (board.ExecutedMoves.Count > 0) last = PgnParser.ToUci(board.ExecutedMoves[^1]);
        }
        catch (Exception) { /* ungültige Start-FEN: Grundstellung des Kopfes stehen lassen */ }

        // Uhr je Seite: der letzte [%clk] nach einem Zug dieser Seite (Halbzug i gehört Weiß, wenn die Startstellung Weiß am Zug hat und i gerade ist).
        var whiteFirst = FenFields.WhiteToMove(start);
        int? wClk = null, bClk = null;
        for (var i = 0; i < Math.Min(plies, clocks.Count); i++)
            if (clocks[i] is { } c)
            {
                if ((i % 2 == 0) == whiteFirst) wClk = c; else bClk = c;
            }
        var eval = plies > 0 && plies - 1 < evals.Count ? evals[plies - 1] : null;

        var gameUrl = H("GameURL");
        var id = gameUrl is not null && gameUrl.LastIndexOf('/') is var slash and >= 0 && slash < gameUrl.Length - 1
            ? gameUrl[(slash + 1)..] : $"{H("White")}|{H("Black")}|{H("Round")}";
        var result = H("Result") ?? "*";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(pgn.Trim())))[..16];
        return new LiveGame(id, H("Round"),
            new LivePlayer(H("White") ?? "?", H("WhiteTitle"), Int(H("WhiteElo")), Fide(H("WhiteFideId")), H("WhiteTeam"), wClk),
            new LivePlayer(H("Black") ?? "?", H("BlackTitle"), Int(H("BlackElo")), Fide(H("BlackFideId")), H("BlackTeam"), bClk),
            fen, last, plies, result, eval, hash);
    }

    private static int? Int(string? s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
    private static string? Fide(string? s) => s is not null && s.All(char.IsAsciiDigit) && s != "0" ? s : null;

    /// <summary>
    /// Hauptvariante: SAN je Halbzug und die Uhr/Bewertung aus dem Kommentar DIREKT dahinter. Varianten (…) und NAGs
    /// fallen weg, Zugnummern auch („12." und „12...", auch angeklebt „1.e4").
    /// </summary>
    internal static (List<string> Sans, List<int?> Clocks, List<LiveEval?> Evals) Scan(string moveText)
    {
        var sans = new List<string>();
        var clocks = new List<int?>();
        var evals = new List<LiveEval?>();
        var i = 0;
        var n = moveText.Length;
        while (i < n)
        {
            var c = moveText[i];
            if (c == '{')
            {
                var end = moveText.IndexOf('}', i + 1);
                if (end < 0) end = n;
                var comment = moveText.Substring(i + 1, Math.Max(0, end - i - 1));
                if (sans.Count > 0)
                {
                    if (ClockOf(comment) is { } clk) clocks[^1] = clk;
                    if (EvalOf(comment) is { } ev) evals[^1] = ev;
                }
                i = end + 1;
            }
            else if (c == '(')
            {
                var depth = 0;
                for (; i < n; i++)
                {
                    if (moveText[i] == '{') { var e = moveText.IndexOf('}', i + 1); i = e < 0 ? n : e; continue; }
                    if (moveText[i] == '(') depth++;
                    else if (moveText[i] == ')' && --depth == 0) { i++; break; }
                }
            }
            else if (c == ';')
            {
                var e = moveText.IndexOf('\n', i);
                i = e < 0 ? n : e + 1;
            }
            else if (char.IsWhiteSpace(c)) i++;
            else
            {
                var start = i;
                while (i < n && !char.IsWhiteSpace(moveText[i]) && moveText[i] is not ('{' or '(' or ';')) i++;
                var tok = MoveNumberPrefix().Replace(moveText[start..i], "");
                if (tok.Length == 0 || tok[0] == '$') continue;
                var clean = PgnParser.ExtractMainlineSans(tok);
                if (clean.Count != 1) continue;
                sans.Add(clean[0]);
                clocks.Add(null);
                evals.Add(null);
            }
        }
        return (sans, clocks, evals);
    }

    /// <summary><c>[%clk 1:30:59]</c> (auch „1:30:59.5") → Sekunden.</summary>
    internal static int? ClockOf(string comment)
    {
        var m = ClockRe().Match(comment);
        if (!m.Success) return null;
        return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
               + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 60
               + int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
    }

    /// <summary><c>[%eval 0.18]</c> → 18 cp; <c>[%eval #-3]</c> → Matt −3; optional „,23" = Tiefe.</summary>
    internal static LiveEval? EvalOf(string comment)
    {
        var m = EvalRe().Match(comment);
        if (!m.Success) return null;
        int? depth = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : null;
        if (m.Groups[1].Success) return new LiveEval(null, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), "broadcast", depth);
        var pawns = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return new LiveEval((int)Math.Round(pawns * 100), null, "broadcast", depth);
    }

    [GeneratedRegex(@"^\d+\.+")]
    private static partial Regex MoveNumberPrefix();
    [GeneratedRegex(@"\[%clk\s+(\d+):(\d{1,2}):(\d{1,2})(?:\.\d+)?\]")]
    private static partial Regex ClockRe();
    [GeneratedRegex(@"\[%eval\s+(?:#([+-]?\d+)|([+-]?\d+(?:\.\d+)?))(?:,(\d+))?\]")]
    private static partial Regex EvalRe();
}
