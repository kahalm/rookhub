using System.Diagnostics;

namespace RookHub.Api.Services;

/// <summary>
/// Engine-Einschätzung einer Stellung (relativ zur Seite am Zug). <see cref="MateIn"/> &gt; 0 =
/// erzwungenes Matt in N Zügen für die ziehende Seite; <c>null</c> = kein Matt gefunden.
/// </summary>
public record EngineHint(string EvalText, int? MateIn, string? BestMoveUci);

/// <summary>
/// Ruft eine lokale Stockfish-CLI (UCI über stdin/stdout) auf, um eine Stellung zu bewerten.
/// Wird NUR im Import-/Reprocess-Pfad als Zusatzsignal für die Tipp-Generierung genutzt — kein
/// Laufzeit-Pfad beim Lösen. Engine-Binary kommt aus dem API-Image (siehe Dockerfile).
/// Konfiguration: <c>Stockfish:Path</c> (Default <c>stockfish</c>), <c>Stockfish:Depth</c> (Default 20).
/// </summary>
public class StockfishAnalyzer
{
    private readonly string _path;
    private readonly int _depth;
    private readonly ILogger<StockfishAnalyzer> _logger;

    public StockfishAnalyzer(IConfiguration config, ILogger<StockfishAnalyzer> logger)
    {
        _path = config["Stockfish:Path"] ?? "stockfish";
        _depth = int.TryParse(config["Stockfish:Depth"], out var d) && d is > 0 and <= 40 ? d : 20;
        _logger = logger;
    }

    /// <summary>Bewertet die Stellung; gibt <c>null</c> zurück, wenn die Engine nicht verfügbar ist
    /// oder ein Fehler/Timeout auftritt (Tipp-Generierung läuft dann ohne Engine-Signal weiter).
    /// <paramref name="setupMovesUci"/> = optionale Setup-Züge (UCI, leerzeichengetrennt), die die
    /// Engine vor der Analyse anwendet — so wird ohne Chess-Lib die echte Lösungsstellung erreicht.</summary>
    public async Task<EngineHint?> AnalyzeAsync(string fen, string? setupMovesUci = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fen)) return null;
        var positionCmd = string.IsNullOrWhiteSpace(setupMovesUci)
            ? $"position fen {fen}"
            : $"position fen {fen} moves {setupMovesUci.Trim()}";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _path,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            var lines = new List<string>();
            var readTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
                {
                    lines.Add(line);
                    if (line.StartsWith("bestmove", StringComparison.Ordinal)) break;
                }
            }, timeout.Token);

            await proc.StandardInput.WriteLineAsync("uci");
            await proc.StandardInput.WriteLineAsync("isready");
            await proc.StandardInput.WriteLineAsync("ucinewgame");
            await proc.StandardInput.WriteLineAsync(positionCmd);
            await proc.StandardInput.WriteLineAsync($"go depth {_depth}");
            await proc.StandardInput.FlushAsync();

            try
            {
                await readTask.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("StockfishAnalyzer: Timeout/abgebrochen bei FEN {Fen}", fen);
            }
            finally
            {
                if (!proc.HasExited) { try { proc.Kill(true); } catch { /* ignore */ } }
            }

            return ParseUciOutput(lines);
        }
        catch (Exception ex)
        {
            // Engine nicht installiert / Fehler → Tipp-Generierung läuft ohne Engine-Signal weiter.
            _logger.LogWarning(ex, "StockfishAnalyzer: Engine nicht verfügbar ({Path}).", _path);
            return null;
        }
    }

    /// <summary>
    /// Parst die UCI-Ausgabezeilen (letzte <c>info … score …</c> + abschließendes <c>bestmove</c>).
    /// Scores sind relativ zur Seite am Zug. Rein statisch → ohne echten Prozess testbar.
    /// </summary>
    public static EngineHint? ParseUciOutput(IReadOnlyList<string> lines)
    {
        int? scoreCp = null;
        int? mate = null;
        string? bestMove = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("info", StringComparison.Ordinal) && line.Contains(" score "))
            {
                var toks = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < toks.Length - 2; i++)
                {
                    if (toks[i] != "score") continue;
                    if (toks[i + 1] == "cp" && int.TryParse(toks[i + 2], out var cp)) { scoreCp = cp; mate = null; }
                    else if (toks[i + 1] == "mate" && int.TryParse(toks[i + 2], out var m)) { mate = m; scoreCp = null; }
                    break;
                }
            }
            else if (line.StartsWith("bestmove", StringComparison.Ordinal))
            {
                var toks = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (toks.Length >= 2 && toks[1] != "(none)") bestMove = toks[1];
            }
        }

        if (scoreCp == null && mate == null && bestMove == null) return null;

        string evalText;
        if (mate != null)
        {
            evalText = mate.Value == 0 ? "#" : $"#{(mate.Value > 0 ? "" : "-")}{Math.Abs(mate.Value)}";
        }
        else if (scoreCp != null)
        {
            var pawns = scoreCp.Value / 100.0;
            evalText = (pawns >= 0 ? "+" : "") + pawns.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            evalText = "";
        }

        return new EngineHint(evalText, mate, bestMove);
    }
}
