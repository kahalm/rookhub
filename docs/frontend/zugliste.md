# Zugliste: der aktive Zug scrollt NUR seinen Kasten, nie die Seite (0.514.1)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


`MoveListComponent.scrollToActive` benutzte `scrollIntoView({ block: 'nearest' })` — und das scrollt JEDEN
scrollbaren Vorfahren mit, das Dokument eingeschlossen. Am Handy steht die Zugliste unter dem Brett
(`.moves-section` mit `max-height: 40vh`), der aktive Zug liegt beim Blättern oft unter dem Bildschirmrand, und
jeder Zug schob die ganze Seite samt Brett nach oben (gemeldet 2026-09-23: „das Brett wandert immer weiter nach
oben"). `scrollIntoContainer(el)` (in `move-list.component.ts`) sucht den NÄCHSTEN Vorfahren, der wirklich
scrollt (`overflow-y: auto|scroll` UND Überhang; `html`/`body` zählen nie) und setzt nur dessen `scrollTop` —
`.move-list` selbst, wo es eine feste Höhe hat, sonst `.moves-section`. Gibt es keinen solchen Kasten, passiert
NICHTS: das Brett darf sich beim Navigieren nie bewegen, das ist die Regel. Spec: langer Seiteninhalt, ein
60-px-Kasten, `scrollIntoView` darf nicht aufgerufen werden und `window.scrollY` bleibt 0.

**Partienliste (`features/games/games-list.component.ts`, 0.515.0)**: je Partie kommt `analysis` (Stand der verknüpften
Analyse) mit der Liste. `analysisState` → `none` (Knopf; auch `failed` — der Knopf lädt zum Neuversuch), `running`
(statt des Knopfs `.progress` mit „NN %", derselbe Platz wie ein Icon-Knopf, damit die Zeile nicht springt), `done` (kein
Knopf; `.accuracy` „♔ 87 % · ♚ 72 %" in der Meta-Zeile, `—` für eine Seite ohne bewertbaren Zug). Nachgefragt wird
alle `ANALYSIS_POLL_MS` (10 s) NUR, solange irgendeine Partie läuft (`schedulePoll` nach jedem Laden und nach dem
Einwurf) — sonst ruht die Liste. Die Genauigkeit rechnet der SERVER (`GameAccuracy`), die Liste rechnet nichts.
`GameAnalysisService.list(true)` (Seite „Partie-Analysen") nimmt die Partie-Analysen mit; die Punktepartie-Seite ruft
`list()` ohne Parameter.

**Seit 0.526.0 ist die Liste eine TABELLE im Schnitt der chess.com-Übersicht** (gewünscht 24.09.2026, Spalten aus dem
DOM-Dump der Profilseite): Quelle + Bedenkzeit · Spieler mit Wertung · Ergebnis · Genauigkeit · Züge · Datum · Aktionen.
Vier Dinge, die dabei tragen: (1) Kopfzeile und Zeilen benutzen DASSELBE Raster (`.row`), sonst stehen die Spalten nicht;
(2) die Genauigkeits-SPALTE trägt auch den Analysieren-Knopf bzw. den Fortschritt — genau wie chess.com dort den
Rückblick-Knopf zeigt, und die Zeile springt beim Wechsel nicht; (3) der Fehler-Fortschritt steht als Fußzeile der Zeile
(`.foot`, `grid-column: 2 / -1`), wo chess.com die Eröffnung hinschreibt; (4) unter 760 px wird aus dem Raster ein
umbrechendes Flex-Layout und die Kopfzeile verschwindet — sie beschriftete Spalten, die es dort nicht mehr gibt.
Von den sechs früheren Icon-Knöpfen bleibt ▶ sichtbar, der Rest steht im ⋮-Menü (UI-Dichte-Regel).
Die Bedenkzeit kommt als PGN-Wert vom Server (`180+2`); `features/games/time-control.util.ts` macht daraus einen
i18n-SCHLÜSSEL samt Zahlen (`games.tc.plus|plusSec|min|sec|days`) statt eines fertigen Satzes — und liefert `null`,
wo nichts Verlässliches herauszulesen ist (dann steht dort nichts, statt etwas geraten zu werden).

**Tippzonen am Brettrand** (`.board-tap` in shared-game, pgn-viewer, shared-line, analysis): EINE globale Regel in
`styles.scss` — `user-select: none` + `touch-action: manipulation` (0.514.3). Ohne sie markierte ein schnelles
Doppeltippen Text, und Safari zoomte. Bewusst nur auf den Zonen, nicht seitenweit: Namen und Züge bleiben kopierbar.
Seit 0.525.2 zusätzlich `-webkit-tap-highlight-color: transparent` für `.board-tap`, `.cg-wrap`, `cg-container` und
`cg-board` (ebenfalls global in `styles.scss`): mobile Browser färbten angetippte klickbare Flächen kurz blau ein —
gemeldet als „beim Tippen aufs Brett blinkt etwas blau auf".

**Partieseite: Tipp oder Zug** (0.654.0, Wunsch 2026-10-04: „erkennst du den Unterschied zwischen Tippen und Ziehen?"):
in `shared-game` gibt es KEINE `.board-tap`-Zonen mehr — sie lagen über dem Brett und schluckten jede Berührung, eine
Figur ließ sich dort nie ziehen. Das Brett ist jetzt immer `playable`; `onBoardTap` wertet `pointerdown`/`pointerup`
(nur Touch, nur primärer Finger) als TIPP, wenn sich der Finger höchstens 10 px bewegt und höchstens 500 ms liegt
(`TAP_SLOP_PX`/`TAP_MAX_MS`) — linke 40 % zurück, rechte 40 % vor, Mitte nichts. Alles andere ist Ziehen und gehört
Chessground; ein gezogener Zug (`onBoardMove`) schaltet die Live-Engine ein und spielt ihn als eigene Variante. Auf groben
Zeigern (`(pointer: coarse)`) ist Antippen-und-Zielfeld aus (`ChessBoardComponent.clickToMove = false`), sonst wäre ein
Tipp auf eine Figur „auswählen" statt „blättern". Pointer-Ereignisse kommen trotz Chessgrounds `preventDefault` auf
`touchstart` an (deshalb vorher die Overlays: ein `click` entsteht dort nicht). pgn-viewer, shared-line und das
Analysebrett behalten ihre Zonen (dort liegen sie neben dem Brett bzw. das Brett ist nicht spielbar).

**Partieseite: Knöpfe unter dem Brett in der eigenen Variante** (0.667.0, Wunsch 2026-10-05): steht man in einer Variante der
Live-Engine, laufen ⏮ ◀ ▶ ⏭ (und ← →, Tipp aufs Brett) durch SIE (`navPrev`/`navNext`/`navStart`/`navEnd`, eine Stelle für
Knöpfe, Tasten und Tipp): ⏮ an ihren Anfang (`LiveEngineSession.toStart`, die Züge bleiben), ⏭ an ihr Ende, am Variantenende
ist ▶ gesperrt statt in die Partie zu springen. Die Live-Leiste zeigt dort nur „Zurück zur Partie" (`[steps]="false"`); im
Fehler-Training behält sie „Zug zurück/vor" (dort gibt es keine Knöpfe unter dem Brett). Anordnung jetzt auch am PC wie am
Handy: Drehen · ⏮ ‖ ◀ ▶ ‖ ⏭ · Live · ⋮, ◀ ▶ groß, ⏮/⏭ mit 20 px Abstand — dicht daneben verklickte man sich.

**Rückblick-Tabelle: Klasse anklicken = hinspringen** (0.688.0, Wunsch 2026-10-06): das Symbol im Tabellenkopf (!!, ?, ✓ …)
springt zum nächsten Zug dieser Klasse NACH dem aktuellen (beide Farben), die Zahl darunter nur zu Zügen dieser Farbe; danach
keiner mehr → von vorn (wiederholtes Klicken läuft alle durch). Regel `nextOfClass` in `game-review.util.ts`, dieselbe Zuordnung
wie die Zählung (`m.cls`). Ohne Treffer bleibt Symbol/Zahl ein Text, kein Knopf. Geht über `moveClicked` — wirkt also überall,
wo der Rückblick hängt (Partieseite, geteilte Partie, LeagueHub-Nachspielen).

**Große Repertoires ganz lesen** (0.712.0, gemeldet 2026-10-07 an /repertoires/7): `parsePgnText` deckelt seit 0.40.15 bei
2 MB / 500 Partien (gegen ein einfrierendes Tab). Ein Chessable-Repertoire hat bis ~10 MB — gemessen 6 MB, 1220 Linien, davon
sah der Browser 274: Linienliste, Zugbaum und Trainer endeten dort, der Server (Stellungssuche, `gameIndex`) kannte alle, und
„Ansehen" aus „Stellung in meinen Repertoires" trug `line=` leer. Jetzt: `exceedsSyncLimits` → `parsePgnTextWithSourceAsync`
(ganzes PGN, gibt den Browser alle 30 ms frei; am Stück wären es ~7 s), sonst wie bisher synchron (Aufrufer/Specs bleiben
synchron — eine async-Funktion läuft bis zum ersten `await` synchron). Repertoire-Seite (mit Fortschrittszeile, Zugbaum aus
denselben Partien statt zweitem Parse) und Trainer; der Link sucht nur den EINEN Abschnitt (`splitPgnGames` + `findSection`, `features/repertoire/repertoire-line-link.ts`).

**Partieseite am PC: drei Spalten** (0.714.0, gemeldet 2026-10-07 mit Screenshot: „viel vergeudeter Platz, sehr kleines
Brett"): ab 1280 px stehen Brett | Zugliste (300 px) | Auswertung (`.side-section`: Rückblick, Repertoire, ähnliche Partien,
`clamp(340px, 24vw, 460px)`, scrollt in sich auf Brett-Höhe) nebeneinander; vorher lag die Auswertung unter dem Brett und die
Seite war auf `--page-max-width` (1240 px) begrenzt — jetzt `min(1760px, 96vw)`. Brett:
`clamp(360px, min(100vh − 260px, 100vw − 860px), 760px)` (darunter `100vw − 440px`). 769–1279 px: Auswertung unter Brett
und Zugliste. Handy unverändert (Auswertung per `order` vor der Zugliste).
**Seit 0.735.0 ein festes Raster** (gemeldet 2026-10-10 mit zwei Screenshots: Karte sprang beim Erscheinen der Analyse in
der Breite, rechts wurde der Engine-Umschalter, die Genauigkeitstabelle und „In welchen Repertoires?" abgeschnitten):
ab 1280 px `grid-template-columns: var(--board-size) var(--moves-width) var(--side-width)` mit `--moves-width` 280 px und
`--side-width` `clamp(420px, 30vw, 520px)`, Brett `min(100vh − 250 − Kurz-erzählt, 100vw − Zugliste − Auswertung − 140)`;
die Spalten hängen nicht mehr am Inhalt (`fit-content` maß vorher die langen Linien mit). Die Seitenspalte scrollt nur
senkrecht (`overflow-x: hidden`). „Kurz erzählt" über dem Brett zieht `--recap-room` (80 px) von der Brett-Höhe ab
(`.viewer:has(> .recap)`). Rückblick, Repertoire und ähnliche Partien sind gleich gerahmte Karten (Rahmen von der Seite,
leerer Rückblick per `:empty` weg); `app-position-repertoires` hat dafür `appearance="row"` (Zeile mit Pfeil statt
umrandetem Knopf, Vorgabe `button` unverändert für Analysebrett & Co.). Die Zugliste (`move-list`, überall) hat eine
abgesetzte Nummern-Spalte (40 px), Textschrift mit Tabellenziffern statt der nie geladenen Monospace, aktiver Zug in
`--rh-accent`. Am Handy sitzt das ⋮ absolut oben rechts in der Kopfzeile.

**Fehler-Training teilen** (0.741.0, Wunsch 2026-10-10: „Link zum Teilen, wo man für die ausgewählte Farbe die Fehler
nachspielen kann — ein-/ausgeloggt egal"): der Teilen-Link der Partie mit `?train=white|black` (`/g/{token}?train=…`).
`SharedGameComponent` liest den Parameter (alle Modi), die gewählte Seite schlägt die des Besitzers (`trainSide` →
`mistakeSide`), und ein `effect` in `GameTrainingController` (`features/games/game-training-controller.ts`, seit Z26) startet `trainMistakes()` EINMAL von selbst, sobald die Analyse Aufgaben dieser Seite hergibt.
Anonym sieht der Betrachter die vom Besitzer verknüpfte Analyse (wie die Kurve des Teilen-Links); Fortschritt wird nur bei der
eigenen Partie gemeldet. Öffnet der Besitzer seinen eigenen Link, behält die Umleitung auf `/games/{id}` den Parameter.
Menü ⋮ der eigenen Partie: „Fehler-Training teilen — Weiß/Schwarz (n)", nur Seiten mit Aufgaben (`shareTraining`).
