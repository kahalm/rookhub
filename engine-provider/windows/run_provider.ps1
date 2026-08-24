# Startet den Lichess External-Engine-Provider dauerhaft und startet ihn automatisch neu,
# wenn er (oder die Engine darunter) abstuerzt. Siehe engine-provider/README.md, Abschnitt
# "Robuster Dauerbetrieb (Auto-Restart + Aufraeumen)" fuer den Hintergrund.
#
# Vor dem ersten Start anpassen:
#
# Macht DIESEN Prozess immun gegen Strg+C-artige Konsolen-Signale. Ohne das kann der Wrapper mit
# STATUS_CONTROL_C_EXIT (0xC000013A) sterben, obwohl niemand Strg+C gedrueckt hat - ein
# GenerateConsoleCtrlEvent-Broadcast erreicht offenbar auch versteckte (-WindowStyle Hidden)
# Konsolen, wenn mehrere solche Prozesse dieselbe Konsolensitzung teilen. Per AttachConsole +
# GenerateConsoleCtrlEvent reproduzierbar, per SetConsoleCtrlHandler(NULL, true) zuverlaessig
# behoben - das ist die dokumentierte Win32-Standardtechnik dafuer.
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class CtrlCImmune {
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetConsoleCtrlHandler(IntPtr HandlerRoutine, bool Add);
    public static void Ignore() { SetConsoleCtrlHandler(IntPtr.Zero, true); }
}
"@ -ErrorAction SilentlyContinue
try { [CtrlCImmune]::Ignore() } catch {}

$pythonExe = "python.exe"                                       # ggf. Vollpfad, falls nicht auf PATH
$script    = "C:\stockfish\example-provider.py"
$engine    = "C:\stockfish\stockfish-windows-x86-64-bmi2.exe"   # passende Variante siehe README-Tabelle;
                                                                  # in einer VM zuerst pruefen, siehe README!
$name      = "RookHub PC"
$maxThreads = 6                                                  # ein bis zwei Kerne unter der Kernzahl lassen
$maxHash    = 2048                                                # MiB

$wrapperLog = "C:\stockfish\wrapper.log"
$stdoutLog  = "C:\stockfish\provider_stdout.log"
$stderrLog  = "C:\stockfish\provider_stderr.log"

$argList = @(
    "`"$script`"",
    "--engine", "`"$engine`"",
    "--name", "`"$name`"",
    "--max-threads", "$maxThreads",
    "--max-hash", "$maxHash",
    "--keep-alive", "3600",
    "--log-level", "info"
)

while ($true) {
    "$(Get-Date -Format o) [wrapper] starte example-provider.py" | Out-File -FilePath $wrapperLog -Append -Encoding utf8

    # Direktes Redirect auf OS-Ebene (Start-Process), kein PowerShell-Textstream dazwischen -
    # sonst entweder ErrorRecord-Rauschen (2>&1 | Out-File) oder falsches Encoding (native *>>).
    # stdout/stderr sind waehrend der Laufzeit live mitlesbar, werden pro Neustart ueberschrieben.
    $proc = Start-Process -FilePath $pythonExe -ArgumentList $argList `
        -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog `
        -NoNewWindow -PassThru -Wait

    "$(Get-Date -Format o) [wrapper] Prozess beendet (Exit $($proc.ExitCode)) - Neustart in 10s" | Out-File -FilePath $wrapperLog -Append -Encoding utf8
    Start-Sleep -Seconds 10
}
