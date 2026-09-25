using System.Buffers;
using System.Text;
using System.Text.Json;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Getreuer Port von lila-engine <c>emit.rs</c>: macht aus den <c>info</c>-Zeilen einer Suche die
/// JSON-Zeilen, die der Anfragende bekommt —
/// <c>{ "time": ms, "depth": n, "nodes": n, "pvs": [ { "moves": [uci…], "cp"|"mate": n, "depth": n } ],
/// "bestmove"?: uci, "ponder"?: uci }</c>.
///
/// <para>Die Regeln, auf die sich Browser (<c>mapBrokerLine</c>) und Worker (<c>AnalysisJobStream</c>)
/// verlassen, und warum:</para>
/// <list type="bullet">
/// <item><c>multipv</c> fehlt ⇒ 1. Eine Zeile mit <c>multipv 1</c> setzt <c>time</c>/<c>depth</c>/<c>nodes</c>
/// neu (nur die vorhandenen) und LEERT alle Plätze (ohne die Liste zu kürzen); Zeilen mit <c>multipv &gt; 1</c>
/// senken nur <c>depth</c> auf ihr Minimum. So steht oben immer die Tiefe, die ALLE Linien erreicht haben.</item>
/// <item>Eine Linie braucht <c>depth</c> UND <c>score</c> UND <c>pv</c>; bei <c>multipv 1</c> zählt sie nur ohne
/// <c>lowerbound</c>/<c>upperbound</c> (Aspirationsfenster-Zwischenstände), bei <c>multipv &gt; 1</c> immer.</item>
/// <item>Gesendet wird nur ein VOLLSTÄNDIGER Satz (<see cref="ShouldEmit"/>) — keine halben MultiPV-Sätze.</item>
/// <item>Bewertung aus WEISS-Sicht: ist Schwarz am Zug (in der Stellung nach <c>initialFen</c>+<c>moves</c>),
/// werden <c>cp</c> und <c>mate</c> umgedreht (sättigend, <c>mate 0</c> bleibt 0).</item>
/// <item>Variante auf der Stellung nachgespielt, beim ersten illegalen Zug abgeschnitten, höchstens 30 Züge,
/// Rochade als König-schlägt-Turm (<see cref="BrokerChess"/>).</item>
/// <item><see cref="Finish"/>: leere Plätze raus, <c>bestmove</c>/<c>ponder</c> dazu. <b>Abweichung</b>: bei
/// <c>bestmove (none)</c> fehlt das Feld, lila-engine schreibt <c>"(none)"</c> hinein (liest heute niemand).</item>
/// </list>
/// </summary>
public sealed class EmitBuilder
{
    public const int MaxPvMoves = 30;

    private readonly string _rootFen;
    private readonly bool _blackToMove;
    private readonly List<EmitPv?> _pvs = [];
    private ulong _timeMs;
    private uint _depth;
    private ulong _nodes;
    private bool _finished;
    private string? _bestmove;
    private string? _ponder;

    /// <param name="rootFen">Stellung NACH <c>initialFen</c> + <c>moves</c> (dort wird die Variante nachgespielt).</param>
    public EmitBuilder(string rootFen)
    {
        _rootFen = rootFen;
        _blackToMove = BrokerChess.BlackToMove(BrokerChess.Load(rootFen));
    }

    private sealed record EmitPv(IReadOnlyList<string> Moves, bool Mate, long Value, uint Depth);

    public void Update(UciInfo info)
    {
        var multiPv = info.MultiPv ?? 1;
        var pv = Extract(info, multiPv);

        if (multiPv <= 1)
        {
            if (info.TimeMs is { } time) _timeMs = time;
            if (info.Depth is { } depth) _depth = depth;
            if (info.Nodes is { } nodes) _nodes = nodes;
            for (var i = 0; i < _pvs.Count; i++) _pvs[i] = null;
        }
        else if (info.Depth is { } depth)
        {
            _depth = Math.Min(_depth, depth);
        }

        while (_pvs.Count < multiPv) _pvs.Add(null);
        if (pv is not null) _pvs[multiPv - 1] = pv;
    }

    public bool ShouldEmit => _pvs.Count > 0 && _pvs.TrueForAll(p => p is not null);

    /// <param name="bestmove"><c>null</c> = <c>(none)</c> oder gar kein <c>bestmove</c> (Upload endete ohne).</param>
    public void Finish(string? bestmove, string? ponder)
    {
        _pvs.RemoveAll(p => p is null);
        _finished = true;
        _bestmove = bestmove;
        _ponder = ponder;
    }

    public string ToJson()
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("time", _timeMs);
            w.WriteNumber("depth", _depth);
            w.WriteNumber("nodes", _nodes);
            w.WriteStartArray("pvs");
            foreach (var pv in _pvs)
            {
                if (pv is null) { w.WriteNullValue(); continue; }
                w.WriteStartObject();
                w.WriteStartArray("moves");
                foreach (var m in pv.Moves) w.WriteStringValue(m);
                w.WriteEndArray();
                w.WriteNumber(pv.Mate ? "mate" : "cp", pv.Value);
                w.WriteNumber("depth", pv.Depth);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            if (_finished && _bestmove is not null)
            {
                w.WriteString("bestmove", _bestmove);
                if (_ponder is not null) w.WriteString("ponder", _ponder);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private EmitPv? Extract(UciInfo info, int multiPv)
    {
        if (info is not { Depth: { } depth, Score: { } score, Pv: { } pv }) return null;
        if (multiPv <= 1 && (score.Lowerbound || score.Upperbound)) return null;
        var value = _blackToMove ? SaturatingNeg(score.Value, score.Mate) : score.Value;
        return new EmitPv(NormalizePv(pv), score.Mate, value, depth);
    }

    private static long SaturatingNeg(long value, bool mate) =>
        mate
            ? (value == int.MinValue ? int.MaxValue : -value)
            : (value == long.MinValue ? long.MaxValue : -value);

    private List<string> NormalizePv(IReadOnlyList<string> pv)
    {
        var moves = new List<string>(Math.Min(pv.Count, MaxPvMoves));
        if (pv.Count == 0) return moves;
        var board = BrokerChess.Load(_rootFen);
        foreach (var uci in pv.Take(MaxPvMoves))
        {
            var played = BrokerChess.TryPlay(board, uci);
            if (played is null) break;
            moves.Add(played);
        }
        return moves;
    }
}
