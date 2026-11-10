# Partienliste: Laschen Alle / Online / Am Brett (0.949.0)

Wunsch 2026-11-10: „unter /games gibt es atm saved games – da wäre eine unterscheidung vermutlich fein (in tabs?) –
allgemeine saved games, online games, otb games“. Die Regel, welche Partie wohin gehört, steht am Server
(`docs/api/gespeicherte-partien.md`, „Laschen der Partienliste“); das Frontend rechnet nichts davon nach. Seit 0.956.1 machen
Turnierseiten-Adressen (chess-results.com, fide.com) im Kopf eine Partie nicht mehr zur Online-Partie, und eine
lichess-Studie mit Turnier-Event und klassischer/leerer Bedenkzeit zählt als Brettpartie — die Zahlen der Laschen
(`kind-counts`) ziehen ohne Frontend-Änderung mit.

- **Komponente**: `features/games/games-list.component.ts`. Laschen als `mat-tab-nav-bar` + `mat-tab-link` mit
  `routerLink="."` und `[queryParams]="{ kind }"` (`queryParamsHandling: 'merge'`, „Alle“ = Parameter weg), die Liste
  steht im `mat-tab-nav-panel`. Kein `mat-tab-group`: alle drei Laschen zeigen dieselbe Liste, nur gefiltert.
- **Adresse ist die Quelle**: `ActivatedRoute.queryParamMap` → `parseGameKind()` (unbekannt = `all`) →
  `distinctUntilChanged` → Signal `kind` + `reloadList()`. Der erste Wert ist das erste Laden; Klick, Zurück und ein
  geteilter Link `/games?kind=otb` laufen denselben Weg.
- **Aufrufe**: `GamesService.list(take, q, kind)` hängt `&kind=` an (bei `all` nicht — der Aufruf bleibt wie vorher),
  `kindCounts()` → `GET /api/games/kind-counts` beim Start, nach dem Hochladen und nach dem Löschen. Die Zahlen gelten
  für das ganze Konto (ohne Suche); fehlen sie (Fehler), stehen die Laschen ohne Zahl da.
- **Suche × Lasche**: `q` und `kind` reisen zusammen; jede Antwort, die zu einer inzwischen geänderten Suche ODER Lasche
  gehört, fällt weg (`fetchList`). Beim Laschenwechsel mit aktiver Suche wird zusätzlich die Liste der Lasche OHNE Suche
  geholt (`unsearched`, Grundlage der Filterwerte Klassifizierer/Tags). Diese Filter und „nur offene Fehler“ bleiben
  clientseitig und wirken innerhalb der Lasche.
- **Leere Lasche**: ein Satz (`games.kind.emptyOnline`/`emptyOtb`), die Laschen bleiben stehen; die Leerkarte mit
  „PGN hochladen“ nur, wenn das ganze Konto leer ist (`hasGames()` schaut auch auf `counts().all`).
- **Zeichnen**: die Komponente ist Eager (`ChangeDetectionStrategy.Default`); `kind` und `counts` sind Signale, damit
  die Laschen auch nach einer HTTP-Antwort ohne Markierung neu zeichnen (Angular-22-Falle, `src/frontend/CLAUDE.md`).
- **Specs**: `games-list-kinds.spec.ts` (RouterTestingHarness: `?kind=` → Aufruf, Klick → Adresse + neuer Aufruf,
  „Alle“ entfernt den Parameter, leere Lasche, Suche innerhalb der Lasche), `games.service.spec.ts`.

## Nur meine Partien, Zusammenfassung, Turnier (0.954.0)

- **„Nur meine Partien“**: `mat-checkbox.only-mine` unter dem Suchfeld (nur mit Partien), Zustand in der Adresse
  (`?mine=1`, aus = Parameter weg, `parseMine()`); der Strom der Adresse trägt `{ kind, mine }`. Wechselt `mine`, lädt die
  Liste UND die Laschen-Zahlen neu (`kindCounts(mine)`), ein Laschenwechsel nur die Liste (die Laschen-Links mergen die
  Parameter, `mine` bleibt). `GamesService.list(take, q, kind, mine)` hängt `&mine=true` an. Regel am Server.
- **Zusammenfassung**: in `.actions` vor ▶ ein 40-px-Platz: `done` = `a.summary-done` (`article`, Link `summary.link`,
  `appIconLabel` „Zusammenfassung“), `requested`/`running` = `span.summary-wait` (`hourglass_top`, `role="img"`,
  aria-label + Tooltip „Zusammenfassung angefordert …“ bzw. „wird geschrieben …“, kein Knopf), sonst leerer Platz
  (`.summary-none`), damit die Knöpfe nicht springen. Aktionsspalte 128 px. Kein Nachfragen: der Stand gilt beim Laden.
- **Turnier**: `span.event` als dritte Zeile unter den Namen im Spieler-Link, eine Zeile mit „…“, voller Name im Tooltip.
  In allen Laschen, wo der Server eins liefert (nie bei Online-Partien) — dieselbe Partie sieht in „Alle“ und „Am Brett“
  gleich aus, und in „Alle“ unterscheidet die Zeile so Turnier- von Freizeitpartien.
- **Specs**: `games-list-summary.spec.ts` (drei Zustände, Turnier, Häkchen ↔ Adresse ↔ Aufruf), `games.service.spec.ts`.
