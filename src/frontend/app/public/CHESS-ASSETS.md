# Schach-Assets (Drittquellen / Attribution)

Figuren-Sets und Brett-Texturen stammen aus dem Lichess-Projekt **lila**
(https://github.com/lichess-org/lila) — maßgeblich ist `lila/COPYING.md`.

## Figuren-Sets (`public/piece/<set>/`)
| Set | Autor | Lizenz |
|-----|-------|--------|
| merida | Armando Hernandez Marroquin | GPLv2+ |
| fantasy | Maurizio Monge | MIT |
| spatial | Maurizio Monge | MIT |
| celtic | Maurizio Monge | MIT |
| chessnut | Alexis Luengas | Apache 2.0 |
| rhosgfx | RhosGFX | CC0 1.0 (Public Domain) |
| cburnett (Default, via `chessground`) | Colin M.L. Burnett | GPLv2+ |

Bewusst NICHT verwendet: Sets mit CC BY-NC-SA (nicht-kommerziell), z. B. alpha,
california, dubrovny, maestro, staunty, horsey — falls RookHub später kommerziell wird.

## Brett-Texturen (`public/board/`)
`wood4.jpg`, `blue3.jpg`, `marble.jpg`, `metal.jpg`, `leather.jpg`, `maple.jpg` — Lichess (lila authors) — **AGPLv3+**.
(AGPL ist mit RookHub als Open-Source-Projekt vereinbar; bei einem späteren Wechsel zu
Closed-Source/SaaS müssten diese Texturen ersetzt werden.)

## Maia-3 (Sparring im Analysebrett)
- **Modell** `maia3_simplified.onnx` aus `CSSLab/maia-platform-frontend` (https://github.com/CSSLab/maia-platform-frontend,
  Pfad `public/maia3/`), Commit `a6e52f5c811ee18863cb2f0e81f2433a5b9905de` — **GPL-3.0** (wie RookHub).
  Paper: „Chessformer: A Unified Architecture for Chess Modeling" (Monroe, Eilender, Chalmers, Tang, Anderson; ICLR 2026).
- **Kodierung** (Stellung → Eingabe, Zug ↔ Index) nachgebaut nach deren `src/lib/engine/tensor.ts`
  (`src/app/features/analysis/maia/maia-encoding.ts`).
- **Laufzeit** `onnxruntime-web` (Microsoft) — **MIT**; drei Dateien aus `node_modules/onnxruntime-web/dist`
  werden als Assets unter `/assets/ort/` ausgeliefert.
- Das Modell liegt NICHT im Repo: der Build holt es (`maia-model/fetch.sh`, Pin per Commit + sha256).
