# Zug-Klasse als Symbol an der Figur (0.557.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


**Aussehen seit 0.559.0 = Variante „D“** aus dem Vergleich vom 2026-09-27 (Artefakt, vom Nutzer gewählt): Kreis R 24 um
(90, 10), weißer Ring 3, weicher Schatten, Glyphen mit `R / 18` skaliert; poppt nach der Ankunft der Figur auf
(`BADGE_POP_CLASS` → Keyframes `rh-badge-pop` GLOBAL in `styles.scss`, weil Chessground das SVG per innerHTML
einsetzt; `prefers-reduced-motion` schaltet es ab). Verworfen: Feld in der Klassenfarbe tönen (chess.com-Art),
Symbol nur bei auffälligen Zügen.

Wie chess.com beim Durchsehen (gewünscht 2026-09-27): die Klasse des AKTUELLEN Zugs steht als runder Kreis in der
oberen rechten Ecke seines Zielfelds (bei der Rochade das Feld des Königs). `features/games/move-badge.util.ts`
(`moveBadgeSvg`) baut das SVG-Schnipsel — Farbe aus `MOVE_CLASS_COLORS`, Zeichen aus `MOVE_CLASS_SYMBOLS` (dieselbe
Tabelle wie die Abzeichen im Rückblick); Satzzeichen als Text, Stern/Daumen/Haken/Kreuz/Buch GEZEICHNET (als Text
wären es Emoji, je System anders). Der Rückblick meldet es über `badgeChange` (leer, wenn `engineHidden` — Training
und Live-Engine zeigen eine andere Stellung), die Seite legt es über `[badge]` auf ihr Brett;
`ChessBoardComponent.applyArrows` zeichnet es als Chessground-`customSvg`-autoShape neben den Pfeilen (Raster
0..100 über dem Feld; die `cg-custom-svgs`-Ebene ist um ein halbes Feld versetzt, der Ursprung ist also die obere
linke Ecke des Felds). Eingebaut auf der Partieseite und im Nachspiel-Dialog (`PgnViewerComponent`).
**Erst die Figur, dann das Symbol** (0.558.2): neue Stellung und neues Symbol kommen in ZWEI Prüfläufen an; das Brett nimmt
das alte Symbol beim Stellungswechsel sofort weg und hält jedes Symbol, das in die Fahrzeit fällt
(`AnimationMs` 200 + `BadgeSettleMs` 30), bis zu ihrem Ende zurück (`fenChangedAt`, `badgeTimer`).

**Ein/aus-Schalter** (0.820.0, Wunsch 2026-10-21): neben „Computer-Linien" und „Pfeil für den besten Zug" blendet ein dritter
Schalter (`button.badge-toggle`, Symbol `new_releases`) das Zug-Symbol an der Figur aus. Je Gerät gemerkt
(`rookhub_game_badges`, Vorgabe an — nur ein ausdrückliches Aus bleibt); aus = `badge()` liefert `null`.

**Auch in der Zugliste** (0.836.0, Wunsch 2026-10-25: „die !!, !, !?, ?!, ? und ?? direkt bei den angezeigten Zügen, damit man
direkt zu den Fehlern kommt — mit Schalter"): `GameReviewComponent.moveMarks` gibt je Halbzug ein Zeichen aus (`moveMarksChange`),
die Partieseite reicht es an `app-move-list [marks]` weiter (Schild hinter dem Zug, Klick springt wie bisher). Nur die
Klassen mit Notationszeichen (Brillant, Great, Ungenauigkeit, Fehler, verpasster Gewinn, grober Fehler) — „!?" kennt die
Auswertung nicht. Derselbe Schalter wie das Symbol an der Figur (Beschriftung jetzt „Zug-Symbole (Brett und Zugliste)");
im Fehler-Training (`engineHidden`) leer, beim Erkunden eigener Züge bleibt die Hauptlinie markiert.
