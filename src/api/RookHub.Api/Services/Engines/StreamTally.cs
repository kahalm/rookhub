namespace RookHub.Api.Services.Engines;

/// <summary>Zählwerk EINES Stream-Laufs. Beantwortet beim Abriss die Frage, an der sich die Ursachen
/// scheiden: war die Leitung vorher stumm (dann kappt jemand eine untätige Verbindung) oder lief bis
/// zuletzt Verkehr (dann ist es kein Untätigkeits-Timeout)? Lebenszeichen sind Leerzeilen (Proxy),
/// <c>{"keepalive":true}</c> (Provider, vom Broker durchgereicht) und zeichengleich wiederholte
/// Datenzeilen (älterer RookHub-Provider); Zeilen ohne Tiefe sind alles Übrige.</summary>
public sealed class StreamTally
{
    public int DataLines { get; private set; }
    public int Heartbeats { get; private set; }
    public int OtherLines { get; private set; }
    public DateTime? LastDataUtc { get; private set; }
    public DateTime? LastAnyUtc { get; private set; }

    private string? _lastData;

    /// <summary>Eine Datenzeile, die ZEICHENGLEICH ihre Vorgaengerin wiederholt, ist KEIN Fortschritt,
    /// sondern ein Lebenszeichen: der RookHub-Provider bis 0.478.10 sendete die letzte weitergegebene
    /// <c>info</c>-Zeile erneut, wenn nach oben laenger nichts ging, und auf fremden Rechnern laeuft er
    /// weiter. Eine rechnende Engine kann sich nicht wiederholen: <c>time</c> und <c>nodes</c> wandern
    /// mit jeder Zeile. Das zu unterscheiden ist die Grundlage des Stillstands-Waechters — ohne sie
    /// sieht eine haengende Engine genauso aus wie eine, die gerade an einer tiefen Iteration rechnet.</summary>
    public static bool IsRepeat(string? line, string? previous) => previous is not null && line == previous;

    /// <summary>Das Lebenszeichen des offiziellen Providers (seit lichess-org/external-engine d0eeb242):
    /// alle 15 s <c>{"keepalive":true}</c>, vom Broker als eigene Zeile an den Empfaenger weitergereicht.
    /// Es traegt keine Tiefe und stellt den Stillstands-Waechter deshalb NICHT neu — gezaehlt wird es
    /// trotzdem als Lebenszeichen, sonst meldete der Abriss-Log eine stumme Leitung, die keine war.</summary>
    public static bool IsKeepalive(string line) =>
        line.AsSpan().TrimStart().StartsWith("{\"keepalive\"", StringComparison.Ordinal);

    public void Note(string line, DateTime nowUtc)
    {
        LastAnyUtc = nowUtc;
        if (string.IsNullOrWhiteSpace(line) || IsKeepalive(line)) { Heartbeats++; return; }
        if (AnalysisJobStream.DepthOf(line) is not null)
        {
            if (IsRepeat(line, _lastData)) { Heartbeats++; return; }   // Wiederholung = Lebenszeichen
            _lastData = line;
            DataLines++; LastDataUtc = nowUtc; return;
        }
        OtherLines++;
    }

    /// <summary>Sekunden seit der letzten ZEILE MIT DATEN (null, wenn nie eine kam).</summary>
    public double? DataGapSeconds(DateTime nowUtc) => LastDataUtc is { } t ? (nowUtc - t).TotalSeconds : null;

    /// <summary>Sekunden seit dem letzten Byte überhaupt — inklusive Lebenszeichen.</summary>
    public double? AnyGapSeconds(DateTime nowUtc) => LastAnyUtc is { } t ? (nowUtc - t).TotalSeconds : null;
}
