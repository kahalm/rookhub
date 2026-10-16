# board-train — Brettfeld-Netz trainieren (DGX Spark)

Trainiert das Netz, das RookHub zum Lesen gescannter Schachdiagramme nutzt (`Assets/ebook-board-cnn.onnx`), auf den
Opus-Lesungen aller Bücher. Läuft als kleiner Dienst im Container; der RookHub-Server schickt die Daten hin und holt
das Ergebnis ab.

| Datei | Zweck |
|---|---|
| `train.py` | Training (GPU automatisch, CPU geht auch); 10 Bretter je Buch zurückgehalten, bestes Netz gespeichert, ONNX-Export |
| `server.py` | HTTP-Dienst: `POST /jobs` (tgz), `GET /jobs/<id>`, `GET /jobs/<id>/files/<name>`, Token-Pflicht |
| `send.py` | Auf dem RookHub-Server: Daten packen, hochladen, warten, Ergebnis nach `~/claude/ebook-board-training/spark/out/` |
| `Dockerfile`, `compose.yaml` | Image `ghcr.io/kahalm/rookhub-board-train` (arm64), gebaut von `.github/workflows/board-train.yml` |

## Spark einrichten (einmal)
```bash
docker login ghcr.io            # Token mit read:packages
mkdir board-train && cd board-train && curl -O <compose.yaml aus diesem Ordner>
TRAIN_TOKEN=$(openssl rand -hex 24) docker compose up -d   # Token merken
curl http://localhost:8000/health   # {"ok": true, "cuda": true}
```

## Trainieren (vom RookHub-Server)
```bash
BOARD_TRAIN_TOKEN=<token> python3 tools/board-train/send.py --url http://<spark>:8000 --epochs 12
```
Ergebnis: `model_all.onnx` (Eingang `cells` [n,1,32,32] 0–1, Ausgang `logits` [n,13], Klassen `PNBRQKpnbrqk.`),
`model_all.pt`, `eval.tsv` (je Buch richtig von 10 zurückgehaltenen, schwächste zuerst), `train.log`.

Die Trainingsdaten (Ausschnitte aus Buchscans) liegen NIE im Repo — nur auf dem Server und für die Dauer eines Laufs im Container.
