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

## Nicht in diesem Repo

- **log-watcher** (eigenes Repo): Dockerfile ohne `USER`, Container läuft als root, `data/` gehört root — dort `USER`
  mit fester UID und `chown` von `data/` (bzw. des Volumes) nachziehen.
- piratechess (S2-007) und Explorer (S6-002) laufen ebenfalls als root — eigene Funde.
