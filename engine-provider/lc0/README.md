# Lc0 (Leela Chess Zero) als RookHub-Engine auf dem DGX Spark

Derselbe Engine-Provider wie beim Stockfish-Container (`../`), nur mit Lc0 statt Stockfish. Lc0 rechnet
mit einem neuronalen Netz auf der GPU. Der Container verbindet sich **ausgehend** mit RookHub und
meldet dort eine oder mehrere Engines an — es muss kein Port freigegeben werden.

## Voraussetzungen auf der Spark

- Docker mit NVIDIA Container Toolkit (auf dem DGX Spark ab Werk vorhanden). Prüfen:
  ```bash
  docker run --rm --gpus all nvidia/cuda:13.0.1-base-ubuntu24.04 nvidia-smi
  ```
- Ausgehend HTTPS zu `rookhub.oberschmid.homes`, beim Bauen zu GitHub, Docker Hub und `storage.lczero.org`.
- Ein RookHub-Token mit Scope **Engine** (Profil → API-Tokens).
- Rund 15 GB Platz für den Bau (CUDA-Entwicklungsabbild), das fertige Abbild ist etwa 3 GB groß.

## Einrichten

```bash
git clone https://github.com/kahalm/rookhub.git      # oder nur den Ordner engine-provider/ kopieren
cd rookhub/engine-provider/lc0
cp .env.example .env                                  # ROOKHUB_API_TOKEN eintragen, Namen prüfen
docker compose up -d --build                          # baut Lc0 aus den Quellen, ~15–30 min
docker compose logs -f                                # „Starte Engine-Provider … RookHub Spark Lc0"
```

Danach steht die Engine im RookHub-Analysebrett unter den externen Engines zur Auswahl.

Wichtig: **auf der Spark selbst bauen.** Das Abbild ist für ARM64 und Rechenfähigkeit 12.1 (GB10) gebaut.
Für eine andere Karte `CUDA_CC` in `compose.yml` anpassen (Werte stehen im Dockerfile).

## Prüfen ohne RookHub

```bash
docker compose run --rm --entrypoint /opt/lc0/lc0 engine-provider-lc0 \
  benchmark --weights=/opt/lc0/net/default.pb.gz --backend=cuda-fp16
```

Zeigt Knoten pro Sekunde. Ohne GPU geht dasselbe mit `--backend=blas`, nur sehr langsam.

## Einstellungen (.env)

| Variable | Bedeutung |
|---|---|
| `ENGINE_PRIMARY_NAME` | Name der Live-Engine. Der Name ist die Registrierung, also je Rechner eindeutig wählen. |
| `ENGINE_BACKGROUND_COUNT` | Zahl der Hintergrund-Engines, Vorgabe 0 (siehe „Tiefe"). |
| `LC0_BACKEND` | `cuda-fp16` (Vorgabe, schnell), `cuda` (fp32), `blas` (CPU, nur zum Prüfen). |
| `LC0_MAX_THREADS` | Such-Threads von Lc0, Vorgabe 2. Mehr bringt auf einer GPU kaum etwas. |
| `LC0_WEIGHTS` | Pfad zum Netz im Container. Eigenes Netz per Volume einhängen. |
| `LC0_ARGS` | weitere Lc0-Schalter, z. B. `--nncache=2000000 --minibatch-size=256`. |
| `ENGINE_SCHEDULE` | Zeitplan: wann wie viel gerechnet wird, z. B. `Mo-Fr 08:00-18:00 0%`. Leer = immer alles. Gleiche Regeln wie beim Stockfish-Container (`../README.md`, Abschnitt „Zeitplan"); auf einer GPU spart das nicht nur Kerne, sondern Strom und Wärme. Prüfen: `docker compose run --rm -e ENGINE_SCHEDULE_AT="Mo 09:00" engine-provider-lc0`. |

Netze (Bau-Argument `LC0_NET_URL` / `LC0_NET_SHA256` im Dockerfile):

| Netz | Größe | Wofür |
|---|---|---|
| BT4-1024x15x32h-swa-6147500-policytune-332 (Vorgabe) | 383 MB | stärkstes veröffentlichtes Netz, braucht GPU |
| t1-256x10-distilled-swa-2432500 | 37 MB | deutlich schneller, schwächer; gut für Durchsatz |

## Was anders ist als bei Stockfish

**Der Zwischenschalter (`lc0-uci.py`).** Der Provider schickt Befehle, die Lc0 nicht kennt (`Hash`,
`UCI_AnalyseMode`, `UCI_Variant`). Lc0 antwortet darauf mit einer Fehlerzeile. Der Zwischenschalter
verwirft diese drei Optionen und deckelt `Threads`; alles andere geht unverändert durch.

**Tiefe.** RookHub begrenzt jede Suche über die Tiefe: das Analysebrett mit seinem Tiefenregler, die
Hintergrund-Aufträge mit Tiefe 20 bis 22. Lc0 zählt Tiefe anders als Stockfish: sie steigt nur langsam,
weil Lc0 wenige, dafür sehr gute Stellungen ansieht. Wie lange Tiefe 20 bei Lc0 auf der Spark dauert,
ist noch nicht gemessen — so lässt es sich nachsehen:

```bash
printf 'uci\nisready\nposition startpos moves e2e4 e7e5\ngo depth 20\n' | \
  docker compose run --rm -T --entrypoint /opt/lc0/lc0-uci engine-provider-lc0 | grep -E '^info depth|bestmove'
```

Ein Hintergrund-Auftrag gilt in RookHub erst als fertig, wenn die gemeldete Tiefe das Ziel erreicht.
Bis die Messung zeigt, dass Lc0 Tiefe 20 in Sekunden schafft, gilt:

- **Als Live-Engine gut geeignet:** im Analysebrett auswählen, die Suche läuft, bis man weiterklickt.
- **In der Hintergrund-Liste nicht empfohlen:** Meisterpartien, Spielerpartien und Zugvergleiche würden
  pro Stellung ein Vielfaches der Zeit brauchen. Darum ist `ENGINE_BACKGROUND_COUNT=0` die Vorgabe.

**Die GPU wird geteilt.** Läuft auf derselben Spark das Sprachmodell (vLLM), rechnen beide auf derselben
GPU. Lc0 bremst dann die Übersetzungen und umgekehrt. Bei 128 GB gemeinsamem Speicher passt beides in
den Speicher, aber die Rechenzeit teilen sie sich.

## Lizenz

Lc0 steht unter GPL-3.0 (`/opt/lc0/COPYING` im Abbild), die Netze unter den Bedingungen von lczero.org.
