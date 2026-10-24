using System.Text.Json;

namespace RookHub.Api.Services.Engines;

/// <summary>Reine, testbare Bausteine des Workers: Zeilen des Broker-Streams lesen und entscheiden,
/// ob eine Zeile das gespeicherte Ergebnis ersetzt.</summary>
public static class AnalysisJobStream
{
    /// <summary>Tiefe einer ndjson-Zeile (<c>{"depth": n, …}</c>); null bei Leer-/Heartbeat-/kaputter Zeile.</summary>
    public static int? DepthOf(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("depth", out var d) && d.ValueKind == JsonValueKind.Number
                ? d.GetInt32() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Suchtempo einer ndjson-Zeile (Knoten/Sekunde) aus <c>nodes</c> + verstrichener <c>time</c> (ms);
    /// null, wenn eines fehlt oder 0 ist (die ersten Zeilen tragen oft time=0).</summary>
    public static int? NpsOf(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            if (!r.TryGetProperty("nodes", out var n) || n.ValueKind != JsonValueKind.Number) return null;
            if (!r.TryGetProperty("time", out var t) || t.ValueKind != JsonValueKind.Number) return null;
            var nodes = n.GetInt64(); var ms = t.GetInt64();
            return ms > 0 && nodes > 0 ? (int)Math.Min(int.MaxValue, nodes * 1000 / ms) : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Knotenzahl einer ndjson-Zeile; null, wenn sie fehlt.</summary>
    public static long? NodesOf(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            return r.ValueKind == JsonValueKind.Object && r.TryGetProperty("nodes", out var n) && n.ValueKind == JsonValueKind.Number
                ? n.GetInt64() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Knotenziel erreicht? Meldet die Engine selbst das Ende (<paramref name="streamEnded"/>), genügt knapp
    /// darunter (Lc0 hört bei <c>go nodes N</c> teils ein paar Knoten früher auf); sonst muss das Ziel voll da sein.</summary>
    public static bool NodeGoalMet(long target, long nodes, bool streamEnded) =>
        nodes >= target || (streamEnded && nodes >= target - target / 20);

    /// <summary>Trägt die Zeile ein <c>bestmove</c>? So endet eine Suche, die die Engine SELBST beendet hat — aber
    /// ebenso eine, die von außen gestoppt wurde (UCI antwortet auf <c>stop</c> mit <c>bestmove</c>).</summary>
    public static bool HasBestMove(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("bestmove", out var b) && b.ValueKind == JsonValueKind.String;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Hat die Engine einen Knotenauftrag VOR dem Ziel aus eigenem Ermessen beendet (0.681.1, gemeldet 2026-10-06: Lc0
    /// rechnete dieselben Stellungen alle 30 s neu, ohne je fertig zu werden)? Dann ist das Ergebnis endgültig.
    ///
    /// <para>Lc0 bricht eine Suche mit Knotenlimit ab, sobald der beste Zug mit dem Restbudget nicht mehr kippen kann
    /// (Smart Pruning, ab Werk an): bei 50 000 Knoten oft schon bei 39 000. Ein neuer Lauf hört an derselben Stelle
    /// wieder auf. Ein <c>bestmove</c> unter dem Ziel kommt aber AUCH, wenn jemand die Suche von außen stoppt (UCI
    /// antwortet auf <c>stop</c> mit <c>bestmove</c>) — die Frage ist also, ob wir einen solchen Stopp sehen würden:</para>
    /// <list type="bullet">
    /// <item><b>Direkt angebunden</b> (<c>rhe_…</c>): jede Arbeit für diese Engine läuft über UNSEREN Broker. Ein Stopp von
    /// außen wäre unser eigener — der Worker hat abgebrochen (<paramref name="cancelledByUs"/>) oder eine Live-Analyse
    /// rechnet auf der Engine (<paramref name="liveOnEngine"/>). Ist beides nicht der Fall, hat die Engine selbst
    /// aufgehört: EIN Lauf genügt, nichts wird doppelt gerechnet.</item>
    /// <item><b>Über Lichess</b> (<c>eei_…</c>): dort kann jemand die Engine im Analysebrett von lichess.org benutzen, und
    /// davon erfahren wir nichts. Erst ein zweiter Lauf, der wieder sauber endet und nicht spürbar weiter kommt (höchstens
    /// 10 % mehr Knoten), beweist das eigene Ende — eine gestoppte Suche käme beim nächsten Versuch weiter.</item>
    /// </list>
    /// </summary>
    public static bool EngineEndedOwnSearch(bool directEngine, bool sawBestMove, bool cancelledByUs, bool liveOnEngine,
        long? previousNodes, long nodes)
    {
        if (!sawBestMove || cancelledByUs || liveOnEngine || nodes <= 0) return false;
        return directEngine || StoppedEarlyAgain(previousNodes, nodes);
    }

    /// <summary>Zweiter Lauf an derselben Stelle: höchstens 10 % mehr Knoten als der vorige (siehe
    /// <see cref="EngineEndedOwnSearch"/>, Fall Lichess).</summary>
    public static bool StoppedEarlyAgain(long? previousNodes, long nodes)
        => previousNodes is long prev && prev > 0 && nodes > 0 && nodes <= prev + prev / 10;

    /// <summary>Eine Fortsetzung (und ein Neustart mit mehr Linien) liefert die flachen Iterationen erneut —
    /// übernommen wird nur, was mindestens so tief ist wie das gespeicherte Ergebnis.</summary>
    public static bool ShouldPersist(int lineDepth, int reachedDepth) => lineDepth >= reachedDepth;

    /// <summary>Liest den ndjson-Stream zeilenweise und ruft <paramref name="onLine"/> für jede Zeile mit Tiefe.
    /// Endet mit dem Stream oder per Abbruch (Exception wird durchgereicht). <paramref name="tally"/> zählt
    /// mit, WAS über die Leitung kam — beim Abriss ist das die entscheidende Frage.</summary>
    public static async Task ConsumeAsync(Stream ndjson, Func<string, int, Task> onLine, CancellationToken ct,
                                          StreamTally? tally = null)
    {
        using var reader = new StreamReader(ndjson);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var now = DateTime.UtcNow;
            tally?.Note(line, now);
            if (DepthOf(line) is int depth)
                await onLine(line, depth);
        }
    }
}
