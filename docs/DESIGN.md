# RookHub-Frontends — Gestaltungsregeln

Stand: 2026-10-11 (v0.751.2). Gilt für alle fünf Oberflächen im Angular-Workspace `src/frontend/app`.
Pfade unten sind relativ zu `src/frontend/app/`, sofern nicht anders angegeben.

---

## 1 Zweck & Geltung

Diese Datei legt fest, **wie Seiten gestaltet sind und was wo verwendet wird** — zuerst allgemein (alle
Oberflächen), dann die Eigenheiten der einzelnen Bereiche (Abschnitt 8). Sie beschreibt den **Ist-Stand des
Codes** (abgeleitet aus den Quellen, mit Dateiverweisen). Wo der Bestand uneinheitlich ist, steht die
**„Regel ab jetzt“** dabei — im Zweifel das Muster, das die UI-Sweep-Runden vom 10./11.10.2026 eingeführt haben.

| Projekt | Quelle | Wurzel-Selektor | Grundstil |
|---|---|---|---|
| `app` (RookHub) | `src/` | `app-root` | Angular Material 22 (M3), hell/dunkel |
| `turnier` (Turnierseite) | `src-turnier/` | `trn-root` | wie RookHub (Material, gleiches Thema) |
| `kidhub` (Kinderseite) | `src-kidhub/` | `kid-root` | eigene Kinder-Optik, nur hell, kaum Material |
| `leaguehub` (Ligen) | `src-leaguehub/` + `leaguehub.scss` | `lh-root` | eigene Gestaltung (Barlow, Tiroler Rot), hell/dunkel |
| `clubhub` (Kartei) | `src-clubhub/` + `clubhub.scss` | `ch-root` | eigene Gestaltung (Karteikasten), hell/dunkel |

Alle Projekte laden zuerst `src/styles.scss` (Material-Thema, Brett-Stile, globale Helfer); LeagueHub und
ClubHub legen ihre eigene globale Datei darüber (`angular.json` → `styles`).

**Rangfolge:** Bereichsregeln (Abschnitt 8) gehen den allgemeinen Regeln (2–7) vor, wenn sie ausdrücklich
abweichen. Ergänzende Quellen: `rookhub/CLAUDE.md` → „Wichtige Konventionen“ (UI-Dichte-Regel),
`src/frontend/CLAUDE.md` (Architektur, Lokalisierung, Vollbild, Fehlerbehandlung, kein Seiten-Scroll),
`tools/ui-sweep/README.md` (Prüfwerkzeug).

**Neue Entscheidungen hier eintragen.** Wer eine Gestaltungsentscheidung trifft (neues Muster, neue Ausnahme,
geänderte Regel), trägt sie (1) im passenden Abschnitt ein und (2) als Zeile im
[Änderungsprotokoll](#änderungsprotokoll) am Ende — Datum, Version, Entscheidung, Grund, Quelle (Fund-ID).
Im Code steht der Grund wie bisher als Kommentar: `UI-Sweep 2026-10-11 (r2-k-counter): …`.

---

## 2 Grundsätze

1. **Eine primäre Aktion je Ansicht**, höchstens drei sekundäre sichtbar, alles Weitere ins ⋮-Menü
   (UI-Dichte-Regel seit v0.334.0, `rookhub/CLAUDE.md`).
2. **Höchstens ein Satz Fließtext je Karte** — mehr gehört hinter das ?-Symbol (`app-help-hint`), nie als
   gestapelte `<p class="muted">`.
3. **Farben aus Tokens**, nicht als Hex-Wert (Abschnitt 3.1). Jede Farbe muss hell **und** dunkel tragen.
4. **Dunkel ist die Vorgabe** (RookHub, Turnierseite, LeagueHub, ClubHub folgen dem geteilten Design-Modus);
   KidHub ist bewusst nur hell.
5. **Handy zuerst prüfen** (390 px): die Seite scrollt nie waagerecht, breite Tabellen werden unter 600 px zu
   Karten, Tippziele sind mindestens 44 px groß.
6. **Laden, leer und Fehler sind drei verschiedene Zustände** — ein gescheitertes Laden sieht nie aus wie
   „es gibt nichts“.
7. **Nichts Internes zeigen:** keine Rechte-Schlüssel, Dateinamen, i18n-Schlüssel, PGN-Platzhalter („?“),
   rohe FEN-Zeilen oder Fachjargon (Engine-Karte, r2-rk-engine-jargon).
8. **Nichts ist ohne sichtbaren Grund gesperrt:** ein deaktivierter Knopf nennt den Grund darunter, Reiter
   werden nicht `[disabled]`, sondern erklären sich im Inhalt.
9. **Gleiche Sache, gleiche Form:** ein geteilter Baustein statt Varianten (Spielweise-Dialog, Zähler-Pille,
   Sperrkarte, Turnierkarte, Rückfrage-Dialog).
10. **Immer ein Weg zurück** (Rechtsseiten, Sperrkarten, Anmeldemasken der Kinderseite).
11. **Was nach HTTP angezeigt wird, liegt in Signalen** (Abschnitt 5.14).

---

## 3 Tokens

### 3.1 Farben

**RookHub und Turnierseite — Material-Thema** (`src/_tokens.scss`, eingebunden aus `src/styles.scss`):

```scss
html { @include mat.theme((color: (theme-type: light, primary: mat.$azure-palette, tertiary: mat.$blue-palette),
                          typography: Roboto, density: 0)); }
html.dark-theme { color-scheme: dark; @include mat.theme((color: (theme-type: dark, …))); }
```

- **Modus:** `ThemeService` (`src/app/core/theme.service.ts`) setzt `html.dark-theme`. Vorgabe `dark`; Wahl
  `system → light → dark` (`toggle()`), gespeichert in `localStorage rookhub_app_theme` und im geteilten Cookie
  `rookhub_theme` (gilt dadurch über RookHub, Turnierseite, LeagueHub und ClubHub hinweg). KidHub injiziert den
  Dienst nicht und bleibt hell.
- **Flächen:** `body { background-color: var(--mat-sys-surface) }` (`styles.scss`, UX-055). Die Textfarbe bleibt
  die des Browsers, weil die vielen `color-mix(currentColor …)`-Töne davon ausgehen.
- **Material-Rollen** direkt nutzen, wo es sie gibt: `--mat-sys-primary`, `--mat-sys-on-primary`,
  `--mat-sys-error`, `--mat-sys-surface`, `--mat-sys-surface-container-*`, `--mat-sys-on-surface(-variant)`,
  `--mat-sys-outline(-variant)` (214 Verwendungen in `src/`, 68 in `src-turnier/`).

**Semantische Zustandsfarben** (`src/_tokens.scss`, Codereview F8-003) — **Vordergrund**-Tokens (Text, Symbol,
Rahmen), je Modus ≥ 4,5:1 auf Seite, Karte und Dialog, geprüft in `src/app/core/design-tokens.spec.ts`:

| Token | hell | dunkel | Bedeutung |
|---|---|---|---|
| `--rh-error` | `var(--mat-sys-error)` (#ba1a1a) | `var(--mat-sys-error)` (#ffb4ab) | Fehler, Löschen |
| `--rh-accent` | `var(--mat-sys-primary)` (#005cbb) | `var(--mat-sys-primary)` (#abc7ff) | Akzent, „aktiv“ |
| `--rh-success` | #1b5e20 | #81c784 | Erfolg, gelöst |
| `--rh-warn` | #bf360c | #ffb74d | Warnung, unsicher |
| `--rh-info` | #1565c0 | #90caf9 | Information |

- **Getönte Flächen** nie mit dem Token direkt, sondern gemischt:
  `background: color-mix(in srgb, var(--rh-success) 18%, transparent)`.
- **Sekundärtext:** `color: color-mix(in srgb, currentColor 60%, transparent)` (häufigster Wert, 147×).
  Regel ab jetzt: **nicht unter 55 %** für lesbaren Text (60 % ergibt auf Weiß ≈ 5,7:1, 47 % nur ≈ 3,6:1).
  Trennlinien/Rahmen: `currentColor` 8–14 %.
- **Brettfarben** (Brettthemen, Koordinaten in `styles.scss`) sind feste Werte und von der Token-Regel
  ausgenommen.
- **Ist-Stand / Regel:** In `src/` stehen noch ~830 Hex-Werte (häufigste: `#1976d2` 72×, `#2e7d32` 55×,
  `#3f51b5` 41×, `#c62828` 40×) gegen 43 Token-Verwendungen. **Regel ab jetzt:** neue Farben nur über Tokens;
  wer eine Komponente ohnehin anfasst, stellt ihre Zustandsfarben auf `var(--rh-…)` um.

Die Farbtabellen von KidHub, LeagueHub und ClubHub stehen in Abschnitt 8.

### 3.2 Abstände

Keine Abstands-Tokens; gemessen in `src/` (`gap`): 8px (135×), 6px (93×), 4px (87×), 0.5rem (82×), 12px (77×).
**Regel ab jetzt:** Raster 4 / 8 / 12 / 16 / 24 px (rem-Entsprechungen erlaubt); Seitenpolster 16 px;
Knopfreihen `gap: 8px` (eng: 0.5rem), Abschnitte 16–24 px.

### 3.3 Radien

| Oberfläche | häufigste Werte | Verwendung |
|---|---|---|
| RookHub | 4px (67×), 6px (52×), 8px (49×), 10px (36×), 999px (23×) | klein 4–6, Kästen/Banner 8, Pillen 999 |
| Turnierseite | 8px (12×), 4px, 999px | Karten 8, Abzeichen 999; Kartenaktionen am Handy 12 |
| KidHub | 999px, 18px, 16px, 24px, 20px | Pillen, große Knöpfe 18, Karten 16–24 |
| LeagueHub | 6px (21×), 4px, 10px | Knöpfe/Felder 6, Karten/Panels 10 |
| ClubHub | 10px, 8px, 50 % | Karten 10, Knöpfe 8, Reiter `8px 8px 0 0` |

Dialoge: Material-Vorgabe; der Spielweise-Dialog 20 px, als Blatt am Handy `20px 20px 0 0`.

### 3.4 Schatten / Höhe

- **RookHub/Turnierseite:** praktisch keine eigenen Schatten — Höhe kommt aus Material (`mat-card`, Dialog,
  Menü). Eigene Rahmen statt Schatten: `1px solid color-mix(in srgb, currentColor 14%, transparent)`.
- **KidHub:** „Knopf-Schatten“ ohne Unschärfe, `box-shadow: 0 Npx 0 var(--kid-shadow)` (N = 1…6), beim Drücken
  `transform: translateY(2–3px)` und kleinerer Schatten.
- **LeagueHub/ClubHub:** flach — 1 px Rahmen in `var(--line)`, keine Schatten (außer Abdunklung hinter Dialogen).

### 3.5 Typografie

| Oberfläche | Schrift | Grundgröße | Überschriften |
|---|---|---|---|
| RookHub, Turnierseite | Roboto (Material-Typo, `public/fonts/fonts.css`, selbst ausgeliefert) | Material body | Material; Seitentitel `h1` |
| KidHub | Roboto, kräftige Schnitte 700–900 | 1.05–1.3rem | 1.35–1.5rem, 800/900 |
| LeagueHub | Barlow (Text), Barlow Semi Condensed (`--cond`, Köpfe/Zahlen) | 17px/1.5 | 22–40px `--cond` 600–700 |
| ClubHub | Atkinson Hyperlegible (Text, eindeutige 0/O, 1/l/I), Zilla Slab (`--slab`) | 17px/1.5 | h1 32 / h2 23 / h3 19px |

- Symbole: **Material Icons** (Ligaturen, `<mat-icon>`), ebenfalls selbst ausgeliefert. KidHub nutzt Emoji
  (🦉, 🌐, ★, ✓).
- Kleintext in RookHub: 0.85rem / 0.8rem / 0.9rem am häufigsten; Abzeichen 0.72rem.
- Zahlen in Tabellen: `font-variant-numeric: tabular-nums` (LeagueHub/ClubHub global auf `body`).
- Codes, FEN, Formular-Rohtext: `ui-monospace, SFMono-Regular, Menlo, monospace`.
- Eingabefelder auf Touch mindestens **16 px** Schrift (sonst zoomt iOS beim Antippen).

---

## 4 Layout & Breakpoints

### 4.1 Seitenbreite

- `:root { --page-max-width: 1240px; }` (`styles.scss`, UI-Welle 3). Inhaltsseiten:
  `max-width: min(var(--page-max-width), 96vw); margin: 0 auto;` (16 Stellen). Ausnahme: Admin-Tabellen
  1400 px (`features/admin/admin.component.scss`).
- Eigene Spalten: KidHub `KID_PAGE_WIDTH = { home: 1160, map: 980, list: 820, legal: 840 }`
  (`src-kidhub/app/shared/kids-layout.ts`); LeagueHub `.wrap` 780 / `.wrap.mid` 1240 / `.wrap.wide` 1440;
  ClubHub `.wrap` 860.
- **Ist:** viele RookHub-Seiten haben noch feste Werte (768 px 18×, 760 px 12×, 600 px 9×). **Regel ab jetzt:**
  neue Inhaltsseiten nur über `--page-max-width`; schmaler nur für Formular-/Leseseiten (Anmeldekarte 400 px,
  Rechtstexte), dann begründet.
- **Breitbild-Stufe ≥ 2400 px** (UI-Sweep u1, 0.762.0): Token `--page-max-width-wide` (`styles.scss`; unter 2400 px
  = `--page-max-width`, ab 2400 px **1880 px**). **Nur** Raster-, Tabellen- und Brettseiten nehmen es — opt-in je Seite
  und ausschließlich innerhalb `@media (min-width: 2400px)`: `max-width: min(var(--page-max-width-wide, 1880px), 96vw)`.
  Bisher: Dashboard (5 Kachelspalten), Kurse (6 Spalten), Linien durchsehen (Brett bis Fensterhöhe), Repertoires, Aufgabenblätter, Punktepartie-Liste, Admin,
  Statistik (zweispaltig), Turnier-Verlauf, Gespeicherte Partien, Punktepartie-Brett, Spielervorbereitung (Statistik-Blöcke
  nebeneinander), geteilte Linie `/l` und Rekonstruktion `/r` (Brett bis Fensterhöhe, ≤ 1 080 px), Puzzle-Löser (Standard, Endlos-Spielbrett, Buch/Kurs/Tagespuzzle/Wochenpost: Brett bis Fensterhöhe, daneben ≥ 360 px Seitenspalte). **Lese- und Formularseiten bleiben** bei `--page-max-width` bzw. ihrer
  schmaleren Breite. Unter 2400 px ändert sich dadurch nichts (Laptop/Handy/1920 pixelgleich). Bretter wachsen dort
  höchstens bis zur Fensterhöhe (`min(…, calc(100vh - Kopf/Leisten))`). Die Kopfleiste (RookHub + Turnier) bleibt
  randlos, ihr Inhalt rückt ab 2400 px auf die Kante des breiten Inhalts (`padding-inline: max(16px, calc((100% - var(--page-max-width-wide, 1880px)) / 2 + 16px))`, fluchtet mit Brett/Raster;
  Kalkulations-Modus ausgenommen). Prüfung: `features/page-width.spec.ts`.

### 4.2 Breakpoints (gemessen: 157 `@media`-Regeln)

| Grenze | Anzahl | Bedeutung |
|---|---|---|
| `max-width: 768px` | 37 | Navigation → Hamburger, PGN-Dialog randlos, Fußzeile, Tablet hochkant |
| `max-width: 600px` / `599.98px` | 15 / 5 | **Handy**: Tabellen → Karten, Dialog → Blatt, Beschriftungen unter Symbolen |
| `min-width: 900px` / `max-width: 899.98px` | 12 / 3 | zweispaltig (Brett + Seitenleiste), Kalkulations-Modus |
| `max-width: 640px` (+ `min-width: 641px`) | 9 | LeagueHub/ClubHub-Handy |
| `max-width: 520px`, `480px`/`479.98px` | 6, 7 | Feinschliff (Navbar abgemeldet, Kandidatenzeilen) |
| Einzelwerte 280–1400 px | je 1–3 | Sonderfälle |

**Regel ab jetzt:** drei Stufen — **< 600 px** (Handy), **≤ 768 px** (Navigation/Tablet), **≥ 900 px**
(mehrspaltig). Obergrenzen als `599.98px` / `899.98px` oder Bereichsschreibweise (`(width < 600px)`, wie
`KID_STACKED`) — nie `599px`, sonst entstehen 1-px-Lücken. 480/380 px nur für Feinschliff einzelner
Komponenten. LeagueHub/ClubHub behalten ihre 640-px-Stufe.
Vierte Stufe nach oben: **≥ 2400 px** (`min-width: 2400px`, Breitbild 21:9/4K) — nur für die Breitbild-Regeln aus 4.1
(Raster-, Tabellen- und Brettseiten, Kopfleiste); Lese- und Formularseiten bekommen dort keine eigenen Regeln.

Weitere Medienabfragen: `(pointer: coarse)` für Touch-Vergrößerungen (8×), `(hover: hover) and (pointer: fine)`,
`(prefers-reduced-motion: reduce)` für jede Animation (7×), `print` blendet Navbar/Fußzeile aus.

### 4.3 Containerabfragen

Komponenten, die in verschiedenen Breiten leben, fragen den Container statt des Fensters:
LeagueHub `.club-scroll` / `.scan-wrap` (`container-type: inline-size`, Karten unter 640 px Containerbreite).
Für neue Listen in Karten/Seitenleisten bevorzugt.

### 4.4 Kein waagerechter Seiten-Scroll

Breite Inhalte bekommen ein **eigenes** `overflow-x: auto` (Tabellen: `min-width` auf die Tabelle, Wrapper
scrollt; Streifen: `flex: 1 0 <Mindestbreite>`). Begründung und Test-Muster (`scrollWidth <= clientWidth` bei
390 px) in `src/frontend/CLAUDE.md` → „Kein horizontaler Seiten-Scroll“. Der UI-Sweep prüft „Seite zu breit“
und „ragt raus“ automatisch.

### 4.5 Sonderlayouts

- **Vollbild-Brett / App-Vollbild:** `shared/fullscreen/` (Details `src/frontend/CLAUDE.md`). Ins Vollbild
  projizierte Elemente tragen `data-fs-only`.
- **Kalkulations-Modus** (`body.calc-mode`, ab 900 px): Navbar schwebt oben rechts, Fußzeile unten rechts
  (`styles.scss`).
- **KidHub-Aufgabenseiten:** Brettgröße als Variable `--kid-board`/`--kid-row` in der App-Hülle, drei Ansichten
  (`KID_STACKED`, `KID_SHORT`, PC).

### 4.6 Erster Blick und Scrollen

**Falz** = was beim Öffnen ohne Scrollen sichtbar ist. Geprüft in **drei Ansichten**: Handy 390 × 844, Laptop
1366 × 768, großer Bildschirm 1920 × 1080.

*Erster Blick*
1. Jede Seite hat eine **Hauptsache** — der Inhalt oder die Aktion, für die man kommt (Brett, Liste/Tabelle,
   Hauptknopf, Formular). Sie steht im Routen-Katalog des UI-Sweeps (`main`).
2. Die Hauptsache **beginnt über der Falz**, in allen drei Ansichten (Listen: mindestens der erste Eintrag;
   Hauptknopf: ganz).
3. **Vorlauf** (App-Kopf, Seitenkopf, Banner, Erklärtext, Filter über der Hauptsache) höchstens **40 %** der Höhe
   (Handy ≈ 340 px, Laptop ≈ 300 px, groß ≈ 430 px).
4. **Großer Bildschirm nutzt den Platz:** keine schmale Spalte mit leerem Rest, wenn der Inhalt Breite brauchen könnte
   (z. B. Brett und Zugliste nebeneinander statt untereinander); Fließtext bleibt bei ≤ ~75 Zeichen je Zeile. Die
   Hauptsache steht nicht verloren in einer Ecke.
5. **Erklärtexte** über der Hauptsache höchstens 3 Zeilen; Längeres hinter „Mehr“/ⓘ oder unter die Hauptsache.
6. **Höchstens ein Banner** über der Hauptsache; weitere Hinweise darunter oder in die Glocke.
7. **Filter/Einstellungen** mit mehr als einer Zeile am Handy zugeklappt („Filter ▾“, aktive Filter als Chips).
8. Gilt auch für **Leer- und Fehlerzustände**: Hinweis samt Weg zum Weiterkommen über der Falz.

*Nach dem Scrollen*

9. **Klebende Leisten** zusammen höchstens 64 px (Handy) bzw. 72 px (Laptop/groß); sie verdecken keine Fokus- und
   Ankerziele (`scroll-padding-top`).
10. Unter **schwebenden/klebenden Elementen unten** (FAB, Speichern-Leiste) bleibt der letzte Inhalt frei.
11. **Seitenlänge** je Ansicht: ab **6 Bildschirmen** braucht die Seite Gliederung (Abschnittsköpfe mit Sprung,
    geteilte Listen mit „Mehr“/Seiten); ab **12 Bildschirmen** ist es ein Befund.
12. **Abschnitte bleiben einordenbar:** eigener Kopf je Abschnitt; lange Tabellen behalten ihren Kopf.
13. **Unten gelten dieselben Regeln wie oben:** keine engeren Abstände, keine abgeschnittenen Texte, letzte Karte mit
    Abstand zum Fuß.

*Prüfung (UI-Sweep):* gemessen ohne Modell in allen drei Ansichten — Lage der Hauptsache (2), Vorlauf (3),
Bannerzahl (6), klebende Leisten (9), Verdeckung unten (10), Seitenlänge (11). Bilder fürs Modell: „Erster Blick“
(ein Bildschirm je Ansicht, Regeln 1–8) und „Gescrollt“ (bildschirmhohe Streifen, 10 % Überlappung, Originalgröße,
nur bei Seiten über 2 Bildschirmen, Regeln 9–13).

**Ausnahmen:** Vollbild-Brett, Dialoge, Druckansicht des Aufgabenblatts.

---

## 5 Komponenten-Muster

### 5.1 Seitenkopf

```html
<div class="header">
  <h1>{{ 'courses.title' | translate }}</h1>
  <div class="header-actions"> <!-- sekundär zuerst, primär zuletzt (rechts) --> </div>
</div>
<p class="intro">Ein Satz.</p>   <!-- mehr → app-help-hint -->
```

`.header { display: flex; justify-content: space-between; align-items: center; gap: 12px }`,
`.header h1 { margin: 0 }`, `.header-actions { display: flex; gap: .5rem; flex-wrap: wrap }`; am Handy bricht
die Aktionszeile unter den Titel (`flex-basis: 100%`) (Vorbild `features/courses/course-list.component.ts`).
Ist: Klassen `.head` (15×) und `.header` (11×) — **Regel ab jetzt:** `.header` / `.header-actions`.

### 5.2 Karten

- RookHub/Turnierseite: `mat-card`. **Karten einer Liste sind gleich hoch, Titel einzeilig mit „…“, die
  Aktionszeile sitzt unten bündig** (r2-rg-rep-cards); die Art als kleines Abzeichen neben dem Titel.
- Aktionszeile: primäre Aktion (`mat-flat-button color="primary"`), ggf. eine sekundäre, rechts das ⋮
  (Vorbild `features/courses/course-card.component.ts`).
- Turnierkarte (`src-turnier/.../tournament-card.component.ts`): **eine** Kurzansicht für Liste, Karten-Popup und
  Kalender; Aktionen an **festen Plätzen** (Abschnitt 8.2).
- Bearbeiten in Listen-Karten: Stift-Symbol, Inhalt nur lesend (Wochenpost, 0.740.0).
- **Kopfzeile einer Listen-Karte mit Symbolknöpfen:** zwei Teile ohne Umbruch — links Titel/Status/Meta
  (`flex: 1 1 auto; min-width: 0`, bricht in sich um), rechts die Knöpfe (`flex: none`) oben in der ersten Zeile; nie
  ein Knopf allein in einer eigenen Zeile (Partie-Analysen, 0.759.2).

### 5.3 Listen und Tabellen

- Tabelle: `<table>` (RookHub 21, LeagueHub 14) oder `mat-table` (RookHub 5, Turnierseite 8).
- **Unter 600 px werden breite Tabellen zu Karten**: beide Fassungen im Markup, CSS schaltet
  (`features/admin/admin.component.scss`: `.users-table-wrap` / `.user-cards`, `@media (max-width: 599.98px)`;
  LeagueHub `.club-table` → `.gc-*` per Containerabfrage < 640 px). Wo eine Tabelle bleiben darf: `min-width` +
  scrollender Wrapper.
- Spalten ohne Inhalt weglassen (Titelspalte nur, wenn jemand einen Titel hat, r2-t-title-empty / t-title-col).
- Zellentext einzeilig mit `text-overflow: ellipsis`, voller Text im Tooltip (l-club-table).
- Zahlen rechtsbündig, `tabular-nums`; Anteil und Anzahl als **getrennte** Spalten (r2-rg-explorer-cols).
- Meta-Zeilen am Handy: „Wert · Wert“ kompakt unter dem Namen (l-names-mobile, r2-t-meta-dot).
- Blätterleisten übersetzt (`core/translated-paginator-intl.ts`).
- **Liste neben dem Brett** (Repertoire-Linien, a1-rk-linienliste-scrollkasten): zweispaltig darf sie in einem Kasten
  scrollen, der mindestens so hoch ist wie Brett + Leiste darunter, mit Weiterscroll-Schatten am unteren Rand (nur
  sichtbar, solange noch etwas kommt); einspaltig läuft sie mit der Seite — kein Kasten im Kasten.
- `matListItemMeta` nie direkt auf einen Text-Knopf (`mat-flat-button`/`mat-stroked-button`), sondern auf ein
  umschließendes `<div>` — sonst übernimmt der Knopf Schrift, Farbe und Ausrichtung des Listen-Endbereichs
  (Beschriftung winzig und fast unsichtbar, d1-rk-revenge-loesen-knopf-kaputt).

### 5.4 Aktionen und Knöpfe

| Rolle | RookHub/Turnierseite | Bestand (src) |
|---|---|---|
| **Primär** (genau eine je Ansicht) | `mat-flat-button color="primary"` | 70× flat, 88× `mat-raised-button color="primary"` |
| Sekundär | `mat-stroked-button` | 188× |
| Tertiär / Text | `mat-button` | — |
| Nur Symbol | `mat-icon-button` + `[appIconLabel]` | 251× |
| Gefährlich | im ⋮-Menü zuletzt, `class="delete-item"` (rotes Symbol) | — |

- `styles.scss` (UX-052) füllt `mat-raised-button color="primary"` wie einen Flat-Knopf, `color="warn"` mit der
  Fehlerfarbe — beide Schreibweisen sehen gleich aus. **Regel ab jetzt:** neu nur `mat-flat-button color="primary"`.
- **Gefahrknopf gefüllt:** `mat-flat-button color="warn"` (Konto löschen, Bestätigen im Rückfrage-Dialog) bekommt global
  `--mat-sys-error` / `--mat-sys-on-error` (`styles.scss`, d1-x-account-delete-warn-ignored) — im M3-Thema wirkt `color`
  sonst nicht. Hubs setzen die beiden Rollen auf ihrer Karte (LeagueHub `--red`/`--on-red`, ClubHub `--bad`/`--bad-ink`,
  KidHub `--kid-bad-fg`). Link-Regeln in Karten treffen Knöpfe nicht: `a:not(.mat-mdc-button-base)`.
- Knopf = Symbol + kurze Beschriftung; ganz schmal (≤ 380 px) darf die Beschriftung wegfallen (r2-rg-rep-toolbar).
  Steht ein Hauptknopf neben einer Moduswahl im Seitenkopf, wird er unter 600 px zum Symbolknopf (Name im `aria-label`
  + `title`), damit beide in **einer** Zeile bleiben und der Kopf in allen Modi gleich hoch ist (Repertoire, 0.759.2).
  Symbole **in** Knöpfen behalten die Material-Größe (r2-rg-trainer-icons).
- **Gesperrter Knopf → Grund sichtbar darunter** (leise Zeile), nicht nur im Tooltip (r2-rg-analyze-disabled) —
  am Handy gibt es keinen Hover. In einer Knopfzeile stehen Knopf und Grund in einer eigenen schmalen Spalte
  (≤ 280 px), nie als 100-%-Zeile im Aktionsblock — sonst rutscht das ⋮ allein in eine Zeile (a1-rk-shared-kopf-grund).
- Kein zweiter gleicher Hauptknopf auf einer Seite (Punktepartie: nur ein „Zur Aufgabe“, 0.750.1). Hat ein Modus
  seine eigene Hauptaktion im Panel (Lochfinder „Löcher suchen“), tritt der Kopfknopf dort als `mat-stroked-button`
  zurück (a1-rk-holes-zwei-hauptknoepfe).
- LeagueHub: `.btn-pri` (gefüllt in `--ink`), `.btn-sec` (1.5 px Rahmen), `.btn-link`, `.icon-btn` (30 px);
  ClubHub: `.btn` (min. 44 px), `.btn.primary` (eine je Seite), `.btn.danger`, `.btn.slim`; KidHub: Pillen und
  `.big` (Abschnitt 8.3).

### 5.5 ⋮-Menüs

- `mat-menu` hinter `mat-icon-button` mit `more_vert`; Beschriftung `common.moreActions` (Tooltip + `aria-label`,
  bevorzugt per `appIconLabel`).
- Reihenfolge: Öffnen/Navigieren → Einstellungen/Umschalten → Teilen/Export → **Löschen zuletzt, rot**
  (`.delete-item mat-icon` in Fehlerfarbe; Vorbilder `course-card`, `repertoire-list`, `reconstruct-detail`).
- Ins ⋮ wandert, was in der Kopfzeile nicht mehr passt (Rekonstruktion: in der Kopfzeile nur ✓ und ⋮,
  r2-rg-recon-actions; Turnierseite am Handy, 0.746.0).
- Am Handy bleibt das ⋮ **in derselben Zeile** wie die Knöpfe (0.751.2, Kursseite).
- Brett-Stellungen haben ein eigenes ⋮ (`features/analysis/position-menu.component.ts`).
- Selten gebrauchte Werkzeugkarten stehen nicht von vornherein auf der Seite: ein ⋮-Eintrag blendet sie ein (und
  scrollt sie in Sicht), ein ✕ in ihrer Kopfzeile wieder aus; solange sie arbeiten, bleiben sie ohne ✕ stehen
  (Analysebrett: „Sparring gegen Maia“ im ⋮ der Stellung, sichtbar während eines Sparrings, 0.760.0).

### 5.6 Dialoge und Blätter

- **Rückfragen** nur über `ConfirmService.ask(…)` (`shared/confirm-dialog/confirm-dialog.component.ts`, 47 Dateien),
  Texteingabe über `PromptService` (`shared/prompt-dialog/`). **Nie `window.confirm`** (unsichtbar im Vollbild,
  abschaltbar, Browsersprache; festgehalten durch `NativeConfirmGuardTests`). Oberflächen ohne Sprachwahl geben
  Knopftexte über `CONFIRM_LABELS` vor.
- **Breiten** (Ist: 360–760 px, häufig 440 / 400 / 560). **Regel ab jetzt:** einfache Frage/kurzes Formular
  400–440 px, Auswahl-Dialog 560 px, Inhalts-Dialog bis 760 px, immer `maxWidth: '94vw'`.
- **Blatt am Handy:** kein `MatBottomSheet`, sondern derselbe `MatDialog` mit
  `{ width: '100vw', maxWidth: '100vw', position: { bottom: '0' }, panelClass: ['solve-mode-panel', 'solve-mode-sheet'] }`
  unter 600 px, sonst `{ width: '560px', maxWidth: '94vw' }`; Hintergrund `.solve-mode-backdrop` (rgba(0,0,0,.6)),
  Ecken 20 px bzw. `20px 20px 0 0` + `env(safe-area-inset-bottom)` (`core/solve-mode.service.ts`, `styles.scss`).
  Bisher nur der Spielweise-Dialog; **Regel ab jetzt:** neue Auswahl-Dialoge, die beim Einstieg erscheinen,
  folgen diesem Muster.
- **Blockierend** (`disableClose: true`) nur, wenn die Wahl nicht rückgängig zu machen ist; Auswahl-Dialoge mit
  `autoFocus: 'dialog'` (sonst wirkt die erste Kachel vorausgewählt).
- **Titel kurz**; lange Namen (Buchtitel) als kleine Zeile darunter (`subtitle`, r2-rp-mode-title).
- Gleiche Frage = gleicher Dialog (Wochenpost nutzt `SolveModeService.openDialog`, r2-rk-weekly-mode-dialog).
- Im Vollbild hängt `FullscreenOverlayService` die Overlays ins Vollbild-Element (`usePopover: false`).
- PGN-Viewer-Dialog: ohne Polster, ≤ 768 px randlos (`.pgn-viewer-dialog`, `styles.scss`).

### 5.7 Snackbars

Nur über `SnackbarService` (`core/snackbar.service.ts`, 82 Dateien; roh `MatSnackBar` nur noch 1×). Text kommt
fertig übersetzt; Dauer nach Methode:

| Methode | Dauer | Wofür |
|---|---|---|
| `show` / `info` | 3000 ms | Hinweis, Fehler einer Aktion |
| `success` | 2000 ms | Bestätigung |
| `quick` | 1500 ms | Umschalter-Rückmeldung |
| `warn` | 5000 ms | wichtig / offline |
| `copy` | 2000 ms, ohne Knopf | „kopiert“ |

Fehler einer **Nutzeraktion** werden immer gemeldet; still nur bei Hintergrund-Abfragen
(`src/frontend/CLAUDE.md` → „Fehlerbehandlung“).

### 5.8 Formulare

- RookHub/Turnierseite: `mat-form-field appearance="outline"` (171 von 172 bzw. 22 von 22) — **immer outline**.
- LeagueHub/ClubHub: native Felder, Beschriftung über dem Feld (`.field { display: grid; gap: 4px }`), Zustand in
  Signalen, kein FormsModule.
- Feldbreite nach Inhalt (ClubHub-Code-Feld so breit wie „ABCDE-FGHJK“, r2-c-code-field); Feldgruppen per
  `grid-template-columns: repeat(auto-fit, …)` statt `auto-fill` (r2-rg-edit-fields).
- Speichern-Leiste klebt unten → `scroll-padding-bottom`, damit fokussierte Felder nicht dahinter liegen
  (ClubHub `html:has(.form-save, .save-bar)`).
- Ungespeicherte Änderungen: `LeaveConfirm.canLeave()` + `unsavedChangesGuard`.
- Dateiauswahl in deutschsprachigen Oberflächen nicht mit dem Browser-Text („Choose File“): `FilePickDirective`
  (LeagueHub, l-filepicker).
- `mat-select` mit leerem Wert zeigt einen sichtbaren Eintrag („Überall“), nicht nichts (r2-t-radius-empty).

### 5.9 Chips, Pillen, Abzeichen

- **Auswählbare Filter:** `mat-chip-listbox`/`mat-chip-option` (RookHub/Turnier); LeagueHub `.fchip`, ClubHub
  `.chip-btn` jeweils mit `aria-pressed`.
- **Anzeige-Abzeichen:** eigene `.chip` (65×) / `.badge` (30×): ~0.72rem, `padding: 1px 7px`,
  `border-radius: 999px`, Fläche `color-mix(in srgb, currentColor 10%, transparent)`; Warnung
  `color-mix(in srgb, var(--mat-sys-error) 22%, transparent)` (`tournament-card.component.ts`).
- **Nur gesetzte Werte** bekommen ein Abzeichen (keine Vorgabe-Themen wie „Taktik“, r2-rp-taktik-chip).
- Ein erklärungsbedürftiges Abzeichen klappt seine Erklärung **per Tipp** auf (am Handy kein Tooltip,
  r2-rp-course-warn).
- Zähler im Seitenkopf: KidHub `.kid-counter` (Abschnitt 8.3).

### 5.10 Lade-, Leer- und Fehlerzustände

| Zustand | Baustein | Form |
|---|---|---|
| Seite lädt | `app-loading-spinner` (`shared/loading-spinner/`) | Material-Spinner 40 px, zentriert, `padding: 2rem` |
| Auftrag läuft | `mat-progress-bar` | über/unter dem betroffenen Bereich |
| Knopf arbeitet | `mat-spinner diameter="16"` im Knopf | Knopf gesperrt, Text „läuft …“ |
| Laden gescheitert | `app-load-error` (`shared/load-error/`) | `cloud_off` + Meldung + „Erneut versuchen“, `role="alert"`, **an der Stelle des Leerzustands** |
| Leer | (kein geteilter Baustein) | zentriert, optional Symbol, **ein Satz**, höchstens **eine** Aktion |

- Leer heißt „erklären, warum“: leerer Kurs vs. reiner Partien-Kurs (r2-rp-empty-course), freundlicher
  Leerzustand der KidHub-Kursseite (k-courses-empty).
- Ist: Klassen `.empty` (75×), `.empty-hint`, `.empty-state`, `.empty-text`. **Regel ab jetzt:** `.empty-state`
  mit `text-align: center; padding: 2rem 1rem`; ein geteilter Baustein ist offen (Abschnitt 10).
- Ein Ladekreis, der nach dem Laden stehen bleibt, ist ein Fehler (UI-Sweep-Prüfung „lädt noch“).
- Hinweiszeilen **im** Leerzustand (`.muted`, `.empty-hint`) erben dessen 60 % und werden nicht ein zweites Mal
  abgeblasst (sonst 36 %, hell ≈ 2,5:1) — Unterschied nur über die Größe (≈ .85–.9em) (a1-rk-leer-hinweis-blass).

### 5.11 Hinweise, Banner, Sperrkarten

- **Hilfe:** `app-help-hint` (`shared/help-hint/`): 22-px-Knopf, Deckkraft .55 (Fokus/Hover 1), Touch-Trefferfläche
  34 px, Tooltip per Klick fest (`.hh-tooltip`: max. 320 px, `pre-line`, Absätze mit `\n\n`, max. 60vh scrollbar).
- **Banner** (Seitenhinweise): Fläche getönt, Symbol links, Text `flex: 1 1 220px`, ein Knopf rechts, Radius 8 px
  (Vorbild `shared/reprocess-banner/`); Offline-Banner, Impersonation-Banner (`shared/impersonation-banner/`).
- **„Mehr“ steht im Satz:** als Textknopf am Satzende (`aria-expanded`), nicht als eigener Flex-Eintrag — sonst
  steht er am Handy allein in einer Zeile (0.751.2).
  Zugeklappter Text wird **gemessen** auf höchstens 3 Zeilen gekürzt (`shared/clamp-more/clamp-more.directive.ts`:
  Wortende + „…“, Knopf in der letzten Zeile, nur wenn der Text nicht ganz passt) — keine feste Zeichenzahl (passt
  nur für eine Breite) und kein CSS-`line-clamp` (schneidet den Knopf mit ab bzw. lässt ihn ganz weg). Beschriftung
  `common.showMore`/`common.showLess` (0.759.2).
- **Lange Texte** werden nicht in einen eigenen Scrollkasten gesperrt, sondern mit „Weiterlesen“ aufgeklappt
  (r2-k-course-scroll).
- **Tastaturhinweise** („Leertaste/Enter“, „←/→“) nur mit feiner Zeigesteuerung: Klasse + `@media (pointer: coarse)
  { display: none }` — am Touch-Gerät verlängern sie nur den Vorlauf (a1-rk-flashcards-tastaturhinweis-handy).
- **Sperrkarte** (Seite ohne Recht/Konto): **eine** Karte für alle betroffenen Seiten, Klartext statt
  Rechte-Schlüssel, **Weg zurück** (Startseite / Verknüpfen) als Knopfreihe mit Abstand
  (ClubHub `.gate` + `.gate-actions { gap: 8px }`, r2-c-gate-deadend; LeagueHub `.gate .actions`, r2-l-gate-text).

### 5.12 Brett-Komponenten

- Drei Bretter: `PuzzleBoardComponent` (Löser), `AnalysisBoardComponent`, `shared/pgn-viewer/chess-board.component.ts`;
  alle mit Vollbild-Knopf **im** Brett. Neue Brett-Ansichten benutzen eines davon.
- Koordinaten global 11 px (≥ 900 px: 12 px), halbfett, Ränge links oben, Linien unten rechts, Farbe je Feld
  (UX-060, `styles.scss`); KidHub ≈ 2,2 % der Brettbreite (`KID_COORDS`). Lesbar heißt ≥ 4,5:1 zum eigenen Feld:
  im Thema brown ist auch das dunkle Feld (#c0ae91) zu hell für helle Schrift — dort dunkle Schrift mit hellem Hof
  auf beiden Feldfarben (a1-rk-brett-koordinaten-dunkle-felder).
- Brettthemen (`board-theme-*`) und Figurensätze (`piece-set-*`) nur über `styles.scss`.
- Tippzonen neben dem Brett: `.board-tap` (kein Markieren, kein Doppeltipp-Zoom); kein Tipp-Aufblinken.
- Hinweiszeile unter dem Löser: bei `(pointer: coarse)` ≥ 44×44 px.
- Analysebrett: Zusatzkarten unter der Zugliste starten zugeklappt — der Eröffnungs-Explorer zeigt nur die Kopfzeile
  (Titel + Aufklapp-Pfeil) und fragt erst aufgeklappt; ein auf dem Gerät gemerkter Zustand gilt weiter (0.760.0).
- Stellung markieren: `app-mark-position-button` („+“, markiert grün gefüllt, `aria-pressed`).
- Analysebrett: Brettspalte klebt erst ab **900 px**, wo Brett und Seitenspalte sicher nebeneinander stehen (Brettspalte
  lässt dort 300 px frei); 769–899 px steht das Brett mittig einspaltig und scrollt mit. Bewertungsleiste mit 1-px-Rahmen
  `--mat-sys-outline` (Weißanteil sonst im hellen Design unsichtbar). Pfeile (autoShapes) erst auf ein gemessenes Brett
  (Breite > 0), sonst NaN-Koordinaten (a1-rk-analyse-tablet-brett, a1-rk-evalbar-hell, a1-rk-svg-nan-pfeile).
- **Knopfleiste unter dem Brett nie breiter als das Brett** (`max-width: 100%`, ◀ ▶ schrumpfen bis 44 px, Abstände
  wachsen mit dem Brett); am Handy (≤ 480 px) Nebenknöpfe 36 px, damit ◀ ▶ je ≥ 56 px bleiben (Partieseite,
  a1-rk-brettleiste-plus; die 36 px auch im Analysebrett).
- **FEN nie roh anzeigen:** Mini-Brett + „Weiß am Zug · Zug 5“ + Kopierknopf (r2-rg-recon-fen).
- **Puzzle-Löser** (alle drei Modi): zweispaltig erst ab 900 px; 769–899 px einspaltig, Brett mittig bis Fensterhöhe
  (`min(100%, 100vh - 180px)`), Seitenkarten volle Breite darunter. In der Seitenspalte zuerst die Bedienung
  (Kontext-Card mit Fortschritt/„Überspringen“, Status-Card, Aktionsleiste), der ungekürzte Kurs-Kommentar als eigener
  Block danach. Teilen + ⋮ der Aktionsleiste sind eine Einheit (`.pab-actions`), Kennzahlen-Raster `auto-fit` statt
  fester Spaltenzahl; leise Textknöpfe („Überspringen“) über `--mat-sys-on-surface-variant`, nie über Deckkraft (a1).
- Gleiche Erweiterung in allen drei Puzzle-Modi prüfen (Standard, Endless, Buch/Kurs; `rookhub/CLAUDE.md`).

### 5.13 Zahlen, Mehrzahl, Datum

- **Einzahl/Mehrzahl** über ein Schlüsselpaar `…` / `…One`, gewählt im Template:
  `{{ (n === 1 ? 'reconstruct.gapsOne' : 'reconstruct.gaps') | translate:{ count: n } }}` (14 Paare in
  `public/i18n/de.json`). Ist: 16 Texte noch mit Klammerform („Linie(n)“, „Repertoire(s)“). **Regel ab jetzt:**
  keine Klammerform; beim Anfassen auf das Schlüsselpaar umstellen.
- **Datum/Uhrzeit** in der App-Sprache (Locale-Daten für `en/de/hr/hu` registriert, `core/locale-data.ts`):
  deutsch „16.07.2026, 21:00“; Zeiträume „… – …“ (Halbgeviertstrich, r2-t-date-format); ClubHub TT.MM.JJJJ.
  **Keine festen Muster** (`dd.MM.yyyy`, `HH:mm` …) in der DatePipe, nur Locale-Formate (`date-format-usage.spec`,
  0.761.0): Datum mit Wochentag `EEE` + `, ` + `mediumDate` (+ ` · ` `shortTime`) — de „Do., 09.07.2026 · 21:00“,
  en „Thu, Jul 9, 2026 · 9:00 PM“; knappe Zeitpunkte in Listen/Tabellen `short` — de „11.10.26, 00:20“, en
  „10/11/26, 12:20 AM“; Uhrzeit allein `shortTime`/`mediumTime`.
- **Trenner** zwischen Angaben: „ · “; als `::before` des folgenden Elements, damit er beim Umbruch nie allein am
  Zeilenende steht (r2-t-meta-dot). Leerzeichen zwischen Interpolation und `@if` fest ins Markup schreiben —
  Angular entfernt sie sonst („Spielennoch offen“, 0.751.2).
- Ländernamen ausgeschrieben, nicht als Code (LeagueHub, 0.751.0).
- **Ergänzungsstrich** („Fortschritt und -Statistiken“) nie allein am Zeilenende: vor dem Strich U+00A0, danach
  Wortverbinder U+2060 („und -⁠Statistiken“, 0.763.0, `i18n-parity.spec`).
- **Zahl und Einheit/%** mit geschütztem Leerzeichen (U+00A0) zusammenhalten, auch in Übersetzungen hinter einem
  Platzhalter („{{percent}} %“) — sonst steht „%)“ allein in der nächsten Zeile (0.759.0, `i18n-parity.spec`).
  **Sprachausnahme Prozent** (0.761.0): Englisch und Ungarisch schreiben „46%“ ohne Leerzeichen (en/hu-JSON
  „{{percent}}%“), Deutsch und Kroatisch „46 %“ mit U+00A0. Prozent im Code immer über **`formatPct`/`| pct`**
  (`shared/format/pct.ts`, `Intl.NumberFormat` `style: 'percent'` in der Oberflächensprache) — nie „ %“ oder „%“
  anhängen, nie `toLocaleString(undefined)` (Browser- statt App-Sprache).

### 5.14 Notation

- Angezeigte Züge laufen durch **`SanPipe`** (`shared/chess/san.pipe.ts`): `{{ san | san }}` für einen Zug,
  `{{ line | san:'text' }}` für eine Zugfolge — Figurenbuchstaben der Oberflächensprache (deutsch/kroatisch
  „Sf3“, ungarisch „Hf3“).
- **Nie** für Eingabe, Speicherung, PGN-Export oder Schlüssel — dort bleibt Englisch.
- Gilt für Zuglisten, Explorer, Repertoires, Punktepartie, Engine-Linien, Zugbaum, Analyse-Aufträge, Korrektur
  (0.750.0/0.750.1).

### 5.15 Daten nach HTTP

Angular 22 zeichnet unmarkierte Views nach einer Antwort nicht zuverlässig neu. Abgesichert ist das zentral durch
`renderAfterHttpInterceptor` (`core/render-after-http.interceptor.ts`, Teil von `rhInterceptors()` in
`core/http-chain.ts`, in allen fünf Oberflächen). **Regel trotzdem:** alles, was aus einer Antwort angezeigt wird,
liegt in Signalen (`signal()`, `toSignal`, `computed`); Objekte nicht in einem Signal mutieren, sondern ersetzen.
Neue Komponenten `ChangeDetectionStrategy.OnPush`.

---

## 6 Text und Sprache

- **Jeder sichtbare Text über i18n** (ngx-translate 18, `TranslatePipe`, Dateien `public/i18n/<code>.json`).
  Vollständig gepflegt: **en, de, hr, hu** (`FORMAT_LOCALES`, `core/locale.service.ts`; `i18n-parity.spec.ts`
  verlangt gleiche Schlüssel und Platzhalter); 21 weitere Sprachen fallen schlüsselweise auf `en` zurück. Nach
  jeder Änderung alle 25 Dateien mit `JSON.parse` prüfen.
- LeagueHub und ClubHub sind deutsch ohne Sprachwahl; KidHub bietet de/en/hr/hu (`KIDS_LANGUAGES`).
- Geteilte Spielerkarte (`shared/player-card/`): Texte unter `playerCard.*`, Züge/Datum/Prozent über `card-lang.ts` —
  in RookHub (Spielervorbereitung) in der Oberflächensprache, in LeagueHub deutsch (die App stellt `de` ein: S/L/T/D,
  „24.05.2026“, „39 %“). Neue Texte der Karte nie als Klartext ins Template.
- Nicht übersetzt: FEN/PGN, Eigennamen, „RookHub“, „Stockfish“, gecrawlte Daten (Züge siehe 5.14).
- **Anrede „du“**, kurze Beschriftungen, keine Fachbegriffe ohne Not; KidHub in einfacher Sprache.
- **Deutsche Anführungszeichen typografisch „…“** (in `de.json` 145 Werte, 5 noch mit geraden), keine »…«
  (`i18n-parity.spec`, 0.763.0). Im deutschen
  Changelog-Text Pflicht: ein gerades `"` beendet den JS-String und bricht den Build.
- **Englische Anführungszeichen typografisch “…”** (innen ‘…’) in `en.json` — keine »…«, „…“ oder geraden `"`
  (ausgenommen Syntax: HTML-Attribute, PGN-Tags in Platzhaltern); `i18n-parity.spec` prüft es (0.761.0).
- Ein deutsches Wort im englischen Text nur als erkennbarer Befehl/Name mit Erklärung („/motivation, option “aus”
  (off)“); Sprachnamen im Satz in der Oberflächensprache (`Intl.DisplayNames`: „Original language: German“), die
  Eigenbezeichnung („Deutsch“) nur in Sprachauswahlen (0.761.0).
- Changelog-Einträge zweisprachig (`en`/`de`) in `src/environments/changelog-data.ts`.
- Platzhalter aus Fremddaten nicht anzeigen: PGN-„?“ heißt „ohne Name/Kapitel“ (r2-rg-rep-question),
  chess-results-„various“ heißt „verschiedene Spielorte“ (kursiv, r2-t-various), keine internen Dateinamen
  (r2-rp-sourcefile).

---

## 7 Barrierefreiheit

- **Kontrast ≥ 4,5:1** für Text in beiden Modi: Tokens (`design-tokens.spec.ts`), Löser
  (`features/puzzles/puzzle-contrast.spec.ts`), KidHub (`src-kidhub/app/kids-contrast.spec.ts`). Gesperrte
  Elemente nicht pauschal auf 55 % Deckkraft setzen (k-locked-contrast).
- **Reine Symbolknöpfe:** `[appIconLabel]="'…' | translate"` (`shared/icon-label/icon-label.directive.ts`) setzt
  Tooltip **und** `aria-label` — statt `matTooltip` (nicht zusätzlich, NG0309). Ist: 18× `appIconLabel` gegen
  262× `matTooltip` (oft mit Hand-`aria-label`). **Regel ab jetzt:** bei neuen und angefassten Symbolknöpfen
  `appIconLabel`.
- **Tippziele ≥ 44 px** (KidHub-Test `kids-targets.spec.ts`, `MIN_TARGET = 44`; Turnierkarten-Aktionen am Handy
  64×48; LeagueHub-Fußlinks; ClubHub `.btn`). Kleinere Symbole vergrößern ihre Trefferfläche unsichtbar
  (`::after { inset: -6px }`, `@media (pointer: coarse)`).
- **Fokus sichtbar:** `:focus-visible` — LeagueHub/ClubHub global `outline: 3px solid var(--focus); outline-offset: 2px`;
  KidHub-Pillen `:focus-within`. `outline: none` nur mit Ersatz.
- **Zustände benennen:** Umschalter mit `aria-pressed`, Aufklapper mit `aria-expanded`, Zustandszeichen mit
  Klartext im `aria-label` (ClubHub-Anwesenheit ✓/✗, r2-c-attendance-da), Fehler mit `role="alert"`,
  versteckte Beschriftungen per `.sr-only`.
- **Reiter nicht `[disabled]`** (dort feuert kein Tooltip): wählbar lassen, Grund im Inhalt (r2-t-history-disabled-tab).
- Animationen respektieren `prefers-reduced-motion`.

---

## 8 Bereiche

### 8.1 RookHub (`src/`)

- Material 22 (M3, Azure), dunkel als Vorgabe, `ThemeService` (3.1). Navbar (`shared/navbar/`): ≤ 768 px
  Hamburger; abgemeldet ≤ 480 px nur „Anmelden“ und ☰ (0.740.0). Fußzeile (`shared/app-footer/`) bleibt auf
  kurzen Seiten unten; Links ≥ 44 px.
- Dashboard: neue Kacheln nicht in `DEFAULT_VISIBLE`; angepinnte Kurse in eigener breiter Zeile (r2-rk-dash-stretch).
- Löser: Aktionen über `PuzzleActionBarComponent`; Spielweise per `SolveModeService` (einmal je Bereich, für
  Kurse je Kurs; „für alle Puzzle-Bereiche übernehmen“).
- Admin-Seiten: 1400 px, Tabellen mit Karten-Fassung am Handy; Admins werden zu ihren Konversationen geführt.
- Spielervorbereitung: die Spielerkarte stammt aus LeagueHub (`features/prep/prep-card.scss`, eigenes Style-Bündel
  `prep-card`), ihre Variablen werden auf Material-Rollen abgebildet (`--ink: var(--mat-sys-on-surface)` …);
  Bedienelemente sprechen in RookHub Material (r2-rg-prep-style).
  Ihre Texte, Notation, Datum und Prozent folgen der Oberflächensprache (Abschnitt 6, e1-rk-prep-card-german).
- `index.html` theme-color `#3f51b5`.

### 8.2 Turnierseite (`src-turnier/`)

- Gleiches Material-Thema und gleicher Design-Modus wie RookHub (Cookie geteilt); theme-color `#172345`.
  Navbar `shell/turnier-navbar.component.ts`: ≤ 768 px Kopfknöpfe + ⋮; 769–1023 px „Zu RookHub“ nur als Symbol;
  auf `/login`, `/register`, `/forgot-password`, `/reset-password` kein „Anmelden“.
- **Turnierkarte** (`features/tournament-directory/tournament-card.component.ts`): vier feste Plätze
  Merken | Kalender | Ausblenden | Melden (40 px); fehlt eine Aktion, hält `.tc-slot` den Platz. ≤ 600 px:
  Kurzwort unter jedem Symbol, Fläche ≥ 64×48, Radius 12, `justify-content: space-between`. Gemerkt =
  gefülltes Symbol + `color-mix(var(--mat-sys-primary) 16%)`. Gedrängte Fassung `.tc-overview` für Karten-Popup
  und Kalenderfenster.
- **Karten-Pins** (`pin-palette.ts`, `map-pin-marker.ts`, Leaflet): eingefärbt nach **genau einem** Merkmal
  (Bedenkzeit | Turnierart | Nachwuchs | Geschlecht), Rand je Füllung von Hand, Zahl auf hellen Füllungen dunkel
  (`darkLabel`).
- Gleichnamiges Turnier am selben Tag: eigene Karte mit Hinweis (r2-t-dup-cards). Titel nur echte (GM, IM …).
- Geteilter Turnier-Link: dieselbe Knopfreihe wie die Detailseite — Merken (primär), Beobachten, Volle Ansicht,
  Teilen (r2-t-public-actions).
- Datenschutz mit eigener Einleitung (`LegalSiteKind 'turnier'`).
- **Breitbild ≥ 2400 px** (u1): Turnierkalender zeigt Liste (Karten zweispaltig) und Karte nebeneinander unter EINEM
  Reiter „Liste & Karte“ (Karte klebt, Fensterhöhe), Kalender bleibt Reiter; Turnier-Detail und `/t/:id`: Spieler/Teams
  als Reiter links, Paarungen der gewählten Runde rechts in eigenem klebendem Scroll-Kasten. Abfrage im Code über
  `wideScreen()` (`src-turnier/app/core/wide-screen.ts`, mit Rückruf zum Nachladen). Darunter unverändert Reiter.

### 8.3 KidHub (`src-kidhub/`)

- **Nur hell**, kein `ThemeService`; die Hülle malt ihren Grund selbst (Verlauf + `--kid-bg`). Keine
  Material-Formularfelder, -Dialoge oder -Menüs.
- **Farben** (`app/app.component.ts`, `:host`):

| Token | Wert | Zweck |
|---|---|---|
| `--kid-bg` / `--kid-card` | #eaf6ff / #ffffff | Grund / Karten |
| `--kid-title` | #1d4f91 | Titel, Rahmen, Fokus |
| `--kid-shadow` | rgba(30,70,120,.22) | Knopf-Schatten |
| `--kid-green` / `--kid-green-strong` | #2fb35f / #1a7f3e | Flächen ohne Schrift / Knöpfe mit Schrift (5,1:1) |
| `--kid-good-bg` / `--kid-good-fg` | #d6f5de / #11632f | gelöst, Zähler |
| `--kid-bad-bg` / `--kid-bad-fg` | #ffe0e0 / #9b1c1c | falsch |
| `--kid-yellow`, `--kid-sky`, `--kid-peach`, `--kid-info-bg` | #ffcc33, #cfe9ff, #ffe1cc, #fff3c4 | Akzente |

- **Bausteine** in `app/shared/kids-layout.ts`: `KID_STACKED`, `KID_SHORT` (Ansichten), `KID_BACK`
  („← Start“-Pille, ≥ 44 px), `KID_COORDS`, `KID_PAGE_WIDTH` + `kidPageWidth()`, `KID_COUNTER`; dazu
  `KID_AUTH_STYLES` (`kids-auth.ts`), `KID_PRIVACY_STYLES` (`kids-privacy.ts`), `KID_HOME_WIDE`. Neue Seiten
  binden diese ein statt zu kopieren.
- **Knöpfe:** Pillen (`border-radius: 999px`, 2 px Rahmen, Schatten `0 3px 0`), Hauptknöpfe `.big`
  (Radius 18, 1.3rem/800, Schatten `0 5px 0`, `:active` 3 px tiefer); Mindesthöhe 44 px, Hauptknöpfe 52 px.
- **Eule** 🦉 sagt den Stand der Aufgabe (`kids-puzzle.component.ts`), Sterne ★ zeigen Fortschritt; Aufgabe
  „x von 6“ neben dem Brett.
- **Zähler:** EINE grüne Pille `.kid-counter` mit Zeichen + Zahl (★ 14, ✓ 3, ✓ 0/20), ohne Schatten und Zeiger —
  sieht nicht wie ein Knopf aus (r2-k-counter).
- Zahleneingaben als großer Zähler − / + (gedrückt halten zählt schneller) und Umschalter statt vieler
  Zahlknöpfe (0.747.0).
- Stufen- und Sternenkarte am Handy dreispaltig; gesperrte Stufen lesbar; angefangene Stufen zeigen den
  Zwischenstand; Kurskachel mit Stand „8 von 287“ + dünnem Balken.
- Breitbild ≥ 2400 px (4.1): die Hülle setzt `--kid-board` bis zur Fensterhöhe (≤ 1240 px; Aufgabenseiten wachsen mit
  `--kid-row`) und `--kid-wide-max: 1880px` für Startseite und Karten — dort größere Kacheln statt mehr Spalten.
  Tablet (600–899 px): Startkacheln 2 × 2, eine allein übrige nimmt die ganze Zeile. Kurs-Zähler ist die `.kid-counter`-Pille.
- Sprachwahl als Pille mit 🌐 über einem nativen `select`; Anmeldemasken mit „← Start“ über der Karte;
  Datenschutz kindgerecht, kein Impressum.

### 8.4 LeagueHub (`src-leaguehub/`, `leaguehub.scss`)

- Hell/dunkel über `ThemeService`; Schriften Barlow / Barlow Semi Condensed (`public-leaguehub/fonts`).
- **Farben:**

| Variable | hell | dunkel | Zweck |
|---|---|---|---|
| `--paper` / `--surface` | #f3f5f8 / #ffffff | #0f141b / #161d26 | Grund / Karten |
| `--ink` / `--muted` / `--line` | #17202b / #5a6878 / #d9dfe7 | #e6ebf1 / #97a5b5 / #28323e | Text / leise / Linien |
| `--red` | #c8102e | #ff4d63 | **Tiroler Rot** — wahrscheinlichster Spieler, Akzent, Fehler |
| `--on-red` | #ffffff | #0f141b | Schrift auf gefülltem Rot (Masken-Hauptknopf, Gefahrknopf, `.cta .btn-pri`), ≥ 4,5:1 (d1-l-mask-red-contrast) |
| `--ok` / `--warn` | #1f7a4d / #b45309 | #4cc28a / #f0a04b | bestätigt / unsicher |
| `--bar`, `--track`, `--sq-*` | | | Balken, Brettfarbe-Quadrate |
| `--focus` | #1c5fd4 | #7aa7ff | Fokusrahmen |

- Kopf: Wortmarke `.brand` (40 px `--cond`), Untertitel neutral (r2-l-subtitle); Reiter `.tabs` mit Unterstrich
  (aktiv `border-bottom: var(--ink)`), am Handy waagrecht scrollend, aktiver Reiter sichtbar, ein Konto-Knopf mit
  Menü (l-nav-mobile).
- Flächen `.panel`, `.gate`, `.empty`, `.fixture`: `var(--surface)`, 1 px `--line`, Radius 10. Aufforderung
  `.cta`: roter Balken links.
- Tabellen `.rtable`, `.src-tbl`, `.pairings`: Kopf in `--cond`, klein, `--muted` (teils Versalien); Vereinspartien
  am Handy als Karten (Containerabfrage < 640 px).
- Knöpfe 5.4; Segmentschalter `.seg` (`aria-pressed`); Spielerkarte als natives `<dialog class="card">` mit
  klebendem Kopf.
- Auswahlfelder wie `.pick select` (Fläche, 1 px Rahmen, Radius 6), auch der Vereinswechsel (r2-l-club-select).
- Fußzeile immer unten: `lh-root` ist eine Flex-Spalte über `100dvh`, `main.wrap` wächst (wie ClubHub; u1-l-fuss-schwebt).
- Breitbild ≥ 2400 px: `.wrap.mid` und `.wrap.wide` (Formular prüfen/korrigieren, Brett ≤ `min(100vh - 240px, 960px)`) auf
  `--page-max-width-wide`, Prognose-Bretter vierspaltig, bei 6 (bzw. 3) Brettern dreispaltig (u1-l-prognose-bretter,
  a1-l-prognose-ultrawide-6-bretter); kurze Tabellen (Übertragungen) bleiben 1 208 px breit, linksbündig.
- Links ohne eigene Klasse in `--ink` (unterstrichen), nie Browserblau; Bereichsregeln (`.foot a` …) gehen vor.
- Gesperrte Knöpfe `.btn-pri`/`.btn-sec` mit festen Farben (`--muted` auf `--track` bzw. `--line`-Rahmen), nicht per Deckkraft.
- Wer mehrere Konto-Vorschläge hat (`.sugg-group.multi`), steht als umrahmte Fläche mit „N Konten“ im Kopf, Knöpfe auf einer Höhe.

### 8.5 ClubHub (`src-clubhub/`, `clubhub.scss`)

- Bild: **Karteikasten** — Kopf in Leinengrün mit Bereichen als Karteireitern, Karteiblatt mit roter Kopflinie,
  „heute“ als Textmarker. Hell/dunkel über `ThemeService`. Kein Service Worker.
- **Farben:**

| Variable | hell | dunkel | Zweck |
|---|---|---|---|
| `--paper` / `--card` | #eef1ec / #ffffff | #0f1513 / #18201d | Tisch / Karte |
| `--ink` / `--muted` / `--line` | #1b2a26 / #5a6963 / #d2d9d3 | #e4ebe7 / #9aaaa3 / #2b3632 | Text |
| `--linen` (+ `-ink`, `-muted`) | #1f4d43 | #173a33 | Kopf („Einband“) |
| `--primary` | #1f4d43 | #57c48c | **der eine gefüllte Knopf je Seite** |
| `--rule` | #c4453a | #e0695e | rote Kopflinie, Abschnittsköpfe |
| `--mark` / `--mark-ink` | #ffe45c / #2b2600 | #d8b92a / #1c1800 | Textmarker, Codes |
| `--ok`/`--ok-soft`, `--bad`/`--bad-soft` | #1d7347 / #e2f1e8, #b3261e / #f9e4e2 | … | anwesend / fehlt |
| `--bad-ink` | #ffffff | #1b0f0e | Schrift auf gefülltem `--bad` (Gefahrknopf der Rechtsseiten) |

- Überschriften Zilla Slab 700 (h1 32, h2 23, h3 19 px), Text Atkinson Hyperlegible 17 px.
- `.btn` (min. 44 px, Radius 8, 1.5 px Rahmen), `.btn.primary`, `.btn.danger`, `.btn.slim` (36 px);
  Gruppenfilter als `.chip-btn` statt Auswahlliste.
- `.sheet` (Karteiblatt, Kopflinie `2px solid var(--rule)`), `.cards` (Liste in einer Karte), `.gate`
  (Sperrkarte mit `.gate-actions`), `.empty` (gestrichelter Rahmen), klebende `.form-save` über die volle Breite.
- Anwesenheit `.matrix`: eigener Scrollkasten, Namensspalte klebt links, Zellen ✓/✗ 26 px.
- Datum TT.MM.JJJJ; der Verknüpfungsstand des Kontos steht in der Fußzeile (r2-c-footer-link).
- Gesperrte `.btn` mit voller Deckkraft: Schrift `--muted`, Rahmen gestrichelt in `--muted`, `.btn.primary` auf 14 %
  Primär-Tönung — nie pauschal `opacity`. Gesperrt bleibt ein Knopf nur, wenn seine Beschriftung den Grund nennt („keine
  spätere ›") oder gerade gespeichert wird; fehlt eine Eingabe, fokussiert der Tipp das Feld und darunter steht, was fehlt
  (`.need`, a1-c-disabled-button-contrast).
- Metazeilen `.kid-meta`: Trenner „ · " als `::before` ab der zweiten Angabe, nicht vor Chips (5.13); Seitenkopf
  `.page-head`: Titelblock schrumpft (`flex: 1 1 320px`), Knöpfe bleiben daneben. Rechtsseiten-Karte linksbündig an der
  Logo-Kante. Ab 2400 px nur Seiten mit Anwesenheitsmatrix in `--page-max-width-wide` (Verlauf ≤ 46em), Rest 860 px.

### 8.6 Geteilte Masken (Anmeldung und Recht)

- Komponenten aus `src/app/features/auth/` und `src/app/features/legal/` (Login, Registrierung, Passwort
  vergessen/zurücksetzen, Datenschutz, Impressum, Konto löschen), in den Hubs per Alias `@rh/*` geroutet.
  Jede App prüft ihre Links mit `checkSharedPageLinks` (`src/app/testing/shared-page-links.ts`).
- **`LEGAL_SITE`** je Oberfläche (`features/legal/legal-site.ts`): Impressum ja/nein, Kontaktadresse, `kind`
  (eigene Datenschutz-Einleitung: rookhub/turnier/kidhub/leaguehub/clubhub), Rückziel `back`, `accountHome`.
- Rücklink „← Zurück“ bzw. zur Startseite statt immer zur Anmeldung (`legalBack`, x-back-login); keine doppelten
  Rechtslinks unter der Anmeldekarte in den Hubs (`.legal-links { display: none }`); kein „Anmelden“-Knopf auf
  der Anmeldeseite selbst (x-login-headbtn, r2-t-login-headbtn).
- **Umgestaltung je Oberfläche — nie die geteilte Komponente für einen Hub ändern**, sondern im globalen
  Stylesheet des Hubs die Material-Variablen auf der Karte überschreiben:

| Oberfläche | Wie |
|---|---|
| RookHub, Turnierseite | Material unverändert |
| LeagueHub | `lh-root :is(app-login, …, app-account-deletion) .mat-mdc-card { --mat-sys-primary: var(--red); --mat-card-elevated-container-shape: 10px; --mat-button-*-container-shape: 6px; … }`, Schrift Barlow, Rechtsseiten-Köpfe rot (r2-l-legal-style) |
| ClubHub | dieselbe Technik mit `:is(…) .mat-mdc-card`, Primär grün, Köpfe Zilla Slab in `--rule` (r2-c-privacy-style); Knöpfe auf Rechtsseiten wie `.btn` (Radius 8, 44 px) |
| KidHub | `KID_AUTH_STYLES` / `KID_PRIVACY_STYLES` in der Hülle + „← Start“-Pille; Rechtsseiten = `/privacy` **und** `/account-deletion` (`isLegalUrl`, Breite `KID_PAGE_WIDTH.legal`); Fachbegriff-Listen für Eltern zugeklappt (`details.kid-parent`), davor ein Kindersatz (`foldPrivacy`, `foldAccountDeletion`) |

---

## 9 Prozess für Gestaltungsänderungen

1. **Sichten:** `tools/ui-sweep` (`./sweep.sh`) rendert jede Seite aller Oberflächen — Vorgabe Handy
   (390×844, Touch) + breit (1920×1080), dunkel, Deutsch, abgemeldet + normales Konto; Zusatzachsen Tablet
   (820×1180), Laptop (1366×768), hell, Englisch, Rollen (`--auth`). Automatisch geprüft: Seite zu breit, ragt
   raus, Text abgeschnitten, Schlüssel statt Text, Ladekreis steht, Konsole/API-Fehler, Umleitung; dazu
   Pixel-Vergleich mit dem vorigen Lauf. Gestaltung selbst prüft ein Mensch bzw. Claude **gegen diese Datei**.
   Der Skill **`/ui-design-check`** (`~/.claude/skills/ui-design-check/`) macht das in einem Zug: Sweep →
   geänderte Bilder gegen diese Datei sichten (Sonnet) → Kandidaten bestätigen + Mockup (Opus) → Prüfseite.
2. **Fund + Mockup:** jeder Fund bekommt eine ID mit Bereichskürzel (`x-` alle, `t-` Turnier, `k-` KidHub,
   `l-` LeagueHub, `c-` ClubHub, `r…-` RookHub-Bereiche; `r2-` = zweite Runde) und ein Mockup auf der Prüfseite
   (Seitenbauer `~/claude/.ui-sweep-review/build.mjs`); der Nutzer entscheidet je Fund „passt / anders / weglassen“.
3. **Umsetzen:** nur bestätigte Funde. Kommentar `UI-Sweep <Datum> (<ID>): <Grund>` an der Stelle; wo messbar
   ein Spec (Breite bei 390 px, Kontrast, Tippziel). Tests je Projekt (`npx ng test app|turnier|kidhub|leaguehub|clubhub`),
   Build, Changelog zweisprachig, Version.
4. **Nachprüfen:** `./sweep.sh --local` oder nach Dev-Deploy erneut; Vorher/Nachher auf der Prüfseite; der Nutzer
   bestätigt.
5. **Festhalten:** neue Regel hier eintragen + Änderungsprotokoll.

---

## 10 Bekannte Abweichungen / offene Punkte

| Punkt | Ist | Regel / Vorschlag |
|---|---|---|
| Hex-Farben | ~830 in `src/`, 39 Turnier; Tokens 43× | beim Anfassen auf `--rh-*`/`--mat-sys-*` umstellen |
| Indigo-Reste | `#3f51b5` 41×, theme-color in `src/index.html` | Thema ist Azure (`--mat-sys-primary`) |
| Primärknopf | `mat-raised-button color="primary"` 88× neben `mat-flat-button` 70× | neu nur flat |
| Seitenbreiten | feste 768/760/600 px auf vielen Seiten | `--page-max-width` |
| Breakpoints | 26 verschiedene Werte | 600 / 768 / 900 (+ 640 in LH/CH) |
| Dialogbreiten | 360–760 px gemischt | 400–440 / 560 / ≤ 760 |
| Blatt am Handy | nur Spielweise-Dialog | weitere Einstiegs-Dialoge nach diesem Muster (offen: welche) |
| Leerzustand | kein geteilter Baustein, vier Klassennamen | `.empty-state`; geteilte Komponente offen |
| Mehrzahl | 16 Klammerformen „(n)/(s)“ | Schlüsselpaar `…One` |
| Symbolknöpfe | `matTooltip` 262× vs. `appIconLabel` 18× | `appIconLabel` |
| Seitenkopf-Klassen | `.head` / `.header` | `.header` |
| Feste Zustandsfarben in geteilten Bausteinen | `mark-position` #2e7d32, `reprocess-banner` #ffb300/#ef6c00, `.delete-item` #e53935 | Tokens (`--rh-success`, `--rh-warn`, `--rh-error`) |
| Handy-Grenze in TS | `SolveModeService.narrow()` `max-width: 600px` vs. CSS `599.98px` | bei 600 px genau ein Pixel Überlappung; angleichen |
| Kommentar-Markierung | `UI-Sweep r3: …` ohne Datum/ID neben `UI-Sweep 2026-10-11 (r2-…)` | immer Datum + ID |
| KidHub dunkel | bewusst nicht vorhanden | bleibt so (Entscheidung) |

---

## Änderungsprotokoll

| Datum | Version | Entscheidung | Grund | Quelle |
|---|---|---|---|---|
| 2026-07-26 | 0.334.0 | UI-Dichte-Regel: eine primäre Aktion, ≤ 3 sekundäre, Rest ⋮; ein Satz je Karte, Rest hinter ? | Dichte wuchs mit jedem Feature | UI-Welle 2/3, `rookhub/CLAUDE.md` |
| 2026-07-26 | 0.334.0 | Eine Seitenbreite `--page-max-width: 1240px` | page-eigene 1400px/96vw, tote Info-Spalten | UI-Welle 3, `styles.scss` |
| 2026-09-29 | — | Semantische Zustands-Tokens `--rh-*` hell/dunkel, Vordergrund-only | Hex-Werte unter 4,5:1 im Dunkelmodus | Codereview F8-003 |
| 2026-09-29 | — | `mat-raised-button color="primary"/"warn"` gefüllt | M3-„protected“-Knopf im Dunkelmodus 1,08:1 | UX-052 |
| 2026-09-29 | — | `appIconLabel` für Symbolknöpfe | Knopf hieß für Vorleser nur „Schaltfläche“ | UX-014 |
| 2026-09-29 | — | Rückfragen nur über `ConfirmService` | native Rückfrage im Vollbild unsichtbar, abschaltbar | F8-005 |
| 2026-10-10 | 0.740.0 | Brettkoordinaten 11/12 px halbfett für alle Bretter | 9 px / 80 % kaum lesbar | UI-Sweep 10.10. |
| 2026-10-10 | 0.740.0 | Handy-Kopf abgemeldet: nur „Anmelden“ + ☰; Fußzeile bleibt unten; Datum in App-Sprache; Listen mit Stift statt Formular | UI-Sweep Runde 1 | 0.740.0 |
| 2026-10-10 | 0.743.0 | Spielweise-Dialog: abgedunkelter Grund, zwei Kacheln, am Handy Blatt unten | Dialog hing scheinbar schief über dem Brett | UI-Sweep 10.10. |
| 2026-10-10 | 0.746.0 | Turnierkarte mit festen Aktionsplätzen + Beschriftung am Handy; ⋮ im Turnierkopf am Handy | Symbole sprangen je Karte, unverständlich am Handy | t-card-actions, t-card-labels |
| 2026-10-10 | 0.746.0 | Rechtsseiten: „← Zurück“ statt immer Anmeldung; keine doppelten Rechtslinks; kein „Anmelden“ auf /login; eigene Datenschutz-Einleitungen je Hub | Sackgassen, Dopplungen | x-back-login, x-login-legal, x-login-headbtn, x-privacy-intro |
| 2026-10-10 | 0.746.0 | KidHub: Logo in der Inhaltsspalte (`KID_PAGE_WIDTH`), Sprachwahl als Pille, gesperrte Stufen lesbar, freundlicher Leerzustand | Logo klebte links, Kontrast < 4,5:1 | k-logo, k-lang-select, k-locked-contrast, k-courses-empty |
| 2026-10-10 | 0.746.0 | LeagueHub: Vereinspartien am Handy als Karten, scrollende Reiter + Konto-Menü, deutsche Dateiauswahl | Tabelle/Kopf am Handy unbedienbar | l-club-mobile, l-nav-mobile, l-filepicker |
| 2026-10-10 | 0.747.0 | KidHub: Zähler − / + und Umschalter statt vieler Zahlknöpfe | sieben Knöpfe + Feld überforderten | k-free-controls |
| 2026-10-11 | 0.750.0 | Angezeigte Züge über `SanPipe` in den Figurenbuchstaben der Sprache | deutsch und englisch gemischt | r2-rg-notation |
| 2026-10-11 | 0.750.0 | Gleiche Frage = gleicher Dialog (Wochenpost → `SolveModeService.openDialog`) | eigene, anders aussehende Abfrage | r2-rk-weekly-mode-dialog |
| 2026-10-11 | 0.750.0 | FEN als Mini-Brett + Kopierknopf; PGN-„?“ nie anzeigen; Anteil/Partien getrennte Spalten | Rohdaten statt Information | r2-rg-recon-fen, r2-rg-rep-question, r2-rg-explorer-cols |
| 2026-10-11 | 0.751.0 | Breite Tabellen unter 600 px als Karten | Admin-Benutzerliste am Handy unbenutzbar | r2-rk-admin-users-mobile |
| 2026-10-11 | 0.751.0 | Einzahl/Mehrzahl als Schlüsselpaar `…One` | „1 Repertoires“, „Linie(n)“ | r2-rg-rep-reprocess u. a. |
| 2026-10-11 | 0.751.0 | KidHub: EINE Zähler-Pille `.kid-counter` | vier verschiedene Zählerformen | r2-k-counter |
| 2026-10-11 | 0.751.0 | Lange Texte mit „Weiterlesen“ statt Scrollkasten | Kasten im Kasten am Handy | r2-k-course-scroll |
| 2026-10-11 | 0.751.0 | EINE Sperrkarte mit Weg zurück; Klartext statt Rechte-Schlüssel | Sackgasse | r2-c-gate-deadend, r2-l-gate-text |
| 2026-10-11 | 0.751.0 | Geteilte Masken im Hub-Look per Variablen-Überschreibung im Hub-Stylesheet | RookHub-Material wirkte fremd | r2-l-legal-style, r2-c-privacy-style |
| 2026-10-11 | 0.751.0 | Gesperrter Knopf nennt den Grund darunter; Reiter nicht `[disabled]` | Tooltip am Handy/bei disabled unsichtbar | r2-rg-analyze-disabled, r2-t-history-disabled-tab |
| 2026-10-11 | 0.751.0 | Aktionen, die nicht passen, ins ⋮ (Rekonstruktion); Repertoire-Karten gleich hoch, Aktionen unten | vier Knöpfe passten am Handy nicht | r2-rg-recon-actions, r2-rg-rep-cards |
| 2026-10-11 | 0.751.0 | Dialogtitel kurz, lange Namen als Unterzeile | dreizeiliger Titel am Handy | r2-rp-mode-title |
| 2026-10-11 | 0.753.0 | `matListItemMeta` auf einem Container, nie auf einem Text-Knopf | „Lösen“ kaum lesbar, Symbol und Text versetzt | d1-rk-revenge-loesen-knopf-kaputt |
| 2026-10-11 | 0.751.0 | Abzeichen nur für gesetzte Werte; keine internen Dateinamen; Spalten ohne Inhalt weglassen | Scheininformation | r2-rp-taktik-chip, r2-rp-sourcefile, r2-t-title-empty |
| 2026-10-11 | 0.751.2 | „Mehr“ als Textknopf am Satzende; ⋮ bleibt am Handy in der Knopfzeile; Abstand zwischen Sperrkarten-Knöpfen; Leerzeichen um `@if` fest im Markup | Einzelwörter brachen allein in eine Zeile, „Spielennoch offen“ | Nachprüfung Runde 3 |
| 2026-10-11 | 0.751.2 | Diese Datei angelegt; Gestaltungsentscheidungen werden ab jetzt hier festgehalten | Wunsch des Nutzers: eine Stelle für „wie Seiten designt sind“ | — |
| 2026-10-11 | 0.752.0 | Aufräumrunde nach Abschnitt 10 über alle Oberflächen: Zustandsfarben → `--rh-*`/`--mat-sys-*` (Indigo `#3f51b5` entfernt, theme-color Azure `#005cbb`), globale Klasse `.empty-state`, Primärknöpfe nur `mat-flat-button`, Breakpoints auf 600/768/900 (`599.98px`-Form; `SolveModeService.narrow()` angeglichen), Klammer-Mehrzahl → `…One`-Schlüssel, Symbolknöpfe `appIconLabel`, Seitenkopf `.header`, globale Löschfarbe `.mat-mdc-menu-item.delete-item mat-icon`. Bewusst geblieben: Brett-/Marken-/Kategorie-/Diagrammfarben, KidHub-Palette, LH/CH-eigene Variablen, Karten-Pin-Palette, laufzeitfarbiger „Weiter“-Knopf | Abweichungen aus Abschnitt 10 abbauen (Wunsch „räume gezielt auf“) | Aufräumrunde 11.10. |
| 2026-10-11 | 0.752.3 | Knopf-Variablen folgen der Knopfart: `mat-flat-button` → `--mat-button-filled-*` (nicht `protected-*`); KidHub-Masken als zentrierte Spalte oben; Symbol/Kreisel im Knopf als EIN Wurzelelement je Zweig, Text außerhalb | Aufräumrunde 0.752.0 hatte raised→flat getauscht, die Variablen aber nicht — KidHub-Knopf verlor Farbe/Form; Symbol rutschte unter die Textlinie | d1-k-kid-primary-button-restyled, d1-k-kid-auth-card-left-wide, d1-k-kid-auth-pills-offset, d1-rk-update-all-icon-misaligned |
| 2026-10-11 | 0.753.0 | Gefahrknopf `mat-flat-button color="warn"` global in `--mat-sys-error`/`--mat-sys-on-error` (Hubs setzen die Rollen auf der Karte, Link-Unterstreichung nur `a:not(.mat-mdc-button-base)`); LeagueHub-Token `--on-red` (dunkel #0f141b) statt festem Weiß auf Rot, ClubHub `--bad-ink` + Knopfform wie `.btn` auf Rechtsseiten; KidHub-Rechtsseiten umfassen „Konto löschen“ (Kinderkarte, „← Start“, Breite 840, Löschknopf groß in `--kid-bad-fg`, Fachbegriffe für Eltern zugeklappt hinter einem Kindersatz) | Löschknopf sah überall wie die Hauptaktion aus; Weiß auf #ff4d63 nur 3,2:1; KidHub-Seite im RookHub-Look voller Fachjargon | d1-x-account-delete-warn-ignored, d1-l-mask-red-contrast, d1-k-account-deletion-style, d1-k-account-deletion-jargon |
| 2026-10-11 | 0.753.0 | Neuer Abschnitt 4.6 „Erster Blick und Scrollen“: Hauptsache je Seite über der Falz in Handy/Laptop/groß, Vorlauf ≤ 40 %, ≤ 1 Banner, Erklärtext ≤ 3 Zeilen, großer Bildschirm nutzt den Platz, klebende Leisten ≤ 64/72 px, Seitenlänge 6/12 Bildschirme; UI-Sweep misst das und liefert „Erster Blick“-Bilder und Streifen | Nutzerwunsch: das Wichtigste sofort sichtbar, auch nach dem Scrollen gutes Design | — |
| 2026-10-11 | 0.755.0 | Hauptsache vor Verwaltung: Admin-Bereiche (Katalog-Freigaben, Push-Einstellungen) unter der Liste und zugeklappt mit Zählern in der Kopfzeile; zweiter Hinweis als Satz ins Aufbereitungs-Banner (`app-reprocess-banner` nimmt Inhalt an); Erklärtext hinter ⓘ am Abschnittskopf bzw. „Mehr“; Brett und Lernkarte wachsen am großen Bildschirm mit der Fensterhöhe (`max(<bisher>, 100vh − X)`, gedeckelt), am Handy steht das Brett vor der Linienliste | Vorlauf > 40 %, zwei Banner, schmale Spalten am großen Bildschirm | d2-rk-catalog-freigaben-zuerst, d2-rk-courses-zwei-banner, d2-rk-rep-list-lead, d2-rk-notif-push-card, d2-rk-browse-*, d2-rk-flashcards-gross-schmal |
| 2026-10-11 | 0.755.0 | LeagueHub: Erklärtexte über der Hauptsache als ein Satz + ⓘ (`.chk-btn` + `.info-panel`, ⓘ nie in einem `label`); seltene Formulare hinter „+ …“; Prognosen-Auswahl am Handy als Zusammenfassungszeile mit „Ändern ▾“; Werbesatz am Handy nur abgemeldet (`.lede.anon`); lange Listen in Seiten zu 20 mit „Weitere N von M“ + Suchfeld; `.rtable thead th` klebend; `wide`/`mid` an Kopf, Reitern, Inhalt und Fuß gemeinsam; `.wrap.mid` ab 1280 px zweispaltig | Falz-Durchsicht: Vorlauf bis 92 %, `/konten` 96 Bildschirme, Kanten sprangen | d2-l-* |
| 2026-10-11 | 0.755.0 | Erster Blick (4.6) in RookHub: Brettspalte klebt ab 769 px und richtet sich nach der Fensterhöhe (`min(…, calc(100vh − X), 820px)`); seltene Verwaltung (Upload, Themen, Teilen/QR, Angaben am Handy) als zugeklappte Zeile mit Knopf statt offener Karte vor der Hauptsache; angefangene Durchläufe als „Weiterspielen“ oben; Erklärtext ein Satz, Rest im ⓘ am Titel | Hauptsache unter der Falz (Vorlauf bis 105 %), großer Bildschirm blieb leer | d2-rk-analysis-brettspalte, d2-rk-weekly-upload, d2-rk-ws-detail-lead, d2-rk-guess-durchlaeufe-unten u. a. |
| 2026-10-11 | 0.755.0 | Rechtsseiten: gemeinsame `LEGAL_PAGE_STYLES` (Karte 760 px, Fließtext ≤ 68ch außer KidHub, Handy 12 px Rand/Polster, 1rem Einzug; ClubHub-Karte 660 px); lange Verzeichnisse am Handy als `<details>` zugeklappt (Datenschutz bis 768 px, Hilfe bis 1279 px, ab 1280 klebende Spalte), Hilfe-Abschnitte zugeklappt mit Vorschauzeile und `hidden="until-found"`; ein klebender „↑ Inhalt“-Knopf statt Rücklinks je Abschnitt; Anmeldemaske höchstens ein Hinweiskasten; Hub-Masken-Hauptknopf 44 px/16 px; ClubHub-Masken ohne 80vh-Zentrierung | Falz-Durchsicht: Hilfe 16 Bildschirme, Verzeichnisse füllten den ersten Bildschirm, Zeilen 95–115 Zeichen | d2-rk-help-*, d2-rk-legal-zeilenlaenge, d2-t-privacy-toc-mobile, d2-l-datenschutz-lang, d2-c-* (Masken) |
| 2026-10-11 | 0.755.0 | Turnier/ClubHub/KidHub: lange Spielerlisten in Portionen zu 50 mit „Weitere 50 anzeigen“; Tabellenköpfe kleben (Wrapper ab 769 px ohne `overflow-x`, mat-tab-Hülle `overflow: clip`); Handy-Filter als ein Knopf mit Menü und Chips für abweichende Werte; Kopf als eine Zeile; ClubHub-Werbezeile nur ohne Club-Zugang; KidHub-Erklärkarte am Handy drei Zeilen mit „Weiterlesen“ und „Schließen“ | Falz-Durchsicht: 222 Spieler in einem Zug, Filter füllten den Handy-Bildschirm | d2-t-*, d2-c-*, d2-k-* |
| 2026-10-12 | 0.757.0 | LeagueHub (z1): Auswahl-Filter am Handy starten immer zugeklappt; Steuerzeilen über der Hauptsache zu einer leisen Zeile zusammengelegt; klebende Tabellenköpfe ziehen ihre Unterlinie als `box-shadow: inset 0 -1px 0` mit; Farbe des Gegners als rundes Steinchen mit einmaliger Legende statt Kästchen; Feldhinweise (`.field .small`) in Textschrift 400; nackte Icon-Knöpfe erst ≤ 380 px (darüber Kurzbeschriftung) | Nachprüfung Zyklus 1: Fixes wirkten nur halb | z1-l-* |
| 2026-10-12 | 0.757.0 | Turnier/ClubHub/KidHub (z1): Knöpfe und Zähler am Handy mit Kurztext („+ Kind“, „8/287“, „5 von 10“), Langtext in aria-label bzw. nur visuell versteckt; Hinweis ohne Konto bzw. ohne Suchprofil als „· Textknopf ⓘ“ hinter der Trefferzahl statt als Kasten; gesperrte Grafik mit fester Farbe ≥ 3:1 statt Deckkraft | Nachprüfung Zyklus 1 | z1-t-*, z1-c-*, z1-k-* |
| 2026-10-12 | 0.757.0 | RookHub (z1, Partien/Prep): Seitenspalte neben großem Brett nie unter ~440 px (Seite ab 1600 px bis 1680 px breit); Liste mit nur einem Reiter zeigt keine Reiterleiste; Suche, Filter und Sortieren (⇅ mit Menü) in einer Zeile über der Liste; Auswahl-Parameter einer Aktion als Umschaltgruppe, die nur wählt, nicht auslöst; Spielervorbereitung: Bedienzeile umbrechend, am Handy nur die Farbwahl offen, Rest hinter „Filter ▾“; Ladestand als leise Zeile statt Kasten | Nachprüfung Zyklus 1 | z1-rk-analysis-*, z1-rk-guess-*, z1-rk-prep-*, z1-rk-recon-*, z1-rk-edit-*, z1-rk-scoresheet-grund |
| 2026-10-12 | 0.757.0 | RookHub (z1, Lernen/Statistik): Abschnitts-Unterzeilen in Listen stehen im ⓘ am Abschnittskopf; am Handy werden beschriftete Kopfknöpfe zu Symbolknöpfen mit aria-label in der Titelzeile; Karten einer Reihe: Inhalt als Flex-Spalte, Fortschritt `margin-top: auto`, Fuß mit Mindesthöhe; Seitenkästen neben dem Brett nur so hoch wie ihr Inhalt, Brettgröße `clamp(400px, 100vh − 300px, 640px)`; Handy-Regeln stehen immer NACH den Grundregeln | Nachprüfung Zyklus 1 | z1-rk-courses-*, z1-rk-stats-*, z1-rk-rep-*, z1-rk-endless-* u. a. |
| 2026-10-12 | 0.757.0 | Masken/Recht (z1): Rechtsseiten-Karte = Textspalte (68ch + 32 px), Fließtext Zeilenhöhe 1,55 mit Abstand zwischen Punkten; Datenschutz-Verzeichnis zweispaltig, nur ab 769 × 900 offen; „↑ Inhalt“ als rundes 44-px-Symbol hinter dem letzten Abschnitt mit eigenem Freiraum, ab 1200 px neben der Karte; klebende, in sich scrollende Verzeichnisse blenden unten aus (`.more`), wenn mehr darunter liegt; Rücklink ohne Verlauf = Startseite (abgemeldet `GUEST_START_URL`, nur LeagueHub/ClubHub `/login`); LeagueHub-Fokus in `--focus` statt Akzentrot; ClubHub-Maskenknopf 44 px/16 px fett; Turnier-Masken am Handy ohne 80vh-Zentrierung; KidHub-Rechtsseiten 620 px | Nachprüfung Zyklus 1: „↑ Inhalt“ verdeckte Text, Verzeichnis offen am Laptop | z1-rk-privacy-*, z1-rk-help-toc-verborgen, z1-rk-legal-back-anon, z1-t-*, z1-c-login-*, z1-l-masken-fokus-rot, z1-k-legal-line |
| 2026-10-12 | 0.758.2 | LeagueHub-Datenschutz am Handy: Abschnitte ab dem dritten als Aufklapper (vom User freigegeben) — unter 600 px `<details>` mit der Abschnittsüberschrift als `<summary>` (≥ 44 px, ▸-Pfeil in der Akzentfarbe), Verantwortlicher, Einleitung und Liga-Abschnitt offen; Verzeichnis-Sprung und `#anker` klappen das Ziel vor dem Scrollen auf, Browser-Suche öffnet zugeklappte Abschnitte, Druck alles offen; breiter und in den anderen Oberflächen unverändert offen. Rechtsseiten-Textspalte 33em statt 68ch (≤ ~75 Zeichen; ClubHub-Karte = Textspalte + Polster); „↑ Inhalt“ über dem Text (unter 1200 px) nur beim Hochscrollen | Nachprüfung Zyklus 2: Datenschutz 12,2 Handy-Bildschirme, „↑“ verdeckte Wortenden, Zeilen noch 80–85 Zeichen | z2-l-datenschutz-laenge, z2-rk-privacy-knopf-handy, z2-rk-legal-zeilenlaenge |
| 2026-10-12 | 0.758.2 | LeagueHub (z2): Konto-Vorschläge ab 1280 px zwei Spieler je Zeile (mehrere Vorschläge = volle Breite); Vereinspartien breit ohne eigene Zählerzeile (Anzahl/PGN neben h2, „Partien hinzufügen“ in der Suchzeile) und Handy-Karte mit Meta + Aktionen in einer Zeile; Übertragungen am Handy „Zeitraum · Ort“ als Meta-Zeile; Hinweiskästen der Masken in `--focus` statt Rot — Hinweiskasten nie in der Fehlerfarbe | Nachprüfung Zyklus 2 | z2-l-* |
| 2026-10-12 | 0.758.2 | RookHub (z2, Partien/Prep/Rekonstruktion): Am Handy gehören Werkzeuge (Export, Baum, Training) hinter ⋮, nicht hinter „Filter ▾“; ein Filter-Umschalter darf am Handy als Häkchen ins ⇅-Menü wandern, damit Suche + ⇅ eine Zeile bleiben; Kurzzeilen trennen kürzbaren Text (Namen, „…“) vom festen Teil (Ergebnis); Etikett + Auswahl in einer gemeinsamen Hülle; Anmeldemaske zeigt nach einer Umleitung nur die Kurzform der Einleitung (`AuthIntro.loginShort`) | Nachprüfung Zyklus 2 | z2-rk-guess-*, z2-rk-prep-*, z2-rk-recon-*, z2-rk-edit-*, z2-rk-turnier-login-hinweis |
| 2026-10-12 | 0.758.2 | KidHub/ClubHub/Turnier (z2): KidHub-Startseite und Karten wachsen am großen Bildschirm mit (`KID_HOME_WIDTH`, `KID_MAP_WIDTH`), Anmelde-Masken 432 px (`KID_AUTH_WIDTH`); ClubHub-Hülle als Spalte über 100dvh (Fußzeile unten auf kurzen Seiten), Speichern-Leiste ≈ 65 px in allen Breiten, Gruppenchips am Handy einzeilig wischbar; Turnierseite: gemeinsamer Kopf `_tournament-head.scss`, Zahlenspalten rechtsbündig mit gleich breiten Ziffern, gleichnamige Turniere direkt hintereinander („1 von 2 gleichen Namens“) | Nachprüfung Zyklus 2 | z2-k-*, z2-c-*, z2-t-* |
| 2026-10-12 | 0.758.2 | RookHub (z2, Lernen/Statistik): Banner sichtbar nur Kurzsatz, Langtext im ⓘ, Knopf als Textknopf rechts in der ersten Zeile, kein Umbruch (5.11); Listensuche ab 600 px in der Kopfzeile, Erklärsatz immer im ⓘ (5.1); Hauptknopf 381–599 px mit Kurztext, nur ≤ 380 px Symbol (5.4); gefährliche Zeilenaktion (Freund entfernen) ins ⋮ (5.5); Brettkoordinaten mit Lichthof in Gegenfarbe (5.12); Hilfe-Verzeichnis ab 1280 px ohne Aufklapper, aktueller Abschnitt per aria-current | Nachprüfung Zyklus 2 | z2-rk-courses-*, z2-rk-stats-*, z2-rk-help-*, z2-rk-brett-koordinaten u. a. |
| 2026-10-12 | 0.758.3 | LeagueHub (z3): Werbesatz im Kopf nur für Abgemeldete (alle Breiten); alle Reiter-Seiten in derselben Inhaltsbreite, schmale Formulare darin per max-width; Raster mit gemischt breiten Karten `grid-auto-flow: dense`; Rechtsseiten-Karte endet mit der Textspalte; Links unter dem Hauptknopf grau statt Fehlerrot; Formularfelder im festen Takt (gap 12 px, Fehlerzeile `dynamic`) | Nachprüfung Zyklus 3 | z3-l-* |
| 2026-10-12 | 0.758.3 | Club/Kid/Turnier (z3): Kopf von Zahlenspalten mit Pfeil vorne rechtsbündig über `justify-content: flex-start` (Kopf ist `row-reverse`); Symbol neben umbrechendem Text: `align-items: flex-start`, Symbol `flex: 0 0 auto`; zugeklappter Text auf ganze Zeilen gerundet (`round(down, …)`); zusammengehörige Teile einer Unterzeile als unbrechbare Stücke + `text-wrap: balance`; Fußzeilen-Links als Gruppe, die geschlossen umbricht | Nachprüfung Zyklus 3 | z3-c-*, z3-k-*, z3-t-* |
| 2026-10-12 | 0.758.3 | RookHub (z3): Banner am Handy: Kurzsatz über die volle Breite, Aktionsknopf rechts in einer flachen zweiten Zeile (nie als feste Spalte neben dem Text); Zusatzhinweise im Banner nur als Chip + Zahl + ⓘ; unbeschriftete Symbolknöpfe am Handy ins ⋮-Menü mit ausgeschriebenen Punkten; /stats nutzt die gemeinsame Seitenbreite (4.1) | Nachprüfung Zyklus 3 | z3-rk-* |
| 2026-10-12 | 0.759.0 | RookHub (v1, Repertoire): im Trainer-Lernmodus steht der Zugkommentar nur einmal (Kontextkarte), unter „Diesen Zug einprägen“ nur noch der Weiter-Hinweis; PGN-Platzhalter („?“, „-“) erscheinen auch im Trainer nicht als Linienname; Zahlenfelder in Werkzeugleisten (Lochfinder-Grenzwert) als outline-Formularfeld mit Einheit als Suffix, nie als Roh-Eingabe; Seitenaktionen im Kopf stehen rechts bei der Moduswahl, nicht als zusätzliche Zeile im Titelstapel (Kopf in allen Modi gleich hoch) | Varianten-Durchsicht v1 | v1-rk-trainer-learn-comment-doppelt, v1-rk-trainer-learn-platzhalter-fragezeichen, v1-rk-holes-grenzwert-eingabe-unstyled, v1-rk-repertoire-abfragen-knopf-wandert |
| 2026-10-12 | 0.759.0 | RookHub (v1, Kurse): Zugleiste der Kurs-Durchsicht über `SanPipe` und ohne eigenen Scrollkasten (bricht ganz um); beim Import angeklebte Kapitelüberschriften im Kurskommentar werden wie in KidHub abgesetzt und fett gezeigt (`shared/course-text.ts`, nur Kommentare ohne eigene Umbrüche); Kursbeschreibung über 180 Zeichen bzw. 3 Zeilen zugeklappt mit „Mehr“/„Weniger“ als Textknopf im Satz; Schlagwörter als einzelne Abzeichen (technisches „personal“ ausgeblendet); leerer Flashcard-Stapel als `.empty-state` mit einem Knopf „Linien durchsehen“, ohne Bedienhinweis und Werkzeuge; „1 Puzzle“ per Schlüsselpaar `puzzleCount`/`puzzleCountOne` (Kurskarte, Wochenpost) | UI-Sweep v1 | v1-rk-browse-zugliste-*, v1-rk-course-comment-ueberschrift-angeklebt, v1-rk-kursseite-*, v1-rk-flashcards-marked-leer, v1-rk-falsche-mehrzahl-puzzles |
| 2026-10-12 | 0.759.0 | RookHub (v1, Partien/Analyse): mehrzeiliger Spalten-Flex am Handy immer `flex-wrap: nowrap` (sonst wird die Spalte so breit wie ihr breitester Inhalt und die Seite scrollt waagerecht); Partieseite am Handy: Kopfaktionen in einer Zeile, „Originalpartie öffnen“/„Teilen-Link kopieren“ im ⋮, „Kurz erzählt“ höchstens drei Zeilen mit Antippen = ganz; gespeicherte Partien: Filter am Handy hinter „Filter ▾“ mit Chips für gesetzte Werte, Kopfknöpfe mit Kurztext; Analyse-Titel ohne PGN-Platzhalter („Ohne Titel“, „ohne Name“); „1 Variante“ per Schlüsselpaar; Züge in Partie-Analyse und „Züge vergleichen“ über `SanPipe`/`localizeSanText`; Grund unter gesperrtem „Analyse starten“; Zahl + % mit geschütztem Leerzeichen (5.13) | UI-Sweep v1 | v1-rk-analysis-card-overflow-mobile, v1-rk-first-view-lead-long-mobile, v1-rk-games-mobile-fold-lead-filters-open, v1-rk-analysis-pgn-placeholder-title, v1-rk-plural-1-varianten, v1-rk-san-english-in-de-ui, v1-rk-analysis-jobs-disabled-no-reason, v1-rk-analysis-jobs-percent-wrap |
| 2026-10-12 | 0.759.2 | RookHub (v1-Nachrunde): Listen-Karten-Kopf zweiteilig (Text schrumpft/bricht um, Knöpfe `flex: none` rechts oben); Repertoire-Kopf am Handy: Abfragen als Symbolknopf neben der Moduswahl, beide rechtsbündig in einer Zeile; zugeklappte Texte („Mehr“ im Satz) gemessen auf 3 Zeilen gekürzt (`ClampMoreDirective`) statt fester Zeichenzahl oder `line-clamp`, gilt für Kursbeschreibung und „Kurz erzählt“/Roast — dort jetzt mit sichtbarem „Mehr“/„Weniger“; generische Beschriftung `common.showMore`/`common.showLess` (ersetzt `courses.detail.descMore/descLess`, `courses.reimport.more/less`, `chessable.useExtension.more/less`, `puzzles.bar.more`); Engine-Linien der Auswertung und der Live-Engine über `SanPipe` (5.14) | Nachprüfung v1 | v1-rk-analysis-jobs-percent-wrap, v1-rk-repertoire-abfragen-knopf-wandert, v1-rk-kursseite-beschreibung-vorlauf-handy, Nebenbefunde Engine-Linien englisch / „Kurz erzählt“ ohne „Mehr“ |
| 2026-10-12 | 0.760.0 | RookHub (Analysebrett): „Sparring gegen Maia“ nicht mehr als feste Karte, sondern über das ⋮ der Stellung einblenden, ✕ blendet aus, während eines Sparrings immer sichtbar (5.5); Eröffnungs-Explorer standardmäßig eingeklappt, gemerkter Aufklapp-Zustand je Gerät bleibt (5.12) | Nutzerwunsch: Analysebrett zu voll | Nutzerwunsch 2026-10-12 |
| 2026-10-13 | 0.761.0 | RookHub/Turnier/KidHub (e1): Datum/Uhrzeit nur über Locale-Formate der DatePipe statt fester `dd.MM`/`HH:mm`-Muster (Wochenpost `EEE, mediumDate · shortTime`, Listen `short`, 5.13); Turnierkarte „1 Teilnehmer“ per Schlüsselpaar `tournamentDirectory.players`/`playersOne`; KidHub-Start: Weiter-Knopf zweiteilig (`kids.home.continue` + `kids.home.puzzleN`, Trenner als `::before`, `continueAt` entfällt), Puzzles-Kachel mit `&ngsp;` und „· “ als `::before` vor den Sternen | UI-Sweep e1 | e1-rk-date-fixed-pattern, e1-t-plural-1-players, e1-k-start-continue-dot-wrap, e1-k-start-levels-done-stars-glued |
| 2026-10-13 | 0.761.0 | RookHub (e1): Spielerkarte der Spielervorbereitung zweisprachig — Texte `playerCard.*` (en/de/hr/hu), Züge in den Figurenbuchstaben der Sprache, Datum `mediumDate`, Prozent nach Sprache (en/hu „39%“, de/hr „39 %“); LeagueHub bleibt deutsch (Abschnitt 6); Maia-Stärkefeld mindestens 128 px; Partie korrigieren am PC: Runde mindestens 88 px (Ort 1fr); Segmente „Weiß | Schwarz | Automatisch“ alle groß | UI-Sweep e1 | e1-rk-prep-card-german, e1-rk-maia-strength-cut, e1-rk-edit-round-cut, e1-rk-scoresheet-side-auto-case |
| 2026-10-13 | 0.761.0 | RookHub/Turnier (e1, Sprache): Prozent zentral über `formatPct`/`PctPipe` (`shared/format/pct.ts`) — en/hu „46%“, de/hr „46 %“ (5.13; Explorer, Zugbaum, Genauigkeit, Partien, Statistik, Freunde, Wochenpost); en.json-Anführungszeichen einheitlich “…” (6); Sprachnamen im Satz in der Oberflächensprache, Eigenbezeichnung nur in Sprachauswahlen; Hilfe-Sätze „Your games are under …“/„In … Catalog, you see …“; Datenschutz „/motivation, option “aus” (off)“ | UI-Sweep e1 | e1-rk-percent-space-en, e1-rk-en-anfuehrungszeichen-gemischt, e1-rk-kurs-originalsprache-eigenname, e1-rk-hilfe-partien-satz, e1-rk-datenschutz-motivation-aus |
| 2026-10-13 | 0.762.0 | RookHub/Turnier (u1): Breitbild-Stufe ≥ 2400 px mit Token `--page-max-width-wide` (1880 px) nur für Raster-, Tabellen- und Brettseiten, opt-in in `@media (min-width: 2400px)`; Lese-/Formularseiten bleiben (4.1/4.2); Dashboard 5 Kachelspalten, Kurse 6 Spalten, Statistik zweispaltig (Themen-Paar rechts); Kopfleiste randlos, Inhalt auf 2400 px Mitte; Ladestand der Spielervorbereitung „seit …“ als Datum der Sprache; en-Hilfe ohne „Under ☰ → … you …“ | UI-Sweep u1 (ultrawide) + Englisch-Nachprüfung | u1-x-breite-stufe, u1-rk-dashboard-5spalten, u1-rk-navbar-raender, u1-rk-stats-2spalten |
| 2026-10-13 | 0.762.0 | RookHub (u1, Bretter): Brettseiten wachsen ab 2400 px nur bis zur Fensterhöhe — Analyse drei Spalten (Brett ≤ 1 100 px | Engine + Züge | Explorer offen + Repertoires + FEN/PGN, Seite ≤ 2 560 px; offener Explorer dort nur für die Sitzung, der gemerkte Zustand für schmale Fenster bleibt), Partie-Ansicht vier Spalten (Brett ≤ 1 080 px, Zugliste 280 px, Auswertung zweispaltig je 400–520 px), Repertoire im Token `--page-max-width-wide` mit Brett ≤ 1 040 px; Breitbild-Abfrage im Code über `mediaSignal()` (`shared/layout/wide-media.ts`); Raster mit auto-Spalten neben einer gedeckelten Spalte immer `justify-content: center` (Kalkulations-Modus: kein Loch zwischen Brett und Seitenspalte, alle Breiten) | UI-Sweep u1 (Ultrawide) | u1-rk-analyse-3spalten, u1-rk-partie-brett, u1-rk-repertoire-brett, u1-rk-calc-luecke |
| 2026-10-13 | 0.762.0 | Turnier/LeagueHub (u1, Hubs): Turnierkalender ab 2400 px Liste + Karte nebeneinander (Reiter „Liste & Karte“, Karte sticky); Turnier-Detail und `/t/:id` ab 2400 px Paarungen rechts neben Spielern/Teams (eigener Scroll-Kasten), Seite im Token; LeagueHub-Fußzeile in allen Breiten am Fensterende (Flex-Spalte wie ClubHub); LeagueHub ab 2400 px mittlere Seiten 1 880 px, Bretter vierspaltig (8.2/8.4) | UI-Sweep u1 (Ultrawide) | u1-t-kalender-karte, u1-t-turnier-paarungen, u1-l-fuss-schwebt, u1-l-prognose-bretter |
| 2026-10-13 | 0.762.2 | RookHub/Turnier (u1-Nachprüfung, alles-voll-2): Kopfleiste ab 2400 px auf die Kante des 1 880-px-Inhalts statt auf 2 400 px; Turnierkalender „Liste & Karte“: Reiter-Hülle `overflow-x: clip` (kein waagrechtes Scrollen mehr, sticky bleibt), Karte `100vh - 16rem` (samt Legende im Fenster); Repertoireliste ab 2400 px Kartenspalten ≥ 420 px; Statistik ab 2400 px: Umschalter nicht gestreckt, ohne Themen-Paar alles volle Breite; Paarungen ohne Daten: Satz mit Grund statt leerem Tabellenkopf (`tournaments.pairings.none`) | UI-Sweep alles-voll-2 (Selbstprüfung u1) | u1-rk-navbar-raender, u1-t-kalender-karte, u1-x-breite-stufe, u1-rk-stats-2spalten, u1-t-turnier-paarungen |
| 2026-10-13 | 0.763.0 | Turnier (a1): Favoriten-Stern leer in `--mat-sys-on-surface-variant`, gesetzt Bernstein mit der Textfarbe gemischt (≥ 3:1 hell und dunkel); Startliste: Sternspalte 40 px, Namen einzeilig mit `title`; Kopf 769–1023 px „Zu RookHub“ nur Symbol, Spacer ≥ 24 px; Passwort-Masken ohne „Anmelden“ im Kopf; Turnierdetail 600–899 px „Melden“ in Kurzform; Quell-Link im Primär; „Spieler verfolgen“ immer rechts; Admin-Nutzerliste am Handy Name allein in Zeile 1; Fußzeile 60 % statt 47 % (≈ 5,7:1), Hover voll deckend, Impressum + Datenschutz am Handy als gemeinsame Zeile | Kontrast < 3:1 bzw. < 4,5:1, springende Zeilen/Knöpfe, randvolle Kopfleiste | UI-Sweep a1: a1-t-fav-stern-hell, a1-t-namen-umbruch-tablet, a1-t-navbar-tablet-en, a1-t-reset-kopf-anmelden, a1-t-melden-allein-tablet, a1-t-quelle-link-standardfarbe, a1-t-verlauf-verfolgen-ort, a1-t-admin-handy-knopf, a1-t-fuss-kontrast, a1-t-fuss-rechtslinks-en |
| 2026-10-13 | 0.763.0 | LeagueHub (a1): Breitbild ≥ 2400 px auch für `.wrap.wide` (Formular prüfen/korrigieren, Brett bis Fensterhöhe ≤ 960 px), 6 Prognose-Bretter 3 + 3, Übertragungen-Tabelle 1 208 px; Links ohne Klasse in `--ink`; gesperrte Knöpfe mit festen Farben statt Deckkraft + Grund unter „Partien prüfen“; Kontaktadresse unter „Link senden“ grau; Vereinspartien am Tablet (Container 641–960 px) Namen mit „…“, Eröffnung ≥ 14ch; Vereine-Einleitung ein Absatz mit ⓘ am Satzende; Mehrfach-Kontovorschläge als umrahmte Fläche; Wartezeit des Formulars „1:02:05“ / „seit 1 Tag 18 Std.“, ab 30 min Hinweis, Nachsatz in Textgröße mit Strich als `::before` | UI-Sweep a1 | a1-l-konten-mehrfachgruppe, a1-l-vereine-einleitung-info, a1-l-passwort-mail-link-rot, a1-l-vereinspartien-tablet-eroeffnung, a1-l-prognose-ultrawide-6-bretter, a1-l-formular-wartezeit, a1-l-link-browserfarbe, a1-l-partien-pruefen-gesperrt, a1-l-korrigieren-ultrawide, a1-l-uebertragungen-ultrawide |
| 2026-10-13 | 0.763.0 | RookHub (a1, Partieseite `/g`, `/games/:id`, `/club-games/:id`): „Partie analysieren“ + Grund in eigener Spalte ≤ 280 px (⋮ bleibt in der Knopfzeile, 5.4); 769–1279 px Knöpfe in eigener Zeile unter dem Titel, Meta-Abzeichen `nowrap`; Name + Elo je Spieler als Einheit, „(Elo n)“ ohne Umbruch (5.13); Brett-Abzug `100vw - 400px` (Tablet ≈ 420 statt 380 px); Brettleiste nie breiter als das Brett, Handy-Nebenknöpfe 36 px (5.12); ab 2400 px Auswertungsraster `min-content 1fr` (Meisterpartien direkt unter Repertoires); „Kurz erzählt“ ≤ 75ch je Zeile (4.6 Regel 4); Ladefehler dreigeteilt: 403 Vereinspartie → Sperrkarte mit Klartext + „Zur Startseite“, 4xx → „gibt es nicht“ + Weg zurück, Netz/5xx → `app-load-error` mit „Erneut versuchen“ (5.10/5.11) | ⋮ allein in einer Zeile, Titel Wort für Wort umbrochen, Leiste ragte übers Brett, Sackgasse ohne Grund | UI-Sweep a1: a1-rk-shared-kopf-grund, a1-rk-partie-kopf-tablet, a1-rk-elo-verwaist, a1-rk-partie-tablet-brett, a1-rk-brettleiste-plus, a1-rk-ultrawide-seitenspalten, a1-rk-recap-zeilenlaenge, a1-rk-vereinspartie-fehler-sackgasse |
| 2026-10-13 | 0.763.0 | KidHub/ClubHub (a1): KidHub ab 2400 px Brett bis Fensterhöhe (≤ 1 240 px), Start/Karten 1 880 px mit größeren Kacheln (`--kid-wide-max`); Startkacheln am Tablet 2 × 2; Kurs-Zähler als `.kid-counter`; Masken-Pillenreihe 16 px Abstand nach oben; „Konto löschen“ auch die Anleitung für Eltern zugeklappt, Kindersatz unter der Einleitung (`kids.accountDeletion.parentsHowTo`). ClubHub: gesperrte Knöpfe ohne Deckkraft (gestrichelt, `--muted`), fehlende Eingabe als Hinweis statt Sperre; Trenner „ · “ in Metazeilen; Seitenkopf-Knöpfe neben dem Titelblock; Rechtsseiten-Karte linksbündig; Matrix-Seiten ab 2400 px 1 880 px | Ultrawide ungenutzt, verwaiste Kachel, Kontrast 2,4:1, Vorlauf 42 %, Kanten fluchteten nicht | UI-Sweep a1: a1-k-ultrawide-board-small, a1-k-ultrawide-raster, a1-k-home-tablet-orphan-card, a1-k-course-counter-plain, a1-k-auth-mail-link-button-gap, a1-k-deletion-fachjargon, a1-c-disabled-button-contrast, a1-c-groups-meta-separator, a1-c-fold-lead-group-detail-laptop, a1-c-legal-card-vs-header-width, a1-c-wide-matrix-ultrawide |
| 2026-10-13 | 0.763.0 | RookHub (a1, Analyse): Bewertungsleiste mit Rahmen `--mat-sys-outline`; Brettspalte klebt erst ab 900 px (dort 300 px für die Seitenspalte frei), 769–899 px mittig einspaltig (5.12); Pfeile erst auf ein gemessenes Brett (Analysebrett + `app-chess-board`, ResizeObserver-Arbeit im nächsten Frame); Leerzustand: Hinweiszeile ohne zweites Abblassen (5.10, Aufträge + Favoriten); Partie-Analysen: Einwurf-Formular bei vorhandenen Analysen zugeklappt hinter „Partie einwerfen“ (Hauptknopf im Kopf, im Leerzustand offen, PGN-Feld 3 Zeilen wachsend), Einleitung ein Satz ≤ 75ch + ⓘ, Knöpfe immer rechts über dem Grund, Karten-Meta am Handy eigene linksbündige Zeile; Partie-Analyse und „Züge vergleichen“ ab 2400 px in der 1 880-px-Spalte, Brett bis Fensterhöhe (4.1); Aufträge: 12 px zwischen Kopfknöpfen und Einleitung, en „Analysis board“ | Weißanteil unsichtbar, klebendes 539-px-Brett am Tablet, Konsolenfehler, Kontrast ≈ 2,5:1, Vorlauf 57–92 %, Brett 375–520 px in 3 440 px | UI-Sweep a1: a1-rk-evalbar-hell, a1-rk-analyse-tablet-brett, a1-rk-svg-nan-pfeile, a1-rk-leer-hinweis-blass, a1-rk-partie-analysen-vorlauf, a1-rk-ga-intro-zeile, a1-rk-ga-grund-abseits, a1-rk-ga-meta-umbruch, a1-rk-analyse-unterseiten-ultrawide, a1-rk-jobs-kopf-handy |
| 2026-10-13 | 0.763.0 | RookHub (a1, Partien/Prep/Teilen): `/games` in `--page-max-width` (statt fest 1 040 px) und ab 2400 px 1 880 px, Filter als `mat-select` outline wie `/games/mistakes` (5.8), Leerkarte ein Satz + „PGN hochladen“ (5.10), am Handy 12 px unter der Filterzeile, Einleitung `text-wrap: pretty`; Punktepartie-Liste ab 600 px festes Raster mit „Spielen“ rechts über Titel- und Metazeile (5.2); Punktepartie-Brett, Spielervorbereitung (Blöcke nebeneinander, `auto-fit`) und geteilte Linie/Rekonstruktion ab 2400 px breit bzw. Brett bis Fensterhöhe (4.1); geteilte Linie/Rekonstruktion stapeln unter 900 px (4.2, Brett ≤ Fensterhöhe); `permissionGuard` wie `menuGuard`: Hinweis „nicht freigeschaltet“ bzw. Anmeldung mit Rücksprung (5.11); Rekonstruktions-Kartentitel in Textfarbe, Akzent bei Hover/Fokus (3.1); Partieformular: Grund des gesperrten Knopfs in eigener Zeile darunter (5.4); geteilte Linie: Datum `nowrap`, Trenner „ · “ als `::before`, am Handy „Name · Datum“ einzeilig mit „…“ (5.13) | feste 1 040-px-Spalte, native Auswahl, doppelter Leertext, springender Knopf, stille Umleitung, Browser-Linkfarbe, ineinanderlaufende Sätze, Brett 360–680 px | UI-Sweep a1: a1-rk-games-ultrawide-schmal, a1-rk-games-native-auswahl, a1-rk-games-leer-zwei-saetze, a1-rk-games-handy-filter-abstand, a1-rk-guess-tablet-spielen, a1-rk-guess-brett-ultrawide, a1-rk-prep-ohne-recht-stumm, a1-rk-recon-titel-linkfarbe, a1-rk-geteilt-brett-ultrawide, a1-rk-geteilt-recon-tablet, a1-rk-prep-ultrawide, a1-rk-scoresheet-grund-trenner, a1-rk-geteilte-linie-datum-handy |
| 2026-10-13 | 0.763.0 | RookHub (a1, Kurse): Linien durchsehen ab 2400 px im Token `--page-max-width-wide`, Brett/Zugleiste/Kommentar bis Fensterhöhe; Brett-Untergrenze 400 px statt 560 px (Laptop: Linienknöpfe über der Falz); Zugnummer + weißer Zug als ein nicht umbrechendes Stück; Kursliste 600–1023 px: Titel links, Knöpfe rechts, Suche volle Breite darunter; Kennzahlen der Kursseite als Subgrid (Werte einer Zeile auf einer Linie, auch bei umbrechender Beschriftung); Fortschrittsspur global aus der Textfarbe (16 %, hell und dunkel sichtbar); „Schwarz am Zug“-Punkt global schwarz mit 2-px-Ring aus der Textfarbe (70 %); Spielweise-Dialog Sekundärtexte direkt `--mat-sys-on-surface-variant`, Kachelname `--mat-sys-on-surface` (nie Prozent von currentColor im Dialog — der ist schon gedimmt) | leere Breite, Knöpfe unter der Falz, Zugnummer allein am Zeilenende, Knöpfe links in eigener Zeile, Werte versetzt, Spur 1,17:1, Punkt ≈ 1,4:1, Dialogtext ≈ 3,4:1 | UI-Sweep a1: a1-rk-browse-breitstufe, a1-rk-zugnummer-allein, a1-rk-browse-laptop-linienknoepfe-falz, a1-rk-kennzahl-beschriftung-umbruch, a1-rk-kurse-tablet-kopf, a1-rk-fortschritt-spur-hell, a1-rk-spielweise-dialog-hell, a1-rk-am-zug-punkt-dunkel |
| 2026-10-13 | 0.763.0 | RookHub (a1, Endlos-Verlauf/Aufgabenblätter): Endlos-Verlauf in `--page-max-width` (statt fest 900 px), ab 2400 px 1 880 px; Datum `short` (5.13), Tabellenzellen `nowrap` mit eigenem `overflow-x`; Leersatz in `--mat-sys-on-surface-variant`, Symbol 45 %; Handy-Kennzahlen 3 + 2 mit Beschriftung 0,75 rem/65 %; Aufgabenblatt bearbeiten: Diagramme 168 px, Tablet 140 px (nie kleiner als Handy), ab 2400 px 220 px in 1 880 px; Grund unter gesperrtem „Stellung hinzufügen“ als `mat-hint` (5.4); Liste am Handy Knöpfe oben rechts in der Titelzeile (5.2); Druckvorschau ≤ 700 px Zugangabe unter der Überschrift, Überschrift `overflow-wrap: break-word`; Löser `/w/…` ab 2400 px Karte 1 100 px, Brett bis 1 000 px/Fensterhöhe; Flashcard-Koordinaten in `currentColor` 70 % statt festem Dunkelbraun | Kontrast 2,1:1 bzw. unsichtbar im Dunkeln, Datum/Köpfe umbrochen, verwaiste Kennzahl, 900-/640-px-Insel in 3 440 px, Diagramme 88–132 px, Knopf ohne Grund gesperrt, Knöpfe in eigener Zeile, „Grundreih / e“ | UI-Sweep a1: a1-rk-endless-verlauf-leer-kontrast, a1-rk-endless-verlauf-tabelle-datum, a1-rk-endless-verlauf-ultrawide, a1-rk-endless-verlauf-handykarte, a1-rk-ws-detail-bretter-klein, a1-rk-ws-hinzufuegen-gesperrt, a1-rk-ws-liste-aktionen-eigene-zeile, a1-rk-ws-druck-titel-umbruch, a1-rk-w-loeser-ultrawide, a1-rk-flashcard-koordinaten-dunkel |
| 2026-10-13 | 0.763.0 | RookHub (a1, Puzzle-Löser): ab 2400 px Seite im Token `--page-max-width-wide`, Brett ohne 820-px-Deckel bis Fensterhöhe (≥ 360 px Seitenspalte bleibt; Standard, Endlos-Spielbrett, Buch/Kurs/Tagespuzzle/Wochenpost); 769–899 px einspaltig mit Brett mittig bis Fensterhöhe (Seitenkarte vorher ~270 px); Kurs-Kommentar ungekürzt, aber als eigener Block hinter Kontext-Card, Status-Card und Aktionsleiste; „Überspringen“ in `--mat-sys-on-surface-variant` statt Deckkraft 0,7; Wochenpost 8 px zwischen Fortschrittszeile und Umschaltknopf; Statistik-Kennzahlen `auto-fit` (3 + 2) mit Ellipse; Teilen + ⋮ als Einheit (5.12); Endlos: Themen-Chips mit `font-family: inherit`, Sitzungszähler 65 % statt 50 %, Verlauf-Link 0,9 rem mit voller mat-button-Tippfläche (statt 28 px) | Brett 816 px in 3 440 px, Bedienung 3,4 Bildschirme unter der Falz, Kontrast 3,35:1, ⋮ allein in einer Zeile, Kennzahlen ragten aus der Karte | UI-Sweep a1: a1-rk-loeser-breitstufe, a1-rk-puzzle-brett-ultrawide, a1-rk-tablet-seitenspalte-eng, a1-rk-kommentar-schiebt-aktionen, a1-rk-skip-kontrast-hell, a1-rk-einfachmodus-knopf-umbruch, a1-rk-wochenpost-fortschritt-klebt, a1-rk-puzzle-statistik-tablet-ueberlauf, a1-rk-puzzle-menue-allein-tablet, a1-rk-endless-chips-schrift, a1-rk-endless-verlauf-link-klein |
| 2026-10-13 | 0.763.0 | RookHub (a1, Repertoire): Bearbeiten ohne 800-px-Deckel, ab 900 px Ablage + Dateiliste zweispaltig; Ablage-Rand `currentColor` 50 % (hell ≈ 3,3:1), Symbol 60 %, Rand beim Darüberziehen in Primär; Brettkoordinaten brown auf dunklen Feldern dunkel mit hellem Hof (≈ 6:1, alle Bretter, 5.12); Brett einspaltig bis 640 px (`min(100%, 640px, 100vh - 480px)`); Seitenpanel mindestens Brett + 56 px hoch, Linienliste mit Weiterscroll-Schatten, einspaltig ohne eigenen Kasten (5.3); Lochfinder: Kopfknopf „Abfragen“ als Rahmenknopf (5.4), Karte vor der Suche so hoch wie ihr Inhalt, am Handy Werkzeug vor dem leeren Brett; Trainer: Brett `100vh - 200px`, einspaltig `min(100%, 100vh - 420px)` + Infospalte volle Breite, ab 2400 px Seite im Token, Brett ≤ 1 100 px bis Fensterhöhe, Seitenspalte ≤ 640 px (4.1); Flashcards: ab 2400 px Lernkarte bis Fensterhöhe (≤ 1 100 px, Digital-Ansicht im Token, Druckvorschau bleibt 1 000 px), Tastaturhinweis bei `pointer: coarse` aus (5.11) | Ultrawide/Tablet ungenutzt, Rahmen ≈ 1,5:1, Koordinaten ≈ 1,5:1, Kasten im Kasten, zwei Primärknöpfe, Vorlauf 71 % | UI-Sweep a1: a1-rk-repertoire-edit-breite, a1-rk-dropzone-rahmen-hell, a1-rk-brett-koordinaten-dunkle-felder, a1-rk-tablet-brett-400, a1-rk-linienliste-scrollkasten, a1-rk-holes-zwei-hauptknoepfe, a1-rk-holes-karte-leer-hoch, a1-rk-holes-handy-brett-vor-werkzeug, a1-rk-trainer-ultrawide-brett, a1-rk-trainer-brett-hoehe, a1-rk-flashcards-ultrawide, a1-rk-flashcards-tastaturhinweis-handy |
| 2026-10-13 | 0.763.0 | RookHub/Turnier (a1, Profil, Dashboard, Admin, Bestenlisten, Statistik, Freundesvergleich, Hilfe, Datenschutz): Profil-Hinweise/Zwischentitel aus der Textfarbe (`color-mix` currentColor 70–80 %, `inherit`) bzw. `--rh-warn`/`--rh-success` statt fester Hellgrau-Werte; Profilformular (geteilt mit der Turnierseite): alle Felder `subscriptSizing="dynamic"`, Abstand einheitlich 16 px, Suchblock per Containerabfrage — < 680 px Knopf (40 px) und Hinweis nebeneinander über volle Breite, < 480 px untereinander mit Hinweis 16 px eingerückt wie `mat-hint`; Admin-Nutzerliste bis 899.98 px als Karten (Einsteigen + ⋮ rechts in der Kopfzeile, Gruppenzeile nur mit Gruppen), Tabelle: Datum/Aktionen `nowrap`; Dashboard: Zähler-Kacheln mit `…One`-Einzahl, Puzzle-Genauigkeit über `formatPct`, Kachelknöpfe ohne Mindestbreite, einzeilig, Zeile bricht um; Bestenlisten 1 / 2 (≥ 600 px) / 4 (≥ 1 100 px) Spalten, Zeile rechts 12 px Polster; Freundesvergleich: Zahlenspalten fest 6rem (Handy 5.5rem), ab 2400 px beide Karten nebeneinander in 1 880 px; Statistik ohne Versuch EINE Leerkarte + „Puzzles lösen“, Level-Hinweis nach Ausweichen auf „Alle“ mit Grund (`stats.levelEmptySwitched`); de: »…« → „…“, Ergänzungsstrich mit U+00A0/U+2060 | Kontrast ≈ 1,2–1,8:1 im hellen Design, springende Feldabstände, Speichern unter der Falz, verdeckte Aktionen am Tablet, „1 Kurse“, „78.6 %“, 3 + 1-Raster, harte Trennung „Abzugsan-griff“, Sackgassen-Leerkarten | UI-Sweep a1: a1-rk-profile-hell-blass, a1-t-profil-feldabstaende, a1-rk-profil-suche-falz, a1-rk-admin-nutzer-schmal, a1-rk-dash-mehrzahl, a1-rk-dash-prozent-komma, a1-rk-dash-play-versatz, a1-rk-dash-puzzles-knoepfe-umbruch, a1-rk-hilfe-guillemets, a1-rk-bestenlisten-raster, a1-rk-bestenlisten-eigene-zeile, a1-rk-datenschutz-ergaenzungsstrich, a1-rk-freundesvergleich-spalte, a1-rk-freundesvergleich-breit, a1-rk-statistik-leer, a1-rk-statistik-level-hinweis |
| 2026-10-13 | 0.763.1 | Nachprüfung a1 (Lauf a1-nachpruef, 116/119 behoben): Partie-Kopf 769–1279 px — steht dort nur das ⋮, bleibt es rechts neben dem Titel statt eigener Zeile; Partien-Leerkarte: 40-px-Symbol nur direkt in der Karte (`> mat-icon`), nicht im Knopf; Bestenlisten ab 1 400 px bis 1 400 px breit (vier Spalten kürzten Namen); Partie-Analysen: Einleitung 12 px unter der Knopfzeile; Kurs-Löser ≥ 2400 px Brett `100vh - 200px`; Repertoire-Trainer einspaltig Brett `100vh - 540px` (Einprägen-Knopf über der Falz); LeagueHub: Strich vor dem Formular-Nachsatz bringt sein Leerzeichen selbst mit (`"\00a0–\00a0"`, Angular entfernt Leerraum), Vereinspartien am Tablet kürzt nur der Name, die Elo bleibt; ClubHub: Metazeilen-Trenner als `::after` am vorigen Element (bei Umbruch am Zeilenende, nie am Zeilenanfang) | Folgefehler der a1-Umsetzung | Nachprüfung a1: neu-rk-partie-kopf-tablet-nur-menue, neu-games-leer-knopf-icon, neu-rk-bestenlisten-namen-gekuerzt, neu-ga-intro-klebt-handy, neu-loeser-breitstufe-scroll, a1-rk-trainer-brett-hoehe, a1-l-formular-wartezeit, neu-l-vereinspartien-tablet-elo-doppelt, neu-c-meta-trenner-zeilenanfang |
