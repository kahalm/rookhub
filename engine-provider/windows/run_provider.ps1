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

# ===========================================================================

$wrapperLog = Join-Path $logDir "wrapper.log"
New-Item -ItemType Directory -Path $logDir -Force -ErrorAction SilentlyContinue | Out-Null

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

Write-WrapperLog "Start: $($engines.Count) Engine(s) - live $liveThreads Threads, $bgCount x $bgThreads Threads im Hintergrund; Ziel $(if ($rookhubUrl) { $rookhubUrl } else { 'lichess.org' })"

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

# Ein Prozess je Engine, einzeln ueberwacht. Ein gemeinsamer Loop mit -Wait wie im
# Einzel-Engine-Skript geht hier nicht: stirbt eine von siebzehn Engines, soll genau die neu
# starten und nicht der ganze Verband.
$running = @{}
foreach ($e in $engines) {
    if ($running.Count -gt 0 -and $startDelay -gt 0) { Start-Sleep -Seconds $startDelay }
    $running[$e.Slot] = Start-Provider $e
    Write-WrapperLog "gestartet: '$($e.Name)' ($($e.Threads) Threads, $($e.Hash) MiB) PID $($running[$e.Slot].Id)"
}

while ($true) {
    Start-Sleep -Seconds 10
    foreach ($e in $engines) {
        $proc = $running[$e.Slot]
        if ($null -eq $proc -or $proc.HasExited) {
            $code = if ($null -eq $proc) { "n/a" } else { $proc.ExitCode }
            Write-WrapperLog "'$($e.Name)' beendet (Exit $code) - Neustart"
            $running[$e.Slot] = Start-Provider $e
        }
    }
}
