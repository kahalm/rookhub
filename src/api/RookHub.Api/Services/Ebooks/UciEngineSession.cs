using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Ebooks;

/// <summary>Ergebnis einer Suche: Bewertung für die Seite am Zug (cp; Matt in n = ±(10000 − n)) und Hauptvariante (UCI).</summary>
public sealed record UciResult(int Score, bool IsMate, IReadOnlyList<string> Pv);

/// <summary>Eine Engine, die Stellungen bewertet — als Schnittstelle, damit die Prüflogik ohne Stockfish testbar ist.</summary>
public interface IPositionEvaluator : IAsyncDisposable
{
    Task<UciResult?> EvaluateAsync(string fen, IReadOnlyList<string>? moves, int moveTimeMs, CancellationToken ct);
}

/// <summary>
/// EINE dauerhafte Stockfish-Sitzung (UCI über stdin/stdout) für viele Stellungen hintereinander — der Prüfdienst bewertet
/// je Buch hunderte Stellungen; ein Prozess je Stellung (wie <c>StockfishAnalyzer</c>) kostete jedes Mal Start + Init.
/// Niedrige Priorität (nice), 1 Thread, 64 MB Hash: der Dienst soll Jellyfin und die API nicht ausbremsen.
/// </summary>
public sealed class UciEngineSession : IPositionEvaluator
{
    private static readonly Regex FenChars = new(@"\A[A-Za-z0-9/ \-]{1,120}\z", RegexOptions.CultureInvariant);
    private static readonly Regex UciMove = new(@"\A[a-h][1-8][a-h][1-8][qrbn]?\z", RegexOptions.CultureInvariant);

    private readonly Process _proc;
    private readonly ILogger _logger;

    private UciEngineSession(Process proc, ILogger logger) { _proc = proc; _logger = logger; }

    public static async Task<UciEngineSession?> StartAsync(string path, ILogger logger, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(path)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            var proc = Process.Start(psi);
            if (proc == null) return null;
            try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { /* nicht überall erlaubt */ }
            var s = new UciEngineSession(proc, logger);
            await s.SendAsync("uci");
            await s.WaitForAsync("uciok", ct, TimeSpan.FromSeconds(10));
            await s.SendAsync("setoption name Threads value 1");
            await s.SendAsync("setoption name Hash value 64");
            await s.SendAsync("isready");
            await s.WaitForAsync("readyok", ct, TimeSpan.FromSeconds(10));
            return s;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "E-Book-Prüfung: Stockfish nicht startbar ({Path})", path);
            return null;
        }
    }

    private Task SendAsync(string line) => _proc.StandardInput.WriteLineAsync(line).ContinueWith(_ => _proc.StandardInput.FlushAsync()).Unwrap();

    private async Task<List<string>> WaitForAsync(string prefix, CancellationToken ct, TimeSpan timeout)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var lines = new List<string>();
        while (true)
        {
            var line = await _proc.StandardOutput.ReadLineAsync(cts.Token) ?? throw new IOException("Stockfish beendet");
            lines.Add(line);
            if (line.StartsWith(prefix, StringComparison.Ordinal)) return lines;
        }
    }

    public async Task<UciResult?> EvaluateAsync(string fen, IReadOnlyList<string>? moves, int moveTimeMs, CancellationToken ct)
    {
        if (!FenChars.IsMatch(fen.Trim())) return null;
        if (moves != null && !moves.All(UciMove.IsMatch)) return null;
        var pos = moves is { Count: > 0 } ? $"position fen {fen.Trim()} moves {string.Join(' ', moves)}" : $"position fen {fen.Trim()}";
        await SendAsync(pos);
        await SendAsync($"go movetime {Math.Clamp(moveTimeMs, 20, 10_000)}");
        var lines = await WaitForAsync("bestmove", ct, TimeSpan.FromMilliseconds(moveTimeMs + 10_000));
        return Parse(lines);
    }

    /// <summary>Letzte <c>info … score … pv …</c>-Zeile (MultiPV 1). Kein Score (Matt/Patt auf dem Brett) → <c>null</c>.</summary>
    public static UciResult? Parse(IReadOnlyList<string> lines)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var l = lines[i];
            if (!l.StartsWith("info ", StringComparison.Ordinal) || !l.Contains(" score ")) continue;
            var m = Regex.Match(l, @" score (cp|mate) (-?\d+)");
            if (!m.Success) continue;
            var n = int.Parse(m.Groups[2].Value);
            var mate = m.Groups[1].Value == "mate";
            var score = mate ? (n > 0 ? 10000 - n : -10000 - n) : n;
            var pv = Regex.Match(l, @" pv (.+)$");
            var moves = pv.Success ? pv.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList() : new List<string>();
            return new UciResult(score, mate, moves);
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        try { await SendAsync("quit"); _proc.WaitForExit(2000); } catch { /* weg */ }
        try { if (!_proc.HasExited) _proc.Kill(true); } catch { /* weg */ }
        _proc.Dispose();
    }
}
