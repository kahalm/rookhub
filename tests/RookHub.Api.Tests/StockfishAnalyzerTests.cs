using Microsoft.Extensions.Configuration;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Reine Parse-Logik des UCI-Outputs (ohne echten Engine-Prozess).</summary>
public class StockfishAnalyzerTests
{
    [Fact]
    public void ParseUciOutput_Mate_ReturnsHashAndMateIn()
    {
        var lines = new[]
        {
            "info depth 1 score cp 30 pv e2e4",
            "info depth 12 score mate 3 pv d1h5 g8f6 h5f7",
            "bestmove d1h5 ponder g8f6",
        };

        var hint = StockfishAnalyzer.ParseUciOutput(lines);

        Assert.NotNull(hint);
        Assert.Equal("#3", hint!.EvalText);
        Assert.Equal(3, hint.MateIn);
        Assert.Equal("d1h5", hint.BestMoveUci);
    }

    [Fact]
    public void ParseUciOutput_Centipawns_FormatsPawnsAndKeepsLastScore()
    {
        var lines = new[]
        {
            "info depth 10 score cp 80 pv g1f3",
            "info depth 20 score cp 152 pv e2e4 e7e5",
            "bestmove e2e4",
        };

        var hint = StockfishAnalyzer.ParseUciOutput(lines);

        Assert.NotNull(hint);
        Assert.Equal("+1.5", hint!.EvalText);   // 152 cp → +1.5, letzter Score gewinnt
        Assert.Null(hint.MateIn);
        Assert.Equal("e2e4", hint.BestMoveUci);
    }

    [Fact]
    public void ParseUciOutput_NegativeCp_HasMinusSign()
    {
        var lines = new[] { "info depth 18 score cp -240 pv c7c5", "bestmove c7c5" };

        var hint = StockfishAnalyzer.ParseUciOutput(lines);

        Assert.Equal("-2.4", hint!.EvalText);
    }

    [Fact]
    public void ParseUciOutput_BestmoveNone_NoCrash()
    {
        var lines = new[] { "info depth 1 score mate 0", "bestmove (none)" };

        var hint = StockfishAnalyzer.ParseUciOutput(lines);

        Assert.NotNull(hint);
        Assert.Null(hint!.BestMoveUci);
        Assert.Equal(0, hint.MateIn);
    }

    [Fact]
    public void ParseUciOutput_Empty_ReturnsNull()
    {
        Assert.Null(StockfishAnalyzer.ParseUciOutput(System.Array.Empty<string>()));
    }

    [Fact]
    public async Task AnalyzeAsync_Timeout_QuiescesReaderAndParsesPartialOutput()
    {
        // Regression: nach dem Timeout lief der Reader-Task weiter und mutierte `lines`,
        // WÄHREND ParseUciOutput iterierte (Race → InvalidOperationException → null) bzw. las
        // nach dem Dispose aus dem geschlossenen stdout. Jetzt wird der Reader vor dem Parsen
        // quiesziert; das bis dahin Gelesene wird sauber ausgewertet.
        if (!OperatingSystem.IsLinux()) return;   // Fake-Engine ist ein Shell-Skript

        var script = Path.Combine(Path.GetTempPath(), $"fake-stockfish-{Guid.NewGuid():N}.sh");
        // SOFORT einen großen Burst 'info'-Zeilen ausgeben (deterministisch auch unter paralleler
        // Test-Last: der Reader hat vor dem Timeout garantiert Zeilen), dann endlos weiter OHNE je
        // 'bestmove' → erzwingt den Timeout-Pfad. NIE 'bestmove' = der Reader bricht nicht selbst ab.
        await File.WriteAllTextAsync(script,
            "#!/bin/sh\nfor i in $(seq 1 200); do echo 'info depth 1 score cp 42 pv e2e4'; done\n" +
            "while true; do echo 'info depth 1 score cp 42 pv e2e4'; sleep 0.05; done\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Stockfish:Path"] = script })
                .Build();
            var analyzer = new StockfishAnalyzer(config,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<StockfishAnalyzer>.Instance)
            { TimeoutMs = 2000 };

            var hint = await analyzer.AnalyzeAsync("8/8/8/8/8/8/8/K6k w - - 0 1");

            Assert.NotNull(hint);
            Assert.Equal("+0.4", hint!.EvalText);   // 42 cp aus dem Partial-Output
        }
        finally
        {
            File.Delete(script);
        }
    }
}
