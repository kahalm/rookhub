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
$maxLogBytes = 1MB

# FALLE (behoben): Die Auswahl lief frueher ueber einen WQL-Filter
#   Get-CimInstance Win32_Process -Filter "Name LIKE 'stockfish-windows-x86-64-*.exe'"
# WQL ist aber nicht PowerShell. Dort kennt LIKE als Platzhalter '%' und '_' - ein '*' ist ein
# GEWOEHNLICHES Zeichen. Der Filter traf deshalb NIE etwas, und das Skript meldete brav
# "Lauf ohne Befund", waehrend die Waisen weiter RAM belegten: es sah nach Ueberwachung aus
# und war keine. Gefiltert wird jetzt mit PowerShells -like (dort ist '*' richtig) ueber die
# ohnehin geholte Prozessliste.
$enginePattern = 'stockfish-windows-x86-64-*.exe'

function Write-ReapLog([string]$message) {
    # Ohne Rotation waechst die Datei bei einem 15-Minuten-Takt endlos.
    if ((Test-Path $reapLog) -and ((Get-Item $reapLog).Length -gt $maxLogBytes)) {
        Move-Item -Path $reapLog -Destination "$reapLog.1" -Force -ErrorAction SilentlyContinue
    }
    "$(Get-Date -Format o) [reaper] $message" | Out-File -FilePath $reapLog -Append -Encoding utf8
}

# EIN Aufruf statt einer Abfrage je Kandidat: die Elternsuche braucht ohnehin alle Prozesse.
$allProcesses = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
$byPid = @{}
foreach ($p in $allProcesses) { $byPid[[int]$p.ProcessId] = $p }

$candidates = @($allProcesses | Where-Object { $_.Name -like $enginePattern })
$killed = 0

foreach ($proc in $candidates) {
    $parent = $byPid[[int]$proc.ParentProcessId]

    # Die PID allein genuegt NICHT: Windows vergibt PIDs zuegig neu. Wurde die PID des toten
    # cmd.exe inzwischen von irgendeinem fremden Prozess belegt, sieht die Waise fuer immer
    # adoptiert aus - und ueberlebt jeden weiteren Lauf. Ein echter Elternteil ist AELTER als
    # sein Kind; ist der Fund juenger, ist es ein Namensvetter auf recycelter PID.
    $parentAlive = $false
    if ($null -ne $parent) {
        $parentAlive = ($null -ne $parent.CreationDate) -and ($null -ne $proc.CreationDate) `
                       -and ($parent.CreationDate -le $proc.CreationDate)
    }

    if (-not $parentAlive) {
        try {
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop
            Write-ReapLog "Waise beendet: PID $($proc.ProcessId) ($($proc.Name)), erstellt $($proc.CreationDate)"
            $killed++
        } catch {
            Write-ReapLog "Konnte PID $($proc.ProcessId) nicht beenden: $($_.Exception.Message)"
        }
    }
}

if ($killed -eq 0) {
    Write-ReapLog "Lauf ohne Befund ($($candidates.Count) Engine-Prozess(e) geprueft, alle mit lebendem Elternteil)"
}
