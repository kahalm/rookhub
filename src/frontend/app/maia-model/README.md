# Maia-3-Modell (Sparring im Analysebrett)

`maia3_simplified.onnx` ist das neuronale Netz Maia-3 des CSSLab der Universität Toronto
(Paper „Chessformer: A Unified Architecture for Chess Modeling", ICLR 2026): es sagt vorher, welchen
Zug ein Mensch einer bestimmten Spielstärke wählt. RookHub lässt es im Browser laufen
(`onnxruntime-web` in einem eigenen Worker, siehe `src/app/features/analysis/maia/`). Herkunft:
`CSSLab/maia-platform-frontend`, Pfad `public/maia3/maia3_simplified.onnx`, Lizenz GPL-3.0 wie RookHub.
Commit, Prüfsumme und Größe stehen als Pin in `fetch.sh` — die Datei selbst liegt nicht im Repo
(45 MB), der Docker-Build holt sie beim Bauen des RookHub-Images und bricht ab, wenn sie nicht passt.

Lokal (für `ng build app` / `ng serve app` mit Maia) im Ordner `src/frontend/app`:
`sh maia-model/fetch.sh` — lädt die Datei einmal hierher und prüft sie; liegt sie schon mit
richtiger Prüfsumme da, tut das Skript nichts. Ohne die Datei baut die App trotzdem, nur meldet das
Analysebrett dann „Das Maia-Modell ist auf diesem Server nicht verfügbar".
