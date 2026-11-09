using System.Diagnostics;
using System.Text;

namespace RookHub.Api.Services;

/// <summary>Ergebnis eines <see cref="ExternalProcess"/>-Laufs. <see cref="NotFound"/>: das Programm ließ sich nicht starten
/// (fehlt im Image); <see cref="TimedOut"/>: nach der Zeitschranke getötet. In beiden Fällen ist <see cref="ExitCode"/>
/// −1 bzw. −2 (wie die früheren Einzel-Starter es meldeten).</summary>
public sealed record ExternalProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut = false, bool NotFound = false)
{
    public bool Succeeded => !TimedOut && !NotFound && ExitCode == 0;
}

/// <summary>
/// EIN Starter für kurzlebige Kommandozeilen-Werkzeuge (R3-006: ddjvu, pdftoppm, pdfinfo, …) statt vier eigener Abläufe mit
/// unterschiedlichem Timeout-/Kill-Verhalten. Regeln:
/// <list type="bullet">
/// <item>Argumente nur über <see cref="ProcessStartInfo.ArgumentList"/> (keine Shell, kein Quoting).</item>
/// <item>Zeitschranke → Prozessbaum töten, <see cref="ExternalProcessResult.TimedOut"/>.</item>
/// <item>Abbruch des ÄUSSEREN Tokens (Herunterfahren) → ebenfalls töten, dann <see cref="OperationCanceledException"/>
/// (vorher lief der Kindprozess in den async-Startern weiter).</item>
/// <item>stdout/stderr werden ganz gelesen (sonst blockiert ein volles Pipe-Puffer das Werkzeug), aber nur bis
/// <see cref="MaxOutputChars"/> behalten.</item>
/// </list>
/// Langlebige Sitzungen (UCI-Engines: <c>UciEngineSession</c>, <c>ScoresheetPlausibility</c>) haben einen eigenen Lebenszyklus
/// und gehören nicht hierher.
/// </summary>
public static class ExternalProcess
{
    /// <summary>So viele Zeichen je Strom werden behalten (pdfinfo über 1 000 Seiten ≈ 60 KB).</summary>
    public const int MaxOutputChars = 1_000_000;

    public static async Task<ExternalProcessResult> RunAsync(string exe, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = new Process { StartInfo = psi };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new ExternalProcessResult(-1, string.Empty, $"{exe} not available: {ex.Message}", NotFound: true);
        }
        // Lesen ohne Token: die Ströme enden, sobald der Prozess (auch getötet) weg ist. Eigene Tasks, damit ein Lesen,
        // das synchron fertig wird, den Aufrufer nicht vor dem Warten aufhält.
        var stdout = Task.Run(() => ReadCappedAsync(process.StandardOutput));
        var stderr = Task.Run(() => ReadCappedAsync(process.StandardError));
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (ct.IsCancellationRequested) throw;
            return new ExternalProcessResult(-2, string.Empty, "timeout", TimedOut: true);
        }
        return new ExternalProcessResult(process.ExitCode, await stdout, await stderr);
    }

    /// <summary>Synchron für Aufrufer ohne async-Pfad (blockiert den Thread wie zuvor <c>WaitForExit</c>).</summary>
    public static ExternalProcessResult Run(string exe, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
        => RunAsync(exe, args, timeout, ct).GetAwaiter().GetResult();

    private static void Kill(Process process)
    {
        try
        {
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);
        }
        catch (Exception treeFailed) when (treeFailed is not OutOfMemoryException)
        {
            // Den Baum aufzuzählen kann scheitern (z. B. /proc nicht ganz lesbar) — dann wenigstens den Prozess selbst.
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { /* schon weg */ }
            catch (System.ComponentModel.Win32Exception) { /* schon weg */ }
        }
    }

    private static async Task<string> ReadCappedAsync(StreamReader reader)
    {
        var sb = new StringBuilder();
        var buf = new char[8192];
        int n;
        try
        {
            while ((n = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
                if (sb.Length < MaxOutputChars) sb.Append(buf, 0, Math.Min(n, MaxOutputChars - sb.Length));
        }
        catch (IOException) { /* Strom nach Kill geschlossen */ }
        catch (ObjectDisposedException) { /* dito */ }
        return sb.ToString();
    }
}
