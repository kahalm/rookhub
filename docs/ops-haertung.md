# Container-Härtung (Codereview I1-013) — Vorschlag für den Betreiber

Stand 26.10.2026. **In den Images umgesetzt**, in den Compose-Dateien **bewusst noch nicht**: `read_only` kann
Schreibpfade übersehen (nginx-Cache, .NET-Temp, `/keys`, E-Book-Titelbilder), das muss je Dienst auf Dev laufen, bevor
es in `/opt/stacks/rookhub-schach{,-dev}` und in die Compose-Dateien des Repos kommt (`.claude/rules/compose.md`: alle
fünf gleich halten; `DeploymentConfigTests` nachziehen).

## Schon in den Images

| Image | Laufzeitnutzer | Beschreibbar für den Nutzer | Root-eigen, nur lesbar |
|---|---|---|---|
| Oberflächen (`src/frontend/Dockerfile`: RookHub, Turnier, KidHub, LeagueHub, ClubHub) | `nginx` | `/var/cache/nginx` (Temp-Dateien, Kachel-Cache `tiles/`), `/var/log/nginx` (→ stdout/stderr), `/tmp/nginx.pid` | `/usr/share/nginx/html` (Bundles, `build-info.json`), `/etc/nginx/conf.d` |
| API (`src/api/RookHub.Api/Dockerfile`) | `appuser` | `/keys` (DataProtection-Volume), `/tmp` | `/app` |

Vorher gehörten `html/` und `conf.d/` per `chown` dem nginx-Nutzer: eine Schwachstelle mit Schreibrecht im
nginx-Prozess hätte `main-*.js` für jeden Besucher bis zum nächsten Recreate verändern können (`docker restart` behält
die Schicht). Wache: `DeploymentConfigTests.Images_RunUnprivileged_AndCannotRewriteWhatTheyServe`.

Im Start-Log der Oberflächen steht jetzt `10-listen-on-ipv6-by-default.sh: info: can not modify
/etc/nginx/conf.d/default.conf` — harmlos, unsere `nginx.conf` lauscht selbst auf 8080 (das Skript hätte die Datei
ohnehin nur bei der Original-Prüfsumme angefasst).

## Vorschlag Compose (nach Dev-Test)

Oberflächen (`frontend`, `turnier`, `kidhub`, `leaguehub`, `clubhub`):

```yaml
    read_only: true
    tmpfs:
      - /tmp
    volumes:
      # Kachel-Cache (proxy_cache_path, bis 512 MB) soll Neustarts überleben → Volume statt tmpfs.
      - frontend-cache:/var/cache/nginx
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL          # nginx als Nutzer nginx auf 8080 braucht keine Capability
```

API (`api`):

```yaml
    read_only: true
    tmpfs:
      - /tmp         # .NET-Temp, pdftoppm/ddjvu (Titelbilder), Uploads im Zwischenschritt — Größe beobachten
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
```

Vor dem Umstellen auf Dev prüfen: Anmeldung (DataProtection schreibt nach `/keys`), E-Book-Titelbild erzeugen, Partie-
formular hochladen, Kurs-/PGN-Import, Analyse (Stockfish), Kartenkacheln auf der Turnierseite (`X-Tile-Cache: MISS` →
`HIT`), Container-Neustart. Schreibt etwas außerhalb dieser Pfade, scheitert es mit `Read-only file system` im Log.
Danach E2E (`compose.e2e.yml`) mit denselben Einstellungen grün.

## api: stop_grace_period: 30s (Prod/Dev-Compose)

Codereview A8-011. Beim Stopp arbeiten zwei Worker der API noch ab, was in ihren (nur im Arbeitsspeicher liegenden)
Warteschlangen steht — `WebhookTaskWorker` und `BackgroundTaskWorker`, NACHEINANDER. Ohne `stop_grace_period` gibt
Docker 10 s, dann `SIGKILL`. Mit je 5 s füllten die beiden Drains dieses Fenster allein; für die übrigen Hosted
Services (Abbruch laufender Analysen, Heartbeat) und den Log-Flush blieb nichts, die letzten Logzeilen gingen verloren.

- **Im Code (umgesetzt):** gemeinsames Drain-Budget `BackgroundTasks:DrainSeconds` (`ShutdownDrain`, Vorgabe 3 s,
  0–30; zusammen 6 s < 10 s). `0` = gar nicht drainieren (Rest wird wie bisher als „verworfen“ protokolliert).
- **Im Compose (macht der Betreiber, Dateien liegen außerhalb des Repos):** für den Dienst `api` in
  `/opt/stacks/rookhub-schach{,-dev}/compose.yaml`

```yaml
    stop_grace_period: 30s
```

  Die .NET-Vorgabe `HostOptions.ShutdownTimeout` ist ebenfalls 30 s — erst damit hat das Herunterfahren die Zeit, die
  der Host ohnehin einplant. Wer das Budget danach erhöhen will (`BACKGROUNDTASKS__DRAINSECONDS`), bleibt mit beiden
  Drains unter der Grace-Period.

## Nicht in diesem Repo

- **log-watcher** (eigenes Repo): Dockerfile ohne `USER`, Container läuft als root, `data/` gehört root — dort `USER`
  mit fester UID und `chown` von `data/` (bzw. des Volumes) nachziehen.
- piratechess (S2-007) und Explorer (S6-002) laufen ebenfalls als root — eigene Funde.
