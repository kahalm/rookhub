namespace RookHub.Api.Services;

/// <summary>
/// Wo die Stockfish-CLI liegt — EINE Regel für den Tipp-Generator (<see cref="StockfishAnalyzer"/>) und die Engine-Prüfung
/// der Formular-Lesung (<see cref="StockfishScoresheetEngine"/>): die Einstellung, sonst <see cref="DebianPath"/>, sonst
/// <c>stockfish</c> aus dem PATH.
///
/// <para>Das API-Image installiert das Debian-Paket <c>stockfish</c>, und das legt die Datei nach <c>/usr/games</c> —
/// das steht im Container NICHT im PATH. Bis 0.646.1 rief der Tipp-Generator schlicht <c>stockfish</c> auf, fand nichts
/// und erzeugte jeden Tipp ohne Engine-Signal (gefunden 2026-10-03 beim Bau der Formular-Prüfung).</para>
/// </summary>
public static class StockfishPath
{
    /// <summary>Wohin das Debian-Paket die Engine legt.</summary>
    public const string DebianPath = "/usr/games/stockfish";

    public static string Resolve(string? configured, Func<string, bool>? exists = null)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        return (exists ?? File.Exists)(DebianPath) ? DebianPath : "stockfish";
    }
}
