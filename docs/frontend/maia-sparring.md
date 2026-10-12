# Maia-Sparring (`features/analysis/maia/`, 0.632.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


Sparringsgegner im Analysebrett (Entscheidungen, Fallen, Pin und Regeln: Haupt-CLAUDE.md „Maia-Sparring im Analysebrett").

**Sichtbarkeit (0.760.0):** Die Karte ist standardmäßig NICHT auf der Seite. `PositionMenuComponent` zeigt mit
`[sparring]="!editing"` den Eintrag „Sparring gegen Maia" (`pm-sparring`, `analysis.maia.title`) und meldet `sparringOpen`;
das Analysebrett setzt `maiaCardOpen` (nur im Speicher) und scrollt die Karte in Sicht. `maiaCardVisible` = offen ODER
`spar.sparring`; die Karte bekommt `[closable]="true"` und zeigt ✕ (`maia-close` → `dismiss`) nur ohne laufendes Sparring.
Spec: `analysis-maia-card.spec.ts` (eigene Datei, `analysis.component.spec.ts` ist > 2 000 Zeilen).

- `maia-model.ts` — feste Werte: Modell-Adresse, `version`/`bytes` (Pin-Spiegel von `maia-model/fetch.sh`), Stärken, Vorgabe,
  localStorage-Schlüssel, `MAIA_TOP_P`.
- `maia-encoding.ts` — rein, ohne Angular: Stellung → Tokens (Schwarz am Zug wird gespiegelt), Zug ↔ Index (Formel statt
  Tabelle), Softmax nur über legale Züge, Nucleus-Würfel `pickMove`. Spec mit LITERALEN Vektoren aus dem Plan.
- `maia-model-store.ts` — Modell laden (Fortschritt aus der ERWARTETEN Größe) und in der Cache API halten; die Größe entscheidet,
  nie der Status (SPA-Fallback); `canStore` = ob der Browser es behalten kann. Seams: `fetch`, `caches` (`null` = keine), Modell.
- `maia-worker.js` — klassischer Worker, statisch nach `/assets/maia/` kopiert, rechnet nur das Netz.
- `maia-engine.service.ts` — root-Dienst ohne DI (`new` in Specs): Status/Fortschritt/Fehler/`canStore` als Signale, EIN laufendes
  `prepare()`/`download()`, Antworten über die `id`, Init-Frist `initTimeoutMs` (60 s), `release()`. Seams: `createWorker`,
  `store`, `rng`.
- `maia-sparring-card.component.ts` — die Karte (OnPush, Signale): führt den Lade-Ablauf selbst (Rückfrage mit 46 MB, Fortschritt,
  Fehler + „Erneut versuchen") und meldet `start` erst mit fertigem Modell; die aktive Ansicht schickt nur Ereignisse. Seit 0.644.0
  dazu „Partie analysieren" (`showAnalyze`/`analyzing`/`analyzeBlocked` → `analyze`; ein gesperrter Knopf zeigt keinen Tooltip,
  der Grund steht deshalb als Zeile darunter) und die zwei Schalter „Schlechte Züge melden"/„Bewertungsleiste anlassen"
  (`warnBadMoves`/`keepEvalBar` → `…Change`, in Ruhe- und aktiver Ansicht) samt Warnung zum letzten eigenen Zug (`warning`, nur
  aktiv; „Analysieren" → `analyzeWarning`). Gemerkt, geprüft und gesprungen wird im Analysebrett.
- `sparring-pgn.ts` — rein, ohne Angular (0.644.0): `buildSparringPgn` macht aus Ausgangsstellung + SAN-Zügen das PGN für
  „Partie analysieren" (chess.js schreibt SetUp/FEN und die Nummern; im Zugtext die kompakte Form „4... Bc5", Ergebnis aus der
  Endstellung). Spec mit LITERALEN PGN-Texten.
- `sparring-check.ts` — rein, ohne Angular (0.645.0): „Schlechte Züge melden" — `pawnsOf` (Matt = ±(1000 − n) Bauern),
  `matchingBefore` (Vorher-Wert bei gleicher Tiefe), `evalDrop` (Sicht des Ziehenden), `badMoveVerdict` (ab
  `MAIA_BAD_MOVE_PAWNS` 0,2, in Centibauern verglichen), dazu der Typ `SparringWarning` für die Karte. Spec mit LITERALEN Werten;
  Ablauf (stille Engine, Tiefen-Spur, Maias Zug wartet aufs Urteil) im Haupt-CLAUDE.md.

**Regel: onnxruntime-web wird NIE in TypeScript importiert.** Die drei Laufzeit-Dateien kommen als Assets (`angular.json`, nur
Projekt `app`) nach `/assets/ort/` und werden im Worker per `importScripts` geladen — ein Import zöge die Laufzeit in ein Bundle
(der Abnahme-Check `grep -l onnxruntime dist/app/browser/*.js` muss leer bleiben). Specs fälschen Worker und Store, es entsteht nie
ein echter Worker; in TestBed-Specs des Analysebretts steht `MaiaEngineService` deshalb als Fake-Provider.
