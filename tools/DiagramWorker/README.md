# Diagramm-Worker als Container

Externer Diagramm-Worker für RookHub (ab 0.873.0, Server ab 0.870.0): leiht wartende E-Books über `/api/diagram-worker`,
erkennt die Diagramme mit dem eingebauten `rookhub-diagrams` (`tools/DiagramCli`, derselbe Code wie der Server) und
schickt das Ergebnis zurück. Seit 0.874.0 schneidet er außerdem **Bretter** für die Opus-Schleife aus (`/api/board-worker`,
derselbe Token; Brett-Aufträge gehen vor) — das Ausschneiden ist wörtlich der Code des Laptop-Workers (`boardcut/`,
bytegleiche Ergebnisse). Die WebDAV-Aufträge macht weiter nur der Laptop. Ablauf, Leihe und Endpunkte: `docs/api/ebooks.md`,
„Externe Diagramm-Worker“ und „Brett-Ausschneide-Aufträge“.

## Starten (z. B. DGX Spark)

1. Token anlegen: RookHub → Profil → API-Tokens → Bereich **„Diagramm-Worker“** (ein Token je Rechner).
2. In einem Ordner `compose.yaml` von hier ablegen und daneben:

   ```sh
   echo 'ROOKHUB_TOKEN=rkh_…' > .env && chmod 600 .env
   docker compose up -d && docker compose logs -f
   ```

Image: `ghcr.io/kahalm/rookhub-diagram-worker:latest` (amd64 + arm64), gebaut von `.github/workflows/diagram-worker.yml`
bei jedem Release-Tag (dann zusätzlich `:vX.Y.Z`) und bei Änderungen an Worker oder DiagramCli. Nach einem Tag mit
geänderter Erkennung: `docker compose pull && docker compose up -d`.

## Einstellungen (Umgebung)

| Variable | Vorgabe | Bedeutung |
|---|---|---|
| `ROOKHUB_TOKEN` / `ROOKHUB_TOKEN_FILE` | — | Token bzw. Datei mit Token (wird nie ausgegeben) |
| `ROOKHUB_URL` | `https://rookhub.oberschmid.homes` | Server |
| `WORKER_NAME` | Rechnername (`spark` in compose) | Name im Server-Log |
| `THREADS` | Kerne − 2 | Seitenstapel gleichzeitig |
| `IDLE_SECONDS` | 30 | Pause ohne Arbeit |
| `BOARDS` / `DIAGRAMS` | an | `0` schaltet Brett-Aufträge bzw. Diagramm-Erkennung ab |

`docker stop` gibt ein laufendes Buch sofort frei (SIGTERM), sonst wäre es bis zum Ablauf der Leihe (30 min) gesperrt.

## Lokal bauen

```sh
docker build -f tools/DiagramWorker/Dockerfile --build-arg VERSION=<APP_VERSION> -t rookhub-diagram-worker .
```

Kontext ist die Repo-Wurzel; `Dockerfile.dockerignore` schickt nur API, DiagramCli und den Worker mit. Test des Workers:
`python3 scripts/tests/test_diagram_worker.py` (läuft in der CI im Job `test-scripts`).
