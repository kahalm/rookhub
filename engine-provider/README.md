# Eigene Engine für die RookHub-Analyse

Damit rechnet im RookHub-Analysebrett **Stockfish auf deinem eigenen Rechner** statt der
abgespeckten Browser-Engine — deutlich stärker, und du kannst es auch vom Handy aus nutzen.

Dieser Ordner enthält ein fertiges Docker-Setup dafür. Es läuft auf **deinem Rechner**, nicht
auf dem RookHub-Server.

## Wie das zusammenhängt

```
RookHub-Analysebrett  ──►  Lichess-Broker  ──►  dieser Container  ──►  Stockfish
   (Browser/Handy)          (engine.lichess.ovh)   (dein Rechner)
```

RookHub nutzt dafür die **offene External-Engine-Schnittstelle von Lichess**: Dein Rechner
meldet die Engine bei deinem Lichess-Konto an, RookHub findet sie dort und schickt ihr
Analyse-Aufträge. Praktische Folgen:

- **Kein Port muss offen sein.** Der Container baut die Verbindung von sich aus nach außen
  auf — keine Freigabe im Router, kein DynDNS, kein Zertifikat.
- Die Engine steht damit **auch in Lichess' eigenem Analysebrett** zur Verfügung.
- Dein Rechner muss laufen, wenn du analysieren willst. Ist er aus, rechnet RookHub still
  wieder mit der Browser-Engine weiter (und sagt es dir).

## Einrichten (3 Schritte)

**1. Lichess-Token anlegen** — mit den Scopes `engine:read` **und** `engine:write`:

<https://lichess.org/account/oauth/token/create?scopes[]=engine:read&scopes[]=engine:write&description=RookHub%20Engine%20Provider>

`engine:write` ist nötig, weil dein Rechner die Engine bei Lichess *anmeldet*. (Der Token,
den du später in RookHub hinterlegst, braucht nur `engine:read` — siehe Schritt 3.)

**2. Container starten:**

```bash
cd engine-provider
cp .env.example .env
# .env öffnen und LICHESS_API_TOKEN eintragen (bei Bedarf ENGINE_NAME/MAX_THREADS anpassen)
docker compose up -d --build
```

Läuft alles, steht im Log `Registering new engine` bzw. `Updating engine`:

```bash
docker compose logs -f
```

**3. In RookHub hinterlegen** — Profil → *Externe Engine (Lichess)*: dort einen Lichess-Token
eintragen (dafür genügt `engine:read`, du kannst aber denselben nehmen). Die Karte listet
danach die gefundenen Engines auf. Im Analysebrett erscheint über den Varianten eine
Auswahl **Browser / \<dein Engine-Name\>**.

## Einstellungen

Alles über die `.env` (Details stehen als Kommentar an jeder Variable):

| Variable | Wofür |
|---|---|
| `LICHESS_API_TOKEN` | **Pflicht.** Token mit `engine:read` + `engine:write` |
| `ENGINE_NAME` | Anzeigename in der RookHub-Auswahl |
| `MAX_THREADS` | Rechenkerne (leer = alle Kerne des Rechners) |
| `MAX_HASH` | Hash-Tabelle in MiB (leer = 512) |
| `KEEP_ALIVE` | Sekunden, die eine unbenutzte Engine weiterläuft (leer = 300) |
| `ENGINE_PATH` | Andere UCI-Binärdatei statt des mitgelieferten Stockfish 18 |
| `LOG_LEVEL` | `debug` hilft bei der Fehlersuche |
| `ENGINE_COUNT` | Mehrere Engines in diesem Container (leer/1 = eine; max. 16) — siehe unten |
| `ENGINE_<i>_NAME` / `_MAX_THREADS` / `_MAX_HASH` | Einstellungen der i-ten Engine (sonst `ENGINE_NAME <i>`, `MAX_THREADS`, `MAX_HASH`) |

Nach einer Änderung an der `.env` den Container neu starten, sonst gilt weiter der alte Stand:

```bash
docker compose up -d     # übernimmt die geänderte .env
docker compose down      # Provider stoppen (Engine verschwindet dann aus der RookHub-Auswahl)
```

### Zwei Engines: Live + Hintergrund

Ein Stockfish-Prozess rechnet immer nur **eine** Suche; ein neuer Auftrag an dieselbe Engine
ersetzt den laufenden. Wer neben der Live-Analyse Stellungen im Hintergrund abarbeiten lassen
will, braucht deshalb eine **zweite registrierte Engine** — und die kommt aus demselben Container:

```dotenv
ENGINE_COUNT=2
ENGINE_1_NAME=RookHub Server Live
ENGINE_2_NAME=RookHub Server Hintergrund
MAX_HASH=1024
ENGINE_2_MAX_HASH=8192      # große Hashtabelle: warme Stellungen überleben Pausen länger
```

Beide Engines dürfen **alle Kerne** behalten (`MAX_THREADS` leer): RookHub pausiert die
Hintergrund-Engine, sobald auf der Live-Engine gerechnet wird, und lässt sie erst nach einer
Ruhephase weiterlaufen — die beiden laufen also praktisch nie gleichzeitig, nur der RAM für zwei
Hashtabellen fällt doppelt an. In RookHub erscheinen beide Namen in der Engine-Auswahl; welche
davon die Hintergrund-Engine ist, legst du im Profil fest.

Stirbt einer der Provider, endet der ganze Container mit dessen Exit-Code und `restart:` zieht
alle Engines gemeinsam neu hoch (kein halb lebender Pool). Die Logzeilen beider Provider laufen
in einem Strom zusammen; jeder meldet beim Start seinen Namen.

**Auf dem Arbeitsrechner** lohnt sich `MAX_THREADS` ein bis zwei Kerne unter der Kernzahl —
sonst zieht eine tiefe Analyse den Rechner spürbar zu. Ein zusätzliches `cpus:`-Limit in der
`compose.yml` (auskommentiert vorhanden) begrenzt es hart; der Provider selbst kennt dieses
Limit nicht, deshalb beide Werte zueinander passend setzen.

## Eigene Engine statt des mitgelieferten Stockfish

Der Container bringt **Stockfish 18** mit — die offizielle Binärdatei (Variante `avx2`, also
CPUs ab ~2013). Bewusst nicht das Debian-Paket: das ist ein generischer Build ohne AVX2-Nutzung
und rechnet auf derselben CPU rund ein Drittel langsamer (auf dem Testserver gemessen: 4,98 vs.
8,39 Mio Knoten/s). Läuft dein Rechner auf einer älteren CPU, im `Dockerfile`
`SF_VARIANT=x86-64-sse41-popcnt` samt passender `SF_SHA256` setzen.

Wer einen anderen Build (oder das UCI-Tunnel-Binary eines Cloud-Anbieters wie Chessify)
nutzen will, hängt ihn ein:

```yaml
# compose.yml
    volumes:
      - /pfad/zu/deiner/engine:/engine:ro
```

```ini
# .env
ENGINE_PATH=/engine/stockfish
```

Alles, was UCI spricht, funktioniert — der Provider startet es einfach als Unterprozess.

## Ohne Docker (Linux/macOS)

Es geht auch direkt, wenn Python 3 und eine Engine vorhanden sind:

```bash
python3 -m venv .venv && .venv/bin/pip install requests
curl -O https://raw.githubusercontent.com/lichess-org/external-engine/a6ef15a8e395eb609535857aabf18837ea7696cf/example-provider.py
LICHESS_API_TOKEN=lip_dein_token .venv/bin/python example-provider.py \
  --engine /usr/games/stockfish --name "RookHub Heim-Engine" --max-threads 6
```

Die virtuelle Umgebung ist kein Zierrat: aktuelle Linux-Distributionen (Debian 12+, Ubuntu 23.04+)
und Homebrew lehnen ein direktes `pip install` in die System-Python ab
(`error: externally-managed-environment`).

## Auf Windows

Der Provider ist plattformneutral und läuft unter Windows unverändert; Docker Desktop wird nicht
gebraucht. Auf einem Einzelrechner ist der direkte Weg der einfachere.

> **Vergib einen anderen Namen, wenn schon anderswo ein Provider läuft.** Der Provider erkennt
> seine Registrierung am NAMEN: gleicher Name heißt *aktualisieren*, nicht *hinzufügen*. Startet
> der PC unter dem Namen des Servers, übernimmt er dessen Eintrag — die Server-Engine
> verschwindet dann aus der Auswahl. Mit zwei verschiedenen Namen stehen beide nebeneinander.

**1. Python** von [python.org](https://www.python.org/downloads/windows/) installieren, dabei
„Add python.exe to PATH" ankreuzen. Dann in der PowerShell:

```powershell
pip install requests
```

(Das `externally-managed-environment` aus dem Abschnitt oben betrifft nur Linux/macOS.)

**2. Stockfish** von [stockfishchess.org/download/windows](https://stockfishchess.org/download/windows/)
holen — aktuell Stockfish 18. Passende Variante:

| CPU | Datei |
|---|---|
| Ryzen 3000+ / Intel ab Haswell | `stockfish-windows-x86-64-bmi2.zip` |
| ältere oder unsicher | `stockfish-windows-x86-64-avx2.zip` |
| läuft garantiert überall | `stockfish-windows-x86-64.zip` |

Das NNUE-Netz steckt in der `.exe`, es wird also nur diese eine Datei gebraucht.
**Nach `C:\stockfish\` entpacken — bewusst ein Pfad OHNE Leerzeichen**: der Provider startet die
Engine über die Kommandozeile, ein Pfad wie `C:\Program Files\…` würde dort zerlegt.

**3. Provider holen** (dieselbe gepinnte Fassung wie im Container):

```powershell
cd C:\stockfish
curl.exe -O https://raw.githubusercontent.com/lichess-org/external-engine/a6ef15a8e395eb609535857aabf18837ea7696cf/example-provider.py
```

**4. Starten:**

```powershell
$env:LICHESS_API_TOKEN = "lip_dein_token"
python example-provider.py --engine "C:\stockfish\stockfish-windows-x86-64-bmi2.exe" --name "RookHub PC" --max-threads 6 --max-hash 2048
```

(Bewusst eine lange Zeile: PowerShell bricht Zeilen mit einem Backtick um, der beim Kopieren
kaputtgeht, sobald ein Leerzeichen dahinter steht.)

Im Fenster muss `Registering new engine` erscheinen; danach steht „RookHub PC" in der
Engine-Auswahl des Analysebretts. Beenden mit Strg+C — solange das Fenster offen ist, läuft die
Engine. Bei `--max-threads` ein bis zwei Kerne unter der Kernzahl lassen, sonst wird der Rechner
beim Analysieren zäh.

**Dauerhaft, ohne offenes Fenster:** Aufgabenplanung → Aufgabe erstellen → Trigger „Bei
Anmeldung", Aktion `python` mit denselben Argumenten, „Starten in" `C:\stockfish`. Den Token dann
dauerhaft setzen statt pro Sitzung:

```powershell
setx LICHESS_API_TOKEN "lip_dein_token"
```

Das reicht für den Alltag. Zwei Lücken hat dieser einfache Weg trotzdem: Stirbt der Provider
(Absturz, `terminate()`-Timeout), startet ihn niemand neu, bis du dich wieder anmeldest — und
Windows hinterlässt bei jedem Idle-Timeout einen Zombie-Prozess (Details unten). Für einen
Rechner, der wirklich dauerhaft laufen soll, siehe den nächsten Abschnitt.

### Robuster Dauerbetrieb (Auto-Restart + Aufräumen)

Fertige Skripte dafür liegen unter [`windows/`](windows/) — `run_provider.ps1` (Auto-Restart-Loop)
und `reap_orphans.ps1` (Zombie-Reaper). Beide Variablen am Kopf der Datei vor dem ersten Start
anpassen.

> **In einer VM zuerst die CPU-Features prüfen, nicht die des Hosts.** Ein virtueller Rechner
> gibt AVX2/BMI2 des physischen Hosts nicht zwangsläufig an den Gast durch — je nach
> Hypervisor-Konfiguration (z. B. generisches QEMU-CPU-Modell statt `-cpu host`) sieht der Gast
> nur bis SSE4.2. Der falsche Build stürzt dann beim Start lautlos ab (Illegal Instruction, keine
> Fehlermeldung, `EOFError` im Provider). Prüfen:
> ```powershell
> Add-Type -TypeDefinition @"
> using System.Runtime.InteropServices;
> public class CpuFeat {
>     [DllImport("kernel32.dll")]
>     public static extern bool IsProcessorFeaturePresent(uint feature);
> }
> "@
> "AVX2: " + [CpuFeat]::IsProcessorFeaturePresent(40)
> ```
> `False` → `stockfish-windows-x86-64-sse41-popcnt.zip` von der
> [Stockfish-Release-Seite](https://github.com/official-stockfish/Stockfish/releases) nehmen statt
> AVX2/BMI2, auch wenn die physische CPU eigentlich mehr kann.

**Auto-Restart:** Task Scheduler mit „Bei Anmeldung"-Trigger, der `run_provider.ps1` startet statt
`python` direkt — das Skript läuft in einer Endlosschleife und startet den Provider automatisch neu,
falls er beendet wird:

```powershell
$action  = New-ScheduledTaskAction -Execute "powershell.exe" -Argument '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "C:\stockfish\run_provider.ps1"'
$trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName "RookHub Engine Provider" -Action $action -Trigger $trigger -Settings $settings
```

`-ExecutionTimeLimit ([TimeSpan]::Zero)` ist Pflicht — Task Scheduler killt Aufgaben sonst
standardmäßig nach 72 Stunden, egal wie die Aufgabe selbst konfiguriert ist.

> **Auch ein „Bei Anmeldung"-Task kann trotz offener Sitzung sterben** — beobachtet mit
> `LastTaskResult 0xC000013A` (`STATUS_CONTROL_C_EXIT`), obwohl niemand Strg+C gedrückt hat.
> Reproduzierbar per `AttachConsole` + `GenerateConsoleCtrlEvent`: ein solcher Broadcast erreicht
> offenbar auch versteckte (`-WindowStyle Hidden`) Konsolen, wenn mehrere Prozesse dieselbe
> Konsolensitzung teilen. `run_provider.ps1` registriert deshalb bereits am Anfang einen
> `SetConsoleCtrlHandler(NULL, true)` — die dokumentierte Win32-Standardtechnik, um genau das zu
> ignorieren. Ohne diesen Fix bleibt der Provider bis zur nächsten Anmeldung tot, mit Fix
> übernimmt der `while`-Loop nach spätestens 10 Sekunden von selbst wieder.

> **Fällt der Task-Prozess extern weg** (z. B. `Stop-ScheduledTask`, Absturz), **überlebt der
> bereits gestartete Python-Prozess als Waise** — `Start-Process` koppelt die Lebensdauer des
> Kindes nicht an den Elternprozess. Zum sauberen Stoppen daher immer beides:
> ```powershell
> Stop-ScheduledTask -TaskName "RookHub Engine Provider"
> # NICHT `Get-Process python | Stop-Process`: das trifft JEDEN python.exe auf dem Rechner
> # (Jupyter-Kernel, laufende Skripte, andere Dienste) und schiesst ihn ohne Rueckfrage ab.
> Get-CimInstance Win32_Process -Filter "Name='python.exe'" |
>   Where-Object { $_.CommandLine -like '*example-provider.py*' } |
>   ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
> Get-Process stockfish-windows-x86-64-* -ErrorAction SilentlyContinue | Stop-Process -Force
> ```

**Der Zombie-Leak:** `example-provider.py` startet die Engine unter Windows über
`subprocess.Popen(engine_command, shell=True, …)` und beendet sie bei Idle-Timeout per
`process.terminate()`. Unter Windows tötet das nur die Shell-Hülle (`cmd.exe`), die Stockfish
gestartet hat — der eigentliche Engine-Prozess bleibt als Waise zurück (0 % CPU, aber dauerhaft
belegter Arbeitsspeicher). Das passiert bei **jedem** Timeout, unabhängig vom `KEEP_ALIVE`-Wert;
ein höherer Wert (z. B. `3600` statt der Default-`300`) verlangsamt nur, wie oft das Leck
ausgelöst wird — `run_provider.ps1` setzt ihn deshalb bereits hoch. Behoben wird es durch
`reap_orphans.ps1`, alle 15 Minuten laufen lassen:

```powershell
$action  = New-ScheduledTaskAction -Execute "powershell.exe" -Argument '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "C:\stockfish\reap_orphans.ps1"'
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(15) -RepetitionInterval (New-TimeSpan -Minutes 15) -RepetitionDuration (New-TimeSpan -Days 7300)
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 5) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName "RookHub Orphan Reaper" -Action $action -Trigger $trigger -Settings $settings
```

(`-RepetitionDuration ([TimeSpan]::MaxValue)` scheitert an Task Schedulers XML-Schema — ein
großer, aber gültiger Wert wie 7300 Tage tut's genauso.)

**Was auf einem Arbeitsrechner anders ist als auf einem Server:** Geht der PC in den Ruhezustand
oder schläft der Netzwerkadapter ein, ist die Engine weg — RookHub fällt still auf die
Browser-Engine zurück und blendet den Hinweis ein. Ein PC lohnt sich, wenn er mehr Kerne hat als
der Server; ein Server-Provider lohnt sich, weil er immer erreichbar ist. Beides parallel (mit
zwei Namen) ist der bequemste Fall: Du wählst im Analysebrett, was gerade läuft.

## Wenn etwas nicht klappt

| Symptom | Ursache |
|---|---|
| `Lichess kennt diesen Token nicht` | Token vertippt, widerrufen oder abgelaufen — neu anlegen |
| Container startet immer wieder neu | Genau das ist bei einem Token-Fehler erwartet (`restart: unless-stopped`). `.env` korrigieren, dann `docker compose up -d` |
| `Keine ausführbare Engine-DATEI` | `ENGINE_PATH` zeigt auf einen Ordner statt auf die Binärdatei, das Volume fehlt, oder die Datei ist nicht ausführbar (`chmod +x`) |
| `Dem Token fehlt der Scope engine:write` | Token nur mit `engine:read` erzeugt; der Link oben setzt beide |
| Container startet, aber RookHub zeigt keine Auswahl | In RookHub den Token im Profil hinterlegt? Seite neu laden — die Liste wird beim Öffnen des Analysebretts geholt |
| Analyse fällt auf „Browser" zurück | Container läuft nicht / Rechner aus / kein Netz. RookHub blendet dann den Hinweis „Externe Engine nicht erreichbar" ein |
| Zwei Einträge in der Auswahl | Der Provider erkennt seine Registrierung am **Namen**. Gleicher Name = Aktualisierung, anderer Name = zusätzlicher Eintrag. Auf zwei Rechnern bewusst zwei Namen vergeben — sonst überschreiben sie sich gegenseitig |
| Eine Engine ist aus der Auswahl verschwunden | Zwei Provider liefen unter demselben Namen — der zuletzt gestartete hat den Eintrag übernommen. Einen umbenennen und neu starten |
| Windows: `'python' is not recognized` | Beim Python-Setup war „Add python.exe to PATH" nicht angekreuzt — Setup erneut ausführen (Modify → Repair) oder `py` statt `python` verwenden |
| Windows: `/bin/sh: … not found` bzw. Engine startet nicht | Der Pfad zur `.exe` enthält Leerzeichen. Stockfish nach `C:\stockfish\` entpacken |

Alte Registrierungen aufräumen kannst du auf <https://lichess.org/account/oauth/token> (Token
widerrufen) bzw. über die Engine-Verwaltung im Lichess-Analysebrett.

## Was hier läuft

Der Container startet den **offiziellen Provider von Lichess**
([lichess-org/external-engine](https://github.com/lichess-org/external-engine), GPL-3.0 wie
RookHub). Er wird beim Bauen auf einen festen Commit gepinnt und per Prüfsumme verifiziert,
statt ins Repo kopiert zu werden — so ist die Herkunft eindeutig, und ein Update ist ein
Zeilenwechsel im `Dockerfile`. Ergänzt haben wir nur `entrypoint.sh` (baut den Aufruf aus den
`.env`-Variablen und startet bei `ENGINE_COUNT`>1 mehrere Provider; `bash test/entrypoint.test.sh`
prüft den Argument-Aufbau im Trockenlauf) und `preflight.py` (prüft den Token vorab, damit ein fehlender Scope als
Klartext-Satz erscheint und nicht als endlos wiederholter Stacktrace).

Serverseitig ist die Gegenstelle in `rookhub/CLAUDE.md` unter „Externe Engine" beschrieben.
