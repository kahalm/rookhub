# Live-Engine + eigene Züge auf der Partieseite (0.525.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


Knopf mit dem Chip-Symbol in der Navigationsleiste von `/g/:token` und `/games/:id`: eine Engine rechnet die
Stellung auf dem Brett (3 Linien, blauer Pfeil für ihren besten Zug), und das Brett wird SPIELBAR — eigene Züge
bilden eine Nebenvariante ab dem aktuellen Partiezug. Zustand in `features/games/live-engine-session.ts` (Signale,
Engine per Fabrik — in Tests ein Fake), Leiste in `live-engine-panel.component.ts` (Engine-Name + Tiefe, Variante mit
„Zug zurück"/„Zurück zur Partie", Linien).
- **Eigene Engine-Instanz je Seite** (`new AnalysisEngineService()` — geht, weil der Service weder Konstruktor noch
  `inject()` hat); `stopLive()`/Seitenende beenden sie. Die Seite erzeugt sie über `createLiveSession()` — Tests
  überschreiben die Methode, statt einen echten Stockfish zu starten.
- **Welche Engine**: angemeldet und am Analysebrett eine externe gewählt (`ANALYSIS_PROVIDER_KEY`) → dieselbe (ohne
  Hintergrund-Engines), sonst Stockfish im Browser; Tiefe = `ANALYSIS_DEPTH_KEY` (Vorgabe 22). Die Schlüssel stehen in
  `features/analysis/analysis-settings.ts`, damit die Partieseite nicht das ganze Analysebrett ins Bündel zieht.
- **Abgleich per `ngDoCheck`** (`session.sync`): der PgnViewerService hat keine Signale; geblättert (Pfeile, Zugliste,
  Kurve) → Nebenvariante weg, neue Stellung → rechnen. In der Nebenvariante nimmt ← den letzten eigenen Zug zurück.
- **Pfeiltasten in der eigenen Variante** (0.526.2): ← zurück, → wieder vor (`LiveEngineSession` hält alle Züge +
  `cursor`, `canRedo`); am Variantenende tut → nichts, an ihrem Anfang blättert ← in der Partie. Gemeldet als „die
  Navigation mit den Tasten verloren" — → war in der Variante tot.
- **Analyse im Fehler-Training** (0.526.2): Knopf „Analysieren" nach dem Urteil (daneben/gezeigt/gefunden) →
  `trainingAnalysis` = eigene `LiveEngineSession` ab `fenBefore`, der eigene (bzw. gezeigte) Zug steht schon drauf.
  **Nach einem FEHLVERSUCH (Lösung noch offen) beginnt sie erst NACH dem eigenen Zug** (0.527.2): ← darf nie zurück in
  die Aufgabenstellung, dort zeigte die Engine Pfeil und Linien des gesuchten Zugs (gemeldet 2026-09-24);
  endet per `ngDoCheck`, sobald Aufgabe, Seite oder Phase wechselt. ← → laufen durch die Variante, die Leertaste
  springt nach der Lösung (right/shown) zur nächsten Aufgabe (Fokus vorher weg vom Knopf, sonst zweiter Klick).
- **Navigation am Handy** (0.525.1, ≤ 768 px): Drehen · Anfang ‖ ◀ ▶ ‖ Ende · Live — Zurück/Vor als breite 48-px-Knöpfe
  in der Mitte (`nav-prev`/`nav-next`), Anfang/Ende mit 14 px Abstand und blasser am Rand. Dicht nebeneinander traf man
  statt „einen Zug" oft „ganz an den Anfang/das Ende" (gemeldet 2026-09-24). Am PC unverändert.
- Tippzonen fallen im Live-Modus weg (sie lägen über dem spielbaren Brett), Computer-Linien/Pfeil der gespeicherten
  Analyse ebenso (`engineHidden`), und das Fehler-Training schaltet die Live-Engine ab.
