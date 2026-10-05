using System.Text.Json;
using System.Text.Json.Nodes;

namespace RookHub.Api.Services;

/// <summary>Ein Zwischenstand einer Knotenanalyse (Bewertung <b>aus Sicht der Seite am Zug</b>): der beste Zug und seine
/// Bewertung, als die Suche bei <paramref name="Nodes"/> Knoten stand — zugeordnet der Schwelle <paramref name="Threshold"/>
/// (Vielfaches der Schrittweite, am Ende zusätzlich das Knotenziel selbst).</summary>
public readonly record struct NodeStep(long Threshold, long Nodes, string Uci, int? Cp, int? Mate);

/// <summary>Lesen und Schreiben der Stufen (<c>AnalysisJob.NodeStepsJson</c> / <c>GameAnalysisPosition.NodeStepsJson</c>):
/// <c>[{"t":10000,"n":9873,"m":"e2e4","cp":30}]</c>, aufsteigend nach <c>t</c>.</summary>
public static class NodeSteps
{
    public static List<NodeStep> Parse(string? json)
    {
        var list = new List<NodeStep>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (!e.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.Number) continue;
                if (!e.TryGetProperty("n", out var n) || n.ValueKind != JsonValueKind.Number) continue;
                var uci = e.TryGetProperty("m", out var m) ? m.GetString() : null;
                if (string.IsNullOrWhiteSpace(uci)) continue;
                int? cp = e.TryGetProperty("cp", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
                int? mate = e.TryGetProperty("mate", out var mt) && mt.ValueKind == JsonValueKind.Number ? mt.GetInt32() : null;
                if (cp is null && mate is null) continue;
                list.Add(new NodeStep(t.GetInt64(), n.GetInt64(), uci, cp, mate));
            }
        }
        catch (JsonException) { list.Clear(); }
        list.Sort((a, b) => a.Threshold.CompareTo(b.Threshold));
        return list;
    }

    public static string ToJson(IEnumerable<NodeStep> steps)
    {
        var arr = new JsonArray();
        foreach (var s in steps.OrderBy(s => s.Threshold))
        {
            var o = new JsonObject { ["t"] = s.Threshold, ["n"] = s.Nodes, ["m"] = s.Uci };
            if (s.Cp is int cp) o["cp"] = cp;
            if (s.Mate is int mate) o["mate"] = mate;
            arr.Add(o);
        }
        return arr.ToJsonString();
    }
}

/// <summary>
/// Sammelt beim Lesen des Engine-Streams die Zwischenstände je Schwelle (Schrittweite <c>step</c>, z. B. 10 000
/// Knoten) — ohne Mehrrechnung, die Zeilen kommen ohnehin. Für die Schwelle <c>T</c> zählt die LETZTE Zeile mit
/// <c>nodes ≤ T</c>; sie wird aber nur übernommen, wenn sie höchstens eine Schrittweite alt ist
/// (<c>T − nodes &lt; step</c>). Meldet die Engine in einem Bereich gar nichts, fehlt die Stufe — sie wird nicht aus einer
/// viel älteren Zeile erfunden. Die tatsächlich erreichte Knotenzahl steht an der Stufe.
/// <para>Wiederaufnahme: ein bestehender Stand wird nur ersetzt, wenn die neue Zeile mehr Knoten trägt.</para>
/// </summary>
public sealed class NodeStepRecorder
{
    private readonly long _step;
    private readonly Dictionary<long, NodeStep> _steps = new();
    private (long Nodes, NodeStep Step)? _prev;

    public NodeStepRecorder(long step, IEnumerable<NodeStep>? existing = null)
    {
        _step = step;
        if (existing is not null) foreach (var s in existing) _steps[s.Threshold] = s;
    }

    /// <summary>Seit dem letzten <see cref="Snapshot"/> hat sich etwas geändert.</summary>
    public bool Dirty { get; private set; }

    /// <summary>Aus = Schrittweite 0.</summary>
    public bool Enabled => _step > 0;

    /// <summary>Eine Datenzeile des Streams (<paramref name="nodes"/> + bester Zug mit Bewertung). Zeilen mit weniger
    /// Knoten als die vorige (eine neue Suche beginnt) setzen den Zwischenspeicher zurück.</summary>
    public void Observe(long nodes, string uci, int? cp, int? mate)
    {
        if (!Enabled || nodes <= 0 || (cp is null && mate is null)) return;
        var line = new NodeStep(0, nodes, uci, cp, mate);
        if (_prev is { } prev)
        {
            if (nodes > prev.Nodes)
                for (var t = CeilToStep(prev.Nodes); t < nodes; t += _step) Put(t, prev.Step);
            else if (nodes < prev.Nodes) { /* neue Suche: nichts nachtragen */ }
        }
        _prev = (nodes, line);
    }

    /// <summary>Stream zu Ende: hat die Suche das Knotenziel (fast) erreicht, steht ihre letzte Zeile unter dem Ziel selbst
    /// (mit der tatsächlich erreichten Knotenzahl — Lc0 hört teils knapp früher auf oder zieht ein paar Knoten drüber).
    /// Ohne erreichtes Ziel wird nichts nachgetragen: eine Stufe, die die Suche nie erreicht hat, gibt es nicht.</summary>
    public void Finish(long? target)
    {
        if (!Enabled || _prev is not { } last) return;
        if (target is { } goal && last.Nodes >= goal - goal / 20) Put(goal, last.Step);
        _prev = null;
    }

    /// <summary>Alle Stufen, aufsteigend; setzt <see cref="Dirty"/> zurück.</summary>
    public List<NodeStep> Snapshot()
    {
        Dirty = false;
        return _steps.Values.OrderBy(s => s.Threshold).ToList();
    }

    private long CeilToStep(long nodes) => (nodes + _step - 1) / _step * _step;

    private void Put(long threshold, NodeStep line)
    {
        if (threshold <= 0 || threshold - line.Nodes >= _step) return;   // zu alt (mehr als eine Schrittweite darunter): keine Stufe erfinden
        if (_steps.TryGetValue(threshold, out var old) && old.Nodes >= line.Nodes) return;
        _steps[threshold] = line with { Threshold = threshold };
        Dirty = true;
    }
}

/// <summary>
/// Auswertung „wann zahlt sich tieferes Rechnen aus": je Schwelle, wie oft der beste Zug schon der des Ziels ist und wie
/// weit die Bewertung noch vom Zielwert entfernt ist. Rein, ohne Datenbank.
/// <para>Bezug je Stellung ist ihre letzte (größte) Stufe; Stellungen mit nur einer Stufe fehlen (nichts zu vergleichen).
/// Die Bewertung wird in cp (Matt = ±1000, alles auf ±1000 gedeckelt) und in Gewinnchance-Prozentpunkten verglichen
/// (Lichess-Kurve, 0..100 %). Matt-gegen-cp-Unterschiede zählen zusätzlich getrennt.</para>
/// </summary>
public static class Convergence
{
    public const int MateCp = 1000;

    public sealed record Row(long Threshold, int Positions, double SameMoveShare, double MedianCp, double P90Cp,
        double MedianWinPct, double P90WinPct, int MoveChanges, int MateMismatch, double MeanNodes);

    public sealed record Report(int Positions, long? TargetThreshold, IReadOnlyList<Row> Rows);

    public static int CapCp(NodeStep s) =>
        s.Mate is { } m ? (m > 0 ? MateCp : -MateCp) : Math.Clamp(s.Cp ?? 0, -MateCp, MateCp);

    /// <summary>Gewinnchance in Prozent (0..100) zur Bewertung (Lichess: 2/(1+e^(−0,00368208·cp)) − 1, auf 0..100 umgelegt).</summary>
    public static double WinPercent(int cp) => (2 / (1 + Math.Exp(-0.00368208 * cp)) - 1 + 1) / 2 * 100;

    public static Report Evaluate(IEnumerable<IReadOnlyList<NodeStep>> positions)
    {
        var usable = positions.Where(p => p.Count >= 2).Select(p => p.OrderBy(s => s.Threshold).ToList()).ToList();
        if (usable.Count == 0) return new Report(0, null, []);

        var thresholds = usable.SelectMany(p => p).Select(s => s.Threshold).Distinct().OrderBy(t => t).ToList();
        var rows = new List<Row>();
        foreach (var t in thresholds)
        {
            var same = 0; var changes = 0; var mateMismatch = 0; var n = 0; long nodeSum = 0;
            var dCp = new List<double>(); var dWin = new List<double>();
            foreach (var p in usable)
            {
                var idx = p.FindIndex(s => s.Threshold == t);
                if (idx < 0) continue;
                var s = p[idx]; var reference = p[^1];
                n++; nodeSum += s.Nodes;
                if (string.Equals(s.Uci, reference.Uci, StringComparison.OrdinalIgnoreCase)) same++;
                if (idx > 0 && !string.Equals(p[idx - 1].Uci, s.Uci, StringComparison.OrdinalIgnoreCase)) changes++;
                if ((s.Mate is not null) != (reference.Mate is not null)) mateMismatch++;
                var a = CapCp(s); var b = CapCp(reference);
                dCp.Add(Math.Abs(a - b));
                dWin.Add(Math.Abs(WinPercent(a) - WinPercent(b)));
            }
            if (n == 0) continue;
            rows.Add(new Row(t, n, (double)same / n, Median(dCp), Percentile(dCp, 0.9), Median(dWin), Percentile(dWin, 0.9),
                changes, mateMismatch, (double)nodeSum / n));
        }
        return new Report(usable.Count, thresholds[^1], rows);
    }

    public static double Median(List<double> v) => Percentile(v, 0.5);

    /// <summary>Nächster-Rang-Perzentil (kein Interpolieren: bei wenigen Stellungen bleibt der Wert ein echter Messwert).</summary>
    public static double Percentile(List<double> v, double p)
    {
        if (v.Count == 0) return 0;
        var s = v.OrderBy(x => x).ToList();
        var rank = (int)Math.Ceiling(p * s.Count) - 1;
        return s[Math.Clamp(rank, 0, s.Count - 1)];
    }
}
