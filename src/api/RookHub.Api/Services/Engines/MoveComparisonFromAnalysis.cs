using System.Text.Json;
using System.Text.Json.Nodes;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// Zugvergleiche zu den Fehlern einer Partie aus der schon VERTIEFTEN Analyse befüllen statt neu zu rechnen (Wunsch
/// 2026-11-09 an Partie 145: „warum musst du eigentlich, wenn du die Partie tief analysiert hast, die Fehler nochmals eigens
/// analysieren? Sollten da nicht die bestehenden greifen?").
///
/// <para><b>Was die Analyse hergibt</b> (je Halbzug eine <see cref="GameAnalysisPosition"/>, nach der Vertiefung Tiefe 30 mit
/// fünf Kandidaten samt Hauptvariante, Bewertung aus Sicht der Seite am Zug):</para>
/// <list type="bullet">
/// <item><b>Kandidat = Partiezug</b>: die Folgestellung (Halbzug + 1) IST die Stellung nach dem Partiezug — ihre Kandidaten sind
/// die besten Antworten des Gegners, bis zu <see cref="MoveComparisonService.CandidateLines"/> Linien wie beim eigenen Auftrag.</item>
/// <item><b>Sonst</b> (der beste Zug, oder der Partiezug ohne brauchbare Folgestellung): der Kandidat in der Stellung des Fehlers,
/// seine Hauptvariante ab dem zweiten Halbzug ist EINE Linie der Stellung danach (die beste Antwort samt Fortsetzung).</item>
/// <item><b>Antwort-Tests</b> sind neue Stellungen (bester Zug + Antwort auf den Fehlzug), die die Analyse nicht kennt — außer der
/// Antwort, die ohnehin die Hauptvariante nach dem besten Zug ist (<see cref="DeriveRepliesFromBest"/>). Die übrigen rechnet die
/// Engine wie bisher, als <see cref="MoveComparisonSource.Mixed"/>.</item>
/// </list>
/// <para><b>Schwellen</b>: Stellung <see cref="GameAnalysisPosition.Refined"/> UND <see cref="GameAnalysisPosition.Depth"/> ≥
/// Vergleichstiefe (<c>Refined</c> steht auch bei aufgegebener Vertiefung — dann bliebe Tiefe 20), Fen passt, und die Linie hat
/// mindestens <see cref="MinLinePlies"/> Halbzüge (die Antwort des Gegners UND die eigene Erwiderung — beides nennt die Begründung)
/// oder endet auf dem Brett (Matt/Patt). Für den Partiezug aus der Folgestellung muss deren BESTE Linie taugen, sonst stimmte die
/// Bewertung nicht. Was nicht passt, bleibt offen und geht an die Engine.</para>
/// <para><b>Vorzeichen</b>: <c>CandidatesJson</c> trägt die Sicht der Seite am Zug JENER Stellung, die Zeile des Vergleichs
/// (<see cref="MoveComparisonLine.ResultJson"/>) die Weiß-Sicht wie eine Broker-Zeile — umgerechnet wird hier, gelesen wie jede
/// Broker-Zeile (<see cref="BrokerCandidates.Parse"/>).</para>
/// </summary>
public static class MoveComparisonFromAnalysis
{
    /// <summary>Mindestlänge einer übernommenen Linie (ab der Stellung nach dem Kandidaten): Antwort + eigene Erwiderung.</summary>
    public const int MinLinePlies = 2;

    /// <summary>Ein gespeicherter Kandidat: Zug, Bewertung (Sicht der Seite am Zug seiner Stellung), Hauptvariante (roh, UCI).</summary>
    private sealed record Stored(string Uci, int? Cp, int? Mate, List<string> Pv);

    /// <summary>Eine Linie für die Zeile: Züge ab der Stellung, Bewertung aus Weiß-Sicht.</summary>
    internal sealed record Line(IReadOnlyList<string> Moves, int? CpWhite, int? MateWhite);

    /// <summary>
    /// Offene Kandidatenzeilen eines Vergleichs mit <see cref="MoveComparison.Ply"/> aus der Analyse füllen (Zeile <c>Done</c>,
    /// Weiß-Sicht, erreichte Tiefe). Hat jede Kandidatenzeile ihre Zahlen und wartet der Vergleich noch
    /// (<see cref="MoveComparisonStatus.Waiting"/>), werden gleich die Antwort-Tests angelegt und, soweit möglich, aus der
    /// Hauptvariante des besten Zuges gefüllt: nichts mehr offen → <see cref="MoveComparisonStatus.Explaining"/> (bzw.
    /// <c>Done</c> ohne schwächeren Kandidaten). Setzt <see cref="MoveComparison.Source"/> und bei Übernahme die Tiefe der Daten.
    /// → die gefüllten Kandidatenzeilen (deren laufende Aufträge räumt der Aufrufer weg).
    /// </summary>
    public static List<MoveComparisonLine> Apply(MoveComparison c, IEnumerable<GameAnalysisPosition> positions, int minDepth)
    {
        var filled = new List<MoveComparisonLine>();
        if (c.Ply is not int ply || c.Lines.Any(l => l.Kind == MoveComparisonLineKind.Reply))
        {
            c.Source ??= MoveComparisonSource.Engine;
            return filled;
        }
        var byPly = new Dictionary<int, GameAnalysisPosition>();
        foreach (var p in positions) byPly[p.Ply] = p;
        if (!byPly.TryGetValue(ply, out var at) || !SameFen(at.Fen, c.Fen))
        {
            c.Source ??= MoveComparisonSource.Engine;
            return filled;
        }
        byPly.TryGetValue(ply + 1, out var next);

        var depths = new List<int>();
        foreach (var line in c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate && l.State == MoveComparisonLineState.Pending))
        {
            var found = LinesFor(at, next, line, minDepth);
            if (found is null) continue;
            line.ResultJson = ResultJson(found.Value.Lines, found.Value.Depth);
            line.ReachedDepth = found.Value.Depth;
            line.State = MoveComparisonLineState.Done;
            filled.Add(line);
            depths.Add(found.Value.Depth);
        }
        if (filled.Count == 0)
        {
            c.Source ??= MoveComparisonSource.Engine;
            return filled;
        }
        // Die Tiefe der Daten (alle ≥ minDepth); offene Aufträge rechnen mit ihr weiter, gedeckelt auf das Erlaubte.
        c.Depth = Math.Min(depths.Min(), AnalysisJobService.MaxDepth);
        c.Source = MoveComparisonSource.Mixed;
        var allCandidates = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate).All(l => l.State != MoveComparisonLineState.Pending);
        if (!allCandidates || c.Status != MoveComparisonStatus.Waiting) return filled;

        if (!MoveComparisonService.AddReplyLines(c)) return filled;   // nichts bewertbar → Failed
        DeriveRepliesFromBest(c);
        if (c.Lines.Any(l => l.Kind == MoveComparisonLineKind.Reply && l.State == MoveComparisonLineState.Pending)) return filled;
        c.Source = MoveComparisonSource.Analysis;
        c.Status = MoveComparisonService.HasWeakerCandidates(c) ? MoveComparisonStatus.Explaining : MoveComparisonStatus.Done;
        c.UpdatedAt = DateTime.UtcNow;
        if (c.Status == MoveComparisonStatus.Done) c.FinishedAt ??= c.UpdatedAt;
        return filled;
    }

    /// <summary>Die Linien der Stellung nach dem Kandidaten <paramref name="line"/>, <c>null</c> = die Analyse reicht nicht.</summary>
    private static (List<Line> Lines, int Depth)? LinesFor(GameAnalysisPosition at, GameAnalysisPosition? next, MoveComparisonLine line, int minDepth)
    {
        // Partiezug: die Folgestellung ist die Stellung danach — mehrere beste Antworten, wie ein eigener Auftrag.
        if (string.Equals(line.CandidateUci, at.GameMoveUci, StringComparison.OrdinalIgnoreCase)
            && next is not null && Usable(next, minDepth) && SameFen(next.Fen, line.Fen))
        {
            var stored = Read(next.CandidatesJson);
            if (stored.Count > 0 && LineUsable(line.Fen, stored[0].Pv))
            {
                var white = FenFields.WhiteToMove(next.Fen);
                var lines = stored.Where(s => LineUsable(line.Fen, s.Pv)).Take(MoveComparisonService.CandidateLines)
                    .Select(s => ToWhite(s.Pv, s.Cp, s.Mate, white)).ToList();
                return (lines, next.Depth);
            }
        }
        // Sonst die Hauptvariante des Kandidaten in der Stellung des Fehlers, ab dem zweiten Halbzug.
        if (!Usable(at, minDepth)) return null;
        var cand = Read(at.CandidatesJson).FirstOrDefault(s => string.Equals(s.Uci, line.CandidateUci, StringComparison.OrdinalIgnoreCase));
        if (cand is null || cand.Pv.Count < 1 || !string.Equals(cand.Pv[0], cand.Uci, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = cand.Pv.Skip(1).ToList();
        if (!LineUsable(line.Fen, rest)) return null;
        return ([ToWhite(rest, cand.Cp, cand.Mate, FenFields.WhiteToMove(at.Fen))], at.Depth);
    }

    /// <summary>Mover-Sicht seiner Stellung → Weiß-Sicht.</summary>
    private static Line ToWhite(IReadOnlyList<string> moves, int? cp, int? mate, bool moverWhite)
        => new(moves, cp is int c ? (moverWhite ? c : -c) : null, mate is int m ? (moverWhite ? m : -m) : null);

    private static bool Usable(GameAnalysisPosition p, int minDepth)
        => p.Refined && p.Depth >= minDepth && !string.IsNullOrEmpty(p.CandidatesJson);

    /// <summary>Lang genug (<see cref="MinLinePlies"/>), oder sie endet auf dem Brett (Matt in 1).</summary>
    private static bool LineUsable(string fen, IReadOnlyList<string> moves)
    {
        if (moves.Count >= MinLinePlies) return true;
        if (moves.Count == 0) return false;
        var after = MoveComparisonService.PlayUci(fen, moves[0]);
        return after is not null && MoveComparisonService.Terminal(after) is not null;
    }

    /// <summary>Stellung, Zugrecht und Rochaderechte gleich (Zugzähler und e.p.-Feld schreiben die Bibliotheken verschieden).</summary>
    internal static bool SameFen(string a, string b)
    {
        var x = (a ?? "").Split(' ');
        var y = (b ?? "").Split(' ');
        return x.Length >= 3 && y.Length >= 3 && x[0] == y[0] && x[1] == y[1] && x[2] == y[2];
    }

    private static List<Stored> Read(string? json)
    {
        var result = new List<Stored>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var uci = e.TryGetProperty("uci", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                if (string.IsNullOrWhiteSpace(uci)) continue;
                int? cp = e.TryGetProperty("cp", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
                int? mate = e.TryGetProperty("mate", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : null;
                if (cp is null && mate is null) continue;
                var pv = e.TryGetProperty("pv", out var p) && p.ValueKind == JsonValueKind.Array
                    ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                    : [];
                result.Add(new Stored(uci, cp, mate, pv));
            }
        }
        catch (JsonException) { /* unbrauchbar → nichts übernehmen */ }
        return result;
    }

    /// <summary>Eine Zeile wie vom Broker: <c>{"depth":30,"pvs":[{"moves":[…],"cp":…}]}</c>, Weiß-Sicht.</summary>
    internal static string ResultJson(IEnumerable<Line> lines, int depth)
    {
        var pvs = new JsonArray();
        foreach (var l in lines)
        {
            var o = new JsonObject { ["moves"] = new JsonArray(l.Moves.Select(m => (JsonNode?)JsonValue.Create(m)).ToArray()) };
            if (l.MateWhite is int m) o["mate"] = m;
            else o["cp"] = l.CpWhite ?? 0;
            pvs.Add(o);
        }
        return new JsonObject { ["depth"] = depth, ["pvs"] = pvs }.ToJsonString();
    }

    /// <summary>
    /// Antwort-Tests, die schon in der Hauptvariante nach dem BESTEN Zug stehen: ist die Antwort auf den schwächeren Zug genau die
    /// beste Antwort auf den besten, dann ist die eigene Erwiderung deren Fortsetzung und die Bewertung die des besten Zuges —
    /// dieselben Zahlen, die ein eigener Auftrag (eine Linie, Tiefe − 2) liefern würde. → Zahl gefüllt.
    /// </summary>
    public static int DeriveRepliesFromBest(MoveComparison c)
    {
        var best = c.Lines.FirstOrDefault(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == c.BestUci
            && l.State == MoveComparisonLineState.Done);
        if (best is null || string.IsNullOrEmpty(best.Fen) || BrokerCandidates.Parse(best.ResultJson, best.Fen) is not { Count: > 0 } cands)
            return 0;
        var top = cands[0];
        if (top.Pv is not { Count: >= 2 } pv) return 0;
        var white = FenFields.WhiteToMove(best.Fen);   // Parse liefert die Sicht der Seite am Zug — zurück nach Weiß
        var line = ToWhite(pv.Skip(1).ToList(), top.Cp, top.Mate, white);
        var n = 0;
        foreach (var r in c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Reply && l.State == MoveComparisonLineState.Pending
            && l.AnalysisJobId == null && string.Equals(l.ReplyUci, top.Uci, StringComparison.OrdinalIgnoreCase)))
        {
            var json = ResultJson([line], Math.Max(1, best.ReachedDepth - 2));
            if (BrokerCandidates.Parse(json, r.Fen) is null) continue;   // Erwiderung passt nicht zur Stellung
            r.ResultJson = json;
            r.ReachedDepth = Math.Max(1, best.ReachedDepth - 2);
            r.State = MoveComparisonLineState.Done;
            n++;
        }
        return n;
    }
}
