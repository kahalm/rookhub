using System.Globalization;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>Bewertung einer <c>info</c>-Zeile (<c>score cp -35 lowerbound</c>). <see cref="Mate"/> = <c>mate n</c>.</summary>
public readonly record struct UciScore(bool Mate, long Value, bool Lowerbound, bool Upperbound);

/// <summary>Eine geparste <c>info</c>-Zeile — die Felder, die lila-engine <c>UciOut::Info</c> kennt.</summary>
public sealed class UciInfo
{
    /// <summary>1..5 (ein gelesenes <c>multipv 0</c> wird wie bei lila-engine zu 1).</summary>
    public int? MultiPv { get; set; }
    public uint? Depth { get; set; }
    public uint? SelDepth { get; set; }
    public ulong? TimeMs { get; set; }
    public ulong? Nodes { get; set; }
    public UciScore? Score { get; set; }
    public string? CurrMove { get; set; }
    public uint? CurrMoveNumber { get; set; }
    public uint? Hashfull { get; set; }
    public ulong? Nps { get; set; }
    public ulong? TbHits { get; set; }
    public ulong? SbHits { get; set; }
    public uint? CpuLoad { get; set; }
    public List<string>? Pv { get; set; }
    public string? String { get; set; }
}

/// <summary><c>bestmove &lt;uci&gt; [ponder &lt;uci&gt;]</c>; <see cref="Move"/> null = <c>(none)</c>.</summary>
public sealed record UciBestMove(string? Move, string? Ponder);

public enum UciLineKind { Ignored, Info, BestMove, Error }

/// <summary>Ergebnis einer Zeile: <c>info</c>, <c>bestmove</c>, eine andere (ignorierte) Zeile oder ein Fehler.</summary>
public readonly record struct UciLine(UciLineKind Kind, UciInfo? Info = null, UciBestMove? BestMove = null, string? Error = null)
{
    public static readonly UciLine Ignored = new(UciLineKind.Ignored);
    public static UciLine Fail(string error) => new(UciLineKind.Error, Error: error);
}

/// <summary>
/// Port von lila-engine <c>uci.rs</c> (Parser für <c>UciOut::from_line</c>), beschränkt auf das, was ein
/// Provider hochlädt: <c>info</c> und <c>bestmove</c>. Jede andere erste Silbe (<c>readyok</c>, leere Zeile)
/// ist <see cref="UciLineKind.Ignored"/> — wie <c>Ok(None)</c> bei lila-engine.
///
/// <para>Getreu übernommen: Trenner Leerzeichen UND Tab; ein unbekanntes Schlüsselwort in einer
/// <c>info</c>-Zeile, eine kaputte Zahl, ein <c>multipv</c> über 5 oder ein Zeilenumbruch in der Zeile
/// sind FEHLER; <c>pv</c>/<c>refutation</c>/<c>currline</c> nehmen Züge, solange sie wie UCI-Züge
/// aussehen; <c>string</c> schluckt den Rest der Zeile. Anders als lila-engine bricht der Aufrufer bei
/// einem Fehler den Upload NICHT ab (400), sondern überspringt die Zeile (siehe <c>EngineUploadPump</c>).</para>
/// </summary>
public static class UciLineParser
{
    public static UciLine Parse(string line)
    {
        if (line.AsSpan().IndexOfAny('\r', '\n') >= 0) return UciLine.Fail("unexpected line break in uci command");
        var p = new Cursor(line);
        return p.Next() switch
        {
            "bestmove" => ParseBestMove(ref p),
            "info" => ParseInfo(ref p),
            _ => UciLine.Ignored,
        };
    }

    private static UciLine ParseBestMove(ref Cursor p)
    {
        string? move;
        switch (p.Next())
        {
            case null or "(none)": move = null; break;
            case var m:
                if (!TryParseMove(m, out var mv)) return UciLine.Fail($"invalid move: {m}");
                move = mv;
                break;
        }
        string? ponder = null;
        switch (p.Next())
        {
            case null: break;
            case "ponder":
                switch (p.Next())
                {
                    case null or "(none)": break;
                    case var m:
                        if (!TryParseMove(m, out var pm)) return UciLine.Fail($"invalid move: {m}");
                        ponder = pm;
                        break;
                }
                break;
            default: return UciLine.Fail("unexpected token");
        }
        return new UciLine(UciLineKind.BestMove, BestMove: new UciBestMove(move, ponder));
    }

    private static UciLine ParseInfo(ref Cursor p)
    {
        var info = new UciInfo();
        while (true)
        {
            var token = p.Next();
            if (token is null) return new UciLine(UciLineKind.Info, Info: info);
            string? err = null;
            switch (token)
            {
                case "multipv":
                    if (!Unsigned32(p.Next(), out var mpv, ref err)) break;
                    if (mpv > 5) { err = "invalid multipv: supported range is 1 to 5"; break; }
                    info.MultiPv = (int)Math.Max(1u, mpv);
                    break;
                case "depth": if (Unsigned32(p.Next(), out var d, ref err)) info.Depth = d; break;
                case "seldepth": if (Unsigned32(p.Next(), out var sd, ref err)) info.SelDepth = sd; break;
                case "time": if (Unsigned64(p.Next(), out var t, ref err)) info.TimeMs = t; break;
                case "nodes": if (Unsigned64(p.Next(), out var n, ref err)) info.Nodes = n; break;
                case "score":
                    if (ParseScore(ref p, out var score, ref err)) info.Score = score;
                    break;
                case "currmove":
                    var cm = p.Next();
                    if (cm is null) err = "unexpected end of line";
                    else if (!TryParseMove(cm, out var cmv)) err = $"invalid move: {cm}";
                    else info.CurrMove = cmv;
                    break;
                case "currmovenumber": if (Unsigned32(p.Next(), out var cmn, ref err)) info.CurrMoveNumber = cmn; break;
                case "hashfull": if (Unsigned32(p.Next(), out var hf, ref err)) info.Hashfull = hf; break;
                case "nps": if (Unsigned64(p.Next(), out var nps, ref err)) info.Nps = nps; break;
                case "tbhits": if (Unsigned64(p.Next(), out var tb, ref err)) info.TbHits = tb; break;
                case "sbhits": if (Unsigned64(p.Next(), out var sb, ref err)) info.SbHits = sb; break;
                case "cpuload": if (Unsigned32(p.Next(), out var cpu, ref err)) info.CpuLoad = cpu; break;
                case "refutation":
                    var refuted = p.Next();
                    if (refuted is null) err = "unexpected end of line";
                    else if (!TryParseMove(refuted, out _)) err = $"invalid move: {refuted}";
                    else ParseMoves(ref p);   // gelesen und verworfen (emit.rs benutzt es nicht)
                    break;
                case "currline":
                    if (Unsigned32(p.Next(), out _, ref err)) ParseMoves(ref p);
                    break;
                case "pv": info.Pv = ParseMoves(ref p); break;
                case "string": info.String = p.Rest(); break;
                default: err = "unexpected token"; break;
            }
            if (err is not null) return UciLine.Fail(err);
        }
    }

    private static bool ParseScore(ref Cursor p, out UciScore score, ref string? err)
    {
        score = default;
        bool mate;
        long value;
        switch (p.Next())
        {
            case "cp":
                if (!Signed(p.Next(), long.MinValue, long.MaxValue, out value, ref err)) return false;
                mate = false;
                break;
            case "mate":
                if (!Signed(p.Next(), int.MinValue, int.MaxValue, out value, ref err)) return false;
                mate = true;
                break;
            case null: err = "unexpected end of line"; return false;
            default: err = "unexpected token"; return false;
        }
        bool lower = false, upper = false;
        while (true)
        {
            switch (p.Peek())
            {
                case "lowerbound": p.Next(); lower = true; continue;
                case "upperbound": p.Next(); upper = true; continue;
            }
            break;
        }
        score = new UciScore(mate, value, lower, upper);
        return true;
    }

    private static List<string> ParseMoves(ref Cursor p)
    {
        var moves = new List<string>();
        while (p.Peek() is { } token && TryParseMove(token, out var m))
        {
            p.Next();
            moves.Add(m);
        }
        return moves;
    }

    /// <summary>Wie shakmaty <c>UciMove::from_ascii</c>: <c>e2e4</c>, <c>e7e8q</c>, <c>N@f3</c>, <c>0000</c>.
    /// Liefert die kanonische Schreibweise (Umwandlung klein, Figur beim Einsetzen groß).</summary>
    public static bool TryParseMove(string s, out string move)
    {
        move = s;
        if (s == "0000") return true;
        if (s.Length == 4 && s[1] == '@')
        {
            var role = char.ToUpperInvariant(s[0]);
            if ("PNBRQK".IndexOf(role) < 0 || !IsSquare(s, 2)) return false;
            move = $"{role}@{s[2..]}";
            return true;
        }
        if (s.Length is 4 or 5 && IsSquare(s, 0) && IsSquare(s, 2))
        {
            if (s.Length == 4) return true;
            var promo = char.ToLowerInvariant(s[4]);
            if ("pnbrqk".IndexOf(promo) < 0) return false;
            move = s[..4] + promo;
            return true;
        }
        return false;
    }

    private static bool IsSquare(string s, int at) => s[at] is >= 'a' and <= 'h' && s[at + 1] is >= '1' and <= '8';

    private static bool Unsigned32(string? token, out uint value, ref string? err)
    {
        value = 0;
        if (!Unsigned(token, uint.MaxValue, out var v, ref err)) return false;
        value = (uint)v;
        return true;
    }

    private static bool Unsigned64(string? token, out ulong value, ref string? err) =>
        Unsigned(token, ulong.MaxValue, out value, ref err);

    /// <summary>Rust <c>str::parse</c> für vorzeichenlose Zahlen: optionales <c>+</c>, dann nur Ziffern.</summary>
    private static bool Unsigned(string? token, ulong max, out ulong value, ref string? err)
    {
        value = 0;
        if (token is null) { err = "unexpected end of line"; return false; }
        var digits = token.StartsWith('+') ? token[1..] : token;
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)
            || !ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value > max)
        {
            err = $"invalid integer: {token}";
            return false;
        }
        return true;
    }

    private static bool Signed(string? token, long min, long max, out long value, ref string? err)
    {
        value = 0;
        if (token is null) { err = "unexpected end of line"; return false; }
        var negative = token.StartsWith('-');
        var digits = token.StartsWith('-') || token.StartsWith('+') ? token[1..] : token;
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)
            || !long.TryParse((negative ? "-" : "") + digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
            || value < min || value > max)
        {
            err = $"invalid integer: {token}";
            return false;
        }
        return true;
    }

    /// <summary>Zerlegt an Leerzeichen und Tabs (lila-engine <c>read</c>/<c>read_until</c>).</summary>
    private ref struct Cursor
    {
        private string _rest;

        public Cursor(string s) { _rest = s; }

        public string? Next()
        {
            var (head, tail) = Read(_rest);
            _rest = tail;
            return head;
        }

        public readonly string? Peek() => Read(_rest).Head;

        /// <summary>Der Rest der Zeile ohne führende/abschließende Trenner (für <c>string …</c>).</summary>
        public string Rest()
        {
            var r = _rest.Trim(' ', '\t');
            _rest = string.Empty;
            return r;
        }

        private static (string? Head, string Tail) Read(string s)
        {
            var t = s.TrimStart(' ', '\t');
            if (t.Length == 0) return (null, t);
            var end = t.IndexOfAny([' ', '\t']);
            return end < 0 ? (t, string.Empty) : (t[..end], t[end..]);
        }
    }
}
