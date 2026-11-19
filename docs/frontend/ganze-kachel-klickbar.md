# Ganze Kachel klickbar (`rh-tile`)

Regel: `docs/DESIGN.md` 5.2 „Ganze Kachel klickbar“ — eine Kachel, Karte oder Zeile, die für EIN Ziel steht, ist als
Ganzes klickbar. Baustein: `src/frontend/app/src/_tile.scss` (über `styles.scss` in allen fünf Oberflächen geladen),
Testhilfe: `src/app/testing/tile-hit.ts`.

## Verwenden

```html
<!-- die Kachel -->
<mat-card class="rep-card rh-tile">
  <mat-card-title>{{ rep.name }}</mat-card-title>
  <p>{{ rep.description }}</p>
  <mat-card-actions>
    <!-- das Hauptziel: der vorhandene Link/Knopf -->
    <a mat-button class="rh-tile-target" [routerLink]="['/repertoires', rep.id]">Öffnen</a>
    <!-- eigene Ziele: nichts zu tun -->
    <button mat-icon-button (click)="toggleOffline(rep)">…</button>
    <button mat-icon-button [matMenuTriggerFor]="menu">…</button>
  </mat-card-actions>
</mat-card>
```

- **`rh-tile`** an den Kasten, dessen Fläche klickbar sein soll (Karte, `<li>`, Zeile in einer Sammel-Karte).
- **`rh-tile-target`** an GENAU EIN Element darin: das echte `<a routerLink>` bzw. `<button>`, das die Hauptaktion
  schon heute auslöst (Titel-Link, sonst Haupt-/erster Knopf). Ein Router-Ziel ist ein `<a>` (`<a mat-button …>` sieht
  aus wie der Knopf), damit Mittelklick, Strg-Klick und „in neuem Tab öffnen“ gehen.
- Alles andere Bedienbare in der Kachel (Links, Knöpfe, Felder, `mat-checkbox`, Menü-Auslöser, Elemente mit
  `matTooltip`) liegt von selbst über der Fläche und bleibt eigenes Ziel.
- **`rh-tile-above`** an Inhalt ohne eigene Bedienung, der NICHT zur Fläche gehören soll (aufgeklappte Tabelle,
  Kapitelliste) — und an Textknöpfe, die sich mit `all: unset` zurücksetzen.
- **Bearbeiten-Modus:** `[class.rh-tile]="!editing"` — ohne `rh-tile` ist nur noch der Knopf selbst klickbar.
- **Kachel in einer anderen Komponente als das Hauptziel** (Dashboard-`mat-card` ↔ `app-woodpecker-tile`): geht, die
  Klassen sind global. Die Datei mit dem Hauptziel steht dann in `TILE_FROM_PARENT` in `testing/tile-hit.spec.ts`.
- Kein `(click)` an der Kachel, kein `stopPropagation` an den Nebenelementen.

Stellschrauben (CSS-Variablen an der Kachel bzw. am Bereich):

| Variable | Vorgabe | Wofür |
|---|---|---|
| `--rh-tile-inset` | `0` | Fläche über einen dicken Rahmen ziehen (`0 0 0 -4px` bei 4-px-Kante links) — `inset: 0` endet an der Innenkante des Rahmens |
| `--rh-tile-focus-offset` | `2px` | negativ (`-3px`), wenn ein abschneidender Kasten (`overflow`) den Fokusring außen kappen würde |
| `--rh-tile-hover` / `--rh-tile-pressed` | 6 % / 10 % der Textfarbe | Tönung; LeagueHub/ClubHub aus `--ink` |
| `--rh-tile-focus` / `--rh-tile-focus-width` | `--mat-sys-primary` / 2 px | Fokusring; LeagueHub/ClubHub `--focus` / 3 px, E-Ink 3 px schwarz |

## Wie es funktioniert

Das Hauptziel bekommt ein `::after`, das die Kachel abdeckt — der Klick auf die Fläche trifft damit das Hauptziel
selbst (kein Skript). Üblich wäre `position: absolute; inset: 0` mit der Kachel als `position: relative`; ein
Material-Knopf ist aber selbst `position: relative`, das `::after` bliebe in ihm gefangen. Darum:

- `.rh-tile { contain: layout }` — Layout-Containment macht die Kachel zum Bezugskasten auch für FEST positionierte
  Nachfahren (und zu einem eigenen Stapel).
- `.rh-tile .rh-tile-target::after { position: fixed; inset: 0; z-index: 1 }` — spannt sich über die Kachel, gleich
  wie viele positionierte oder abschneidende Kästen (`overflow: hidden` am gekürzten Titel) dazwischen liegen.
- Nebenelemente: `z-index: 2` (+ `position: relative`, wo sie nicht selbst positioniert sind); bloßer Inhalt bleibt
  auf Ebene 0 darunter.
- Zustände an der Kachel über `:has()`: `.rh-tile:has(.rh-tile-target:hover)` (Tönung), `:active`, `:focus-visible`
  (Ring). Chromium ≥ 105 (Boox: 111) — beides steht hinter `@supports`, ohne Unterstützung bleibt es beim Knopf.

## Fallen

- **`opacity`, `transform`, `filter` an einem Kasten ZWISCHEN Kachel und Nebenelement** bilden einen eigenen Stapel —
  das Nebenelement liegt dann samt seinem `z-index` unter der Fläche (E-Book-Karte: Autoren-Knöpfe in `.eb-meta` mit
  `opacity: .75`). Abtönen über die Farbe (`color-mix`, DESIGN 3.1) statt über `opacity`.
- **`transform`/`contain` an einem Kasten zwischen Kachel und Hauptziel** macht DIESEN zum Bezugskasten — die Fläche
  schrumpft auf ihn.
- **Gesperrte Nebenknöpfe** als `<button>` (schlucken den Klick); ein gesperrter `<a mat-button>` ließe ihn auf die
  Fläche durchfallen.
- **Die Fläche ändert nichts:** eine Hauptaktion, die Daten ohne Rückfrage ändert, bekommt `rh-tile-target` nicht
  (Woodpecker „Zyklus N starten“) — dann der Titel-Link, sonst keine Kachel.
- **Tooltip am Hauptziel:** der Zeiger über der Kachel gilt als „über dem Hauptziel“, ein `matTooltip` dort blitzte
  bei jeder Mausbewegung über die Karte auf. `[appIconLabel]` verzögert ihn an `rh-tile-target` von selbst
  (`TILE_TOOLTIP_DELAY_MS`, 600 ms; die Klasse muss dafür fest im Markup stehen, nicht als `[class.…]`); bei
  `[matTooltip]` an einem Text-Link: `[matTooltipShowDelay]="tooltipDelay" matTooltipPosition="above"`.
- `title` an bloßem Text unter der Fläche erscheint nicht mehr; Text lässt sich dort nicht markieren.
- Margen von Kindern laufen nicht mehr durch die Kachel hindurch zusammen (eigener Formatierungskontext) — bei einem
  Block-Kasten ohne Polster vorher/nachher vergleichen; Flex- und Rasterkästen sind nicht betroffen.

## Testen

```ts
import { clickCenter, clickFreeSpot, expectTileNotClickable, expectWholeTileClickable, recordNavigation } from '../../testing/tile-hit';

const went = recordNavigation();                       // Router.navigateByUrl als Spion
expectWholeTileClickable(card, card.querySelector('a.rep-open')!);
clickFreeSpot(card, card.querySelector('a.rep-open')!);   // Klick auf eine leere Stelle
clickCenter(card.querySelector('.offline-toggle')!);      // Klick mitten auf ein Nebenelement
expect(went()).toEqual(['/repertoires/1']);
```

`expectWholeTileClickable` prüft per `document.elementFromPoint` (Ecken, Mitte, 7 × 7-Raster, Mitte jeder Textstelle):
jeder Punkt trifft das Hauptziel oder ein Nebenelement, und die Mitte jedes Nebenelements trifft dieses selbst. Die
globalen Styles laufen im Karma-Lauf mit; die Kachel muss im Dokument hängen. Der Test ist gegen eine Kachel, bei der
nur der Knopf klickbar ist, rot (gegengeprüft mit geleertem `_tile.scss`). `expectTileNotClickable` für den
Bearbeiten-Modus und das gesperrte Hauptziel. Aus den anderen Oberflächen: `@rh/testing/tile-hit`.

## Wo es benutzt wird

- RookHub: Dashboard (Kacheln, Kurs-Zeilen der angepinnten Kurse, Woodpecker-Kachel), Repertoire-Karten, Kurskarten,
  E-Book-Regal (Bücher und Sammlungen), Trainerstunden (Karten und Trefferzeilen), Wochenpost, Punktepartie-Durchläufe,
  Partie-Analysen, Rekonstruktionen, Favoriten, Aufgabenblätter, frühere Vorbereitungen (`/prep`).
- Turnierseite: Turnierkarte (in der Kalender-Liste ist die umgebende `mat-card` die Kachel, im Karten-Popup und im
  Kalenderfenster die Karte selbst), Zeilen der Merkliste.
- ClubHub: Karteizeilen (`.kid`, Fokusring nach innen wegen `overflow` der Liste) und Gruppenkarten.
- KidHub: nichts umzustellen (die Kacheln sind selbst `<a>`); LeagueHub: keine Kachel mit einem einzelnen Hauptziel.
