# Maia-Sparring im Analysebrett (0.632.0)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Maia-Sparring im Analysebrett (0.632.0)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Wunsch: „Maia-3 in unterschiedlicher Stärke für Sparring von Stellungen, Knopf im Analyse-Modus". Maia-3 (CSSLab,
Universität Toronto, GPL-3.0) ist ein neuronales Netz, das den Zug eines MENSCHEN einer bestimmten Stärke vorhersagt —
kein Rechner. Karte „Sparring gegen Maia" zwischen Engine-Karte und Zugliste; kein Backend, kein Endpunkt, keine
Migration. Code: `features/analysis/maia/` (siehe `src/frontend/CLAUDE.md`). Entscheidungen und warum:
1. **Läuft im Browser** (ONNX-Modell + `onnxruntime-web` WASM, eigener Worker) — kein Dienst, kein Container.
2. **Modell `maia3_simplified.onnx` nicht im Repo** (45 MB): `src/frontend/app/maia-model/fetch.sh` holt es beim Docker-Build
   (nur `APP_PROJECT=app`, VOR `COPY app/ .` → Layer-Cache) bzw. lokal (`sh maia-model/fetch.sh`) und prüft sha256.
3. **`onnxruntime-web` exakt gepinnt** und NIE in TypeScript importiert: drei Dateien aus `dist/` gehen als Assets nach
   `/assets/ort/`, der Worker lädt sie per `importScripts` (Muster wie Stockfish).
4. **Klassischer Worker als statische Datei** (`maia-worker.js` → `/assets/maia/`), kein gebündelter TS-Worker.
5. **Zwischenspeicher = Cache API mit eigenem Code** (Fortschritt!), nicht der ngsw: der puffert ohne Fortschritt. Laufzeit +
   Worker stehen in der `lazy`-Gruppe `maia` (`ngsw-config.json`), das `.onnx` in KEINER Gruppe. Ohne sicheren Kontext (Dev
   über HTTP) gibt es keine Cache API — die Rückfrage sagt dann „wird bei jedem Besuch neu geladen" (`canStore`).
6. **Stärke = Elo als roher Wert** ans Modell (`elo_self` = `elo_oppo`), 600–2600 in 200er-Schritten, Vorgabe 1600, je Gerät
   (`rookhub_analysis_maia_elo`).
7. **Gewürfelt, nicht der wahrscheinlichste Zug** (Temperatur 1, Nucleus `topP` 0,95) — dieselbe Stellung soll verschiedene
   Antworten bekommen.
8. **Engine während des Sparrings AUS** (sonst Bewertung + Pfeile), Schalter gesperrt; danach kommt der vorige Zustand. Ausnahme:
   die zwei Maia-Schalter lassen sie STILL mitlaufen (unten, „Schlechte Züge melden + Bewertungsleiste“).

**Zwei Fallen, live nachgestellt:** (1) `nginx:alpine` kennt `.mjs` nicht und liefert `application/octet-stream` — ORT scheitert
mit „Failed to fetch dynamically imported module"; `nginx.conf` hat dafür `location ~* \.mjs$` (ohne `add_header`, das ersetzte
die CSP). (2) Eine fehlende Datei unter `/assets/` beantwortet der SPA-Fallback mit **200 + index.html** — ob das Modell da ist,
erkennt `MaiaModelStore` deshalb nur an der GRÖSSE, nie am Status, und legt eine falsche Größe nie in den Cache.

**Pin ändern:** COMMIT/SHA256/BYTES in `maia-model/fetch.sh` UND `version` (erste acht Zeichen der sha256) + `bytes` in
`maia/maia-model.ts`, dazu der Commit in `public/CHESS-ASSETS.md`. `DeploymentConfigTests.Maia_ModelPin_AndDelivery_StayConsistent`
hält das zusammen (dazu Dockerfile-Bedingung, `.mjs`-Regel, ngsw, angular.json nur bei `app`, exakter ORT-Pin).

**Sparring-Regeln** (`analysis.component.ts`): Start (die Karte meldet `start` erst mit fertigem Modell) nur in einer Stellung mit
legalen Zügen — der Nutzer spielt die Seite am Zug, Brett dreht sich, `engineOn` wird gemerkt, aber nicht in localStorage
geschrieben. Auf einen EIGENEN Zug antwortet Maia (Mindest-Bedenkzeit `maiaDelayMs` 500), auch auf einen per Explorer/Repertoire-Karte
gespielten (`playRepertoireMoves`); ein Zug für Maias Seite löst nichts aus. Jede Navigation (`goTo`/`goToNode`), der Editor, „Seite wechseln" und das Ende lassen eine laufende Antwort verfallen
(`maiaEpoch`) — Maia zieht nach einer Navigation NIE von selbst, dafür gibt es „Maia zieht". „Seite wechseln" und „Nochmal ab der
Ausgangsstellung" fordern sofort einen Zug, wenn dann Maia am Zug ist. Beenden, jeder neue Baum (Zurücksetzen, FEN/PGN, Stellung
aufbauen, Verlauf-Eintrag) und das Löschen der Ausgangsstellung beenden das Sparring und stellen `engineOn` wieder her; ein Fehler
von Maia → Snackbar, das Sparring bleibt, und ist die Sitzung dabei weg (Status nicht `ready`), baut `prepare()` sie im Hintergrund
aus dem Cache neu auf. Maias Züge laufen über `goToNode` und landen damit im Zugbaum und im Analyse-Verlauf.
Verlassen der Seite → `release()` (gibt ~150 MB frei; das Modell kommt beim nächsten Mal in ~1 s aus dem Cache).

**Partie analysieren nach dem Sparring (0.644.0):** derselbe Weg wie in „Meine Partien" — die Sparring-Partie wird über
`POST /api/games/import` eine gewöhnliche Partie (Quelle `pgn`) und läuft dann durch `AnalyzeGameService.submit`; bei Erfolg
geht es auf `/games/{id}` (Kurve, Zug-Klassen, Fehler-Training), bei einer Absage bleibt man, ohne Id/bei Fehler Snackbar
`analysis.maia.saveFailed`. Die Partie ist die Linie vom Start bis zum zuletzt GESPIELTEN Zug (`sparring.tip`, gesetzt von
eigenem Zug, Maias Zug und Explorer/Repertoire-Zug im Teilbaum des Starts) — nicht `lineThrough(start)`, denn mitten in einer
geladenen Partie stehen dort deren Züge. Beim Ende bleibt sie als `lastSparring` stehen (ab zwei Halbzügen), verfällt mit jedem
neuen Baum und wenn ihre Züge gelöscht werden. Knopf nur angemeldet; während eines Sparrings nur, wenn die Stellung zu Ende
ist (der Klick beendet es dann wie „Beenden"). Die Engine-Auskunft (`status()`) kommt höchstens einmal, sobald der Knopf zum
ersten Mal sichtbar würde. Das PGN baut `maia/sparring-pgn.ts` (chess.js, „Maia 1600" als Name, keine Elo-Kopfzeilen, im
Zugtext die kompakte Form „4... Bc5", damit es auch gegen eine API ohne die Parser-Reparatur geht). **`ownerSide` geht mit**:
für Quelle `pgn` rät `DetermineOwnerSide` die Seite über den Plattform-Namen, und der trifft hier nie — ohne Seite schrieben
die Fehler-Erklärungen neutral, das Fehler-Training nähme die Seite mit den meisten Fehlern, und das Brett drehte nicht.

**Schlechte Züge melden + Bewertungsleiste (0.645.0):** zwei Schalter in der Maia-Karte, je Gerät gemerkt
(`rookhub_analysis_maia_warn`, `rookhub_analysis_maia_evalbar`, Vorgabe aus), jederzeit umschaltbar — auch mitten im Sparring,
wirkt sofort (`refresh()`). Regeln, die nicht kippen dürfen:
* **Stille Engine** (`sparringEngineQuiet` = Sparring UND einer der Schalter): nur die HAUPT-Engine rechnet (auch nach Tiefen-/
  Linienwechsel, `restartSearches`), nie die Vergleichs-Engine; Linien, Pfeile und Kandidaten bleiben LEER — sie verrieten den
  besten Zug. Leiste und `node.evalText` nur mit „Bewertungsleiste anlassen"; ohne sie ist die Leiste neutral. Die Vorlage
  bleibt: der Engine-Schalter ist gesperrt, „Engine pausiert während des Sparrings" stimmt für das, was man SIEHT.
* **Tiefen-Spur** (`evalTrack`): je Stellung die Bewertung der besten Linie je Tiefe (Weiß-Sicht), bei `refresh()` für die neue
  Stellung geleert. Ein EIGENER Zug am Brett (`onMove`, nicht Explorer/Repertoire, nie einer für Maias Seite) nimmt die Spur der
  Stellung davor mit (`pendingBefore`).
* **Vergleich bei GLEICHER Tiefe** (`maia/sparring-check.ts`, rein): Nachher-Wert = die Stellung nach dem Zug bei
  `min(MAIA_CHECK_DEPTH 14, Tiefe)` oder früher, wenn die Suche endet (Matt); Vorher-Wert = die größte Tiefe ≤ der des
  Nachher-Werts (`matchingBefore`) — eine flache gegen eine tiefe Bewertung meldete sonst Schwankungen der Suche als Fehler.
  Warnung ab `MAIA_BAD_MOVE_PAWNS` 0,2 Bauern Verlust aus Sicht des Ziehenden (in Centibauern gerechnet, Matt = ±(1000 − n)
  Bauern). Spätestens nach `maiaCheckTimeoutMs` (4 s) mit dem tiefsten Wert ab `EVAL_SETTLE_DEPTH`, sonst kein Urteil.
* **Maias Antwort wartet auf das Urteil** (dritte Zusage im `Promise.all` von `requestMaiaMove`): so steht die Warnung schon da,
  wenn ihr Zug aufs Brett kommt, und ihr Zug würgt die Suche der Stellung nach dem eigenen Zug nicht ab. Maias eigene Züge
  werden nie geprüft. Navigation, „Seite wechseln", Beenden und Schalter-aus lösen eine offene Prüfung ohne Urteil auf.
* **Die Warnung** („12.Nf3 war nicht gut: +0.40 → −0.30") ersetzt bzw. löscht jede neue Prüfung; sie geht bei Beenden/Ende,
  „Nochmal", „Seite wechseln", neuem Baum und „Analysieren" weg, NICHT bei Navigation. **„Analysieren"** beendet das Sparring
  wie „Beenden" (die Partie bleibt für „Partie analysieren"), schaltet die Engine EIN — nur für diese Sitzung, nicht in
  localStorage — und springt auf die Stellung VOR dem Zug; der Zug steht dort als Fortsetzung, die Linien zeigen, was besser war.
