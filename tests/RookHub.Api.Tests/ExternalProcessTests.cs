using System.Diagnostics;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.Tests;

/// <summary>R3-006: der gemeinsame Starter für Kommandozeilen-Werkzeuge — Exit-Code, Zeitschranke, Abbruch von außen,
/// gedeckelte Ausgabe, fehlendes Programm.</summary>
public class ExternalProcessTests
{
    private static bool Unix => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    [Fact]
    public async Task ExitCodeAndOutput_AreReturned()
    {
        if (!Unix) return;
        var run = await ExternalProcess.RunAsync("/bin/sh", ["-c", "echo hallo; echo fehler >&2; exit 3"], TimeSpan.FromSeconds(10), default);
        Assert.Equal(3, run.ExitCode);
        Assert.Equal("hallo\n", run.StdOut);
        Assert.Equal("fehler\n", run.StdErr);
        Assert.False(run.Succeeded);
        Assert.True((await ExternalProcess.RunAsync("/bin/sh", ["-c", "exit 0"], TimeSpan.FromSeconds(10), default)).Succeeded);
    }

    [Fact]
    public async Task Timeout_KillsTheProcess()
    {
        if (!Unix) return;
        var clock = Stopwatch.StartNew();
        var run = await ExternalProcess.RunAsync("/bin/sh", ["-c", "sleep 30"], TimeSpan.FromMilliseconds(300), default);
        Assert.True(run.TimedOut);
        Assert.Equal(-2, run.ExitCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(25), $"dauerte {clock.Elapsed}");   // sleep 30 wäre länger
    }

    /// <summary>Lebt der Prozess noch (nicht beendet und kein Zombie)?</summary>
    private static bool Alive(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat[(stat.LastIndexOf(')') + 2)] != 'Z';
        }
        catch (IOException) { return false; }
    }

    [Fact]
    public async Task OuterCancellation_KillsTheProcess_AndThrows()
    {
        if (!OperatingSystem.IsLinux()) return;
        var pidFile = Path.Combine(Path.GetTempPath(), "rh-extproc-" + Guid.NewGuid().ToString("N"));
        using var cts = new CancellationTokenSource();
        // Abbrechen, sobald der Prozess läuft (er schreibt seine PID und wird dann selbst zu „sleep 60“).
        var cancel = Task.Run(async () =>
        {
            while (!File.Exists(pidFile) || new FileInfo(pidFile).Length == 0) await Task.Delay(50);
            cts.Cancel();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExternalProcess.RunAsync("/bin/sh", ["-c", $"echo $$ > '{pidFile}'; exec sleep 60"], TimeSpan.FromSeconds(120), cts.Token));
        await cancel;
        var pid = int.Parse(File.ReadAllText(pidFile).Trim());
        File.Delete(pidFile);
        // Ohne Kill liefe „sleep 60“ weiter; mit Kill ist er weg (das Abräumen kann auf einer vollen Maschine dauern).
        var clock = Stopwatch.StartNew();
        while (Alive(pid) && clock.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(100);
        Assert.False(Alive(pid), "Kindprozess lief nach dem Abbruch weiter");
    }

    [Fact]
    public async Task Output_IsCapped_ButDrainedCompletely()
    {
        if (!Unix) return;
        var run = await ExternalProcess.RunAsync("/bin/sh", ["-c", "head -c 3000000 /dev/zero | tr '\\0' a"], TimeSpan.FromSeconds(30), default);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(ExternalProcess.MaxOutputChars, run.StdOut.Length);
    }

    [Fact]
    public void MissingProgram_IsNotFound_Sync()
    {
        var run = ExternalProcess.Run("/nicht/vorhanden/werkzeug", [], TimeSpan.FromSeconds(5), default);
        Assert.True(run.NotFound);
        Assert.Equal(-1, run.ExitCode);
        Assert.Contains("not available", run.StdErr);
    }
}
