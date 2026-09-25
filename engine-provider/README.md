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
| `KEEP_ALIVE` | Sekunden, die ein unbenutzter Stockfish-Prozess im Speicher bleibt (leer = 300). Für Hintergrund-Analysen hochsetzen — siehe unten |
| `ENGINE_PATH` | Andere UCI-Binärdatei statt des mitgelieferten Stockfish 18 |
| `LOG_LEVEL` | `debug` hilft bei der Fehlersuche |
| `ENGINE_PRIMARY_NAME` / `ENGINE_PRIMARY_MAX_THREADS` / `ENGINE_PRIMARY_MAX_HASH` | Die EINE Haupt-Engine für die Live-Analyse — siehe unten |
| `ENGINE_BACKGROUND_NAME` / `ENGINE_BACKGROUND_MAX_THREADS` / `ENGINE_BACKGROUND_MAX_HASH` | Die Hintergrund-Engines, alle gleich |
| `ENGINE_BACKGROUND_COUNT` | Wie viele Hintergrund-Engines (leer = nur eine einzelne Engine wie früher; max. 15) |
| `ENGINE_COUNT` | Älteres Schema: Engines von Hand durchnummerieren (max. 16) |
| `ENGINE_<i>_NAME` / `_MAX_THREADS` / `_MAX_HASH` | Einstellungen der i-ten Engine; schlagen die Werte oben |
| `PROVIDER_START_DELAY` | Sekunden Pause zwischen den Provider-Starts bei mehreren Engines (leer = 3, 0 = aus) — siehe unten |

Nach einer Änderung an der `.env` den Container neu starten, sonst gilt weiter der alte Stand:

```bash
docker compose up -d     # übernimmt die geänderte .env
docker compose down      # Provider stoppen (Engine verschwindet dann aus der RookHub-Auswahl)
```

### `KEEP_ALIVE`

Ein Stockfish-Prozess, der länger als `KEEP_ALIVE` Sekunden keine Suche hatte, wird beendet und beim
nächsten Auftrag neu gestartet. Für Hintergrund-Aufträge lohnt ein hoher Wert:

```dotenv
KEEP_ALIVE=86400      # 24 h
```

Der Prozess bleibt dann länger im RAM — was hier erwünscht ist, weil die warme Hashtabelle ein
Fortsetzen nach einer Pause fast kostenlos macht.

Bis 0.478.10 stand hier eine Warnung: der damals gepinnte Provider erneuerte seinen „zuletzt
benutzt"-Stempel erst am ENDE einer Suche, und sein Wachhund schoss jede Suche ab, die länger als
`KEEP_ALIVE` lief (ab Tiefe ~29 mit 5 Linien schon eine einzige Iteration). Der heutige Stand erneuert
den Stempel während der Suche und beendet nur Prozesse, die wirklich untätig sind.

### Provider-Stand: Suchende und Lebenszeichen

Der gepinnte Provider (`PROVIDER_SHA` im `Dockerfile`, Stand `d0eeb242` vom 2026-09-06) erfüllt zwei
Regeln des Brokers, an denen RookHub hängt. Das frühere Lebenszeichen patcht dieses Image nicht mehr hinein; ein anderer, kleinerer Eingriff kam mit
0.535.1 dazu (siehe unten).

**Jede Suche endet mit `bestmove`.** Der Broker verlangt das seit 2026-09-06 (lila-engine `0e1223b`)
und weist einen Upload ohne mit `400 uci protocol error: expected bestmove before end of stream` ab.
Der bis 0.478.10 gepinnte Stand (`a6ef15a8`) schickte nie ein `bestmove`, bekam die 400 also bei
JEDER Suche, schlief danach 5 s und holte erst dann den nächsten Auftrag. Am Brett hieß das: **ein Zug
kurz nach einer beendeten Suche wartete rund vier Sekunden**, bis die Engine anlief.

**Lebenszeichen alle 15 s** (`{"keepalive":true}`, vom Broker an den Empfänger durchgereicht). Der
Provider gibt nur `info`-Zeilen **mit `score`** weiter; zwischen zwei tiefen MultiPV-Iterationen
vergehen Minuten, und der Broker schließt eine Verbindung nach 60 s Stille. Ohne Lebenszeichen kam ein
Hintergrund-Auftrag nie über die Tiefe hinaus, die zwischen zwei Abrissen erreichbar ist (beobachtet:
Tiefe 20/22 bei 23 Aufträgen, Tiefe 29 bei tiefen Aufträgen). Bis 0.478.10 hat dieses Image dafür einen
eigenen Eingriff in den Provider gepatcht (`patch_provider.py`, eine Wiederholung der letzten
`info`-Zeile); der heutige Stand macht es selbst. Die Variable `HEARTBEAT_SECONDS` ist damit
wirkungslos und kann aus einer bestehenden `.env` gestrichen werden.

`python3 test/provider.test.py <provider.py>` prüft beides gegen einen nachgebauten Broker — dazu,
dass score-lose Zeilen beim Provider bleiben und dass auch ein Zug WÄHREND einer Suche sofort startet.
Gegen den alten Stand schlägt der Test mit genau den vier bis fünf Sekunden Wartezeit fehl. **Beim
Aktualisieren des Pins** läuft er in der CI mit: die Regeln des Brokers ändern sich, ohne dass ein
bereits laufender Provider davon erfährt.

**Ein Eingriff bleibt: eine frische Verbindung je Upload** (`patch_force_close.py`, seit 0.535.1). Der
asynchrone Provider schickt den Upload einer Suche über eine aiohttp-Sitzung mit Verbindungs-Pool: die
Verbindung zum Broker bleibt nach einer Antwort offen und wird für den nächsten Upload wiederverwendet.
Schließt die Gegenseite sie inzwischen (nginx nach dem Ende eines Streams, oder weil der Anfragende weg
ist), merkt aiohttp das erst beim nächsten Schreiben — der Auftrag ist da schon abgeholt, die Engine hat
`go`, der Upload stirbt im ersten Byte („Connection closed while streaming analysis"), und der Anfragende
wartet 15 s ins Leere (Broker-503). Gemeldet als [external-engine #45](https://github.com/lichess-org/external-engine/issues/45)
mit A/B-Nachweis: `TCPConnector(force_close=True)` für die Upload-Sitzung behebt es — so machte es der alte
Provider mit `requests.post` ohnehin. Die Poll-Sitzung bleibt im Pool (die Abfrage alle 10 s soll keinen
TLS-Handshake kosten). Der Patch wird NACH der Prüfsummen-Kontrolle angewandt und bricht den Build ab, wenn
die Textstelle fehlt; `test/provider.test.py` prüft, dass drei Uploads auf drei Verbindungen kommen. Behebt
upstream das Problem, fliegen Skript und Dockerfile-Zeilen wieder raus.

### Gestaffelte Starts (`PROVIDER_START_DELAY`)

Jeder Provider registriert sich beim Start bei lichess.org (Engine-Liste holen, Eintrag aktualisieren).
Bei mehreren Engines im Container passierte das bisher **gleichzeitig** — und dreizehn Registrierungen
im selben Augenblick hielt der DDoS-Schutz von Lichess für einen Angriff: am 2026-09-11 erst `429`,
dann eine Sperre der ganzen IP, null registrierte Engines, und jeder Neustart des Containers wiederholte
genau das. Der Entrypoint wartet deshalb `PROVIDER_START_DELAY` Sekunden zwischen zwei Starts (Vorgabe 3;
13 Engines sind damit nach gut einer halben Minute alle da). `0` schaltet die Pause ab; bei einer einzelnen
Engine hat sie keine Wirkung. Beim Start steht je Engine eine Zeile „Warte 3 s vor Engine 2/13" im Log.

### Für Analyse-Aufträge: VIELE Engines mit WENIGEN Threads

Stockfish skaliert über Kerne schlecht, und für Analyse-Aufträge zählt ohnehin nicht die Zeit einer
Suche, sondern der **Durchsatz** — n Stellungen auf feste Tiefe, und die zerfallen in unabhängige
Teile. Gemessen am 2026-09-10 in einem Container mit 8 Kernen (Stockfish 19, Tiefe 20, 5 Linien,
8 echte Stellungen aus einer laufenden Warteschlange; gemessen wurde die WANDUHR für dieselbe
Menge Arbeit, die Engines liefen also wirklich gleichzeitig):

| Aufteilung | Wanduhr | Stellungen/Min | Gewinn |
|---|---|---|---|
| 1 Engine × 8 Threads | 66,2 s | 7,2 | 1,00× |
| 2 × 4 | 43,4 s | 11,1 | 1,53× |
| **4 × 2** | **22,4 s** | **21,4** | **2,95×** |
| 8 × 1 | 21,4 s | 22,4 | 3,10× |

Eine EINZELNE Suche wurde von 1 auf 8 Threads nur um den Faktor 1,13 schneller (6,2 s → 5,5 s) —
bei MultiPV 5 und dieser Tiefe bringen zusätzliche Threads fast nichts. Vier Instanzen holen fast
den ganzen Gewinn; acht bringen nur noch 5 % mehr und kosten vier weitere Registrierungen samt
eigener Hashtabelle.

```dotenv
# Die HAUPT-Engine für die Live-Analyse (immer genau eine)
ENGINE_PRIMARY_NAME=RookHub Server 19
ENGINE_PRIMARY_MAX_THREADS=8              # dort wartet ein Mensch auf EINE Stellung
ENGINE_PRIMARY_MAX_HASH=4096

# Die HINTERGRUND-Engines (alle gleich eingestellt)
ENGINE_BACKGROUND_NAME=RookHub Server 19 Hintergrund
ENGINE_BACKGROUND_MAX_THREADS=2
ENGINE_BACKGROUND_MAX_HASH=1024

# Wie viele davon
ENGINE_BACKGROUND_COUNT=4
```

Die Namen entstehen daraus als „…Hintergrund", „…Hintergrund 2", „…Hintergrund 3", …: die ERSTE
ohne Ziffer. Das ist keine Kosmetik — der Name IST die Identität der Lichess-Registrierung, und
eine Ziffer an der ersten machte aus jeder bestehenden Engine eine neue mit neuer Kennung.

Die früheren Namen dieser sieben Zeilen (`LIVE_NAME`, `LIVE_MAX_THREADS`, `LIVE_MAX_HASH`,
`BACKGROUND_NAME`, `BACKGROUND_MAX_THREADS`, `BACKGROUND_MAX_HASH`, `BACKGROUND_COUNT`) gelten
weiterhin als Rückfall: eine bestehende `.env` läuft unverändert weiter. Stehen beide da, gewinnt
die Schreibweise mit `ENGINE_`-Präfix.

Das ältere Schema (`ENGINE_COUNT` + `ENGINE_<i>_NAME/_MAX_THREADS/_MAX_HASH`) gilt weiter und
schlägt diese Werte — gebraucht nur noch für eine Engine, die aus der Reihe fallen soll.

Die **Live**-Engine behält alle Kerne: dort zählt die Zeit bis zum Ergebnis, nicht der Durchsatz.
Läuft sie, teilt sie sich die Kerne mit den Hintergrund-Engines — die werden dann eben langsamer.

In RookHub müssen die Hintergrund-Engines im Profil ALLE ausgewählt sein (Mehrfachauswahl, seit
0.460.0): der Server rechnet je Engine genau einen Auftrag, es laufen also so viele nebeneinander,
wie dort stehen. Live gemessen stieg der Durchsatz einer echten Warteschlange dabei von gut 4 auf
21 Stellungen je Minute.

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

Der Container bringt **Stockfish 19** mit — die offizielle Binärdatei. Bewusst nicht das
Debian-Paket: das ist ein generischer Build ohne AVX2/BMI2-Nutzung und rechnet auf derselben CPU
rund ein Drittel langsamer (auf dem Testserver gemessen: 4,98 vs. 8,39 Mio Knoten/s).

Seit Stockfish 19 gibt es **keine Varianten-Dateien** mehr: eine universelle Binärdatei erkennt
die CPU-Merkmale zur Laufzeit selbst (auf dem Testserver meldet sie `x86-64-bmi2`). Eine andere
Plattform (arm64, riscv64) wählt man über `SF_ASSET` samt passender `SF_SHA256`; den alten
`SF_VARIANT` gibt es nicht mehr.

⚠️ Stockfish 19 prüft Stellungen streng und **beendet sich bei einer ungültigen** (etwa einer
königlosen Diagramm-Stellung aus einer Chessable-Info-Linie) mit
`info string CRITICAL ERROR … Incorrect number of kings`. Der Provider stirbt mit der Engine,
`restart: unless-stopped` zieht den Container neu hoch — die Analyse ist also nicht dauerhaft
kaputt, aber eine krumme Stellung kostet den ganzen Pool einen Neustart.

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
python3 -m venv .venv && .venv/bin/pip install aiohttp
curl -O https://raw.githubusercontent.com/lichess-org/external-engine/d0eeb24229bae3cf5eb1e6696c0487ea05ef09ad/example-provider.py
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
pip install aiohttp
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
curl.exe -O https://raw.githubusercontent.com/lichess-org/external-engine/d0eeb24229bae3cf5eb1e6696c0487ea05ef09ad/example-provider.py
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

**Der Zombie-Leak:** `example-provider.py` startet die Engine über eine Shell
(`asyncio.create_subprocess_shell`, bis 0.478.10 `subprocess.Popen(…, shell=True)`) und beendet sie
bei Idle-Timeout per `process.terminate()`. Unter Windows tötet das nur die Shell-Hülle (`cmd.exe`), die Stockfish
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
| Nach dem Start `429` von lichess.org, danach gar keine Antwort mehr | Zu viele Registrierungen auf einmal — der DDoS-Schutz von Lichess sperrt die IP zeitweise. Container stoppen, die Sperre abwarten, `PROVIDER_START_DELAY` nicht auf 0 setzen |

Alte Registrierungen aufräumen kannst du auf <https://lichess.org/account/oauth/token> (Token
widerrufen) bzw. über die Engine-Verwaltung im Lichess-Analysebrett.

## Was hier läuft

Der Container startet den **offiziellen Provider von Lichess**
([lichess-org/external-engine](https://github.com/lichess-org/external-engine), GPL-3.0 wie
RookHub). Er wird beim Bauen auf einen festen Commit gepinnt und per Prüfsumme verifiziert,
statt ins Repo kopiert zu werden — so ist die Herkunft eindeutig, und ein Update ist ein
Zeilenwechsel im `Dockerfile`. Ergänzt haben wir nur `entrypoint.sh` (baut den Aufruf aus den
`.env`-Variablen und startet bei `ENGINE_COUNT`>1 mehrere Provider; `bash test/entrypoint.test.sh`
prüft den Argument-Aufbau im Trockenlauf, `bash test/supervisor.test.sh` den echten Fehlerpfad bei
mehreren Engines: stirbt einer, muss der Container mit DESSEN Code enden — sonst greift
`restart: unless-stopped` nicht) und `preflight.py` (prüft den Token vorab, damit ein fehlender Scope als
Klartext-Satz erscheint und nicht als endlos wiederholter Stacktrace). Den Provider selbst prüft
`python3 test/provider.test.py <provider.py>` gegen einen nachgebauten Broker (siehe „Provider-Stand"
oben).

Serverseitig ist die Gegenstelle in `rookhub/CLAUDE.md` unter „Externe Engine" beschrieben.
