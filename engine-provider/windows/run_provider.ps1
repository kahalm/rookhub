# Startet den Engine-Provider dauerhaft - eine LIVE-Engine und mehrere HINTERGRUND-Engines aus
# EINEM Skript - und startet jeden Prozess einzeln neu, wenn er (oder die Engine darunter)
# abstuerzt. Vorgabe ist der direkte Weg zu RookHub; ueber Lichess geht es weiter, siehe
# $rookhubUrl. Hintergrund: engine-provider/README.md, Abschnitte "Auf Windows" und
# "Robuster Dauerbetrieb (Auto-Restart + Aufraeumen)".
#
# Macht DIESEN Prozess immun gegen Strg+C-artige Konsolen-Signale. Ohne das kann der Wrapper mit
# STATUS_CONTROL_C_EXIT (0xC000013A) sterben, obwohl niemand Strg+C gedrueckt hat - ein
# GenerateConsoleCtrlEvent-Broadcast erreicht offenbar auch versteckte (-WindowStyle Hidden)
# Konsolen, wenn mehrere solche Prozesse dieselbe Konsolensitzung teilen. Per AttachConsole +
# GenerateConsoleCtrlEvent reproduzierbar, per SetConsoleCtrlHandler(NULL, true) zuverlaessig
# behoben - das ist die dokumentierte Win32-Standardtechnik dafuer.
# -At "Mo 09:00" rechnet nur nach, was der Zeitplan zu diesem Zeitpunkt vorsieht, und startet
# nichts. Gedacht zum Pruefen einer frisch geschriebenen Regel:
#     powershell -NoProfile -ExecutionPolicy Bypass -File run_provider.ps1 -At "Mo 09:00"
param([string]$At)

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

# ===========================================================================
#  EINSTELLUNGEN
# ===========================================================================

# DIREKT MIT ROOKHUB (empfohlen): Adresse von RookHub - fuer Anmeldung UND Arbeit. Leer = ueber
# Lichess. Der Token kommt in beiden Faellen aus der Umgebungsvariable LICHESS_API_TOKEN (der
# Provider kennt nur diesen Namen): direkt = der RookHub-API-Token (rkh_..., Profil -> API-Tokens
# -> Bereich "Engine"), ueber Lichess = ein Lichess-Token mit engine:read + engine:write.
# Einmalig setzen:
#   [Environment]::SetEnvironmentVariable("LICHESS_API_TOKEN", "rkh_...", "User")
# BEIDES (RookHub direkt UND lichess.org): ein Prozess = ein Ziel. Fuer das zweite Ziel eine Kopie
# dieses Skripts anlegen ($rookhubUrl leer, eigener $logDir, eigene Namen) und den Lichess-Token
# nur in dieser Kopie setzen, vor dem Start:  $env:LICHESS_API_TOKEN = "lip_..."
$rookhubUrl = "https://rookhub.oberschmid.homes"

$pythonExe = "python.exe"                                       # ggf. Vollpfad, falls nicht auf PATH
$script    = "C:\stockfish\example-provider.py"
$engine    = "C:\stockfish\stockfish-windows-x86-64-bmi2.exe"   # passende Variante siehe README-Tabelle;
                                                                #   in einer VM zuerst pruefen, siehe README!
$logDir    = "C:\stockfish\logs"

# --- Die LIVE-Engine (genau eine) -----------------------------------------
# Hier wartet ein Mensch auf EINE Stellung, also zaehlt die Zeit bis zum Ergebnis und nicht der
# Durchsatz: sie bekommt die meisten Kerne. RookHub pausiert die Hintergrund-Engines, sobald sie
# rechnet, die beiden Zahlen duerfen sich also ueberlappen.
$liveName    = "RookHub PC"
$liveThreads = 24
$liveHash    = 4096      # MiB

# --- Die HINTERGRUND-Engines (alle gleich eingestellt) --------------------
# Fuer Analyse-Auftraege zaehlt der DURCHSATZ, und der zerfaellt in unabhaengige Stellungen:
# VIELE Engines mit WENIGEN Threads schlagen eine mit vielen. Gemessen auf 8 Kernen (Tiefe 20,
# 5 Linien): eine Suche wird von 1 auf 8 Threads nur um den Faktor 1,13 schneller, vier Suchen
# nebeneinander um fast das Dreifache.
#
# 16 x 4 Threads fuellen 64 Kerne - und 16 ist gleichzeitig die Hoechstzahl, die RookHub als
# Hintergrund-Liste annimmt. Auf einem Rechner mit weniger Kernen: $bgCount = Kerne / 4.
# ARBEITSSPEICHER: $bgCount * $bgHash + $liveHash, hier 16 * 1024 + 4096 = rund 20 GiB. Unter
# 32 GiB RAM $bgHash auf 512 setzen - eine zu grosse Hashtabelle laesst Windows auslagern, und
# dann rechnet die Engine langsamer als mit einer kleinen.
$bgName    = "RookHub PC Hintergrund"
$bgCount   = 16
$bgThreads = 4
$bgHash    = 1024        # MiB je Engine

# Der Wachhund des Providers vergleicht laufend gegen den "zuletzt benutzt"-Zeitstempel, den er
# ERST am Stream-Ende setzt: eine Suche, die laenger dauert, wird mitten im Rechnen beendet
# ("Terminating idle engine"). Fuer Hintergrund-Auftraege ist das fatal (ab Tiefe 29 mit 5 Linien
# dauert EINE Iteration laenger als die Vorgabe 300 s), deshalb 24 Stunden. Nebeneffekt: der
# Zombie-Leak unten wird seltener ausgeloest.
$keepAlive = 86400
$logLevel  = "info"

# Sekunden zwischen zwei Anmeldungen beim Start. Gegen Lichess ist das Pflicht (13 Registrierungen
# im selben Augenblick = 429 und zeitweise IP-Sperre); direkt mit RookHub nur Hoeflichkeit.
$startDelay = 2

# --- ZEITPLAN: wann wie viel gerechnet wird --------------------------------
# Leer = immer alles. Sonst Regeln, mit ";" getrennt, je Regel "<Tage> <von>-<bis> <Prozent>":
#
#   $schedule = "Mo-Do 08:00-17:00 0%; Fr 08:00-14:00 25%; Sa,So 100%"
#
# Tage als mo di mi do fr sa so (oder mon tue wed thu fri sat sun), als Bereich (mo-do), als Liste
# (sa,so) oder "*" fuer jeden Tag. Zeiten HH:MM, die Spanne darf ueber Mitternacht gehen
# (22:00-06:00); ohne Uhrzeit meint eine Regel den ganzen Tag ("Sa,So 100%"). Die ERSTE passende
# Regel gilt, passt keine, wird mit 100 % gerechnet.
#
# Was der Prozentsatz bedeutet: 0 = alle Provider aus. Die Engines verschwinden dann aus RookHub
# wie bei einem ausgeschalteten Rechner, laufende Auftraege brechen ab und werden neu vergeben.
# 100 = alle an. Dazwischen bleibt die LIVE-Engine an, und der Anteil gilt den Hintergrund-Engines:
# dort wartet kein Mensch auf das Ergebnis, und dort liegt die Dauerlast.
$schedule = ""
$scheduleTick = 20     # Sekunden zwischen zwei Blicken auf die Uhr

# Worauf sich der Prozentsatz bezieht:
#   "background" (Vorgabe) - die LIVE-Engine bleibt an, solange ueberhaupt gerechnet wird, und der
#                            Anteil gilt nur den Hintergrund-Engines. Dort wartet ein Mensch auf
#                            eine Stellung, dafuer soll der Rechner jederzeit ansprechbar sein.
#   "all"                  - der Anteil gilt ALLEN Engines zusammen, die Live-Engine eingeschlossen.
#                            Bei 50 % von 17 laufen also 9 Prozesse statt 1 + 8. Gedacht fuer einen
#                            Rechner, auf dem auch die Live-Engine zurueckstecken soll.
# 0 % haelt in beiden Faellen alles an.
$scheduleScope = "background"

# ===========================================================================

$wrapperLog = Join-Path $logDir "wrapper.log"
New-Item -ItemType Directory -Path $logDir -Force -ErrorAction SilentlyContinue | Out-Null

function Get-DayNumber([string]$name) {
    switch -Regex ($name.ToLower()) {
        '^(mo|mon|montag|monday)$'            { return 1 }
        '^(di|die|tue|tuesday|dienstag)$'     { return 2 }
        '^(mi|mit|wed|wednesday|mittwoch)$'   { return 3 }
        '^(do|don|thu|thursday|donnerstag)$'  { return 4 }
        '^(fr|fre|fri|friday|freitag)$'       { return 5 }
        '^(sa|sam|sat|saturday|samstag)$'     { return 6 }
        '^(so|son|sun|sunday|sonntag)$'       { return 7 }
        default                               { return 0 }
    }
}

# "08:00" -> Minuten seit Mitternacht, -1 bei Unsinn. 24:00 ist als ENDE erlaubt.
function Get-Minutes([string]$t) {
    if ($t -notmatch '^(\d{1,2}):(\d{2})$') { return -1 }
    $h = [int]$Matches[1]; $m = [int]$Matches[2]
    if ($h -gt 24 -or $m -gt 59 -or ($h -eq 24 -and $m -ne 0)) { return -1 }
    return $h * 60 + $m
}

# Passt der Wochentag auf die Tage-Angabe? $null = die Angabe ist kaputt.
function Test-DayMatch([string]$spec, [int]$day) {
    if ($spec -match '^\*$|^(daily|all|taeglich|täglich|immer)$') { return $true }
    foreach ($part in $spec.Split(',')) {
        if (-not $part) { continue }
        if ($part.Contains('-')) {
            $a = Get-DayNumber $part.Split('-')[0]
            $b = Get-DayNumber $part.Split('-')[1]
            if ($a -eq 0 -or $b -eq 0) { return $null }
            if ($a -le $b) { if ($day -ge $a -and $day -le $b) { return $true } }
            # Bereich ueber das Wochenende, z. B. fr-mo
            elseif ($day -ge $a -or $day -le $b) { return $true }
        } else {
            $a = Get-DayNumber $part
            if ($a -eq 0) { return $null }
            if ($day -eq $a) { return $true }
        }
    }
    return $false
}

# Jede Regel einmal zerlegen. Gibt die Fehlermeldung zurueck oder $null, wenn alles passt —
# geprueft wird BEIM START, nicht erst nachts, wenn die Regel zum ersten Mal greifen wuerde.
function Test-Schedule([string]$plan) {
    if (-not $plan) { return $null }
    $seen = 0
    foreach ($rule in $plan.Split(';')) {
        $r = $rule.Trim()
        if (-not $r) { continue }
        $f = $r -split '\s+'
        # Kurzform ohne Uhrzeit: "Sa,So 100%" meint den ganzen Tag.
        if ($f.Count -eq 2 -and $f[1] -match '^\d+%?$') { $f = @($f[0], '00:00-24:00', $f[1]) }
        if ($f.Count -ne 3) { return "Regel '$r' muss '<Tage> <von>-<bis> <Prozent>' sein, z. B. 'Mo-Do 08:00-17:00 0%'." }
        if ($null -eq (Test-DayMatch $f[0] 1)) { return "'$($f[0])' ist keine Tagesangabe (mo di mi do fr sa so, Bereiche mo-do, Listen sa,so, * fuer jeden Tag)." }
        $span = $f[1].Split('-')
        if ($span.Count -ne 2 -or (Get-Minutes $span[0]) -lt 0 -or (Get-Minutes $span[1]) -lt 0) { return "'$($f[1])' ist keine Zeitspanne HH:MM-HH:MM." }
        $pct = $f[2].TrimEnd('%')
        if ($pct -notmatch '^\d+$' -or [int]$pct -gt 100) { return "'$($f[2])' ist kein Prozentwert von 0 bis 100." }
        $seen++
    }
    if ($seen -eq 0) { return "Der Zeitplan enthaelt keine Regel." }
    return $null
}

# Prozentsatz zu einem Zeitpunkt — die erste passende Regel gewinnt, sonst 100.
function Get-SchedulePercent([int]$day, [int]$minute) {
    if (-not $schedule) { return 100 }
    foreach ($rule in $schedule.Split(';')) {
        $r = $rule.Trim()
        if (-not $r) { continue }
        $f = $r -split '\s+'
        if ($f.Count -eq 2 -and $f[1] -match '^\d+%?$') { $f = @($f[0], '00:00-24:00', $f[1]) }
        if ($f.Count -ne 3) { continue }
        if ((Test-DayMatch $f[0] $day) -ne $true) { continue }
        $span = $f[1].Split('-')
        $from = Get-Minutes $span[0]; $to = Get-Minutes $span[1]
        $hit = if ($from -eq $to) { $true }                                  # ganzer Tag
               elseif ($from -lt $to) { $minute -ge $from -and $minute -lt $to }
               else { $minute -ge $from -or $minute -lt $to }                # ueber Mitternacht
        if ($hit) { return [int]($f[2].TrimEnd('%')) }
    }
    return 100
}

# Wie viele Engines laufen bei diesem Prozentsatz? 0 = keine. Sonst entscheidet $scheduleScope, ob
# der Anteil nur den Hintergrund-Engines gilt (Vorgabe: die Live-Engine bleibt an) oder allen
# zusammen. Kaufmaennisch gerundet, und solange ueberhaupt gerechnet wird, laeuft mindestens eine.
function Get-TargetCount([int]$pct, [int]$total) {
    if ($pct -le 0) { return 0 }
    if ($pct -ge 100) { return $total }
    if ($scheduleScope -eq 'all') {
        $n = [math]::Floor(($total * $pct + 50) / 100)
        if ($n -lt 1) { $n = 1 }
        return $n
    }
    $bg = $total - 1
    $n = [math]::Floor(($bg * $pct + 50) / 100)
    if ($n -lt 1 -and $bg -gt 0) { $n = 1 }
    return 1 + $n
}

function Write-WrapperLog([string]$message) {
    # Ohne Rotation waechst die Datei bei 17 Prozessen und jahrelangem Lauf endlos.
    if ((Test-Path $wrapperLog) -and ((Get-Item $wrapperLog).Length -gt 2MB)) {
        Move-Item -Path $wrapperLog -Destination "$wrapperLog.1" -Force -ErrorAction SilentlyContinue
    }
    "$(Get-Date -Format o) [wrapper] $message" | Out-File -FilePath $wrapperLog -Append -Encoding utf8
}

# Frische Verbindung je Upload - derselbe Eingriff wie im Docker-Image (README, "Ein Eingriff
# bleibt"): ungepatcht stirbt ein Teil der Uploads im ersten Byte, und RookHub bekommt nach 15 s
# einen 503. patch_force_close.py liegt nach README-Schritt 3 neben dem Provider-Skript; der
# Aufruf ist idempotent und bricht ab (ohne etwas zu aendern), wenn die Textstelle nicht passt.
$patch = Join-Path (Split-Path -Parent $script) "patch_force_close.py"
if (-not (Select-String -Path $script -SimpleMatch "force_close=True" -Quiet -ErrorAction SilentlyContinue)) {
    if (Test-Path $patch) {
        $patchOut = & $pythonExe $patch $script 2>&1 | Out-String
        Write-WrapperLog "patch_force_close: $($patchOut.Trim())"
    }
    if (-not (Select-String -Path $script -SimpleMatch "force_close=True" -Quiet -ErrorAction SilentlyContinue)) {
        Write-WrapperLog "WARNUNG: $script ist NICHT gepatcht (force_close) - einzelne Suchen enden mit 503, siehe README Schritt 3"
    }
}

# Die Engines dieses Rechners. NAMENSREGEL: Der Name IST die Registrierung - die erste
# Hintergrund-Engine heisst genau $bgName, die zweite "$bgName 2", die dritte "$bgName 3" (so wie
# im Docker-Image). Wird ein Name geaendert, entsteht eine NEUE Engine mit neuer Kennung, und die
# Hintergrund-Auswahl im RookHub-Profil zeigt ins Leere. Bleibt er gleich, behaelt die Engine ueber
# jeden Neustart hinweg ihre Kennung - angehakt bleibt angehakt.
$engines = @()
$engines += [pscustomobject]@{ Slot = 1; Name = $liveName; Threads = $liveThreads; Hash = $liveHash }
for ($i = 1; $i -le $bgCount; $i++) {
    $name = if ($i -eq 1) { $bgName } else { "$bgName $i" }
    $engines += [pscustomobject]@{ Slot = $i + 1; Name = $name; Threads = $bgThreads; Hash = $bgHash }
}

# Kaputter Zeitplan: lieber hier stehenbleiben als stillschweigend immer 100 % fahren.
if ($scheduleScope -ne 'background' -and $scheduleScope -ne 'all') {
    Write-Host "FEHLER: `$scheduleScope muss 'background' oder 'all' sein (ist '$scheduleScope')."
    exit 1
}
$scheduleError = Test-Schedule $schedule
if ($scheduleError) {
    Write-WrapperLog "FEHLER im Zeitplan: $scheduleError"
    Write-Host "FEHLER im Zeitplan (`$schedule): $scheduleError"
    exit 1
}

# Nur nachrechnen, nichts starten.
if ($At) {
    $f = $At.Trim() -split '\s+'
    $day = if ($f.Count -ge 1) { Get-DayNumber $f[0] } else { 0 }
    $min = if ($f.Count -ge 2) { Get-Minutes $f[1] } else { -1 }
    if ($day -eq 0 -or $min -lt 0) {
        Write-Host "FEHLER: -At erwartet '<Tag> <HH:MM>', z. B. 'Mo 09:00' (ist '$At')."
        exit 1
    }
    $pct = Get-SchedulePercent $day $min
    Write-Host "ZEITPLAN $At`: $pct% - $(Get-TargetCount $pct $engines.Count) von $($engines.Count) Engine(s)"
    exit 0
}

Write-WrapperLog "Start: $($engines.Count) Engine(s) - live $liveThreads Threads, $bgCount x $bgThreads Threads im Hintergrund; Ziel $(if ($rookhubUrl) { $rookhubUrl } else { 'lichess.org' })$(if ($schedule) { "; Zeitplan: $schedule" } else { '' })"

function Start-Provider($engineDef) {
    $argList = @(
        "`"$script`"",
        "--engine", "`"$engine`"",
        "--name", "`"$($engineDef.Name)`"",
        "--max-threads", "$($engineDef.Threads)",
        "--max-hash", "$($engineDef.Hash)",
        "--keep-alive", "$keepAlive",
        "--log-level", "$logLevel"
    )
    if ($rookhubUrl) {
        # Anmeldung (--lichess) UND Arbeit (--broker) gehen an RookHub; es spricht an dieser Stelle
        # dasselbe Protokoll wie Lichess, der Provider bleibt unveraendert.
        $argList += @("--lichess", $rookhubUrl.TrimEnd("/"), "--broker", $rookhubUrl.TrimEnd("/"))
    }

    # Je Prozess EIGENE Log-Dateien: zwei Prozesse koennen nicht in dieselbe Datei umleiten
    # (Windows haelt sie zum Schreiben gesperrt, der zweite Start scheitert dann still).
    # Direktes Redirect auf OS-Ebene (Start-Process), kein PowerShell-Textstream dazwischen -
    # sonst entweder ErrorRecord-Rauschen (2>&1 | Out-File) oder falsches Encoding (native *>>).
    $slot = "{0:00}" -f $engineDef.Slot
    return Start-Process -FilePath $pythonExe -ArgumentList $argList `
        -RedirectStandardOutput (Join-Path $logDir "provider_$slot.out.log") `
        -RedirectStandardError  (Join-Path $logDir "provider_$slot.err.log") `
        -NoNewWindow -PassThru
}

# Der Provider startet Stockfish ueber eine Shell; ein blosses Kill des Python-Prozesses liesse
# die Engine als Waise mit belegtem Arbeitsspeicher zurueck (siehe reap_orphans.ps1). taskkill /T
# nimmt den ganzen Baum mit; faellt es aus, bleibt der Reaper die zweite Verteidigungslinie.
function Stop-Provider($proc) {
    if ($null -eq $proc) { return }
    try { & taskkill.exe /PID $proc.Id /T /F 2>&1 | Out-Null } catch { }
    try { if (-not $proc.HasExited) { $proc.Kill() } } catch { }
}

# Ein Prozess je Engine, einzeln ueberwacht, und im Takt der Uhr so viele, wie der Zeitplan
# vorsieht. Ein gemeinsamer Loop mit -Wait wie im Einzel-Engine-Skript geht hier nicht: stirbt
# eine von siebzehn Engines, soll genau die neu starten und nicht der ganze Verband.
$running = @{}
$lastTarget = -1
while ($true) {
    $now = Get-Date
    $day = [int]$now.DayOfWeek; if ($day -eq 0) { $day = 7 }   # .NET zaehlt Sonntag als 0
    $pct = Get-SchedulePercent $day ($now.Hour * 60 + $now.Minute)
    $target = Get-TargetCount $pct $engines.Count
    if ($target -ne $lastTarget) {
        Write-WrapperLog "Zeitplan: $pct % - $target von $($engines.Count) Engine(s)"
        $lastTarget = $target
    }

    # Zu viele: von hinten abschalten, die Live-Engine (Platz 1) geht als letzte.
    foreach ($e in ($engines | Sort-Object Slot -Descending)) {
        if ($e.Slot -le $target) { continue }
        if ($running.ContainsKey($e.Slot) -and $null -ne $running[$e.Slot]) {
            Write-WrapperLog "Zeitplan: beende '$($e.Name)'"
            Stop-Provider $running[$e.Slot]
            $running.Remove($e.Slot)
        }
    }

    # Zu wenige: starten. Gestorbene, die laufen sollen, kommen hier ebenfalls wieder hoch.
    $startedNow = 0
    foreach ($e in $engines) {
        if ($e.Slot -gt $target) { continue }
        $proc = if ($running.ContainsKey($e.Slot)) { $running[$e.Slot] } else { $null }
        if ($null -ne $proc -and -not $proc.HasExited) { continue }
        if ($null -ne $proc) { Write-WrapperLog "'$($e.Name)' beendet (Exit $($proc.ExitCode)) - Neustart" }
        if ($startedNow -gt 0 -and $startDelay -gt 0) { Start-Sleep -Seconds $startDelay }
        $running[$e.Slot] = Start-Provider $e
        $startedNow++
        Write-WrapperLog "gestartet: '$($e.Name)' ($($e.Threads) Threads, $($e.Hash) MiB) PID $($running[$e.Slot].Id)"
    }

    Start-Sleep -Seconds $scheduleTick
}
