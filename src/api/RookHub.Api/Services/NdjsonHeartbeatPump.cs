namespace RookHub.Api.Services;

/// <summary>
/// Kopiert einen ndjson-Stream 1:1 weiter und schreibt bei Funkstille eine LEERZEILE als Lebenszeichen.
///
/// Warum: Die Analyse-Antwort des Lichess-Brokers liefert nur dann Bytes, wenn die Engine eine neue
/// Info-Zeile hat. Bei MultiPV 5 liegen ab Tiefe ~27 zwischen zwei Zeilen Minuten — und jeder Proxy
/// VOR der API (Nginx Proxy Manager: <c>proxy_read_timeout</c> 60 s Default; Mobilfunk-/Hotel-
/// Gateways) kappt Verbindungen ohne Bytes. Der Browser wertet den Abriss dann als beendete Suche
/// und friert still bei der letzten Tiefe ein. Ein periodisches <c>\n</c> hält jeden Zwischenknoten
/// wach; der ndjson-Parser des Clients ignoriert Leerzeilen (<c>external-engine.service.ts</c>).
/// </summary>
public static class NdjsonHeartbeatPump
{
    /// <summary>Deutlich unter den üblichen 60 s Idle-Timeouts, aber nicht so eng, dass es Traffic macht.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(20);

    private static readonly byte[] Heartbeat = "\n"u8.ToArray();

    /// <summary>Pumpt <paramref name="source"/> nach <paramref name="destination"/> (Flush je Stück) und
    /// schreibt eine Leerzeile, sobald <paramref name="interval"/> ohne Daten verstreicht. Endet mit dem
    /// Quell-Ende oder per Abbruch (<see cref="OperationCanceledException"/> wird durchgereicht).</summary>
    public static async Task PumpAsync(Stream source, Stream destination, TimeSpan interval, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var read = source.ReadAsync(buffer, ct).AsTask();
        while (true)
        {
            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var delay = Task.Delay(interval, delayCts.Token);
            var completed = await Task.WhenAny(read, delay);
            if (completed == delay)
            {
                ct.ThrowIfCancellationRequested();
                await destination.WriteAsync(Heartbeat, ct);
                await destination.FlushAsync(ct);
                continue;               // dieselbe ausstehende Leseoperation weiter abwarten
            }
            delayCts.Cancel();          // Timer nicht bis zum Ablauf leben lassen
            var n = await read;
            if (n == 0) return;
            await destination.WriteAsync(buffer.AsMemory(0, n), ct);
            await destination.FlushAsync(ct);
            read = source.ReadAsync(buffer, ct).AsTask();
        }
    }
}
