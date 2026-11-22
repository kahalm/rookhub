# Maia-3-Modell (Fehler-Erklärungen der API)

Dasselbe Netz wie im Analysebrett (`src/frontend/app/maia-model/README.md`, CSSLab, GPL-3.0): der `MaiaService` der API
sagt damit voraus, welchen Zug ein Mensch der Stärke der Partie spielt — die natürlichen Züge der Fehler-Erklärungen
(`docs/api/zuege-vergleichen.md`, `docs/api/maia-sparring-im-analysebrett.md`). Die Datei liegt nicht im Repo; der
Docker-Build der API holt sie mit `fetch.sh` (gleicher Pin wie das Frontend) nach `/app/maia/maia3_simplified.onnx`.
Lokal: `sh maia-model/fetch.sh` hier im Ordner `src/api/RookHub.Api`.
