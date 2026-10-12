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
  Symbole **in** Knöpfen behalten die Material-Größe (r2-rg-trainer-icons).
- **Gesperrter Knopf → Grund sichtbar darunter** (leise Zeile), nicht nur im Tooltip (r2-rg-analyze-disabled) —
  am Handy gibt es keinen Hover.
- Kein zweiter gleicher Hauptknopf auf einer Seite (Punktepartie: nur ein „Zur Aufgabe“, 0.750.1).
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

### 5.11 Hinweise, Banner, Sperrkarten

- **Hilfe:** `app-help-hint` (`shared/help-hint/`): 22-px-Knopf, Deckkraft .55 (Fokus/Hover 1), Touch-Trefferfläche
  34 px, Tooltip per Klick fest (`.hh-tooltip`: max. 320 px, `pre-line`, Absätze mit `\n\n`, max. 60vh scrollbar).
- **Banner** (Seitenhinweise): Fläche getönt, Symbol links, Text `flex: 1 1 220px`, ein Knopf rechts, Radius 8 px
  (Vorbild `shared/reprocess-banner/`); Offline-Banner, Impersonation-Banner (`shared/impersonation-banner/`).
- **„Mehr“ steht im Satz:** als Textknopf am Satzende (`aria-expanded`), nicht als eigener Flex-Eintrag — sonst
  steht er am Handy allein in einer Zeile (0.751.2).
- **Lange Texte** werden nicht in einen eigenen Scrollkasten gesperrt, sondern mit „Weiterlesen“ aufgeklappt
  (r2-k-course-scroll).
- **Sperrkarte** (Seite ohne Recht/Konto): **eine** Karte für alle betroffenen Seiten, Klartext statt
  Rechte-Schlüssel, **Weg zurück** (Startseite / Verknüpfen) als Knopfreihe mit Abstand
  (ClubHub `.gate` + `.gate-actions { gap: 8px }`, r2-c-gate-deadend; LeagueHub `.gate .actions`, r2-l-gate-text).

### 5.12 Brett-Komponenten

- Drei Bretter: `PuzzleBoardComponent` (Löser), `AnalysisBoardComponent`, `shared/pgn-viewer/chess-board.component.ts`;
  alle mit Vollbild-Knopf **im** Brett. Neue Brett-Ansichten benutzen eines davon.
- Koordinaten global 11 px (≥ 900 px: 12 px), halbfett, Ränge links oben, Linien unten rechts, Farbe je Feld
  (UX-060, `styles.scss`); KidHub ≈ 2,2 % der Brettbreite (`KID_COORDS`).
- Brettthemen (`board-theme-*`) und Figurensätze (`piece-set-*`) nur über `styles.scss`.
- Tippzonen neben dem Brett: `.board-tap` (kein Markieren, kein Doppeltipp-Zoom); kein Tipp-Aufblinken.
- Hinweiszeile unter dem Löser: bei `(pointer: coarse)` ≥ 44×44 px.
- Stellung markieren: `app-mark-position-button` („+“, markiert grün gefüllt, `aria-pressed`).
- **FEN nie roh anzeigen:** Mini-Brett + „Weiß am Zug · Zug 5“ + Kopierknopf (r2-rg-recon-fen).
- Gleiche Erweiterung in allen drei Puzzle-Modi prüfen (Standard, Endless, Buch/Kurs; `rookhub/CLAUDE.md`).

### 5.13 Zahlen, Mehrzahl, Datum

- **Einzahl/Mehrzahl** über ein Schlüsselpaar `…` / `…One`, gewählt im Template:
  `{{ (n === 1 ? 'reconstruct.gapsOne' : 'reconstruct.gaps') | translate:{ count: n } }}` (14 Paare in
  `public/i18n/de.json`). Ist: 16 Texte noch mit Klammerform („Linie(n)“, „Repertoire(s)“). **Regel ab jetzt:**
  keine Klammerform; beim Anfassen auf das Schlüsselpaar umstellen.
- **Datum/Uhrzeit** in der App-Sprache (Locale-Daten für `en/de/hr/hu` registriert, `core/locale-data.ts`):
  deutsch „16.07.2026, 21:00“; Zeiträume „… – …“ (Halbgeviertstrich, r2-t-date-format); ClubHub TT.MM.JJJJ.
- **Trenner** zwischen Angaben: „ · “; als `::before` des folgenden Elements, damit er beim Umbruch nie allein am
  Zeilenende steht (r2-t-meta-dot). Leerzeichen zwischen Interpolation und `@if` fest ins Markup schreiben —
  Angular entfernt sie sonst („Spielennoch offen“, 0.751.2).
- Ländernamen ausgeschrieben, nicht als Code (LeagueHub, 0.751.0).

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
- Nicht übersetzt: FEN/PGN, Eigennamen, „RookHub“, „Stockfish“, gecrawlte Daten (Züge siehe 5.14).
- **Anrede „du“**, kurze Beschriftungen, keine Fachbegriffe ohne Not; KidHub in einfacher Sprache.
- **Deutsche Anführungszeichen typografisch „…“** (in `de.json` 145 Werte, 5 noch mit geraden). Im deutschen
  Changelog-Text Pflicht: ein gerades `"` beendet den JS-String und bricht den Build.
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
- `index.html` theme-color `#3f51b5`.

### 8.2 Turnierseite (`src-turnier/`)

- Gleiches Material-Thema und gleicher Design-Modus wie RookHub (Cookie geteilt); theme-color `#172345`.
  Navbar `shell/turnier-navbar.component.ts`: ≤ 768 px Kopfknöpfe + ⋮; auf `/login`, `/register` kein „Anmelden“.
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
