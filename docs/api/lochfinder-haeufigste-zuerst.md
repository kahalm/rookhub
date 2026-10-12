# Lochfinder + „Häufigste zuerst" (Lichess-Explorer, 0.502.0)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Lochfinder + „Häufigste zuerst" (Lichess-Explorer, 0.502.0)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Zwei Fragen, eine Rechnung (`Services/Repertoires/RepertoireReach.cs`, Modell aus Opening Fenix, GPLv3):
**Wie oft landet man in welcher Repertoire-Stellung?** Die Wurzel hat Wahrscheinlichkeit 1; ist der
NUTZER am Zug, teilt sie sich gleichmäßig auf seine Repertoire-Züge auf, ist der GEGNER am Zug, nach
den Explorer-Häufigkeiten. Knoten sind Stellungen, Zugumstellungen summieren sich. Daraus fallen
(1) die **Löcher** — ein Gegnerzug mit Anteil ≥ Schwelle, dessen Zielstellung NIRGENDS im Repertoire
steht (auch nicht über eine andere Zugfolge), sortiert nach `P(Stellung) × Anteil` — und (2) die
**Linien-Häufigkeit** für den Trainer-Modus „Häufigste zuerst" (P der tiefsten bekannten Stellung
der Hauptvariante, Schlüssel = Endstellung).

Regeln, die dabei nicht kippen dürfen:
* **Nur Gegner-Stellungen MIT Repertoire-Antwort werden abgefragt.** Das Ende einer Linie ist kein
  Loch — sonst wäre jede Chessable-Linie (endet mit dem eigenen Zug) ein Nest aus Löchern.
* **Stellungs-Schlüssel = die ersten DREI FEN-Felder** (ohne en passant), dieselbe Regel wie
  `RepertoirePositionLookupService.NormalizeKey` ↔ `normalizeFen` in `position-filter.util.ts`: die
  Linien-Häufigkeiten ordnet der CLIENT über diesen Schlüssel zu, und chess.js setzt das ep-Feld
  anders als Gera.Chess.
* **Die Farbe je Kapitel rechnet der Client** (`chapterColorsOf` in `repertoire-color.util.ts`,
  dieselbe Funktion wie im Trainer) und schickt sie mit. Eine zweite Heuristik am Server würde
  auseinanderlaufen — und die eigenen Festlegungen liegen ohnehin nur im Browser.
* **Abbruchgrenzen**: `MinReach` (0,02 %) — seltenere Stellungen werden nicht mehr abgefragt;
  `MinGames` (10) — darunter keine Löcher und keine Weitergabe. Repertoire-Gegnerzüge, die der
  Explorer gar nicht kennt, bekommen „eine halbe Partie", damit ihre Linien noch geordnet werden.
* **Zeitbudget statt Hintergrundauftrag** (`RepertoireExplorerService.Budget`, 20 s): der Endpunkt
  antwortet mit dem bisherigen Stand, der Client (`RepertoireExplorerService.run`) fragt Runde um
  Runde nach und hört auf, wenn alles da ist, ein Token fehlt/abgelehnt wird, der Explorer wiederholt
  nicht antwortet oder eine Runde OHNE Drossel keinen Fortschritt bringt.
* **EINE Leitung zum Explorer** (`LichessExplorerGate`, Singleton): Anfragen nacheinander UND aus
  einem eigenen Kontingent — 15 sofort, danach eine je 4 s (`LichessExplorer__Burst`,
  `LichessExplorer__RefillMs`). Gegen den echten Explorer gemessen (mit Token): dicht hintereinander
  429 nach 21 Anfragen, mit 0,5 s Abstand nach 27, mit 1 s Abstand nach 33 — das passt zu einem Eimer
  von gut 20, der mit ~0,3/s nachläuft. Ein fester Abstand hilft deshalb nicht; auf Dauer sind es
  rund 15 Stellungen je Minute. Nach einem 429 eine Minute Ruhe für ALLE Läufe (die Antwort nennt
  `retryAfterSeconds`), der Eimer ist danach LEER. Die erste Abfrage einer Runde geht immer raus —
  sonst könnte eine Runde ohne Fortschritt enden, und der Client hielte den Lauf für festgefahren.
* **Token**: seit 2025 verlangt der Explorer eine Anmeldung (ohne: 401). Zuerst gilt
  `LichessExplorer:Token` (Compose `LICHESS_EXPLORER_TOKEN`, beliebiger Token ohne Scope), sonst der
  Engine-Token, den der Nutzer im Profil hinterlegt hat.
* **Speicher** `LichessExplorerCacheEntries`: je Auswahl (Datenbank + Elo + Tempo) und Stellung,
  90 Tage, für ALLE Nutzer — die ersten Züge sind in jedem Repertoire dieselben.

**Explorer auf dem Analysebrett** (0.504.0, `features/analysis/opening-explorer.component.ts`): Karte
unter der Zugliste, nur angemeldet. Fragt `GET /api/explorer/position` erst 250 ms nach dem letzten
Stellungswechsel (Durchklicken einer Partie mit den Pfeiltasten kostet online sonst Kontingent),
merkt sich Antworten je Stellung + Auswahl im Speicher der Seite, verwirft eine späte Antwort für
eine schon verlassene Stellung und fragt zugeklappt gar nicht (`rookhub_analysis_explorer_open`; seit 0.760.0
ohne gemerkten Wert ZUGEKLAPPT, nur `'1'` klappt auf).
Die Auswahl ist DIESELBE wie im Lochfinder (`rookhub_explorer_settings`). **Vorgabe seit 0.504.1:
lokal + Meister** (`DEFAULT_EXPLORER_SETTINGS`); ohne lokalen Explorer gilt online, und dieser
Rückfall wird NICHT gespeichert (sonst bliebe ein Gerät nach einem Besuch auf einem Server ohne
lokalen Explorer für immer auf online). Dafür tragen die
Explorer-Daten seit 0.504.0 auch Weiß/Remis/Schwarz und das Durchschnitts-Elo je Zug
(`ExplorerMoveStat`, JSON `w`/`d`/`b`/`r`); ein Speicher-Eintrag von vorher (`HasResults == false`)
genügt dem Lochfinder weiter, der Explorer holt ihn neu und überschreibt ihn.

**Beliebtheit im Repertoire-Baum** (0.508.0): hinter jedem Zug steht, wie oft er in der Stellung
davor gespielt wird — für eigene UND gegnerische Züge dieselbe Frage, aus `GET /api/explorer/position`
der aktuellen Baumstellung (`RepertoireDetailComponent.loadTreePopularity`, Auswahl wie im
Lochfinder, je Stellung + Auswahl im Speicher der Seite). Führt der Explorer einen Repertoire-Zug
nicht, steht „—" (kaum gespielt). **Bewusst NICHT die Häufigkeit „wie oft erreicht man die Stellung"**
(0.506.0/0.507.0, wieder entfernt): bei eigenen Zügen stand dort immer der Wert davor — bei einem
Weiß-Repertoire mit nur 1.e4 also 100 % —, und das las sich wie eine Aussage über den Zug.

Frontend: vierter Modus der Repertoire-Detailseite (`repertoire-holes.component.ts`, Lupe;
`?mode=holes`), Brett zeigt die Stellung NACH dem fehlenden Zug. Trainer: Trend-Knopf in der Leiste
(`freqOrder`, localStorage `rookhub_rep_train_freq_order`), die Explorer-Auswahl teilt er sich mit dem
Lochfinder (`rookhub_explorer_settings`). Offline ist der Knopf ausgeblendet.

**Zweite Quelle: der LOKALE Explorer** (0.503.0, `LocalExplorerClient`): `lila-openingexplorer` als
Container im Stack (`rookhub-explorer:9002`, von der Parallel-Sitzung aufgebaut), dieselben
Endpunkte und Antworten wie explorer.lichess.ovh (`BuildUrl`/`Parse` sind geteilt). Eingeschaltet
über `LichessExplorer:LocalUrl` (Compose `LICHESS_EXPLORER_LOCAL_URL`); leer = die Quelle gibt es
nicht, und eine Anfrage mit `source: "local"` ist ein 400. Lokal gilt: kein Token, keine Leitung,
KEIN Datenbank-Speicher (die Daten wachsen dort monatlich), nur eine Stunde im Arbeitsspeicher (Ausnahme: die Zugfolgen-Suche
`ExplorerPathFinder` hält ihre Antworten seit 0.724.1 24 h in einem eigenen gedeckelten Speicher); die
Stellungen einer Tiefenschicht gehen gleichzeitig raus (`RepertoireReach.EvaluateAsync` mit
`prefetchLayer`, `LocalParallelism` = 8). **Ein Ausreißer ist kein Ausfall** (0.503.1): während eines
Imports kompaktiert der Explorer auf der HDD (gemessen Median 88 ms, p99 5,9 s, Spitze 11 s — nachts
22–06 Uhr, dann importiert die Stack-Sitzung). Deshalb: 30 s Timeout je Abfrage, eine gescheiterte
oder zu langsame Stellung bleibt OFFEN (je Aufruf nur EIN Versuch, die nächste Runde fragt erneut),
`fetchFailed` erst, wenn in einem Aufruf GAR KEINE Antwort kam, und eine Schicht wird am Budget
abgeschnitten (mindestens `LocalLayerFloor` = 5 s), damit die Runde vor dem 60-s-Schnitt des
Reverse-Proxys antwortet. Gemessen am Test-Repertoire: 26 Stellungen Meister kalt in
8 s, Lichess 2 s — online waren es 44 s. **Datenstand lokal**: Lichess-Partien erst ab Elo-Schnitt
1600 und ohne (Ultra-)Bullet (darunter kommen korrekt 0 Partien — die Oberfläche blendet diese
Stufen aus, `fitToLocal`), Meister = Lumbra-GigaBase (Brettpartien ab 2200). Die Wahl der Quelle
teilen sich Lochfinder und Trainer (`rookhub_explorer_settings.source`); ist sie „lokal", der Server
hat aber keinen, fällt sie still auf online zurück (`effectiveSettings`).
