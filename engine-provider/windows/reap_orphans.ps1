# Beendet Stockfish-Prozesse, deren Elternprozess nicht mehr existiert (Zombies).
#
# Hintergrund: example-provider.py startet die Engine unter Windows ueber
# subprocess.Popen(engine_command, shell=True) und beendet sie beim KEEP_ALIVE-Timeout
# per terminate(). Unter Windows toetet das nur die Shell-Huelle (cmd.exe), die den
# eigentlichen Stockfish-Prozess gestartet hat - der ueberlebt als Waise mit 0% CPU,
# belegt aber dauerhaft RAM. Passiert bei jedem Timeout, unabhaengig vom KEEP_ALIVE-Wert;
# ein hoeherer Wert verlangsamt nur, wie oft das Leck ausgeloest wird.
#
# Als wiederkehrende Aufgabe registrieren, siehe README fuer das Register-ScheduledTask-Beispiel.

$reapLog = "C:\stockfish\reaper.log"
$enginePattern = "stockfish-windows-x86-64-*.exe"

$candidates = Get-CimInstance Win32_Process -Filter "Name LIKE '$enginePattern'"
$killed = 0

foreach ($proc in $candidates) {
    $parentExists = $null -ne (Get-CimInstance Win32_Process -Filter "ProcessId=$($proc.ParentProcessId)" -ErrorAction SilentlyContinue)
    if (-not $parentExists) {
        try {
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop
            "$(Get-Date -Format o) [reaper] Waise beendet: PID $($proc.ProcessId) ($($proc.Name)), erstellt $($proc.CreationDate)" | Out-File -FilePath $reapLog -Append -Encoding utf8
            $killed++
        } catch {
            "$(Get-Date -Format o) [reaper] Konnte PID $($proc.ProcessId) nicht beenden: $($_.Exception.Message)" | Out-File -FilePath $reapLog -Append -Encoding utf8
        }
    }
}

if ($killed -eq 0) {
    "$(Get-Date -Format o) [reaper] Lauf ohne Befund ($($candidates.Count) Engine-Prozess(e) geprueft, alle mit lebendem Elternteil)" | Out-File -FilePath $reapLog -Append -Encoding utf8
}
