# RookHub-Frontends — Gestaltungsregeln

Stand: 2026-11-10 (v0.950.0). Gilt für alle fünf Oberflächen im Angular-Workspace `src/frontend/app`.
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
| `--rh-hint` | #8a5300 | #ffb74d | „mit Hilfe/Tipp“ (Glühbirne im Kursfortschritt) — hell Bernstein-Braun, klar weg vom Fehler-Rot (`--rh-warn` hell #bf360c lag im Ton fast auf `--rh-error`); E-Ink #4d4d4d (0.808.x, d4) |

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

### 3.6 E-Ink-Modus

Für E-Ink-Tablets (Ziel: Boox Tab X C, 13,3", Kaleido 3, Android-Chrome, CSS-Viewport ≈ 960 × 1280 hoch). Gilt in
RookHub und der Turnierseite (beide laden `src/styles.scss` und starten über `applyEinkEarly()` in `main.ts`).

- **Schalter:** Profil → Darstellung (erste Karte), „E-Ink-Modus (dieses Gerät)“. **Je Gerät** in
  `localStorage rookhub_eink` = `'1'` (an) / `'0'` (aus), sofort wirksam. Ohne gespeicherte Wahl: an, wenn
  `matchMedia('(update: slow), (monochrome)')` passt (Boox meldet das meist nicht — darum der Schalter).
- **Mechanik:** `core/eink.ts` (Zustand, früh gesetzt, Klasse `eink` an `<html>` und `<body>`), `core/eink.service.ts`
  (Signal `enabled`, `set()`, `provideEinkMaterial()` in beiden `app.config.ts`), Stile zentral in `src/_eink.scss`
  (am Ende von `styles.scss`). Tests: `core/eink.service.spec.ts`, `core/eink-styles.spec.ts`.
- **Was er tut:** alle `animation`/`transition` aus, `scroll-behavior: auto`; ausdrückliches
  `scrollTo/scrollBy/scrollIntoView({ behavior: 'smooth' })` wird zu `auto` (Wächter in `eink.ts`); Material-Ripples und
  -Animationen aus (`MAT_RIPPLE_GLOBAL_OPTIONS`, `MATERIAL_ANIMATIONS`); **immer hell** (ThemeService ignoriert „dunkel“,
  die Wahl bleibt gespeichert); Text #000 auf #fff, `--mat-sys-on-surface-variant` #333 (≥ 7:1), `--mat-sys-outline`
  schwarz, Karten/Dialoge/Menüs mit 2-px-Rahmen statt Schatten; Fokus 3 px schwarz, aktiv/gewählt
  (`aria-pressed`, `aria-selected`, `aria-current`, gewählter Umschalter) zusätzlich als 3-px-Innenrahmen + fett;
  unbestimmter Ladekreis = stehender gepunkteter Ring, unbestimmte Leiste = volle gestrichelte Leiste. Snackbar-Aktion
  („Schließen“) schwarz, fett, unterstrichen (Material färbt sie sonst blass für eine dunkle Fläche); Wahlkacheln
  (`.sm-choice`, Spielweise-Dialog) mit 2-px-Rahmen auf Weiß; Zugrecht-Zeile unter dem Puzzle-Brett (`.board-hint`) in
  #333 statt doppelt abgeblasst. Diagramme mit Stufen (Aktivitäts-Raster in /stats) als Helligkeitstreppe Weiß →
  #a3a3a3 → #6b6b6b → #333 → Schwarz mit 1-px-Rahmen je Zelle statt Farbtönen; Kurvenlinien schwarz, Fläche weiß mit
  Rahmen; mehrere Kurven (Elo „Level: Alle“) schwarz mit je eigener Strichart, die Legende zeigt dasselbe Muster;
  Balken/Säulen #333 auf weißer, schwarz umrandeter Bahn, „schwach“ zusätzlich schraffiert (d4). Gewählte End-Segmente
  einer Umschaltergruppe übernehmen die Rundung der Gruppe — der 3-px-Innenrahmen folgt der Pille (d4). **Design-Wahl** (☰-Menü der Gäste, Untermenü System/Hell/Dunkel) ist im Modus wirkungslos — das Menü zeigt
  statt ihrer den Hinweis „bleibt immer hell“ (wie die Profilkarte).
- **Brett:** keine Zuganimation (`boardAnimation()` aus `eink.ts` in allen drei Brett-Komponenten); helle Felder weiß,
  dunkle #a3a3a3 **ohne Feldkanten** (das Kachelmuster ist 2 × 2 Felder groß — Kanten wurden am Kachelrand halbiert,
  jede zweite Linie war dicker); letzter Zug / Auswahl als Innenrahmen statt Farbfläche; Zielpunkte schwarz; Pfeile
  volle Deckkraft; Koordinaten schwarz mit weißem Hof. Gilt auch fürs SVG-Brett `app-flashcard-board` (Lernkarten,
  Aufgabenblatt, Rekonstruktion: Felder per Klasse `fb-l`/`fb-d`, 2-px-Rahmen). **Nachbild-Löscher:** bei einer ganz anderen Stellung (> 4
  Felder anders, `isBoardSwitch`) oder beim Drehen legt `flashEinkRefresh()` je 160 ms Schwarz, dann Weiß über das
  Brett — eine Webseite kann am Gerät keine Vollauffrischung anfordern, das Durchfahren beider Extreme ersetzt sie fürs
  Brett. Ein normaler Zug blitzt nicht.
- **E-Book-Leser:** Diagrammzonen ohne Dauerrahmen (auch nicht auf Touch), nur das aktive Diagramm 2 px schwarz
  (PDF `.pv-zone` in `_eink.scss`, EPUB per Klasse `eink` im Kapitel-Dokument); Mausrad/Scrollen bleiben; Pfeil hoch/runter blättert eine Bildschirmhöhe (PDF, `pageBy`), Tippzonen
  am linken/rechten Buchrand (12 %, ≥ 44 px) blättern zurück/vor (EPUB: Seite, PDF: Bildschirmhöhe).
- **Regel für neue UI:** keine Pflicht-Animationen — jeder Zustand muss ohne Übergang verständlich sein (kein Inhalt,
  der erst „einfährt“, keine Rückmeldung nur als Animation/Ripple: Ergebnis als Text oder Zustand). Bedeutung **nie nur
  über Farbe** (zusätzlich Symbol, Text oder Rahmen). Keine Bedeutung in Haarlinien oder hellen Grautönen. Eigene
  JS-Animationen (Zähler, Fortschritt, Bewegung) fragen `isEinkActive()` bzw. `EinkService.enabled()` und springen.

### 3.7 UI-Skalierung (je Gerät)

Für große Tablets (Boox Tab X C: 13,3", CSS-Viewport ≈ 960 px, dort ist alles winzig). Gilt in RookHub und der
Turnierseite (`applyUiScaleEarly()` in beiden `main.ts`, vor dem ersten Bild).

- **Auswahl:** Profil → Darstellung (erste Karte), unter dem E-Ink-Schalter: „UI-Größe (dieses Gerät)“ als
  Segmentgruppe 100 / 125 / 150 / 175 / 200 %. **Je Gerät** in `localStorage rookhub_ui_scale` (Ganzzahl Prozent;
  100 = Schlüssel entfernt), sofort wirksam; scheitert das Speichern → Snackbar „gilt nur bis zum Neuladen“.
- **Mechanik** (`core/ui-scale.ts`, `core/ui-scale.service.ts`, Tests `core/ui-scale.spec.ts`): auf Touch-/Mobilbrowsern
  (`(hover: none) and (pointer: coarse)` oder Android/iOS-UA) Meta-Viewport `width=<Bildschirmbreite ÷ S>,
  initial-scale=S` — der Browser rechnet ein schmaleres Layout und vergrößert es wie beim Aufziehen, alle Treffer und
  Koordinaten bleiben stimmig; nach Drehen neu berechnet. Greift der Meta-Viewport nicht (Desktop, Windows-Tablet),
  CSS `zoom` auf `<html>`; `cg-container` hebt den Zoom dort auf, weil Chessground Figurenlagen aus
  `getBoundingClientRect` als px-`translate()` setzt (sonst doppelt skaliert).
- **Regel für neue UI:** Lagen/Größen aus `getBoundingClientRect` nie als px-Werte zurück in ein Element INNERHALB der
  Seite schreiben — Verhältnisse rechnen (`(clientX − left) / width`) oder `offsetWidth` nehmen. Canvas-Auflösung
  zusätzlich mit `currentUiScale() / 100` multiplizieren, sonst wird gezeichneter Inhalt (PDF-Seiten) unscharf
  hochgezogen — `devicePixelRatio` bleibt bei beiden Wegen gleich.

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
  nebeneinander), geteilte Linie `/l` und Rekonstruktion `/r` (Brett bis Fensterhöhe, ≤ 1 080 px), Puzzle-Löser (Standard, Endlos-Spielbrett, Buch/Kurs/Tagespuzzle/Wochenpost: Brett bis Fensterhöhe, daneben ≥ 360 px Seitenspalte), Rekonstruktion (Liste + Bearbeiten: Brett-Editor bis 900 px, Brett bis Fensterhöhe), Woodpecker (Kartenraster; Löser wie die Puzzle-Löser), E-Book-Bibliothek (Regal; der Leser deckelt „Breite füllen“ auf 1 400 px, Brett-Leiste bis zur Fensterhöhe, höchstens 46vw — eigene Breite per Ziehgriff gewinnt). Listen in der breiten Seite dürfen die Zeile nicht auseinanderziehen: Zahlenspalten wachsen mit, schmale Zeilen laufen zweispaltig (Punktepartie-Liste), Datum/Gegner auf Inhaltsbreite (b1). Sperr-/Fehlerkarten einer breiten Seite bleiben bei `--page-max-width`. **Lese- und Formularseiten bleiben** bei `--page-max-width` bzw. ihrer
  schmaleren Breite. Unter 2400 px ändert sich dadurch nichts (Laptop/Handy/1920 pixelgleich). Bretter wachsen dort
  höchstens bis zur Fensterhöhe (`min(…, calc(100vh - Kopf/Leisten))`). Die Kopfleiste (RookHub + Turnier) bleibt
  randlos, ihr Inhalt rückt ab 2400 px auf die Inhaltskante **der jeweiligen Seite** (Logo-Kante = Inhalt-Kante + 16 px, ☰ ebenso
  rechts; b1): `padding-inline: max(16px, calc((100% - var(--nav-edge, var(--page-max-width-wide, 1880px))) / 2 + 16px))`.
  `--nav-edge` misst die Kopfleiste selbst (`shared/navbar/nav-edge.ts`, RookHub + Turnier): vom Seiten-Host in `<main>`
  abwärts der erste Kasten, der schmaler als `<main>` ist (Seitencontainer mit `max-width`/`fit-content`); nach Navigation,
  Größen- und Inhaltswechsel neu. **Keine Liste je Seite** — neue Seiten fluchten von selbst. Randloser Inhalt oder ein
  Kasten unter 560 px (Ladekreis) → Rückfall `--page-max-width-wide`. Unter 2400 px wird nichts gemessen.
  Randlose Seiten mit eigener Werkzeugleiste (4.5, z. B. E-Book-Leser) tragen `data-nav-edge="full"` am Seitenkasten:
  die Suche endet dort, der Kopfinhalt steht wie die Leiste bei 16 px (c2).
  Kalkulations-Modus ausgenommen. Prüfung: `features/page-width.spec.ts`, `shared/navbar/nav-edge.spec.ts` (iframe 2 560 px).

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
- **Angedockte Kopfleiste** (`body.nav-docked`, alle Breiten, 0.785.x): Seiten mit eigener Werkzeugleiste (E-Book-Leser)
  bekommen EINE Leiste — die Kopfleiste liegt 48 px hoch oben rechts darauf (Regeln in `navbar.component`, Breite als
  `--rh-nav-w`); Vorbild ist der Kalkulations-Modus.
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

*Muster zum Straffen* (Falz-Runde 16.10., 0.806.x):
- **Gleichartige Hinweise zusammenfassen:** trainieren heute mehrere Gruppen, steht EIN Streifen mit einem Knopf je
  Ziel (ClubHub-Kartei), nie ein Streifen je Gruppe.
- **Titel, die den aktiven Reiter wiederholen**, stehen am Handy nur für Vorleser (ClubHub „Kartei“); ihre Zeile nimmt
  Suche + Anlege-Knöpfe auf. **Rücklinks** am Handy als „←“ vor der Überschrift (Ziel im Text für Vorleser, LeagueHub
  Formular-Korrektur), „Speichern ↓“ in die Zeile darunter.
- **Erklärsatz zur Aktion steht bei der Aktion:** unter dem Startknopf (Fehler nachspielen), in der Formularkarte
  (Partie-Analysen), sonst im ⓘ am Titel; der Leerhinweis steht vor dem Formular, nicht darunter.
- **Abstände vor der Hauptsache** 8–12 px statt 16–24 px (Seitenrand oben, Suchzeile, Zählerzeile); Tippziele bleiben
  ≥ 44 px (Nebenknöpfe in Bannern dürfen wie `.btn.slim` 36 px sein).
- **Lange Listen am Handy:** Regal-Karten werden zu kompakten Zeilen (Titelbild 64 px | Angaben | Symbolknöpfe,
  E-Books); Turnierliste lädt am Handy 25 statt 50 je Abruf; Kurs-Linien einspaltig mit Kapiteln als Aufklapper
  (offen ist das Kapitel der Auswahl, Springen öffnet das Zielkapitel).
- **Brett + schmale Liste nebeneinander ab 600 px** (Repertoire-Baum), statt 640-px-Brett darüber.
- **Profil:** die Darstellungs-Karte (Theme/E-Ink/UI-Größe) steht auf Nutzerwunsch ganz oben und ist dort die
  Hauptsache; sie bleibt knapp (Hinweise 0,8125 rem, Gerätebefund 0,75 rem, UI-Größe ohne Häkchen in einer Zeile).

*Niedrige Fenster* (0.808.x · Boox 200 % Falz): Boox Tab X C mit UI-Größe 200 % (3.7) rechnet **480 × 640** CSS-px,
kleine Handys ≈ 375 × 667 — die Handy-Anordnung ist auf 844 px Höhe ausgelegt. Regeln 2–3 gelten dort genauso
(Vorlauf ≤ 40 % ≈ 256 px). Abfrage überall gleich: **`@media (max-width: 768px) and (max-height: 720px)`** (Handy
hochkant, Tablet und Laptop greifen nicht).
- **Eine gemeinsame Regel** in `styles.scss` (nur RookHub + Turnierseite, Seiten hinter `app-navbar`/`trn-navbar`):
  App-Kopf 48 statt 56 px (Abzeichen an Glocke/Post rücken mit `top: 4px` in die Leiste), Material-Suchfelder 44 statt
  56 px, Reiter 40 statt 48 px, Seitentitel `h1` 1,5 rem, Abschnittstitel `h2` 1,125 rem mit knappen Rändern.
  Komponenten, die `h1`/`h2` selbst setzen, behalten ihre Werte. Tippziele bleiben ≥ 44 px.
- **Je Seite nur, wo das nicht reicht**, in derselben Abfrage und NACH der Grundregel: Seitenrand oben und Abstände vor
  der Hauptsache 4–8 px (Gespeicherte Partien, Aufgabenblätter, Punktepartie, Rekonstruktion, Kurse, Turnier-Kopf);
  Nebenknöpfe mit langem Namen als Symbolknopf, der Name bleibt im `aria-label`/`title` (Aufgabenblatt: „Drucken / als
  PDF speichern“ in die Titelzeile, „Link kopieren“; Kursseite: „Zurück“ als „←“ vor dem Titel); Banner/„Kurz erzählt“
  in 0,875 rem mit 6 px Polster (E-Books ohne Anmeldung, Partieseite); Turnier-Kopf Titel 1,125 rem, Sortierknopf so hoch
  wie das 40-px-Suchfeld; Lernkarte richtet sich nach der Fensterhöhe (`min(92vw, 100vh − 230px)`), damit sie ganz über
  die Falz passt; Repertoire-Brett (Baum/Lochfinder) `min(100%, max(300px, 100vh − 300px))` — Höhenabzüge ohne
  Untergrenze ergaben dort 160 px (d4).
- Prüfung: UI-Sweep `--viewport boox --theme eink --ui-scale 200`; Regeltexte in `features/low-window-fold.spec.ts`
  (samt Wirkung im 480 × 640-iframe), Turnier-Kopf in `tournament-detail.component.spec.ts`/`tournament-tables…spec.ts`.

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
- **Polster sichtbar gleich:** endet eine Karte mit einer Zeile Textknöpfe (`mat-button`, ≈ 8 px unsichtbarer Rand unter der
  Schrift), ist das untere Polster um diese 8 px kleiner (Zeitkarte der Löser, `calc(1rem - 8px)`, d8-rk-zeitkarte-polster-unten).
- **Outline-Feld direkt unter dem Kartentitel:** `mat-card-content` bekommt oben 20 px, damit die schwebende Beschriftung
  nicht an der Überschrift klebt — global in `styles.scss` (`.mat-mdc-card-header + .mat-mdc-card-content:has(> :first-child
  .mat-form-field-appearance-outline)`), keine Ausgleiche je Karte (Wochenpost hochladen, d9-rk-wochenpost-karte-titel-eng).
- **Ganze Kachel klickbar** (0.997.0, alle fünf Oberflächen): Eine Kachel, Karte oder umrandete Zeile, die für EIN Ziel
  steht, ist als Ganzes klickbar — nie nur ihr Titel oder ihr Knopf. Baustein: Klasse `rh-tile` an der Kachel,
  `rh-tile-target` an ihrem Hauptziel (`src/_tile.scss`, Anleitung `docs/frontend/ganze-kachel-klickbar.md`).
  - **Hauptaktion:** ein Klick irgendwo auf die Fläche ist ein Klick auf das Hauptziel — den Titel-Link der Kachel,
    sonst ihren Haupt- bzw. ersten Knopf. Das Hauptziel ist das vorhandene, echte `<a routerLink>` bzw. `<button>`
    (Router-Ziele als `<a>`: Mittelklick und „in neuem Tab öffnen“ gehen); es bleibt sichtbar und sagt, was der Klick
    tut. Kein `(click)` an der Kachel, kein Bedienelement in einem anderen, kein zweiter Tab-Stopp.
  - **Eigene Ziele bleiben eigene Ziele:** zweite Knöpfe, ⋮, Schalter, Häkchen, Links im Text und Elemente mit Tooltip
    liegen über der Fläche und lösen die Hauptaktion nie aus (auch gesperrt nicht). Inhalt, der unberührt bleiben soll
    (aufgeklappte Tabelle, Kapitelliste), trägt `rh-tile-above`.
  - **Nicht klickbar** ist die Fläche, wenn das Hauptziel gesperrt ist, und im **Bearbeiten-Modus** — dort nimmt die
    Komponente `rh-tile` weg (Dashboard „Anpassen“: Griff, Auf/Ab, Auge; Mehrfachauswahl; Formular in der Karte).
  - **Die Fläche ändert nichts:** löst die Hauptaktion etwas aus, das Daten ohne Rückfrage ändert (Woodpecker „Zyklus N
    starten“), bleibt das dem Knopf vorbehalten; die Fläche gehört dann dem Titel-Link. Gibt es keinen, ist die Kachel
    nicht als Ganzes klickbar.
  - **Kein eindeutiges Hauptziel → keine Kachel:** mehrere gleichrangige Ziele oder Titel-Link und Hauptknopf mit
    verschiedenen Zielen ohne klare Hauptsache werden nicht geraten. In einer **Sammel-Kachel** ist die Zeile die
    Einheit (angepinnte Kurse: je Kurs-Zeile, Ziel = der Name), die Sammel-Kachel selbst nicht. Reine Tabellen und
    Textlisten gehören nicht dazu.
  - **Rückmeldung an der GANZEN Kachel:** Zeiger; Tönung beim Überfahren (`--rh-tile-hover`, 6 % der Textfarbe, nur mit
    Maus) und beim Drücken (`--rh-tile-pressed`, 10 %) — nur solange der Zeiger über der Fläche steht, nicht über einem
    Nebenelement; hat das Hauptziel den Tastaturfokus, ein **Fokusring um die Kachel** (2 px `--mat-sys-primary`, 2 px
    Abstand; LeagueHub/ClubHub 3 px `--focus`; E-Ink 3 px schwarz und keine Tönung, 3.6), das Hauptziel selbst zeigt
    dann keinen zweiten Ring.
  - **Tooltip am Hauptziel:** der Zeiger über der Kachel gilt als „über dem Hauptziel“ — ein `matTooltip` dort
    erscheint deshalb verzögert (600 ms, `TILE_TOOLTIP_DELAY_MS`; `appIconLabel` macht das an `rh-tile-target` von
    selbst) und an Text-Links über dem Titel (`matTooltipPosition="above"`), damit er nicht bei jeder Mausbewegung
    aufblitzt und die Angaben darunter verdeckt.
  - **In Kauf genommen:** Text unter der Fläche lässt sich nicht markieren; ein `title` an bloßem Text darunter
    erscheint nicht mehr (Elemente mit `matTooltip` bleiben oben).
  - **Prüfung:** je Kachel ein Treffertest am gerenderten Element — `expectWholeTileClickable(kachel, hauptziel)` aus
    `src/app/testing/tile-hit.ts` (`elementFromPoint` an Ecken, Raster und Textstellen; jedes Nebenelement trifft sich
    selbst); Wache für die Verwendung in `testing/tile-hit.spec.ts`.
  - **Verwendet:** RookHub — Dashboard-Kacheln, Kurs-Zeilen der angepinnten Kurse und Woodpecker-Kachel (8.1),
    Repertoire-Karten („Öffnen“; offline „Trainieren“), Kurskarten (Titel-Link → Detailseite; der aufgeklappte
    Kapitelblock liegt über der Fläche), E-Book-Regal (Titel; nicht bei Mehrfachauswahl), Trainerstunden (Karte und
    Trefferzeile → „Ansehen“), Wochenpost („Spielen“; nicht im Bearbeiten-Formular, die aufgeklappte Bestenliste liegt
    darüber), Punktepartie-Durchläufe, Partie-Analysen und Rekonstruktionen (Titel-Link), Favoriten („Nochmal lösen“),
    Aufgabenblätter („Öffnen“), frühere Vorbereitungen (5.3). Turnierseite — Turnierkarte in Liste, Karten-Popup und
    Kalenderfenster (Name → Detailseite, 8.2) und die Zeilen der Merkliste. ClubHub — Karteizeilen (Name → Karteiblatt,
    die Telefonnummer bleibt eigenes Ziel) und Gruppenkarten (Name → Gruppenseite, 8.5). KidHubs Kacheln sind selbst
    Links; LeagueHub hat keine Kachel mit einem einzelnen Hauptziel.

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
- **Zuglisten** stehen als eine Zeile je Vollzug: Nr. | Weiß + Bewertung | Schwarz + Bewertung
- **Uhr an den Zügen** (1.015.0): trägt die Partie `[%clk]`, steht die Restzeit nach dem Zug klein rechts im Zug
  (`.clk`, 11 px, Schriftfarbe 62 %, Kurzform `1:12`). Ab vier Uhrenständen zeigt die Partieseite in der Auswertungsspalte
  unter dem Rückblick die Karte „Zeitverbrauch“ (`app-clock-chart`): 90 px hoch, Weiß volle Linie in `--rh-accent`,
  Schwarz gestrichelt in der Schriftfarbe (auch ohne Farbe unterscheidbar), Legende mit demselben Muster, größte Restzeit
  oben links als Achsenwert, Cursor-Linie am aktuellen Zug, Punkte mit Tooltip „12. 1:12“, Klick springt zum Zug.
  (`2.6em 1fr 1fr`, Bewertung rechtsbündig mit ≥ 5ch), nie als Raster aus Halbzügen, das Weiß und Schwarz in wechselnde
  Spalten wirft (Zugbaum im Analysebrett, Partie-Analyse; b1-rk-analyse-zugliste-halbzuege).
  **Zug-Klassen** der Engine-Auswertung stehen als kleines Schild direkt hinter dem Zug (Radius 8 px, 11 px fett,
  Fläche = Klassenfarbe, Schrift Schwarz oder Weiß nach Kontrast) — nur die Notationszeichen `!! ! ?! ? ??` und `✗`,
  nie ★/👍/✓/📖 an jedem Zug; ein- und ausblendbar mit dem Schalter der Brett-Symbole (Partieseite, 0.836.0).
- `matListItemMeta` nie direkt auf einen Text-Knopf (`mat-flat-button`/`mat-stroked-button`), sondern auf ein
  umschließendes `<div>` — sonst übernimmt der Knopf Schrift, Farbe und Ausrichtung des Listen-Endbereichs
  (Beschriftung winzig und fast unsichtbar, d1-rk-revenge-loesen-knopf-kaputt).
- **Listenzeile mit zweitem Ziel** (Spielervorbereitung „Frühere Vorbereitungen“, 0.995.0): die Zeile selbst ist das
  Hauptziel (ein `<a>` bzw. `<button>` über Name + Meta-Zeile, `flex: 1 1 auto; min-width: 0`, bricht in sich um); das
  Nebenziel steht als `mat-icon-button` mit `[appIconLabel]` (Name nennt die Zeile: „Trainings-Repertoire zu … öffnen“)
  rechts DANEBEN als Geschwister im `<li>` (`flex: none`) — nie ein Link im Link. Das ist die Zeilen-Form von „Ganze
  Kachel klickbar“ (5.2): die Zeile trägt `rh-tile`, ihr Hauptziel `rh-tile-target` — so trifft auch der Rand um den
  Symbolknopf das Hauptziel, und Tönung und Fokusring gelten der ganzen Zeile. Führt eine Zeile ausnahmsweise nicht
  zum Ziel, sondern löst etwas auf der Seite aus (Suche füllen), ist sie ein `<button>` und sagt es in der Meta-Zeile mit
  Symbol + Text (Lupe + „Spieler suchen“), nicht nur durch anderes Aussehen.

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
- **Aktion mit Arten** (Admin-Tab „Uploads“ → „Einlesen“, 0.901.0): ein `mat-stroked-button` mit Symbol öffnet ein
  `mat-menu` mit den Arten als ganze Sätze („Alle Bilder = eine Partie …“, „Jedes Bild einzeln (n …)“); eine gerade
  nicht mögliche Art bleibt sichtbar, gesperrt, mit dem Grund als zweite leise Zeile im Eintrag (`.why`). Nach der Wahl
  fragt `ConfirmService.ask` mit dem Handlungsnamen als Knopf nach; das Ergebnis steht danach als leiser Vermerk in der Zeile
  (Zeit → verlinkte Ids), nicht nur in der Snackbar.
- **Erledigt markieren** (Admin „Uploads“, 0.903.0): `mat-stroked-button` „Erledigt“ (`task_alt`) ohne Rückfrage, weil
  umkehrbar; die Zeile wird gedämpft (alles außer den Knöpfen, `opacity .55`) mit leisem Vermerk „✓ erledigt …“, der Knopf
  heißt dann „Wieder öffnen“ (`undo`). Erledigtes bleibt in der Liste — Löschen ist der eigene, endgültige Schritt.
- **Zuordnung je Konto umschalten** (Admin „Engines“, 0.950.0): je Konto eine Zeile mit dem Zustand als Satz + Symbol
  („In der Hintergrund-Liste von X“ / „Nicht in der Hintergrund-Liste von X (nur live)“) und daneben ein
  `mat-stroked-button`, dessen Beschriftung Handlung UND Konto nennt („Aus Hintergrund-Liste von X nehmen“ / „In
  Hintergrund-Liste von X aufnehmen“) — kein Schalter ohne Text, weil sonst offen bliebe, wessen Liste sich ändert. Ohne
  Rückfrage (umkehrbar); während des Speicherns alle Knöpfe der Gruppe gesperrt, Ergebnis als `quick`-Snackbar.
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
- Dieselbe Sache, dieselbe Reihenfolge: das ⋮ einer Partie (Partieseite und Zeile der Partienliste) läuft „In Analyse
  öffnen → Original → Korrigieren → (Engine) → Roast/Vereins-Datenbank → Teilen/PGN/Foto → Löschen“ (0.807.x,
  d3-rk-spielmenue-reihenfolge-uneinheitlich) — und hat denselben Inhalt: „PGN kopieren/herunterladen“ auch in der
  Liste, „Löschen“ (eigene Partie, Rückfrage, danach zur Liste) auch auf der Partieseite (0.808.x, d4).
- Ins ⋮ wandert, was in der Kopfzeile nicht mehr passt (Rekonstruktion: in der Kopfzeile nur ✓ und ⋮,
  r2-rg-recon-actions; Turnierseite am Handy, 0.746.0).
- Am Handy bleibt das ⋮ **in derselben Zeile** wie die Knöpfe (0.751.2, Kursseite).
- **Partiekopf (0.948.0, d10):** „Originalpartie öffnen“ und die Teilen-Einträge stehen auf ALLEN Breiten nur im ⋮; im Kopf
  bleiben „Ohne Engine“ (ab 600 px), „Zusammenfassung“ und „Fehler nachspielen“ — das wird der Hauptknopf (`mat-flat-button`),
  sobald es „Partie analysieren“ nicht mehr gibt (analysierte oder Vereinspartie); nie fünf gleichrangige Knöpfe
  (d10-rk-partiekopf-fuenf-sekundaerknoepfe). Am Handy steht „Ohne Engine“ nur im ⋮ (d10-rk-partie-handy-ohne-engine-eigene-zeile).
- Brett-Stellungen haben ein eigenes ⋮ (`features/analysis/position-menu.component.ts`).
- **Untermenüs am Handy** (☰ → Konto usw., `class="nav-submenu"`): unter 600 px liegen sie deckungsgleich über dem
  Hauptmenü (8 px vom rechten Rand, mindestens 232 px breit), statt an den linken Rand geschoben das Hauptmenü
  anzuschneiden (b1-rk-nav-konto-untermenue-handy). Solange ein Untermenü offen ist, sind die Menüs darunter (☰, bei
  Konto → Sprache auch Konto) ausgeblendet — eine Fläche statt zweier versetzter Rahmen (`class="nav-main-menu"` am ☰,
  d8-rk-konto-menue-doppelt).
- **☰-Menüs (Gast und angemeldet): Symbole nur bei externen Zielen** (Discord, Ko-fi, Turnierseite) — alle anderen
  Einträge ohne Symbol, damit die Textkante fluchtet; Untermenüs zeigt der Pfeil ▸. Im Gast-Menü stehen die
  Rechtslinks (Datenschutz, Impressum) zuletzt (d9-rk-gastmenue-icons-uneinheitlich).
- Selten gebrauchte Werkzeugkarten stehen nicht von vornherein auf der Seite: ein ⋮-Eintrag blendet sie ein (und
  scrollt sie in Sicht), ein ✕ in ihrer Kopfzeile wieder aus; solange sie arbeiten, bleiben sie ohne ✕ stehen
  (Analysebrett: „Sparring gegen Maia“ im ⋮ der Stellung, sichtbar während eines Sparrings, 0.760.0).

### 5.6 Dialoge und Blätter

- **Rückfragen** nur über `ConfirmService.ask(…)` (`shared/confirm-dialog/confirm-dialog.component.ts`, 47 Dateien),
  Texteingabe über `PromptService` (`shared/prompt-dialog/`). **Nie `window.confirm`** (unsichtbar im Vollbild,
  abschaltbar, Browsersprache; festgehalten durch `NativeConfirmGuardTests`). Oberflächen ohne Sprachwahl geben
  Knopftexte über `CONFIRM_LABELS` vor.
- **Bestätigen benennt die Handlung** (b1-rk-games-loeschen-ok): der rote Knopf heißt nicht „OK“, sondern sagt, was
  passiert. Lösch-Rückfragen mit einem Schlüssel `….delete…Confirm` (`isDeleteConfirmKey`) bekommen „Löschen“
  (`common.delete`) von selbst; andere Gefahraktionen geben `labels.confirm` mit („Ersetzen“, „Verwerfen“ …).
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
- **Erstes Feld unter dem Titel:** 8 px Luft für die schwebende Beschriftung — global in `styles.scss`
  (`.mat-mdc-dialog-title + .mat-mdc-dialog-content:has(.mat-mdc-form-field) { padding-top: 8px }`); Material setzt dort
  0 und der Inhalt scrollt, das Label des ersten Outline-Felds wurde halb abgeschnitten. Reine Text-Dialoge bleiben
  ohne. Keine eigenen `padding-top`-Ausgleiche je Dialog (gegen Materials Regel wirkungslos) (d7).
- **Dialogfläche dunkel** = `--mat-sys-surface-container-high` (über `mat.dialog-overrides`) + 1 px
  `--mat-sys-outline-variant` (`styles.scss`, `html.dark-theme`); hell Material-Vorgabe ohne Rahmen, E-Ink 2 px schwarz
  (3.6). Vorher war sie `surface` = Seitenfarbe, eine abgedunkelte Karte dahinter verschmolz mit ihr (d7).
- **Hinweistext im Dialog** im Ton von `mat-hint` (`var(--mat-sys-on-surface-variant)`), nicht `currentColor 60 %` —
  im Dialog ist currentColor schon on-surface-variant, 60 % davon lag bei ≈ 3,1:1 (d7-rk-dialog-hint-kontrast; Wache
  `dialog-hint-contrast.spec` für `.hint`/`.hint-small`/`.empty` in allen Dialog-Dateien, d8).
- **Feld mit Hinweis (`mat-hint`) im Dialog:** 12 px Abstand darunter, sonst klebt der Hinweis an der schwebenden
  Beschriftung des nächsten Felds — global in `styles.scss` (`.mat-mdc-dialog-content .mat-mdc-form-field:has(.mat-mdc-form-field-hint)`),
  Wache `dialog-field-spacing.spec` (≥ 12 px Hinweis → nächstes Label). Ein Grund steht **einmal**: nennt ihn die Knopfleiste
  (`.why`), kein zweiter `mat-hint` am Feld (d9-rk-dialog-hinweis-eng, d9-rk-weekly-dialog-hinweis-abstand).
- **„Optional“ nicht ins Label**, sondern als Hinweis unter das Feld (ein Pflichtfeld trägt das Sternchen); bei
  `app-date-time-fields` über `hint` — ein langes Label stieß sonst ans Kalender-Symbol (d9-rk-datepicker-label-eng).
- **Kein Dialog als Sackgasse:** fehlt eine Voraussetzung, die die App selbst schaffen kann (Roast ohne Analyse), bietet der
  Dialog sie als Hauptknopf an („Partie analysieren“, „Schließen“ wird „Abbrechen“; d9-rk-roast-dialog-sackgasse).
- **QR-Code** (`app-qr-code`) mit 4 Modulen weißer Ruhezone (`QR_QUIET_ZONE`), auch auf dunkler Dialogfläche
  (d9-rk-qr-ruhezone-zu-klein).
- **Gesperrter Dialogknopf:** der Grund steht links in der Knopfleiste (`<span class="why" role="status">` als direktes
  Kind von `mat-dialog-actions`) — Pflichtfeld mit `required` (Sternchen). Die Gestaltung kommt **global aus `styles.scss`**
  (`.mat-mdc-dialog-actions > .why`, keine Kopien je Dialog): ≤ 280 px, 0,85 rem, on-surface-variant; der **Grund schrumpft**
  (`flex: 1 1 0; min-width: 0`) und bricht in sich um, die Knöpfe (`flex: none`) bleiben zusammen in einer Zeile — nie der
  Hauptknopf allein in Zeile 2 (d8-rk-dialog-aktionen-umbruch). Steht ein Knopf **vor** dem Grund (Löschen links) oder ist
  das Fenster < 480 px, bekommt der Grund eine eigene Zeile über den Knöpfen. Kein `.spacer` vor dem Grund.
- **Titel** immer `<h2 mat-dialog-title>` in Standardschrift; ✕ nur bei Dialogen ohne Knopfzeile (Kopfzeile mit ✕:
  Inhalt darunter oben ohne Polster wie beim schlichten Titel, mit Formfeld 8 px — global in `styles.scss`).
- Ein scrollender Dialoginhalt bekommt den Weiterscroll-Schatten (`rh-scroll-hint`, global in `styles.scss`:
  `<mat-dialog-content class="rh-scroll-hint">`).
- **Tabellen im Dialog ohne eigenen Grund** (`--mat-table-background-color: transparent`) — sonst stand `mat-table` im Dunkeln
  als Kasten in Seitenfarbe auf der helleren Dialogfläche (Turnier-Mannschaftsfenster, d8).
- Felder mit Kopiertext (Teilen-Link): Fläche/Schrift/Rahmen aus Tokens ohne hellen Festwert, `text-overflow: ellipsis`; Dialog
  mit `autoFocus: 'dialog'`, damit das Feld nicht fokussiert ans Ende springt (d8).
- **Lange Optionstexte im `mat-select`:** geschlossen per `<mat-select-trigger>` nur der Kurzname, die Liste zeigt den
  vollen Text (Token-Bereich, d7).
- **Entscheidungsfrage beim Einstieg** (Woodpecker „Zeit ist um“, 1.007.0, `woodpecker-deadline-dialog.component.ts`):
  440 px, Titel nennt den Anlass, darunter der Stand in einem Satz und je Weg ein Abschnitt (`h3`) mit höchstens einem
  Feld und einem leisen Folgensatz; Knöpfe „Später“ (Text) · Nebenweg (stroked) · häufigerer Weg (Hauptknopf, nennt die
  Wahl: „Um 8 Tage verlängern“). Nicht blockierend; „Später“ fragt in der Sitzung nicht noch einmal von selbst, die Stelle,
  an der die Frage hängt, bietet sie als stroked-Knopf an. Gespeichert wird erst per Knopf, scheitert es (auch offline),
  bleibt das Fenster offen (`warn`-Snackbar).
- **Einstellung mit wenigen, sich ausschließenden Werten** (Reihenfolge im Repertoire-Training, 0.996.0,
  `features/repertoire/train-order-dialog.component.ts`): schmaler Dialog (440 px, `maxWidth: '94vw'`,
  `autoFocus: 'dialog'`), langer Name (Repertoire) als leise Zeile unter dem Titel; `mat-radio-group` mit `aria-label` =
  Titel, je Wert der Name und EIN leiser Erklärsatz darunter (`.hint`, `--mat-sys-on-surface-variant`), Zeile mind. 44 px;
  ein Wert, der für diesen Gegenstand nicht gilt, steht gar nicht in der Liste (statt gesperrt — „Nach dem Gegner“ nur
  für ein Repertoire mit gespeichertem Gegner, 0.999.3); der Inhalt trägt `rh-scroll-hint` (sechs Werte am kleinen Handy);
  der GELTENDE Wert ist vorgewählt; unter der Liste ein Satz zur Geltung. Knöpfe „Abbrechen“ + „Speichern“ (Hauptknopf):
  gespeichert wird erst mit „Speichern“, Erfolg als `success`-Snackbar; scheitert es, bleibt der Dialog mit der Wahl offen
  (`warn`-Snackbar) — nie ein vorgetäuschtes „gespeichert“. Holt der Dialog den Stand selbst und scheitert das, steht
  `app-load-error` (Erneut versuchen) STATT der Auswahl und es gibt keinen Speichern-Knopf — eine Vorgabe sähe sonst wie
  die gespeicherte Wahl aus. Jede Stelle, die die Einstellung anbietet, öffnet DENSELBEN Dialog (Grundsatz 9).

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
- Ein `mat-hint`, der am Handy umbrechen kann, braucht `subscriptSizing="dynamic"` und Abstand zum nächsten Feld (≥ 8 px) —
  die feste Hinweiszeile reserviert nur eine Zeile, die zweite lag sonst auf dem Rahmen darunter (d3-rk-ebook-upload-hint-ueberlappt-feld).
- Passwortfelder haben überall das Auge zum Einblenden: `<app-password-toggle matSuffix #pwEye />` +
  `[type]="pwEye.type()"` (`shared/password-toggle/`, `aria-pressed`, Schlüssel `auth.passwordToggle.*`) — Anmeldung,
  Registrierung und Zurücksetzen gleich (d3-rk-login-passwort-ohne-auge).
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
- **Nur gesetzte Werte** bekommen ein Abzeichen (keine Vorgabe-Themen wie „Taktik“, r2-rp-taktik-chip) — Kurskarte und
  Kursdetail über dieselbe Regel (`features/courses/course-themes.util.ts`, d3).
- **Hinweis-Chips nennen einen Zustand, keine Aktion** („Import veraltet“ statt „Neu holen“, wenn der Tipp nur eine
  Erklärung aufklappt, d3).
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

- **Wartezeit auf den Formular-Leser** (0.817.0): zwei Schritte mit Uhr statt einer Zeile — „Wartet auf den Leser“ (Uhr ab
  dem Hochladen, Nachsatz „schaut jede Minute nach“), nach dem Abholen ✓ dahinter samt Wartezeit und darunter „Wird gelesen“
  mit eigener Uhr; ab 15 min Warten ein Hinweis in Warnfarbe. EIN Bauteil für RookHub und LeagueHub
  (`app-sheet-reading-status`); Uhr h:mm:ss ab einer Stunde. Stehen andere davor (`queueAhead`, 0.902.0), ersetzt
  „Platz N in der Warteschlange“ den Nachsatz, und der 15-min-Hinweis entfällt (Warten in der Schlange ist kein Ausfall).

- Leer heißt „erklären, warum“: leerer Kurs vs. reiner Partien-Kurs (r2-rp-empty-course), freundlicher
  Leerzustand der KidHub-Kursseite (k-courses-empty).
- Kein Fortschritt über nichts: ein Kurs ohne Aufgaben (Studienbuch) zeigt weder im Kopf noch je Kapitel „0/0“ samt
  leerem Balken, sondern den Satz der Kurskarte; „Linien durchsehen“ wird dann der Hauptknopf (b1-rk-studienbuch-fortschritt-0-0).
- Ist: Klassen `.empty` (75×), `.empty-hint`, `.empty-state`, `.empty-text`. **Regel ab jetzt:** `.empty-state`
  mit `text-align: center; padding: 2rem 1rem`; ein geteilter Baustein ist offen (Abschnitt 10).
- Leerzustand **in Dialogen** ohne das Seitenpolster von `.empty-state`: linksbündig direkt unter dem Titel wie
  Fließtext (`margin: 0; padding: 8px 0 4px`) — sonst steht ein Satz zentriert in ≈ 90 px Leere oben und unten
  (Analysebrett „Letzte Analysen“, d8-rk-verlauf-leerzustand-luftig).
- Ein Ladekreis, der nach dem Laden stehen bleibt, ist ein Fehler (UI-Sweep-Prüfung „lädt noch“).
- Hinweiszeilen **im** Leerzustand (`.muted`, `.empty-hint`) erben dessen 60 % und werden nicht ein zweites Mal
  abgeblasst (sonst 36 %, hell ≈ 2,5:1) — Unterschied nur über die Größe (≈ .85–.9em) (a1-rk-leer-hinweis-blass).

### 5.11 Hinweise, Banner, Sperrkarten

- **Hilfe:** `app-help-hint` (`shared/help-hint/`): 22-px-Knopf, Deckkraft .55 (Fokus/Hover 1), Touch-Trefferfläche
  34 px, Tooltip per Klick fest (`.hh-tooltip`: max. 320 px, `pre-line`, Absätze mit `\n\n`, max. 60vh scrollbar).
  Symbol immer das Standard-? (`help_outline`) — kein ⓘ (`info_outline`) neben ? in derselben Karte
  (Lochfinder, d8-rk-lochfinder-hilfe-symbole-uneinheitlich).
  Am Satzende steht davor **U+00A0** (`}}&nbsp;<app-help-hint …/>`, kein Zeilenumbruch im Markup) — sonst bricht das ?
  allein in die nächste Zeile (0.808.x, d4-rk-hilfe-symbol-umbruch). U+00A0 und Symbol stehen **gemeinsam** in
  `<span class="rh-nowrap">&nbsp;<app-help-hint …/></span>` (`styles.scss`): `app-help-hint` ist inline-flex, Chrome
  bricht vor so einem atomaren Element trotz U+00A0 (d7-rk-install-hilfesymbol-allein-in-zeile).
- **Banner** (Seitenhinweise): Fläche getönt, Symbol links, Text `flex: 1 1 220px`, ein Knopf rechts, Radius 8 px
  (Vorbild `shared/reprocess-banner/`); Offline-Banner, Impersonation-Banner (`shared/impersonation-banner/`).
  Aktualisierungs- und Angebots-Banner tönen mit `--rh-info`, `--rh-warn` nur für echte Warnungen — hell liegt
  `--rh-warn` (#bf360c) im Ton fast auf `--rh-error` und wirkt als Fehler (d3-rk-update-banner-rot-hell).
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
- **Formular-Korrektur = EIN Bauteil** (0.816.0): `features/games/sheet-workbench/` (`app-sheet-workbench`) für RookHub
  `/games/:id/edit` UND LeagueHub (Formular prüfen, Vereinspartie korrigieren). Aufteilung Foto | Brett | Prüfen + Zugliste
  (≤ 1 100 px Brett | Prüfen, Foto darunter; ≤ 720 px Prüfen, Brett, Züge, Foto eingeklappt); Seite und „Foto 1 / Foto 2“
  über dem Foto, die Zeile des Zugs im ganzen Foto markiert, Ausschnitt oben im Prüfteil, „Stimmt so“ + rote Links „Partie
  hier beenden“ (streicht diesen und alle folgenden Züge, Rückfrage mit Zug und Anzahl Halbzüge, Bestätigen-Knopf „n
  Halbzüge löschen“, nicht am ersten Zug) und „Zug löschen“ rechts in der Zeile des Zugs (`.danger-links`), beide bis
  zum Speichern rückgängig (Zeile über dem Zug nennt, was gestrichen wurde); am Ende der Zugliste steht derselbe rote Link
  unter dem Ende-Hinweis, solange auf dem Formular noch Einträge übrig sind (Rückfrage nennt die Zahl, Knopf „Rest
  verwerfen“); nach dem Beenden sagt der Ende-Hinweis, dass ein Zug am Brett ohne Formular-Eintrag angehängt wird. Kurve + Engine unter dem Brett. Gestaltung im LeagueHub-Stil mit Token-Rückfall auf
  RookHub (`--sw-*`). Seiten ergänzen nur über Inhaltsplätze (`sheetPhotoTools`, `sheetPlyExtra`, `sheetEnd`) — nie eine
  zweite Fassung der Korrektur bauen. **Uhr nach dem Zug** (1.015.0): unter der Zeile des Zugs ein kurzes Textfeld
  (`.clock-row`, natives Feld, 6,5 em, Rahmen `--sw-line`, ungültig `--sw-red`) mit Hinweis rechts („Restzeit, z. B. 1:12
  oder 72'“), bei unlesbarer Eingabe ersetzt die Fehlerzeile den Hinweis; in der Zugliste steht die Zeit klein unter dem
  Zug (`.ply .clk`, 11 px, `--sw-muted`).
- Analysebrett: Zusatzkarten unter der Zugliste starten zugeklappt — der Eröffnungs-Explorer zeigt nur die Kopfzeile
  (Titel + Aufklapp-Pfeil) und fragt erst aufgeklappt; ein auf dem Gerät gemerkter Zustand gilt weiter (0.760.0).
- Analysebrett: Kopfzeile rechts `.header-actions` — „Ohne Engine“/„Mit Engine“ (`mat-stroked-button`, Symbol
  `visibility_off`/`visibility`, `aria-pressed`) vor „Verlauf“; auf dem freien Brett, nicht mit Linien-Baum (dort die
  Karte „Ohne Engine“). Die Adresse führt `?blind=1` mit (0.895.0). Seit 0.898.0 auch mit Linien-Baum (Partie): derselbe Knopf schaltet
  den Modus, die Leiste „Ohne Engine“ bleibt (Fortschritt, Zähler). Kam man von einer Partieseite (`?game=`), führt
  „Mit Engine“ dorthin zurück; die Partieseite trägt das Gegenstück „Ohne Engine“ (0.900.0).
- Stellung markieren: `app-mark-position-button` („+“, markiert grün gefüllt, `aria-pressed`).
- Analysebrett: Brettspalte klebt erst ab **900 px**, wo Brett und Seitenspalte sicher nebeneinander stehen (Brettspalte
  lässt dort 300 px frei); 769–899 px steht das Brett mittig einspaltig und scrollt mit. Bewertungsleiste mit 1-px-Rahmen
  `--mat-sys-outline` (Weißanteil sonst im hellen Design unsichtbar). Pfeile (autoShapes) erst auf ein gemessenes Brett
  (Breite > 0), sonst NaN-Koordinaten (a1-rk-analyse-tablet-brett, a1-rk-evalbar-hell, a1-rk-svg-nan-pfeile).
- **Knopfleiste unter dem Brett nie breiter als das Brett** (`max-width: 100%`, ◀ ▶ schrumpfen bis 44 px, Abstände
  wachsen mit dem Brett); am Handy (≤ 480 px) Nebenknöpfe 36 px, damit ◀ ▶ je ≥ 56 px bleiben (Partieseite,
  a1-rk-brettleiste-plus; die 36 px auch im Analysebrett).
- **Partie-Zusammenfassung:** ab 900 px zweispaltig, Text | klebendes Brett mit ⏮ ◀ ▶ ⏭, Engine-Knopf (`memory`, an = Akzentfarbe, drei Linien darunter), „+“ (Stellung markieren, als letzter Knopf der Zeile wie auf der Partieseite; ≤ 480 px Nebenknöpfe ⏮ ⏭ Engine „+“ 36 px, die Zeile bleibt bis 320 px Seitenbreite so breit wie das Brett) und Zugliste (Spalte `clamp(320px, 40vw, 520px)`, Seite `--page-max-width`, Text ≤ 75ch, nie höher als das Fenster: Brett gibt nach, Zugliste scrollt); darunter steht das Brett unter dem Text, ein Zug-Klick öffnet es aber als Blatt über dem Text (DESIGN 5.6, Griff + Titel + X, Engine an, Zugliste darunter) statt zu scrollen. „Die Partie“ hinter dem Abschnitt „Zusammenfassung“: `h2` + Zugfolge als Fließtext, Zugpaare brechen nicht in sich um (`inline-block`, `nowrap`), Nummer in on-surface-variant, Züge als Textknöpfe wie `.mv`, der Zug am Brett mit Akzentfläche, Ergebnis fett am Ende — diese Zugliste nur noch bei Texten OHNE eigenen Abschnitt „Die Partie“. **Verschmolzener Text** (ab 0.963.0, neue Zusammenfassungen): „Die Partie“ ist ein Fließtext in Absätzen nach Partiephasen (Zeilenhöhe 1,75), der Abschnitt „Zusammenfassung“ entfällt; Partiezüge als Textknöpfe in Textfarbe, fett (700), ohne Unterstreichung, kein Umbruch im Zug; Untervarianten in runden Klammern kleiner (0,9 em), kursiv, in on-surface-variant, ihre Züge aufrecht und dünner (500); Zugzeichen färben den ganzen Zug: `!`/`!!` `--rh-success`, `?`/`??`/`?!` `--rh-error` (die Bedeutung trägt das Zeichen, E-Ink: Textfarbe); Hover Akzentfläche 16 %, der Partiezug am Brett Akzentfläche mit on-primary (E-Ink: Rahmen); Ergebnis fett hinter dem letzten Partiezug. **Empfohlene Übungen** (0.960.0) als letzter Abschnitt: `h2`, ein Satz Einleitung, je Stelle `h3` (Zugnummer als Textknopf wie `.mv` + Grund), Karten im Raster `auto-fill` ≥ 140 px (umrandet, Radius 8, Brett ohne eigene Bedienung, darunter Übereinstimmung + „Lichess · Wertung“ in on-surface-variant, Häkchen/Kreuz in Erfolg/Fehler); höchstens drei Stellen offen, Rest hinter umrandetem Knopf „Weitere Stellen (N)“; Lösen ab 900 px in einem umrandeten Kasten über den Karten, darunter als Blatt (5.6); ohne Stellen kein Abschnitt und kein Leersatz. **Buchbeispiele** (0.962.0) stehen vorn in derselben Kartenreihe (gleiche Karte, Brett = Diagrammstellung ohne Zugmarke): unter der Übereinstimmung eine Quellzeile in on-surface-variant, 0,75 rem — Symbol `menu_book`, Titel kursiv, „· Autor · Kapitel“ (ohne Kapitel „S. N“ bzw. „Abschnitt N“ im EPUB), als Link in den Leser an das Diagramm, umbricht statt abzuschneiden; ohne bestätigte Buchlösung ist die ganze Karte ein Link in den Leser mit „Beispiel im Buch“ statt der Wertung (kein Löser).
- **Zusammenfassung → roter Faden** (1.021.0): der Absatz mit dem roten Faden steht ganz oben im Text, direkt unter dessen Titel
  (`# Weiß – Schwarz`; ohne Titel als Erstes), und an seiner alten Stelle am Ende von „Die Partie“ nicht mehr. Abgesetzt als
  Kernsatz (`aside.red-thread`): Akzentleiste links 4 px `--rh-accent`, Fläche `--rh-accent` 10 % getönt, Radius rechts 8 px,
  Polster 10/16 px (≤ 600 px 8/12), Schrift 1,05 rem, Zeilenhöhe 1,6, höchstens 75ch. Keine eigene Überschrift und kein Symbol —
  der Text beginnt selbst mit „Der rote Faden …“; die Bedeutung trägt Lage und Leiste, nicht die Farbe (E-Ink: dunkle Akzentleiste `#17386b` auf kaum getönter Fläche). Fett im
  Absatz bleibt fett. Ohne roten Faden keine Box, kein Platzhalter.
- **Zusammenfassung → ⋮ im Kopf** (0.984.0): hinter „Zur Partie“ und „Neu schreiben“ ein ⋮ (5.5) mit „Zur Partie“ (`sports_esports`),
  „Link teilen“ (`share`), bei eigenen Partien „Anonym teilen“ (`visibility_off`) und zuletzt „Teilen-Link widerrufen“ (`link_off`).
  Nur angemeldet auf der eigenen Seite bzw. der Vereinspartie und nur mit fertigem Text; über einen Link geöffnet gibt es kein ⋮.
  Unter 600 px steht das ⋮ oben rechts neben den Namen (die Knöpfe bekommen die Zeile darunter — bei 320 px rutschte es sonst
  allein unter „Neu schreiben“), und der Kopfknopf „Zur Partie“ entfällt, sobald es das ⋮ gibt (Eintrag im Menü, dazu der
  Zurück-Pfeil). Teilen am Handy über das Teilen-Blatt, sonst Zwischenablage mit Snackbar. Anonymer Link
  (`/ga/:token/summary`): Kopf „Weiß – Schwarz“ ohne Zurück-Pfeil und ohne Wege zur Partie.
- **Zusammenfassung → Links und Seite „Meisterpartie“** (0.980.0): ein Link im Text steht in Akzentfarbe, durchgezogen
  unterstrichen (klickbare Züge gepunktet), fett/kursiv nur als Ganzes; Meisterpartie des Explorers im selben Tab,
  lichess.org in neuem Tab. Die Seite „Meisterpartie“ ist einspaltig (≤ 720 px): Kopfkarte mit Spielern (Elo in
  on-surface-variant), Ergebnis und der Zeile „Meisterpartie · Turnier · Ort · Jahr“, darunter dasselbe Brett wie neben der
  Zusammenfassung (Leiste, Engine, „+“, Zugliste). Unbekannte Partie: Leerzustand (5.10) mit `link_off` und Weg zur
  Startseite; Explorer nicht erreichbar: Ladefehler mit „Wiederholen“.
- **Zusammenfassung → „Taktiken trainieren“** (0.968.0, für sich seit 0.973.0): am Ende des Texts, vor dem Modellvermerk, ein
  `mat-stroked-button` mit Symbol `school` und darunter eine leise Zeile, was der Klick tut (Kapitel im Kurs „Persönliche Taktiken“,
  öffnet den Kurs-Löser) — Sekundärknopf, weil die Seite ihre Hauptaktion schon hat; während des Anlegens gesperrt. Die Übersicht
  „Empfohlene Übungen“ darüber (Stellen, Karten mit Brett, Löser) gibt es nicht mehr; der Block ist ein einzelner Aktionsblock ohne
  eigene Überschrift, vom Text abgesetzt durch eine Trennlinie (`--mat-sys-outline-variant`, 1 px) mit 24 px Abstand darüber und 16 px
  darunter, höchstens so breit wie die Textspalte (75ch). Nur angemeldet und mit Zugang zu den Kursen; ohne Anmeldung (Teilen-Links)
  fehlt er ganz, wie die übrigen Konto-Aktionen der Seite. Ohne lösbare Aufgabe kein Knopf.
- **FEN nie roh anzeigen:** Mini-Brett + „Weiß am Zug · Zug 5“ + Kopierknopf (r2-rg-recon-fen).
- **Kurs-Löser, Seitenspalte (2026-10-20, Nutzerwunsch):** Reihenfolge Kontext-Karte (Titel, Sprachwahl + ⓘ) →
  Kommentar → Status-Card (Uhr; „Eval“, „Überspringen“, „Aufgeben“) → Aktionszeile (Teilen + ⋮). Der Kurs-Fortschritt
  (ganzes Buch + Kapitel) liegt hinter einem ⓘ neben der Sprachwahl (zu beim Öffnen); „Überspringen“ steht neben
  „Aufgeben“; die Spielweise wechselt man nur im ⋮ (der Wochenpost behält seinen Knopf). Lange Kommentare sind auf
  ~5 Zeilen gekürzt und scrollen dort (ganzer Text zusätzlich im Dialog), damit die Bedienung über der Falz bleibt
  und die Züge der unteren Zeilen anklickbar sind (0.881.2).
- **„Warum diese Aufgabe:“** (1.001.0, persönliche Taktik-Kurse): `app-line-why` als leise Zeile in der Textspalte der Kontext-Karte
  unter Titel/Kapitel/„Zur Partie“ (0,85 rem, on-surface-variant, nur das Etikett halbfett, umbricht statt abzuschneiden), in
  „Linien durchsehen“ über dem Kommentar in Kommentarbreite. Ohne Vermerk keine Zeile, kein Platzhalter. Kein Kasten, kein Symbol —
  Begründung, nicht Bedienung.
- **Puzzle-Löser** (alle drei Modi): zweispaltig erst ab 900 px; 769–899 px einspaltig, Brett mittig bis Fensterhöhe
  (`min(100%, 100vh - 180px)`), Seitenkarten volle Breite darunter. In der Seitenspalte zuerst die Bedienung
  (Kontext-Card mit Fortschritt/„Überspringen“, Status-Card, Aktionsleiste), der ungekürzte Kurs-Kommentar als eigener
  Block danach. Teilen + ⋮ der Aktionsleiste sind eine Einheit (`.pab-actions`); wird die Leiste schmal (Container
  `pab` < 420 px), zeigt „An Freund schicken“ nur sein Symbol, damit Teilen + ⋮ in der Zeile bleiben (b1).
  Kennzahlen nach Inhaltsbreite in einer Zeile (`flex-wrap`, Beschriftung ungekürzt), erst eine Karte < 330 px bekommt
  drei Spalten (3 + 2) (b1); leise Knöpfe („Überspringen“, „Tipps melden“) über `--mat-sys-on-surface-variant`, Rahmen
  `--mat-sys-outline`, nie über Deckkraft (a1, b1). Aufgedeckte Tipp-Texte stehen ab 900 px in der Seitenspalte unter
  der Status-Karte, darunter unter dem Brett (b1).
  „Nächstes Puzzle“ nach Lösen/Aufgeben steht nur in der Status-Card, die Kontext-Card behält nur Tages-/Buch-Navigation;
  Abzeichen und „Tags anzeigen“ stehen in der Textspalte neben dem Zurück-Pfeil (b1).
- **Linien durchsehen** wie der Löser: zweispaltig erst ab 900 px; darunter Brett mittig bis Fensterhöhe
  (`min(100%, max(320px, 100vh - 300px))`), die Linienliste läuft mit der Seite, Kapitelköpfe kleben oben (b1).
- **Lernkarte (Flashcards):** die verdeckte Seite ist zusätzlich `visibility: hidden` (Wechsel nach halber Drehzeit) —
  WebKit malte sonst die gespiegelte Rückseite über das Brett; Vollbild-Knopfzeile so breit wie die Karte (b1).
- **Lösezeit** in der Status-Karte mit Uhr-Symbol `timer` und Namen „Lösezeit“ (Tooltip, `role="timer"` + `aria-label`),
  nie als nackte Zahl; „Tipps melden“ trägt den Daumen (`thumb_down_off_alt`/`thumb_down`), die Flagge gehört
  „Aufgeben“ (alle drei Modi, d3). Endlos: Rating, Level und Leben nur in den Quick-Stats, die Statistik-Karte zeigt
  „Gelöst“ und „Sitzung“ (d3); die Stufe steht einmal als „Level n (min–max)“ mit Treppen-Symbol in den Quick-Stats
  (nicht noch in der Aktionszeile, kein Fähnchen), der Tiefen-Regler trägt die sichtbare Beschriftung „Stockfish-Tiefe“
  (d4). Endlos am Handy mit demselben 1-rem-Seitenrand wie Standard/Buch.
- **Tipp-Knopf** zählt die Stufe, die der Klick aufdeckt: „Tipp (1/3)“, danach „Nächster Tipp (2/3)“ (alle Löser und
  „Eigene Fehler“); das Vollbild-Abzeichen zählt die gezeigten Tipps und sagt es im `title` (d4).
- **Browser-Wert neben Server-Werten** (1.000.0, „Eigene Fehler“): eine Zahl der Browser-Engine steht nie unbenannt
  neben Werten der Server-Analyse — sie trägt den Zusatz „Schwache Analyse im Browser“ und ihre Tiefe („+1.20 (Tiefe 14)“),
  im selben Format und in derselben Weiß-Sicht wie die übrigen Werte, als eigene Zeile in der Hinweisfarbe (`.cost`);
  während der Suche „… rechnet …“, ohne Engine (kein Worker/WASM, Startfehler, auch E-Ink-Geräte) entfällt die Zeile ganz.
- **Hinweiszeile unter dem Brett** (`.board-hint`, Zugrecht/Tipp): EINE Grundregel in `styles.scss`, 0,875 rem in allen
  Modi (vorher Standard 1em, Buch/Endlos 0,88em, d4).
- **Woodpecker-Statistik = Fenster über dem Löser** (0.992.0, Nutzerwunsch 18.11.2026: „mach die Statistik als
  modales Overlay und pausiere das aktuelle Puzzle währenddessen“; löst den aufklappbaren Block in der Kontext-Card ab,
  0.956.0–0.958.1). In der Kontext-Card bleibt nur der Knopf: eigene kleine Aktionszeile direkt unter der Zyklus-Zeile,
  linksbündig, nicht volle Breite — `mat-stroked-button` kompakt (32 px, mit `(pointer: coarse)` 44 px hoch, 7) mit
  Symbol `bar_chart` und der Beschriftung „Statistik“ (`aria-haspopup="dialog"`; ein Textlink am Zeilenende wurde 0.958.1
  nicht gefunden). Kein gemerkter Auf/Zu-Zustand mehr.
  **Fenster** (`WoodpeckerStatsDialogComponent`): Inhalts-Dialog nach 5.6 — 760 px / `94vw`, ein Scrollbereich mit
  Weiterscroll-Schatten, Titel „Statistik“, darunter leise Trainingsname · „Zyklus n von 7“, EINE Knopfzeile (kein ✕),
  `autoFocus: 'dialog'`; Esc und Klick daneben schließen, der Fokus kehrt zum Knopf zurück. **Die Aufgabe steht, solange
  es offen ist:** die Lösezeit zählt nicht (angezeigte Uhr und gemeldete Zeit), das Brett dahinter ist unkenntlich
  (`filter: blur(18px)`, ohne Übergang — Pausieren ist keine Gratis-Denkzeit). Läuft gerade eine Aufgabe, sagt eine leise
  Zeile mit `pause_circle` „Die Uhr der Aufgabe steht, solange dieses Fenster offen ist.“ und der Hauptknopf heißt
  „Weiter lösen“, sonst „Schließen“.
  **Übersicht ganz oben** (`.wps-top`, durch eine Haarlinie vom Rest getrennt) — das, wofür man das Fenster öffnet,
  deshalb in Lesegröße (1 rem) und Textfarbe statt leise: kleine Tabelle mit den Spalten „Bisher“ (halbfett) und je
  früherem Zyklus „Zyklus k“ (jüngster zuerst), Zeilen „Zeit“ (`formatSecondsClock`) und „Richtig“ (`| pct`); die
  früheren Zyklen zeigen DIESELBEN Aufgaben wie jetzt (ein leiser Satz sagt es). Ist die Schnittmenge kleiner als die
  jetzt bearbeiteten Aufgaben, steht je betroffenem Zyklus ein Satz mit Anzahl und den Jetzt-Werten auf genau diesen
  Aufgaben — nie Gesamtzeit gegen Teilmenge. Darunter eine `.rh-dots`-Zeile mit dem **Tagesziel** (seit 0.993.0, der
  geteilte Baustein unten): „Heute: 23 von 45“ in Textfarbe bzw. grüner Haken + „Tagesziel erreicht“, dahinter klein in
  Klammern die Herleitung „(247 offen + 23 heute, verteilt auf 6 Tage)“. Frist vorbei → „Frist überschritten“
  (`--rh-warn`) + „n offen“ + „Heute: n“ statt des Ziels; Zyklus 1 hat kein Soll (der Satz wächst) → „bisher n pro Tag“
  + „Heute“. Ohne Versuch „–“ statt 0:00 / 0 %, das Tagesziel steht trotzdem. Direkt darunter als eigene Zeile der
  **Zeitplan** (seit 0.994.0, ausführliche Form des zweiten geteilten Bausteins unten): „Zeitplan (Tag 8 von 14): noch
  12 Aufgaben bis 57 %“ bzw. „Zeitplan (Tag 8 von 14): ✓ 57 % erreicht (aktuell 61 %)“; gleiche Größe und Textfarbe wie
  das Tagesziel, ab Zyklus 2, auch nach der Frist (dann ohne Tagesangabe: „Zeitplan: noch 40 Aufgaben bis 100 %“). Die
  Tabelle hat ihren eigenen `overflow-x: auto`-Kasten (viele Zyklen, 4.4).
- **Woodpecker-Tagesziel = EIN Baustein an drei Stellen** (0.993.0, Nutzerwunsch 18.11.2026: „oben auch ohne Statistik
  visualisieren, wenn ich heute das notwendige Minimum erledigt habe (sowohl im Dashboard als auch wenn ich drinnen
  bin)“, „grüne Checkmark oder so vielleicht“): `app-woodpecker-daily-goal` (`features/woodpecker/`) steht im Löser-Kopf
  am ENDE der Zyklus-Zeile (`.wp-meta.rh-dots`, bricht als Ganzes um; dahinter seit 0.994.0 nur noch der Zeitplan), auf der Dashboard-Kachel als eigene
  Zeile unter der Frist (8.1) und im Statistik-Fenster in der Je-Tag-Zeile. **Noch offen:** leise „Heute: 23 von 45“
  (Farbe der Zeile, `tabular-nums`), kein Symbol. **Erreicht:** Symbol `check_circle` (1,3 em) und „Tagesziel erreicht“
  halbfett in `--rh-success`, die Zahl „45 von 45“ klein dahinter — das Hauptzeichen ist der grüne Haken, die Bedeutung
  steht aber immer auch als Klartext daneben (nie nur Farbe/Symbol: 7, E-Ink 3.6; das Symbol ist `aria-hidden`, kein
  Tooltip nötig). **Zyklus 1** (kein Soll): nur „Heute: n“. Kein Ziel (Pause, bereit, abgeschlossen, archiviert, Frist
  überschritten, Server ohne `todayAttempted`) → der Baustein wird gar nicht gerendert, damit in der `.rh-dots`-Zeile
  kein Trennpunkt ohne Angabe steht. Kein zweiter Fortschrittsbalken (der Zyklus-Balken bleibt der einzige). Das Soll
  ist über den Tag fest (`⌈(offen + heute) / Kalendertage bis zur Frist⌉`, `docs/api/woodpecker.md`), damit „erreicht“
  auch erreichbar ist und alle drei Stellen dieselbe Zahl zeigen.
  **Darunter** leise (0,85 rem, `--mat-sys-on-surface-variant`, `tabular-nums`) in Abschnitten mit kleiner h3 (0,85 rem,
  halbfett, `--mat-sys-on-surface`), feste Reihenfolge, und nichts doppelt zur Übersicht: Dieser Zyklus („127 von 141
  gelöst“ halbfett ohne Quote, „121 ohne Tipp“, „9 mit Tipp“ in `--rh-hint`, „Ø 0:51 je Aufgabe“; Hochrechnung „Rest ≈ …
  Lösezeit · bisher 47 pro Tag · fertig ≈ 23.11.2026, rechtzeitig“ in `--rh-success` bzw. „…, Frist …“ in `--rh-warn`;
  Zyklus 1 ein Satz „Bei diesem Tempo (10 pro Tag) ≈ 290 Aufgaben im Satz bis zur Frist“) → Dieselben Aufgaben (gemeinsam
  · Anteil der Zeit, ≤ 50 % in `--rh-success` · Faktor „1,5× schneller“ über `DecimalPipe` `1.1-1` · „127 statt 131 von
  141“; ältere Zyklen je eine Zeile mit Anteil) → Wechsel je Aufgabe (verbessert in `--rh-success`, verschlechtert in
  `--rh-warn`; schneller/langsamer, gespart/verloren; Zeitstufen-Tabelle jetzt/damals) → Hochrechnung (gesamt, Vorzyklus,
  Rest; Ziel „halbe Zeit“ erreicht/nicht) → Tipps (Tabelle Anzahl + Anteil je Stufe; vor dem Stichtag ein Satz „Tipps
  werden erst seit … gezählt“, nie eine vorgetäuschte 0) → Verlauf (Tabelle je ganzem Zyklus, laufender halbfett, „–“
  wo nichts belastbar ist) bzw. in Zyklus 1 „Je Tag“ (jüngste 14) → Sorgenkinder (Bezeichnung · Zeiten · Link „ansehen“,
  nie Stellung/Lösung, 2.7). Jede Angabe als eigenes `.rh-dots`-Kind ohne Umbruch in sich (die Tageszeile der Übersicht
  darf umbrechen); Tabellen rechtsbündig + `tabular-nums` in eigenem `overflow-x: auto`-Kasten (Verlaufstabelle
  `min-width: 420px`), damit bei 390 px weder Seite noch Fenster waagerecht scrollen (4.4, 5.3). Laden = schmaler
  `mat-progress-bar` statt Seiten-Spinner, Fehler = `app-load-error` mit „Erneut versuchen“, leer = ein Satz (5.10).
  Gelöst mit Tipp zählt als gelöst. Datum `mediumDate`, Tempo `number:'1.0-1'`.
- **Woodpecker-Zeitplan = Geschwister des Tagesziels an denselben drei Stellen** (0.994.0, Nutzerwunsch 18.11.2026:
  „neben x pro Tag für fertig auch noch ein Checkmark, wenn ich % der Gesamtpuzzles passend zum aktuellen Tag hab (und in
  Statistik oben ausgewiesen: noch x Puzzle bis z. B. 55 % erfüllt)“): `app-woodpecker-schedule` (`features/woodpecker/`)
  steht im Löser-Kopf direkt HINTER dem Tagesziel als letzte Angabe der Zyklus-Zeile (`.wp-meta.rh-dots`, bricht als
  Ganzes um — am Handy eine dritte Zeile, nie breiter als die Karte), auf der Dashboard-Kachel als eigene Zeile UNTER
  dem Tagesziel (zwei erreichte Ziele = zwei Haken untereinander) und im Statistik-Fenster als eigene Zeile der
  Übersicht. Beantwortet eine zweite Frage als das Tagesziel: liegt der bearbeitete Anteil des Satzes dort, wo er am
  heutigen Tag des Zyklus liegen soll (`⌈Satz × Tag / Tage⌉`, `docs/api/woodpecker.md`)? **Im Zeitplan:** Symbol
  `check_circle` (1,3 em) + „Im Zeitplan“ halbfett in `--rh-success`, der Soll-Anteil („57 %“, `| pct`) klein dahinter —
  gleiche Gestalt wie „Tagesziel erreicht 45 von 45“, Symbol `aria-hidden`, Bedeutung immer auch als Klartext (7, E-Ink
  3.6). **Dahinter:** leise „Zeitplan: noch 12 bis 57 %“ in der Farbe der Zeile — bewusst NEUTRAL, keine Warnfarbe und
  kein Symbol: am Vormittag hinter dem Soll von heute Abend zu liegen ist der Normalfall, kein Fehler. **Ausführlich**
  (nur im Fenster): Beschriftung mit dem Tag „Zeitplan (Tag 8 von 14):“, die Anzahl mit Hauptwort („noch 12 Aufgaben
  bis 57 %“), erreicht „57 % erreicht“ und klein in Klammern der Stand „(aktuell 61 %)“ (0,85 em,
  `--mat-sys-on-surface-variant`, wie die Herleitung des Tagesziels). Haken und sein Text sind EIN nicht umbrechendes
  Teil (der Haken stünde sonst bei 360 px allein hinter der Beschriftung). Kein Zeitplan (Zyklus 1 — der Satz wächst
  noch —, Pause, bereit, abgeschlossen, archiviert) → der Baustein wird gar nicht gerendert. Anders als das Tagesziel
  bleibt er nach der Frist stehen („Zeitplan: noch 40 bis 100 %“) und braucht `todayAttempted` nicht. Kein zweiter
  Fortschrittsbalken und keine Markierung im Zyklus-Balken.
- **Tagespuzzle** nennt sich: Chip „Tagespuzzle“ + Datum (`mediumDate`, UTC) in der Kontext-Card wie der Wochenpost-Chip (d4).
- **Tagespuzzle hat kein „nächstes“**: nach Lösen/Fehlversuch bleibt die Lösung stehen, kein Countdown (auch nicht im
  Brett-Vollbild). Der Hauptknopf „Nächstes Puzzle“ (ohne Zahl) bleibt und startet auf Klick die Zufallspuzzles
  (`/puzzles`, wie „Puzzles lösen“ auf der Startseite); Tage blättert weiter die Datumsleiste (1.013.1).
- Gleiche Erweiterung in allen drei Puzzle-Modi prüfen (Standard, Endless, Buch/Kurs; `rookhub/CLAUDE.md`).

### 5.13 Zahlen, Mehrzahl, Datum

- **Einzahl/Mehrzahl** über ein Schlüsselpaar `…` / `…One`, gewählt im Template:
  `{{ (n === 1 ? 'reconstruct.gapsOne' : 'reconstruct.gaps') | translate:{ count: n } }}` (14 Paare in
  `public/i18n/de.json`). Ist: 16 Texte noch mit Klammerform („Linie(n)“, „Repertoire(s)“). **Regel ab jetzt:**
  keine Klammerform; beim Anfassen auf das Schlüsselpaar umstellen.
- **Datum/Uhrzeit** in der App-Sprache (Locale-Daten für `en/de/hr/hu` registriert, `core/locale-data.ts`):
  deutsch „16.07.2026, 21:00“; Zeiträume „… – …“ (Halbgeviertstrich, r2-t-date-format); ClubHub TT.MM.JJJJ.
  **Datumsfelder** nie als `<input type="date">` (formatiert nach der Browsersprache: „10/10/2026“ in der deutschen
  Oberfläche), sondern `app-iso-date-field` (`shared/date-field/`, Material-Datepicker mit `LocaleNativeDateAdapter`,
  Wert nach außen "YYYY-MM-DD"; b1-rk-edit-datum-us-format). **Zeitpunkte** ebenso nie als `datetime-local`
  („mm/dd/yyyy, --:-- --“, 12-h-Uhr), sondern als Paar Datum + Uhrzeit `app-date-time-fields` (`shared/date-field/`,
  Datepicker + Timepicker 15 min, Wert "YYYY-MM-DDTHH:mm" in Lokalzeit; Datum ohne Uhrzeit → 00:00 sichtbar;
  `date-format-usage.spec`, d8-rk-ausgabe-datetime-us-format).
  **Keine festen Muster** (`dd.MM.yyyy`, `HH:mm` …) in der DatePipe, nur Locale-Formate (`date-format-usage.spec`,
  0.761.0): Datum mit Wochentag `EEE` + `, ` + `mediumDate` (+ ` · ` `shortTime`) — de „Do., 09.07.2026 · 21:00“,
  en „Thu, Jul 9, 2026 · 9:00 PM“; knappe Zeitpunkte in Listen/Tabellen `short` — de „11.10.26, 00:20“, en
  „10/11/26, 12:20 AM“; Uhrzeit allein `shortTime`/`mediumTime`.
- **Trenner** zwischen Angaben: „ · “; als `::before` des folgenden Elements, damit er beim Umbruch nie allein am
  Zeilenende steht (r2-t-meta-dot); in Flex-Zeilen mit Lücke (ClubHub `.kid-meta`) absolut in der Lücke, am
  Zeilenanfang per `overflow-x: clip` abgeschnitten (8.5). **Regel ab jetzt (RookHub/Turnierseite):** das gemeinsame
  Muster `.rh-dots` (`styles.scss`) — jede Angabe ein eigenes Kind, kein Text-„ · “ im Markup; für zentrierte Zeilen
  `.rh-dots--center` (Kinder wachsen, allein in einer Zeile füllen sie die Box, ihr Punkt fällt damit weg). Nutzer:
  Aufgabenblatt-Liste, Brett-Kopf des E-Book-Lesers (0.808.x, d4-rk-brett-kopf-punkt-umbruch). Leerzeichen zwischen Interpolation und `@if` fest ins Markup schreiben —
  Angular entfernt sie sonst („Spielennoch offen“, 0.751.2).
- **Spielertitel** (RookHub, 1.009.0): FIDE-Titel (GM, IM, WGM, FM, WIM, CM, WFM, WCM) stehen VOR dem Namen, mit U+00A0
  daran gebunden, als eigenes Element `.ptitle`: 0,85 em, fett (700), Laufweite 0,03 em, `--rh-accent` — abgehoben vom
  Namen, ohne Pille oder Rahmen (Partieseite/Teilen-Link `/games/:id`, `/g/:token`, Partienliste `/games`). In Text ohne
  Auszeichnung (Link-Vorschau) einfach „IM Grötz, Harald“. Nationale Titel (MK, NM …) erscheinen nicht. LeagueHub zeigt
  Titel bisher als reinen Text vor dem Namen (8.4).
- Ländernamen ausgeschrieben, nicht als Code (LeagueHub, 0.751.0).
- **Ergänzungsstrich** („Fortschritt und -Statistiken“) nie allein am Zeilenende: vor dem Strich U+00A0, danach
  Wortverbinder U+2060 („und -⁠Statistiken“, 0.763.0, `i18n-parity.spec`). Vor einem Schrägstrich ebenso:
  „Chess.com-⁠/Lichess“ mit U+2060 zwischen Strich und „/“ — WebKit bricht sonst dort, die Zeile beginnt mit „/“ (0.765.0).
- **Gedankenstrich** („bereit — versuch es …“) nie am Zeilenanfang: davor U+00A0 statt Leerzeichen — mit
  `text-wrap: balance` brach der kurze Grund unter „Partie analysieren“ genau davor um (0.807.x, `*NoEngine` in
  `i18n-parity.spec`, d3-rk-partie-grund-strich-zeilenanfang). Ein Nachsatz mit Strich als `::before` (`"\00a0–\00a0"`)
  steht nicht in einer Flex-Zeile (dort rutscht er als Ganzes mit „–“ vorn um), sondern im Fließtext
  (`app-sheet-reading-status`, d6-l-wartet-leser-gedankenstrich). Laufende Uhren ab einem Tag in Tagen und Stunden
  („seit 12 Tagen 10 Std.“, `clockDays` + `sheetEdit.status.sinceDays…`), nie „298:59:11“.
- **Zahl und Einheit/%** mit geschütztem Leerzeichen (U+00A0) zusammenhalten, auch in Übersetzungen hinter einem
  Platzhalter („{{percent}} %“) — sonst steht „%)“ allein in der nächsten Zeile (0.759.0, `i18n-parity.spec`).
  **Sprachausnahme Prozent** (0.761.0): Englisch und Ungarisch schreiben „46%“ ohne Leerzeichen (en/hu-JSON
  „{{percent}}%“), Deutsch und Kroatisch „46 %“ mit U+00A0. Prozent im Code immer über **`formatPct`/`| pct`**
  (`shared/format/pct.ts`, `Intl.NumberFormat` `style: 'percent'` in der Oberflächensprache) — nie „ %“ oder „%“
  anhängen, nie `toLocaleString(undefined)` (Browser- statt App-Sprache).
  Die Gesamt-Trefferquote (Puzzles) steht überall mit höchstens einer Nachkommastelle (`pct:1`: Startseite, Statistik,
  Freundesvergleich, Puzzle-Löser); Themen- und Bandwerte ganzzahlig (0.808.x, d4-rk-dashboard-prozent-rundung).

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
- **Englische Knöpfe und Beschriftungen in Satzschreibung** („Give up“, „Next puzzle“, „Show eval“); Überschriften
  dürfen Title Case behalten (d3-rk-puzzle-button-case-inconsistent). Gilt auch für Menüeinträge, Links und
  Kacheltitel der Startseite („Puzzle history“, „Solve puzzles“, „Training goals“); ein Seitentitel, der zugleich als
  Link dient, bekommt einen eigenen Link-Schlüssel („Privacy policy“ = `legal.privacy.link`, Titel „Privacy Policy“).
  `features/d4-partien-texte.spec.ts` prüft `nav.*`, `dashboard.*` und `*.actions/button/menu/label/link.*` (0.808.x).
- **Englisch britisch:** „analyse/analysed/Analyse game“, nie „analyze“ (Schlüsselnamen und Platzhalter ausgenommen;
  0.808.x, d4-rk-en-analyse-analyze-uneinheitlich, Test wie oben).
- **Englische Anführungszeichen typografisch “…”** (innen ‘…’) in `en.json` — keine »…«, „…“ oder geraden `"`
  (ausgenommen Syntax: HTML-Attribute, PGN-Tags in Platzhaltern); `i18n-parity.spec` prüft es (0.761.0).
- Ein deutsches Wort im englischen Text nur als erkennbarer Befehl/Name mit Erklärung („/motivation, option “aus”
  (off)“); Sprachnamen im Satz in der Oberflächensprache (`Intl.DisplayNames`: „Original language: German“), die
  Eigenbezeichnung („Deutsch“) nur in Sprachauswahlen (0.761.0).
- Changelog-Einträge zweisprachig (`en`/`de`) in `src/environments/changelog-data.ts`.
- Platzhalter aus Fremddaten nicht anzeigen: PGN-„?“ heißt „ohne Name/Kapitel“ (r2-rg-rep-question),
  chess-results-„various“ heißt „verschiedene Spielorte“ (kursiv, r2-t-various), keine internen Dateinamen
  (r2-rp-sourcefile).
- **Glossar (eine Form je Begriff):** „Schlagwörter“ (nie „Schlagworte“; d7-rk-schlagworte-schlagwoerter-uneinheitlich,
  `ebook-edit-dialog.component.spec`); „Lc0“ mit großem L (d7-rk-lc0-dialog-info-icon-abgeschnitten).

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
  64×48; LeagueHub- und ClubHub-Fußlinks am Handy; ClubHub `.btn`). Kleinere Symbole vergrößern ihre Trefferfläche unsichtbar
  (`::after { inset: -6px }`, `@media (pointer: coarse)`); ClubHubs Textknöpfe `.btn-link` immer per `::after` auf ≥ 44 px
  Höhe, ohne die Zeile zu strecken.
- **Fokus sichtbar:** `:focus-visible` — LeagueHub/ClubHub global `outline: 3px solid var(--focus); outline-offset: 2px`;
  KidHub-Pillen `:focus-within`. `outline: none` nur mit Ersatz.
- **Zustände benennen:** Umschalter mit `aria-pressed`, Aufklapper mit `aria-expanded`, Zustandszeichen mit
  Klartext im `aria-label` (ClubHub-Anwesenheit ✓/✗, r2-c-attendance-da), Fehler mit `role="alert"`,
  versteckte Beschriftungen per `.sr-only`.
- **Reiter nicht `[disabled]`** (dort feuert kein Tooltip): wählbar lassen, Grund im Inhalt (r2-t-history-disabled-tab).
- Animationen respektieren `prefers-reduced-motion` und den E-Ink-Modus (3.6: keine Pflicht-Animationen, Bedeutung nie nur über Farbe).

---

## 8 Bereiche

### 8.1 RookHub (`src/`)

- Material 22 (M3, Azure), dunkel als Vorgabe, `ThemeService` (3.1). Navbar (`shell/navbar.component.ts`, Messung in `shared/navbar/nav-edge.ts`): ≤ 768 px
  Hamburger; abgemeldet ≤ 480 px nur „Anmelden“ und ☰ (0.740.0). Fußzeile (`shared/app-footer/`) bleibt auf
  kurzen Seiten unten; Links ≥ 44 px.
- **Trainerstunden** `/training-videos` (0.986.0, privat, Menüpunkt Admin-Stufe; Doku `docs/frontend/trainerstunden.md`):
  Über der Liste ein Umschalter (`mat-button-toggle-group`, ≥ 44 px) „Private Partien (n)“ / „Allgemeine Partien (n)“
  mit einem Satz darunter, was die Gruppe enthält (0.988.0); leere Gruppe = eigener Leersatz, nie gesperrt.
  Liste als gleich hohe Karten (5.2) — Datum `mediumDate` als Titel mit „· Teil“ in normaler Stärke, Meta `.rh-dots` (Dauer h:mm:ss `tabular-nums`,
  „18 Stellungen“, „ohne Video“), Zusammenfassung über `appClampMore` auf 3 Zeilen, Partien „Weiß – Schwarz“ + Art als
  `.chip`; genau eine Primäraktion „Ansehen“, daneben „Nachspielen“ umrandet. Suche über der Liste: EIN Feld „Wort oder
  FEN“ (FEN wird erkannt) + „Suchen“ + „Brett aufbauen“ (Stellungs-Editor im Dialog, Muster E-Book); gesuchte Stellung
  als Mini-Brett 160 px + Zugrecht, nie als rohe FEN (5.12). Treffer als umrandete Zeilen: Datum · ▶ Zeit · Partie,
  Ausschnitt, Textknöpfe „Ansehen“ / „Nachspielen ab hier“ (≥ 44 px); „anderes Zugrecht“ als Zustands-Chip.
  **Teilen** (Wunsch 2026-11-17): unter der Stunde ein Abschnitt „Teilen“ mit Trennlinie oben — ein Satz, was der Gast
  sieht und was privat bleibt; Frist als `mat-button-toggle-group` (1 / 7 / 30 Tage, Vorgabe 7) + „Link erstellen“ als
  einzige Primäraktion (kopiert gleich); aktive Links als umrandete Zeilen: „Gültig bis …“ (`medium`, `tabular-nums`),
  der Link als umbrechender `code`, „Kopieren“ umrandet + „Widerrufen“ als Textknopf (Rückfrage, ≥ 44 px). Gast-Ansicht
  `/tl/:token`: dieselbe Seite ohne Zurück-Pfeil, Teil-Wechsel, „Partie öffnen“ und Teilen, dafür ein grauer Satz „nur zum
  Ansehen“; abgelaufen/widerrufen = zentrierte Leerseite mit `link_off`-Symbol, Titel „Link abgelaufen“ und einem Satz.
  **Video + Segmentliste** (Stundenseite): natives `<video controls preload="metadata">` (volle Breite, ≤ 70vh), darunter
  das Brett (≤ 420 px) mit der Stellung des gewählten bzw. gerade laufenden Segments, darunter Zug/Kommentar/Zitat;
  die Segmentliste als Knöpfe (Zeit, Zug über `| san`, Schlagwörter als `.chip`, Kommentar), gewählt = 2-px-Akzentrahmen
  + Fläche + `aria-current` (E-Ink: Innenrahmen + fett, nie nur Farbe). Ab 900 px zwei Spalten (Video/Brett | Liste),
  die Liste scrollt dort in ihrem Kasten und folgt dem Video nur in sich (nie die Seite, 5.3); darunter einspaltig.
  Ohne Video ein Satz statt des Players. **Sprecher** (Wunsch 2026-11-17, nur wenn Sprecherwechsel da sind): direkt
  unter dem Video eine 8-px-Leiste (Touch 12 px, Klick springt, `aria-hidden`) mit den Beiträgen in zwei Tönen —
  Akzent voll / Akzent 35 % (in Graustufen unterscheidbar); darunter links die Legende (Farbfeld + Name, der gerade
  Sprechende fett), rechts ⏮ als Symbolknopf „Zum Anfang des Redebeitrags“ und „Nächster Sprecher“ (`skip_next`,
  umrandet, Tooltip „Nächster Sprecher: Name ab Zeit“). Keine Tastenkürzel (die Seite hat keine). **Nachspielen**: Kopf mit Zurück-Pfeil, Titel, Schalter „Nur Falsche
  wiederholen“; „Stellung 4 / 18“ und Partie-Kopf beim Partiewechsel; Brett (≤ 480 px, zur Seite am Zug gedreht) |
  Seitenspalte ab 900 px. Aufgabe: Satz (Frage bzw. „Welcher Zug …?“), Meldung als umrandete Zeile mit Text (richtig
  `--rh-success`, falsch `--rh-error`, Bedeutung im Text), „Hinweis“ (Kreis um die Figur + Satz mit Feld), „Auflösen“
  (Pfeil, zählt falsch), „Überspringen“ als Textknopf; danach „Weiter“ als einzige Primäraktion. **Ausschnitt-Player
  „Erklärung hören“**: ein umrandeter Knopf (Text wechselt zu „Anhalten“, `aria-pressed`), Zeitspanne daneben,
  Schalter „nur Ton“ (Bild aus, Ton läuft; Vorgabe AN im E-Ink-Modus), kompaktes Video ≤ 360 px nur bei „nur Ton“ aus;
  hält am Ende des Ausschnitts selbst an. Endbild: ✓/✗-Zähler als Text, „Falsche wiederholen“ primär.
  **Trainerkommentar an der Partie** (`/games/:id`, nur eigene Partie): eigene Karte in der Seitenspalte VOR den
  Repertoires, sichtbar getrennt von der Engine-Erklärung („Warum war das ein Fehler?“, 💬) — Rahmen `--mat-sys-outline`,
  4-px-Kante + 6-%-Fläche `--rh-info`, Überschrift „🎓 Trainerkommentar · Datum“; Nebenvariante mit eigenem Mini-Brett
  (120 px); Links „▶ im Video ab 0:12:10“ (nur mit Video) und „Nachspielen“. Darüber eine leise Sprungzeile
  „Trainerkommentare zu 1.e4 4…Ld7 …“ (Textknöpfe, Zug am Brett fett + durchgezogen unterstrichen, `pointer: coarse`
  44 px). Ohne Kommentar zur Partie: gar nichts (kein Leersatz).
  **Trainer-Notizbuch** (`/training-videos/notebook`, 0.987.0; Kopf mit Zurück-Pfeil, Link „Notizbuch“ umrandet im
  Kopf der Liste vor „Fragen-Satz“): drei Abschnitte mit `h2` — „Sagt er immer wieder“ (umrandete Zeilen: „in 4
  Stunden“ fett + Kategorie als `.chip`, stellvertretender Satz halbfett, Textknopf „Alle n zeigen“ klappt die Stellen
  darunter auf), „Eröffnungsempfehlungen“ (Zeile mit Mini-Brett 160 px, zur Seite am Zug gedreht, daneben Notiz und
  Repertoire-Satz mit 📖-Symbol „Im Repertoire „X“: **Sd2, Sc3**“ bzw. leise „Kommt in deinem Repertoire nicht vor.“ —
  ohne eigenes Repertoire keine Zeile; Textknöpfe „Im Repertoire öffnen“ / „Im Analysebrett öffnen“; erst 6, dann „Alle
  n zeigen“ umrandet) und „Alle Notizen“: Suchfeld + „Suchen“ umrandet + zwei Auswahlfelder (Art, Partie) in einer
  umbrechenden Zeile, Kategorien als `mat-chip-listbox` mit „(Anzahl)“ als Reiter (nie leere Reiter), darunter je Stunde
  ein `h3` (Datum, Link zur Stunde) und die Notizen als Zeilen mit Trennlinie: Satz, `.rh-dots`-Meta (Partie als Link +
  Art-`.chip`), Textknöpfe „▶ anhören“ (≥ 44 px, wird zu „Schließen“) und „Rohzitat“ — beide klappen nur unter IHRER
  Notiz auf (Ausschnitt-Player wie im Nachspielen, startet sofort; Zitat als `<blockquote>` mit 3-px-Kante, `<q>` setzt
  die Anführungszeichen der Sprache). Keine Primäraktion auf der Seite (reines Nachschlagen).
- „RookHub weltweit“ `/visitors` (0.982.0, anfangs „Besucher-Herkunft“): Seitenkopf + ein Satz; Kennzahlen als Zahlenreihe (`dl`, Zahl 1.75rem
  halbfett `tabular-nums`, Beschriftung darunter 0.85rem/60 %) — die Hauptzahl heißt „Eindeutige Benutzer“, ohne
  Zeitraum-Zusatz. **Flächenkarte in fünf Stufen** aus dem Akzent: `color-mix(--mat-sys-primary 30/46/62/80/100 %,
  --mat-sys-surface)`, „kein Wert“ = 12 % `--mat-sys-on-surface`; Stufe logarithmisch gegen den größten Wert des
  Ausschnitts (`visitorStep`). Farbe ist nie alleiniger Träger: die Zahl steht in der Rangliste (echte `<table>` mit
  Balken unter dem Namen), am Mauszeiger und nach Antippen in der Zeile unter der Karte (`aria-live`); Skala
  „wenige … viele“ unter der Karte. Karte `role="img"` mit `aria-label` — sie ist Zeiger-Ergänzung, die Bedienung per
  Tastatur läuft über die Chips und die Tabelle. Kontinent-Wahl und Admin-Filter als `mat-chip-listbox` (einfach,
  `required`), Zustand in der URL (`?continent=`, Admin `?days=&surface=`); in der Kontinent-Ansicht zusätzlich ein
  Knopf „Zurück zur Welt“ vor den Chips, Länder anderer Kontinente blass (5 %). Karte neben Rangliste per
  **Containerabfrage** ab 860 px Seitenbreite (4.3), darunter untereinander; Kartenhöhe ≤ min(70vh, 560 px). Die
  Filterleiste sieht nur der Admin (der Server verwirft die Parameter anderer Konten). Leer: eine Leerkarte mit einem
  Satz (5.10). E-Ink: dieselben Stufen als Helligkeitstreppe Weiß → 22 → 36 → 58 → 80 % → Schwarz aus den
  Flächen-Tokens, Ländergrenzen als Linien, Auswahl gestrichelt umrandet, Balken dunkel auf umrandeter Bahn.
- Partienliste `/games` (0.949.0): Laschen „Alle | Online | Am Brett“ als `mat-tab-nav-bar` mit `mat-tab-link` (die
  Lasche ist ein Filter derselben Liste, kein eigener Inhalt — deshalb Links mit `?kind=`, nicht `mat-tab-group`), Zahl
  des ganzen Kontos in Klammern hinter dem Namen, zwischen Erklärsatz und Suchfeld; nur sichtbar, wenn das Konto
  Partien hat. Leere Lasche: ein Satz mit dem Weg dorthin (`.kind-empty`), nicht die Leerkarte des Kontos (5.10).
  Seit 0.954.0: „Nur meine Partien“ als `mat-checkbox` unter dem Suchfeld (gilt in allen Laschen, `?mine=1`); je Zeile
  Zusammenfassungs-Symbol vor ▶ (fertig `article` als Icon-Link, angefordert/beim Schreiben `hourglass_top` ohne Knopf,
  sonst leerer 40-px-Platz — die Knöpfe springen nicht), Turnier als gedämpfte dritte Zeile unter den Namen, einzeilig mit
  „…“ und vollem Namen im Tooltip.
- Dashboard: neue Kacheln nicht in `DEFAULT_VISIBLE`; angepinnte Kurse in eigener breiter Zeile (r2-rk-dash-stretch).
  **Jede Kachel ist als Ganzes klickbar** (5.2, 0.997.0): die Fläche gehört der ersten Aktion der Kachel (Puzzles: „Puzzles
  lösen“; „Tagespuzzle“ und „Endlos“ bleiben eigene Ziele), die Aktionen sind echte Links. Bei den angepinnten Kursen
  ist jede Kurs-Zeile die Einheit (Ziel = Kursname → Kurs-Detailseite; „Der Reihe nach“/„Zufällig“ eigene Ziele), die
  Sammel-Kachel selbst führt nirgends hin; die Uhr-Kachel (Bedienfeld) ist keine klickbare Kachel. Woodpecker: Zyklus
  läuft → „Weiter lösen“, Pause → „Zur Übersicht“, bereit → die Fläche führt wie der Name zur Übersicht („Zyklus N
  starten“ ändert Daten und bleibt dem Knopf vorbehalten). Im Anpassen-Modus ist keine Kachel klickbar.
  **LeagueHub-Kachel** (0.983.0): dritte Ausnahme in `DEFAULT_VISIBLE` (am Ende) — es gibt sie nur für Konten mit
  `league.view` und nur, wo sich die LeagueHub-Adresse bilden lässt; Symbol `shield`, Titel „LeagueHub“, ein Satz, genau
  ein Knopf „LeagueHub öffnen“ mit `open_in_new` (verlässt RookHub, Sprung mit Anmelde-Übergabe). Im Menü steht
  „LeagueHub“ unter „Community“ direkt nach „Turniere“, ebenfalls mit `open_in_new`. Kacheln, die auf eine andere
  Oberfläche führen, tragen dieses Symbol im Knopf; Kacheln mit Router-Ziel bleiben ohne.
  Ausnahme sind Kacheln, die nur mit eigenen Daten erscheinen (angepinnte Kurse, Woodpecker): sie stehen in
  `DEFAULT_VISIBLE`, sind ohne Daten aber unsichtbar und zeigen sich nur im Anpassen-Modus (dort mit einem Leer-Satz),
  damit man sie vorab einordnen kann. **Woodpecker-Kachel** (0.955.0, `features/dashboard/woodpecker-tile.component.ts`):
  das laufende Training (nicht abgeschlossen; bei mehreren das zuletzt bewegte), Untertitel „Zyklus N von 7 · Status“
  (`.sub-part`), darunter der Name (bis zwei Zeilen), in Zyklus 1 „Satz: n Aufgaben von N“, ab Zyklus 2 „n von N
  erledigt“ mit 6-px-Balken, dann „noch N Tage“ bzw. „Frist überschritten“ (`--rh-warn`), darunter seit 0.993.0 das
  Tagesziel als eigene leise Zeile (0,85 rem; „Heute: 23 von 45“ bzw. grüner Haken + „Tagesziel erreicht“, Baustein
  aus 5.12; nur solange der Zyklus läuft und die Frist nicht vorbei ist) und direkt darunter seit 0.994.0 der
  Zeitplan als weitere leise Zeile („Zeitplan: noch 12 bis 57 %“ bzw. grüner Haken + „Im Zeitplan“ + „57 %“, Baustein
  aus 5.12; ab Zyklus 2, auch nach der Frist); genau eine Aktion: „Weiter
  lösen“ (Kurs-Löser, gefüllt), bereit → „Zyklus N starten“ (gefüllt, startet den Zyklus direkt und springt in den Löser;
  während des Aufrufs gesperrt mit 16-px-Spinner statt Symbol, Fehler als Snackbar mit dem API-Grund; seit 0.958.0,
  vorher „Zur Übersicht“), Pause → Pause-Satz + Textknopf „Zur Übersicht“. Kein Prozent-Text; nur mit Menüpunkt
  `woodpecker`. Archivierte Trainings erscheinen nie; einen Hinweis „+N weitere“ gibt es nicht (es gibt nur ein aktives
  Training, Nutzerentscheidung 10.11.).
- Löser: Aktionen über `PuzzleActionBarComponent`; Spielweise per `SolveModeService` (einmal je Bereich, für
  Kurse je Kurs; „für alle Puzzle-Bereiche übernehmen“).
- Admin-Seiten: 1400 px, Tabellen mit Karten-Fassung am Handy; Admins werden zu ihren Konversationen geführt.
- Spielervorbereitung: die Spielerkarte stammt aus LeagueHub (`features/prep/prep-card.scss`, eigenes Style-Bündel
  `prep-card`), ihre Variablen werden auf Material-Rollen abgebildet (`--ink: var(--mat-sys-on-surface)` …);
  Bedienelemente sprechen in RookHub Material (r2-rg-prep-style).
  Ihre Texte, Notation, Datum und Prozent folgen der Oberflächensprache (Abschnitt 6, e1-rk-prep-card-german).
- Spielervorbereitung, Suchseite `/prep` (0.995.0): unter dem Suchfeld steht — nur solange NICHT gesucht wird (Feld leer
  oder unter zwei Zeichen) — der Abschnitt „Frühere Vorbereitungen“ (`h2` 1,1 rem): die eigenen „Prep: …“-Repertoires,
  jüngste zuerst, je Gegner eine Zeile im Stil der Trefferliste. Zeile = Name, darunter die Meta-Zeile als `.rh-dots`
  (5.13): „Vorbereitet am <mediumDate>“, eigene Farbe („ich mit Weiß“), dann Partien · Jahre · „Elo bis …“ wie beim
  Treffer. Zugeordneter Gegner → Link auf `/prep/<id>`; nicht eindeutig zugeordnet → `<button>`, der das Suchfeld mit dem
  Namen füllt und sofort sucht, Meta-Zeile endet dann mit Lupe + „Spieler suchen“ statt der Kennzahlen. Rechts je Zeile
  der Symbolknopf `library_books` zum Trainings-Repertoire (5.3 „Listenzeile mit zweitem Ziel“). Sobald gesucht wird,
  weicht der Abschnitt den Treffern. Ohne Vorbereitungen gibt es ihn gar nicht (kein Leerzustand-Satz — die Hauptsache
  der Seite ist die Suche); scheitert das Laden, stehen Überschrift + `app-load-error` mit „Wiederholen“ (5.10), nie
  nichts. Kein Ladekreis — die Liste erscheint, wenn sie da ist. Der Hinweis unter dem Suchfeld läuft mit
  `subscriptSizing="dynamic"` (5.8), damit seine zweite Zeile am Handy nicht auf der Überschrift liegt.
- E-Book-Bibliothek (`features/ebooks/`, 0.764.0): Karten als **Regal** — Titelbild im Verhältnis 2:3 oben (ohne Bild:
  Symbol + Titel auf getönter Fläche), Lesestand als dünner Balken an der Bildunterkante, Titel zweizeilig mit „…“,
  Formate/Seitenzahl/Sichtbarkeit als `.chip`-Abzeichen, Aktionszeile unten (Hauptaktion links, ⋮ rechts); Raster
  `repeat(auto-fill, minmax(170px, 1fr))`, am Handy 150 px. Titelbild **nie beschneiden** (`object-fit: contain` auf der
  getönten Fläche, d9). Gast-Banner: ein Satz, am Handy einzeilig gebaut („Anmelden“ rechts in derselben Zeile, d9). Filter-Chips (`.eb-chip`, `aria-pressed`) wie 5.9. Der Leser
  ist ein Sonderlayout (4.5): Werkzeugleiste + Seiten in der Resthöhe, eigener Scrollkasten, Seiten auf dunklem Grund.
  Mit erkannten Diagrammen zweispaltig: Buch links, Brett rechts (`clamp(300px, 32vw, 460px)`); unter 900 px Brett als
  Vollbild über dem Buch (0.780.2; vorher Leiste unten, 0.766.0), erst nach dem ersten Doppelklick, Kopf mit ‹ › und Schließen
  klebt oben, Escape schließt. Der Trenner dazwischen ist ziehbar (8-px-Griff, 44-px-Trefferfläche,
  ←/→, Doppelklick = Standard; 0.770.0). Diagrammzonen zeigen sich erst beim Überfahren/Fokus — an Touchgeräten
  (`hover: none`) dauerhaft gestrichelt; unter 900 px steht in der Leiste ein Knopf „▦ N“, der das Brett aufklappt (c1).
  Regal: Hauptknopf füllt die Aktionszeile, einzeilig („Weiter“, Seite im Namen; Download am Handy nur Symbol);
  Filter (Schlagwörter, Sprache, Elo-Spanne) auf JEDER Breite in einer Filter-Komponente hinter „Filter ▾“ mit Zahl der
  aktiven Filter, zugeklappt aktive Filter als Chips (0.877.0); darin Schlagwörter zugeklappt die 8 häufigsten + „Alle
  Schlagwörter (n)“, aufgeklappt nach angezeigter Beschriftung sortiert; Sortierung und „Meine Uploads“ ebenfalls hinter
  „Filter ▾“, auf jeder Breite (0.877.2); über dem Regal eine leise Gesamtzeile „N Bücher · M Dateien (Formate)“; ohne Bücher keine
  Filterzeile; gefiltert leer → „Filter zurücksetzen“. Leser: 404/403 = „gibt es nicht“ + „Zurück zur Bibliothek“, keine
  App-Fußzeile (Seite genau fensterhoch).
  **EINE Leiste oben (0.785.x):** ist das Buch geladen, setzt der Leser `body.nav-docked` — die App-Kopfleiste verliert Logo
  und Hintergrund und dockt als 48 px hoher Block oben rechts auf der Leser-Leiste an (dieselbe Instanz, Glocke/☰/Vollbild
  unverändert, Gast ohne die Text-Links Puzzles/Analyse); die Leser-Leiste ist 48 px hoch und hält rechts die gemessene
  Breite der Kopfleiste frei (`--rh-nav-w`), am Handy in ihrer Titelzeile. Unter 1100 px stehen „Inhalt“ und Schrift/Zoom im ⋮
  des Lesers (oben, `.rd-more-narrow`), der Titel behält ab 600 px `min(11rem, 28vw)`, Gäste sehen angedockt nur „Anmelden“
  („Registrieren“ im ☰; d9). Lade-/Fehlerzustände behalten die normale
  Kopfleiste. Andere Seiten mit eigener Werkzeugleiste dürfen `nav-docked` genauso nutzen.
  Brett-Leiste (`app-ebook-board`, 0.777.0, kompakt 0.785.x): **eine** Zeile über dem Brett — fünf Modi als Symbolknöpfe
  (Name + Erklärung als Tooltip/`aria-label`, gewählter Modus in Primärfarbe getönt, `aria-pressed`): Ansehen, Ziehen,
  Kalkulieren, Lösen, Maia; rechts ⚙ für die Optionen des Modus (Lösen: Art Einfach/Training und Quelle als Auswahl im
  Menü; Maia: Stärke) und ⋮ (Analysieren, Engine an/aus, Brett drehen, FEN kopieren, „Diagramm falsch“). Ab 600 px
  40-px-, am Handy 44-px-Knöpfe, nie umbrechend. Zugrecht, Bewertung und Herkunft der Lösung (Symbol, Text im Tooltip)
  stehen als zweite Zeile im Brett-Kopf; nur „Lösung fraglich“ bleibt eine sichtbare Warnzeile. Unter dem Brett EINE
  Statuszeile (Zustand, „Nochmal“, Lösung zeigen als Symbol; „jeder Zug, der das Ergebnis hält“ als Tooltip). Lösen startet
  in „Training“, die eigene Wahl ist auf dem Gerät gemerkt. Brett ab 900 px bis `100dvh − 220px` (vorher − 394 px).
  **Wer löst (0.975.0):** gehört der erste Zug der Linie dem Gegner, führt „Lösen“ ihn nach 450 ms selbst aus, das Brett
  steht aus Sicht des Lösers, und die Statuszeile nennt die Farbe („Du spielst Weiß und bist am Zug“; davor „Der Gegner
  zieht zuerst …“). Ist ein Diagramm keine Aufgabe (Löser hätte die Verliererseite, Linie unspielbar), fehlt der
  Modus-Knopf „Lösen“ ganz (5.4: kein grauer Knopf ohne Grund); unter „Ansehen“ steht dann EINE leise Zeile (`info`,
  on-surface-variant) mit dem Grund und der Glühbirne für die Buchlinie.
  Engine-Linien wie auf der Partieseite (`app-live-engine-panel`).
  **Baum (0.881.0, Eröffnungsbücher):** Modus-Symbol `park`, nur bei Büchern mit Eröffnungsbaum. Unter dem Brett eine
  Schrittleiste (Grundstellung, ◀, ▶ = Hauptfortsetzung) mit „Als Repertoire speichern“ (`bookmark_add`, Textknopf rechts,
  nur mit Konto); darunter der Weg ab der Grundstellung als fließende Zugfolge (Zugnummern leise, aktueller Zug getönt wie
  in der Partie-Zugliste, Klick springt), darunter „Fortsetzungen im Buch“ als Liste ganzer Zeilen (Zug halbfett links,
  „n Linien“ leise rechts, meiste Linien zuerst, 36 px bzw. 44 px bei Touch). Kein eigener Scrollkasten. Stellung nicht im
  Baum → leiser Hinweissatz über der Leiste. Speichern-Dialog 440 px: Name, Umfang und Farbe als `mat-button-toggle`-Paare
  (Umfang mit Zugzahl), Abbrechen + Speichern.
  **Übersetzung (0.876.0):** gibt es eine fertige Übersetzung, steht in der Leser-Leiste vor dem ⋮ ein Symbolknopf
  `translate` („Sprache des Buchs“) mit Menü „Original“ / „Englisch“ (Häkchen am gewählten, `menuitemradio`); liest man
  die Übersetzung, ist das Symbol in Primärfarbe getönt. Die Wahl gilt je Gerät für alle Bücher. Regal-Karte: Abzeichen
  mit `translate`-Symbol + Sprachkürzel („EN“), Erklärung im Tooltip. Verwalter-Übersicht als Inhalts-Dialog 560 px aus
  dem ⋮ der Bibliothek.

- Woodpecker (`features/woodpecker/`, 0.773.0): Übersicht als Karten-Raster (`minmax(min(100%, 360px), 1fr)`) — je
  Training eine Karte mit Status-Abzeichen, Frist und Fortschrittsbalken, die Zyklen als kleine Zahlentabelle (erst
  nach dem ersten beendeten Zyklus) und genau einer Hauptaktion („Weiter lösen“ / „Zyklus N starten“); in der Pause ist
  der Knopf gesperrt, der Grund steht darunter. Der Löser benutzt Brett und Status-Karte der Puzzle-Modi.
  Zyklus 1: der Balken misst die verstrichene Frist und steht deshalb beschriftet (`aria-label` „3 von 28 Tagen
  vorbei“) direkt unter der Fristzeile, nicht unter „Satz: n Aufgaben von N“; der Löser nennt den Satz im selben
  Wortlaut. Ohne lösbaren Kurs zeigt die leere Übersicht gleich „Zu den Kursen“; im Dialog sind Kurszeilen ganzzeilig
  ≥ 44 px, gleichnamige Kurse tragen eine Meta-Zeile (Herkunft, zuletzt geübt) (d3). Kartentitel (Kursname) bis zwei
  Zeilen, erst dann „…“; das ⋮ bleibt oben (d6).
  **Ein Training + Archiv** (0.958.0, Nutzerentscheidung 10.11.: „soll nur ein Woodpecker geben – für einen neuen muss
  ein alter archiviert werden“): oben steht das eine aktive Training wie bisher; sein ⋮ hat „Archivieren“ (`archive`,
  vor „Löschen“) mit Rückfrage über `ConfirmService` („… archivieren? Es bleibt im Archiv lesbar, und du kannst ein
  neues Training beginnen.“, Knopf „Archivieren“). „Neues Training“ bleibt immer bedienbar (kein gesperrter Knopf ohne
  Ausweg, 2.8): gibt es ein aktives, zeigt der Dialog vorn — vor dem Datum — einen Hinweis mit Archiv-Symbol in
  Warnton-Fläche (`color-mix(--rh-warn 12 %)`, 3-px-Rand links, Text in on-surface) „Dein bisheriges Training ‚…‘ wird
  archiviert – es bleibt lesbar.“, und der Bestätigen-Knopf heißt „Archivieren und starten“. Darunter ein zugeklappter
  Abschnitt `details.archive` „Archiv (N)“ (Zusammenfassung ≥ 44 px), darin kompakte Karten im selben Raster (Name als
  h3 bis zwei Zeilen, „Archiviert am …“ · Zyklus N von 7, Abzeichen „Abgeschlossen“ falls fertig, Zyklen-Tabelle sobald
  es Versuche gibt, keine Hauptaktion); ⋮ mit „Wiederherstellen“ (`unarchive`) und „Löschen“ (zuletzt, rot).
  Wiederherstellen ist neben einem aktiven Training gesperrt und nennt den Grund als zweite leise Zeile im Eintrag
  („Erst das aktive Training archivieren.“, Muster 5.4 „Aktion mit Arten“). Ohne archivierte kein Abschnitt; ohne
  aktives, aber mit Archiv sagt der Leerzustand „Kein aktives Training. …“.
- Repertoire-Trainer (d3): Kopf = Name des Repertoires, „Repertoire-Trainer“ leise daneben; **Reihenfolge der Linien**
  (0.996.0, löst den Umschalter „Häufigste Linien zuerst“ ab) als beschrifteter Rahmenknopf `.order-btn` (`sort` + die
  GELTENDE Reihenfolge als Text, `aria-label` „Reihenfolge im Training: <Wert>“, Tooltip sagt, was der Knopf tut) — er
  öffnet den Einstellungs-Dialog (5.6); derselbe Dialog steht als „Reihenfolge im Training“ (`sort`) im ⋮ der
  Linienleiste der Detailseite und als erster Eintrag im ⋮ der Repertoire-Karte (auch bei geteilten Repertoires).
  Übungszyklen und „Fortschritt zurücksetzen“ (zuletzt, rot) im ⋮; Leerzustand mit genau einer Aktion — dort weder der
  Reihenfolge-Knopf noch „Fortschritt zurücksetzen“ (nur Übungszyklen im ⋮, d4); offline und in der Gegner-Sitzung
  (dort bestimmt der Gegner die Reihenfolge) ebenfalls kein Reihenfolge-Knopf — in der Gegner-Sitzung rückt das ⋮ am
  Handy wie im Leerzustand in die Titelzeile (`.more-solo`), nie ein Knopf allein in einer Zeile. **Kopfleiste bei
  600–768 px** (0.999.1): Zurück, Name und Moduswahl in der ersten Zeile, Reihenfolge-Knopf (links) und ⋮ (rechts)
  GEMEINSAM in einer zweiten, 8 px darunter wie am Handy — in einer Zeile blieben dem Namen dort nur 28–190 px („S…“);
  ab 769 px eine Zeile, der Name kürzt sich mit „…“. Ohne Knopf (Leerzustand, Gegner-Sitzung) bleibt es bei einer Zeile.
  **Gegen den gespeicherten Gegner eines „Prep: …“-Repertoires** (0.999.3) gilt die Reihenfolge-Einstellung: dort steht der
  Reihenfolge-Knopf (und das ⋮ ohne `.more-solo`), der Dialog hat sechs Werte; „kein Knopf, ⋮ in der Titelzeile“ gilt nur
  noch für die AUSDRÜCKLICHE Gegner-Sitzung (`?opponent=` von der Spielerkarte). Der Hinweis unter der Leiste nennt, was
  wirklich gilt („Reihenfolge nach Gegner“ nur, wenn nach ihm gereiht wird). Leerzustand dort (nichts fällig, nichts neu):
  Uhr-Symbol, „Gerade nichts fällig. Nächste Wiederholung in …“ und EINE Aktion „Alle Linien üben“ (`replay`,
  Hauptknopf) — sie startet nur die Sitzung und macht am Server nichts fällig; fehlt, wenn es keine Linie gäbe. Abschluss
  der aufs Fällige beschränkten Sitzung: „Alle n fälligen und neuen Linien …“ mit Hauptknopf „Alle Linien üben“; nach
  einem Durchgang über alle wie bisher „Von vorn“ (ein Knopf sagt, was er bringt, 5.4). Unter 900 px steht nur der Name (Zusatz visuell versteckt, für Bildschirmleser da), darüber
  schrumpft der Zusatz zuerst (d4). Lernen: solange der Trainer den Zug vorspielt (Brett gesperrt), ohne Imperativ —
  „Schwarz am Zug — das ist dein Repertoirezug.“ (d4). Linienliste: neue Linie mit umrandeter 44-px-Pille „⊕ Üben“
  (`playlist_add`) statt eines „+“ im Abzeichen-Stil (d4). Abfragen (0.886.0): ein falscher Zug bleibt mit ROTEM Pfeil
  stehen (Brett gesperrt), „Nochmal“ (Rahmenknopf, `undo`) bzw. Tippen/Leertaste/Enter nimmt ihn zurück; darunter in einer
  Zeile, wie oft dort schon falsch (Chessable je Zug, RookHub je Linie) — nur mit Daten. Unter der Anweisung eine
  Aktionszeile `.trainer-actions` aus Rahmenknöpfen: „Tipp“ (`lightbulb`, beschriftet mit der nächsten Stufe: Tipp → Pfeil
  zeigen → Zug ausspielen; Tipp-Markierung GRÜN, Startfeld bzw. Pfeil), „Linie überspringen“ (`skip_next`) und „Linie
  pausieren“ (`pause_circle`, 0.889.0; Snackbar mit „Rückgängig“), Tasten H/S/P im Tooltip; zuletzt der Schalter „Kommentare zeigen“ (0.893.0, Rahmenknopf mit Häkchen + `aria-pressed`, sonst `chat_bubble_outline`, Taste K, gemerkt je Gerät): an = die Kommentare der zuletzt gespielten Züge (eigener Zug + Antwort) während des Spiels als `.ctx-comment.played-comment` unter der Zugliste, ohne Halt; Pfeile/Felder aus `[%cal]`/`[%csl]` NIE während des Spiels, nur in der Schlussstellung und in der Nachbetrachtung je Stellung (Tipp grün und Fehlzug rot bleiben); eine ausdrücklich gewählte
  pausierte Linie zeigt unter dem Label `.paused-note` (`pause_circle`, `--rh-warn`); im Gegner-Modus (0.892.0) unter dem
  Label `.opponent-note` (Randlinie links `--rh-info`, `person_search`; ab dem ersten Zug ohne Partien des Gegners `--rh-warn`,
  fett, `call_split`, bleibt bis Linienende) — derselbe Baustein im Abweichungs-Trainer (`/repertoires/deviations`: Aufbau wie der
  Trainer, Schwelle als kleines `select` in der Kopfzeile, Frage `.ask` in `--rh-info`, gut/nicht optimal in
  `--rh-success`/`--rh-error`, am Ende ein Primärknopf „Gegen Maia weiterspielen (Elo)“ als Link in neuem Tab); im „Linie fertig“-Kasten (0.890.0) eine
  Nachbetrachtungs-Zeile `.review`: Symbolknöpfe `first_page`/`chevron_left`/`chevron_right`/`last_page`, Rahmenknopf
  „Engine“ (Häkchen + `aria-pressed` wie „Kommentare zeigen“), Textknopf „Im Analysebrett öffnen“ (`open_in_new`); bei
  laufender Engine eine QUERE Bewertungsleiste (10 px, Farben wie die Leiste des Analysebretts) mit Wert + Tiefe und die
  beste Linie darunter; Züge der Zugliste sind dort Knöpfe, der gezeigte hervorgehoben (`--rh-accent` 25 %); benutzte Tipps als `lightbulb`-Zähler in `--rh-hint` neben ✓/✗. Chessable-Link als kleiner Textlink hinter dem
  Linienlabel (neuer Tab). Woodpecker-Löser: seit 0.882.0 der Kurs-Löser (gleiche Oberfläche
  wie im Kurs), Plan-Kopf (Plan-Chip, Zyklus · Satz/Fortschritt · Resttage mit `.rh-dots`, ab Zyklus 2 Balken) in der
  Kontext-Karte; kein „Überspringen“ (der Server gäbe dieselbe Aufgabe wieder). Kalkulieren im E-Book-Brett: ein Satz, Rest hinter ? (d4). Statistik: Elo-Kopf `flex-wrap` mit
  8/12-px-Lücke, Level-Feld am Handy volle Breite unter dem Titel (d4). Repertoire-Ansicht: Brett aus Sicht der Kapitelfarbe (gewählte Linie,
  sonst gedreht, wenn alle Kapitel Schwarz sind). Lochfinder: Quelle/Datenbank mit sichtbarer Zeilenbeschriftung,
  die Seite einfarbiger Repertoires als eigener Chip darüber.

- `index.html` theme-color `#3f51b5`.
- Kursfortschritt im Löser dreigeteilt (`app-course-progress-split`, 2026-10-15): Leiste 6 px mit Abschnitten `--rh-success` (ohne Hilfe) / `--rh-hint` (mit Tipp/Neustart; bis 0.807 `--rh-warn`, hell fast rot) / `--rh-error` (aufgegeben), Rest = offen; darunter die drei Zahlen mit Symbol (✓ / Glühbirne / ✗) und „von N“, Bedeutung im Tooltip.

- Partieseite im Training „Fehler nachspielen“ (0.969.0): solange es läuft, zeigt die Seite nur Brett + Trainer — Zugliste,
  Auswertung, „In welchen Repertoires?“ und „Ähnliche Meisterpartien“ sind ausgeblendet (alle Breiten), nach dem Beenden
  steht alles wieder da. Ab 1280 px steht der Trainer (darunter ggf. die „Analysieren“-Leiste) als zweite Spalte RECHTS
  neben dem Brett, so breit wie Zugliste + Auswertung zusammen (die Karte springt nicht), der Kasten höchstens 680 px,
  höchstens so hoch wie das Brett und in sich scrollend; 769–1279 px ebenfalls rechts, solange ≥ 260 px frei sind; am
  Handy direkt unter dem Brett in voller Breite. Leertaste = „Nochmal“ nach einem Fehlversuch (nach der Lösung weiter
  „nächste Aufgabe“); der Tastenname steht nur im Tooltip des Knopfs (5.11), nicht als Dauertext.
- Zugvergleich am Fehler (0.908.0, Vereinspartien): im Trainer „Fehler nachspielen“ erst NACH dem Urteil (gefunden oder
  gezeigt) — vorher verriete er die Lösung. Unter der Kosten-Zeile, durch eine feine Linie abgesetzt: Kopf „Partiezug gegen
  besten Zug (Tiefe N)“ (0,8 rem, halbfett), zwei Zeilen Beschriftung · Zug (Mono) · ggf. Pille „bester Zug“ · Bewertung
  rechts (Mono, Bester grün, der andere rot), darunter je die Engine-Linie (6 Halbzüge, Mono, leise, eingerückt; am Handy
  ohne Einzug), dann die Begründung als normaler Text und ein Textknopf „Ganzen Vergleich öffnen“ (`compare_arrows`) auf
  die Vergleichsseite (`?clubGame=` → Rücklink „Zur Partie“, keine „Früheren Vergleiche“, keine „geht nicht“-Zeilen).
  Läuft der Vergleich noch: EINE leise Zeile, kein Ladebalken; keiner da oder gescheitert: nichts (kein Leerzustand).

### 8.2 Turnierseite (`src-turnier/`)

- Gleiches Material-Thema und gleicher Design-Modus wie RookHub (Cookie geteilt); theme-color `#172345`.
  Navbar `shell/turnier-navbar.component.ts`: ≤ 768 px Kopfknöpfe + ⋮; 769–1023 px „Zu RookHub“ nur als Symbol;
  auf `/login`, `/register`, `/forgot-password`, `/reset-password` kein „Anmelden“. Der aktive Weg trägt in der Zeile
  Primärfarbe, 600 und einen 2-px-Unterstrich (Innenschatten), im ☰ Primärfarbe + 600; beide mit
  `aria-current="page"` (E-Ink-Rahmen 3.6), „Gemerkt“ nur exakt auf `/tournaments` (d4-t-navbar-aktiv-marker-fehlt).
- **Turnierkarte** (`features/tournament-directory/tournament-card.component.ts`): vier feste Plätze
  Merken | Kalender | Ausblenden | Melden (40 px); fehlt eine Aktion, hält `.tc-slot` den Platz. ≤ 600 px:
  Kurzwort unter jedem Symbol, Fläche ≥ 64×48, Radius 12, `justify-content: space-between`. Gemerkt =
  gefülltes Symbol + `color-mix(var(--mat-sys-primary) 16%)`. Gedrängte Fassung `.tc-overview` für Karten-Popup
  und Kalenderfenster. Die Karte ist **als Ganzes klickbar** (5.2): die Fläche gehört dem Namen (Detailseite), die vier
  Aktionen und Angaben mit Tooltip bleiben eigene Ziele; in der Liste ist die umgebende `mat-card` die Kachel (samt
  Polster), als Kurzansicht die Karte selbst. Ebenso die Zeilen der Merkliste (Fläche = Titel, „Merken aufheben“ eigenes
  Ziel; solange ein Turnier geöffnet wird, ist die Zeile gesperrt).
- **Karten-Pins** (`pin-palette.ts`, `map-pin-marker.ts`, Leaflet): eingefärbt nach **genau einem** Merkmal
  (Bedenkzeit | Turnierart | Nachwuchs | Geschlecht), Rand je Füllung von Hand, Zahl auf hellen Füllungen dunkel
  (`darkLabel`).
- Gleichnamiges Turnier am selben Tag: eigene Karte mit Hinweis (r2-t-dup-cards). Titel nur echte (GM, IM …).
- Geteilter Turnier-Link: dieselbe Knopfreihe wie die Detailseite — Merken (primär), Beobachten, Volle Ansicht,
  Teilen (r2-t-public-actions).
- **Merken ist auf allen Breiten der gefüllte Hauptknopf** (`mat-flat-button color="primary"`, Klasse `bookmark-primary`) —
  die Entscheidung vom 27.09. (Rahmenknopf am Laptop) ist mit d10 aufgehoben; „Merken aufheben“ bleibt ein Rahmenknopf
  (d10-t-merken-kein-hauptknopf-ab-tablet). Mannschaftsergebnisse über `LocalScorePipe` (de „1,5 : 6,5“, d10-t-ergebnis-dezimalpunkt).
- Datenschutz mit eigener Einleitung (`LegalSiteKind 'turnier'`).
- **Kalender am Handy** (Agenda, d7): 3 Turniere je Tag („+n weitere“ klappt auf), nach Kalenderwochen gegliedert
  (Kopf „KW 41 · 5.–11. Okt.“) mit Wochen-Sprungleiste darüber (nicht klebend — die Reiter-Hülle ist ein
  Scroll-Container). Mannschaftsfenster filtert Titel wie die Tabellen (`chessTitle`), ohne echten Titel keine Spalte,
  öffnet mit `autoFocus: 'dialog'` (sonst trägt „Schließen“ die Fokus-Tonfläche, d9).
- Verzeichnis-Einträge (d9): Region über `regionText()` — Platzhalter („-“, „?“) zählen nicht, dann nur der Verband;
  Namen über die Pipe `directoryName` — von chess-results genau auf 50 Zeichen gekappte enden mit „…“. Im Melden-Dialog
  heißt der Zustand „Liga“ (`tournamentDirectory.report.league`), nie die Filter-Beschriftung.
  E-Ink: Umkreis-Regler mit schwarz umrandeter weißer Spur, schwarzem Füllteil, weißem Griff mit Rahmen
  (komponentenlokal per `:host-context(html.eink)`).
- **Kalender-Filterfeld** (b1): Zeilen nach Sinn statt Fließblock — Zeitraum/Umkreis (Handy; sonst nur eigener
  Zeitraum), Suche allein, Art/Alter/Geschlecht, Schalter, Bedenkzeit + Wochenende + „Mind. Teilnehmer“ (14rem) +
  Zurücksetzen. Am Handy stehen Zeitraum und Umkreis nur nebeneinander, wenn beide ganz hineinpassen. Wertechips unter
  der Leiste nur bei zugeklapptem Filter.
- **Turnier-Tabellen** (b1): Team-Spalte „Punkte“ nur, wenn ein Team Punkte hat; Teamname am Handy mit „…“; Rang und
  Punkte 4rem, der Teamname nimmt den Rest (d4). Spieler-
  tabelle 769–1023 px ohne FIDE-ID, Verein ≤ 12rem mit „…“. Leere Paarungen: ohne Runden keine Rundenauswahl; EINE
  Aktion im Leerzustand — „Aktualisieren“ (Detailseite) bzw. „Volle Ansicht“ (`/t/:id`).
- **Breitbild ≥ 2400 px** (u1): Turnierkalender zeigt Liste (Karten zweispaltig) und Karte nebeneinander unter EINEM
  Reiter „Liste & Karte“ (Kartenspalte klebt als Flex-Säule `100vh - 17rem`: Hinweise und Trefferzeile fest, die Karte
  füllt den Rest — b1), Kalender bleibt Reiter; Turnier-Detail und `/t/:id`: Spieler/Teams
  als Reiter links, Paarungen der gewählten Runde rechts in eigenem klebendem Scroll-Kasten. Abfrage im Code über
  `wideScreen()` (`src-turnier/app/core/wide-screen.ts`, mit Rückruf zum Nachladen). Darunter unverändert Reiter.
- Turnier-Detail und `/t/:id` in `min(--page-max-width, 96vw)` (Tabellenseite, 4.1; vorher fest 1 100 px). Niedrige
  Fenster (`max-width: 768px and max-height: 720px`, Boox 200 %): Kalender-Titel nur für Vorleser, sein ⓘ hinter der
  Trefferzahl; Detail-Kopfkarte mit 4-px-Polstern, 40-px-Knöpfen, Suchzeile kompakt wie am Laptop.
- **Spielernamen sind Knöpfe** (1.006.0): in der Startliste (Tabelle und Handy-Karte), in den Einzelpaarungen und im
  Mannschaftsfenster öffnet der Name die **Begegnungen** des Spielers (`player-encounters-dialog.component.ts`,
  Inhalts-Dialog 560 px, `autoFocus: 'dialog'`). Aussehen: Akzentfarbe ohne Unterstreichung (erst bei Hover) — in der
  Startliste stünde sonst neben dem unterstrichenen Verein ein zweiter Unterstrich je Zeile. In der Handy-Karte gehört
  der Name dem Dialog, die übrige Karte schaltet weiter den Stern. Im Dialog je Runde eine Zeile Runde | Farbkästchen,
  Brett, Gegner (klickbar, wechselt auf ihn), Wertung | Ergebnis rechts; die erste offene Runde ist getönt und trägt
  den Chip „Nächste Runde“, weitere offene „geplant“.
- **Live-Bretter** (1.014.0, `/tournaments/:id/live`, `features/tournaments/live/`): Seite als **Bretterwand** bis 1880 px
  breit (seit 1.028.0, Ausnahme von `--page-max-width`: die Seite ist zum Zuschauen, nicht zum Lesen), Kopf mit Zurück-Pfeil, Titel und Quelle („Name · lichess.org“ mit `open_in_new`), darunter
  EINE umbrechende Werkzeugzeile: Runde (`mat-select`, laufende mit „· läuft“), Mannschaft (nur Mannschaftsturnier),
  Schalter „Engine-Balken“, daneben „Besten Zug zeigen“ (1.030.0, je Gerät, ohne Balken gesperrt), Zustand als Symbol + Text (live in `--rh-success`, sonst on-surface-variant — nie nur Farbe).
  **Alle Bretter in EINEM Raster** (1.028.0, Wunsch 2026-11-26 „in einer Wurscht, damit ich 8 auf einmal sehe“):
  Begegnung(en) bzw. Runde, hinzugefügte, fremde und weitere übertragene Partien ohne Abschnittsgrenzen, Raster
  `auto-fill, minmax(min(100%, 240px), 1fr)`, Abstand 12 px — 1920 px: 7 Spalten (Brett ≈ 216 px, zwei Reihen ≈ 700 px),
  1280 px: 4–5, Handy 390 px: 1 (ab ~520 px zwei). Die Herkunft steht als Zusatz „· …“ neben „Brett n“: bei mehreren
  Begegnungen „Heim – Gast“, hinzugefügte ihre Begegnung (sonst „Hinzugefügte Bretter“), fremde „Turnier · Runde“, übrige
  „Weitere übertragene Partien“. Darüber eine kompakte, umbrechende Kopfleiste: je Begegnung eine `h2`-Zeile (1 rem)
  „Heim Ø 2315  2,5 : 3,5  Gast Ø 2290“ und je fremde Runde der Akzent-Link mit `open_in_new` (dort auch „Warte auf …“ bzw.
  der Fehler mit „Brett entfernen“). Je Brett eine umrandete Karte (Radius 8)
  mit „Brett n“, rechts die **Zugnummer** „Zug 34“ (`tabular-nums`, laufend `floor(plies/2)+1`, fertig der letzte Zug;
  Tooltip „Weiß/Schwarz am Zug“ bzw. „Letzter Zug der Partie“) und Ergebnis-Chip oben, Spielerzeile oben (Gegenseite) und unten (eigene Seite): Titel in `--rh-accent`,
  Name mit „…“, Elo leise, Uhr `tabular-nums` — die Seite am Zug fett mit Akzentrahmen (E-Ink: Rahmen schwarz). Das Brett ist
  `app-chess-board` (5.12) ohne eigene Bedienung; Engine-Balken 10 px links am Brett, Weißanteil weiß auf dunkel, ohne
  Bewertung gestrichelter leerer Rahmen, Wert als leise Zeile darunter; mit „Besten Zug zeigen“ ein blauer Pfeil (Chessground
  `blue`, wie die Live-Engine im Analysebrett) und das SAN fett (600, on-surface) am Ende der Wertzeile — nur zur Bewertung der
  aktuellen Stellung, nie zum gehaltenen alten Wert. Mannschaft: Stand fett (`localScore`) in der Kopfleiste, Bretter aus Sicht der gewählten Mannschaft (sonst der Heimmannschaft)
  unten. Ohne Übertragung kein Leerraster, sondern ein leiser Hinweis (5.11, Symbol `live_tv`) mit Feld + Knopf
  „Übertragung verknüpfen“ (Gast: „Anmelden, um …“). Die laufende Uhr tickt nicht im E-Ink-Modus (3.6). Auf der Detailseite
  ist „Live-Bretter“ ein Rahmenknopf mit `live_tv` (`.keep-label`), nur wenn verknüpft; sonst ⋮-Eintrag.
  **Link teilen** (1.024.0): rechts im Kopf ein Rahmenknopf `share` + „Link teilen“ (unter 480 px nur das Symbol, 48 px
  breit, Name per `aria-label`), für Gäste wie Angemeldete. Am Handy (`pointer: coarse` + Web Share API) das Teilen-Blatt,
  sonst Zwischenablage mit Snackbar „Link zu dieser Ansicht kopiert.“ — wie „Link teilen“ der Partien (8.1). Der Link trägt
  die ganze Ansicht (`?round=` immer, `team`/`player`, `add`/`ext`, `eval=1` wenn der Balken an ist). Seit 1.029.0 teilt der Knopf
  den kurzen, lebenden Link `/live/<token>` (die Seite wechselt beim ersten Teilen dorthin); auf `/live/…` steht unter der
  Werkzeugzeile eine leise Hinweiszeile (0,85 rem, on-surface-variant, Symbol 18 px): Besitzer `group` „Geteilte Ansicht —
  Änderungen sehen alle mit dem Link.“, Zuschauer `visibility` „… Änderst du etwas, wird daraus deine eigene Ansicht.“ Unbekannter
  Link: leiser Hinweis (5.11) mit `link_off`.
  **Brett hinzufügen** (1.025.0): Rahmenknopf `add` + „Brett hinzufügen“ in der Werkzeugzeile (immer, auch für Gäste) → Auswahl-
  Dialog 560 px (5.6, `autoFocus: 'dialog'`): Abschnitt „Aus dieser Runde“ (nur mit Mannschafts-/Spielerfilter) mit Suchfeld und
  Zeilen ≥ 44 px (☑/☐ in `--rh-accent`, Spieler „Weiß – Schwarz“, darunter leise „Brett n · Begegnung“), Abschnitt „Aus einer
  anderen Lichess-Übertragung“ mit Link-Feld + Rahmenknopf „Laden“, Fehler als Zeile in `--rh-error`; „Abbrechen“ / „Fertig“
  (gefüllt). Hinzugefügte Bretter: seit 1.028.0 im selben Raster hinter den gefilterten Brettern (vorher eigener Abschnitt), dieselbe Karte mit Zusatz „· Begegnung“ neben „Brett n“ und einem ✕ (`close`,
  40 px, `aria-label` „Brett entfernen“) rechts im Kartenkopf. Elo-Schnitt der Begegnung: „Ø 2315“ leise (0,8 rem,
  `tabular-nums`) hinter dem Mannschaftsnamen (seit 1.028.0 in derselben Zeile), Tooltip „Schnitt über n Bretter“.
  **Analysieren** (1.026.0): im Kartenkopf vor dem ✕ ein Symbolknopf `biotech` (40 px, `aria-label`/`title` „Auf dem
  Analysebrett öffnen (neuer Tab)“) wie „In Analyse öffnen“ der Partienliste; öffnet RookHubs Analysebrett in einem neuen Tab. Verbindungsabriss: Zustand „Live“ bleibt 10 s
  stehen, erst danach „Verbindung unterbrochen“ (kein Blinken, Bretter bleiben stehen, kein Spinner).
- Profil: erste Karte „Darstellung“ = RookHubs `app-theme-card` (`@rh/features/profile/theme-card.component`), auch
  beim Ladefehler. Spieltermine einer Liga mit „·“-Trenner in der Lücke (`overflow-x: clip`, wie ClubHub `.kid-meta`).

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
  Die Spalte neben dem Brett wächst mit (`--kid-side: 480px`, Schrift größer über `KID_SIDE_WIDE`), Kopf + Fuß
  200 px (b1-k-ultrawide-seitenspalte). Brett-Koordinaten mit deckendem Hof in der Gegenfarbe (lesbar auch auf
  Auswahl-/Zugfeldern, b1-k-koordinate-auf-auswahlfeld). Texte vom Server (Kursnamen) folgen der Sprachpille.
  Am Handy (< 600 px, nicht jedes Hochformat) zeigt die Zurück-Pille der Kursaufgabe nur „←", der Stand kurz „8/287“,
  der Titel bricht auf ≤ 2 Zeilen um (5.1); Pfeil und Wort der Pille trennt `gap: .3em` (`KID_BACK`), kein Leerzeichen.
  Markierungen (Sterne, Tipp-Pfeile) gehen erst an ein vermessenes Brett (`KidBoardSizeDirective`, `shared/kid-board-size.directive.ts`,
  wie `applyArrows` in RookHub — sonst NaN-Koordinaten); die Direktive misst nach jedem Neubau von `cg-board` neu, und
  `PuzzleBoardComponent` baut ein Brett unter 8 Gerätepixeln (chessground rundet es auf 0 × 0 ab) nicht neu auf, sondern
  hält die Markierungen zurück und setzt sie nach jedem Neuaufbau mit frischen Maßen wieder (d6-k-sternenjagd-sterne-fehlen).
  Kurs-Fortschrittsbalken über die Textspalte (≤ 520 px), Spur
  `rgba(120,60,0,.22)`, Füllung `#b45f06`, angefangen ≥ 8 px. Anmeldemasken: Feldunterzeile nur mit Hinweis 14 px Luft,
  ohne Hinweis 8 px flach. Registrieren: genau ein Hinweis „Für Eltern“, der Zweck des Kontos steht hinter dem ?
  (keine zweite Einleitung aus `AUTH_INTRO.register`, d6-k-register-doppelter-hinweis).
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
- Spielerkarte, Trainingslinien: Linienzahl als kleines natives Auswahlfeld „Linien“ (10 / 25 / 50 / 100 / 250 / Alle) in der
  Auswahlzeile hinter dem Repertoire-Menü, kein Segmentschalter (sechs Stufen brächen am Handy um); „… und n weitere Linien“ mit
  umrandetem Knopf `.btn-sec` zur nächsten Stufe in derselben Zeile (1.012.0).
- Fußzeile immer unten: `lh-root` ist eine Flex-Spalte über `100dvh`, `main.wrap` wächst (wie ClubHub; u1-l-fuss-schwebt).
- Breitbild ≥ 2400 px: `.wrap.mid` und `.wrap.wide` (Formular prüfen/korrigieren, Brett ≤ `min(100vh - 240px, 960px)`) auf
  `--page-max-width-wide`, Prognose-Bretter vierspaltig, bei 6 (bzw. 3) Brettern dreispaltig (u1-l-prognose-bretter,
  a1-l-prognose-ultrawide-6-bretter); kurze Tabellen (Übertragungen) bleiben 1 208 px breit, Vereinspartien 1 440 px,
  linksbündig; Sperrkarten `.gate` höchstens 760 px (b1-l-sperrkarte-breit).
- Links ohne eigene Klasse in `--ink` (unterstrichen), nie Browserblau; Bereichsregeln (`.foot a` …) gehen vor.
- Häkchen ohne eigene Regel 18 px mit `accent-color: var(--ink)` (Grundregel in `:where()`, Spezifität 0), nie Browserblau.
- Gesperrte Knöpfe `.btn-pri`/`.btn-sec` mit festen Farben (`--muted` auf `--track` bzw. `--line`-Rahmen), nicht per Deckkraft;
  Zeiger `not-allowed`, `progress` nur mit `aria-busy="true"`. Ein gesperrtes „Speichern“ nennt den Grund darüber
  („+“-Fenster: „Zum Speichern erst eine Klassifizierung oder Bemerkung ändern.“), Aktionszeile mit 8 px Abstand (d9).
- Wer mehrere Konto-Vorschläge hat (`.sugg-group.multi`), steht als umrahmte Fläche mit „N Konten“ im Kopf, Knöpfe auf einer Höhe.
- ⋮-Menüs: **jedes** `mat-menu` in LeagueHub trägt `class="lh-menu"` (Fläche `--surface`, 1 px `--line`, Radius 8, Barlow; auch
  Brett-⋮ der Prognose und Kontomenü, d8), „Löschen“ zuletzt mit `.delete-item` in `--red`. Dialoge öffnen mit
  `panelClass: 'lh-dialog'` (Fläche `--surface`, 1 px `--line`, Radius 10, Titel Barlow Semi Condensed, Text Barlow — „+“-Fenster, d8). Aufgeklappte ⓘ-Kästen `.info-panel` enden mit ihrem Inhalt (`fit-content`, ≤ 760 px); ein
  Satz mit ⓘ dahinter ist ein Absatz `.info-inline` (ⓘ am Satzende), keine Flex-Zeile; letztes Wort + ⓘ stehen in
  `.info-tail` (`nowrap`) — das ⓘ bricht nie allein um.
- Vereinspartien-Karte am Handy: die Meta-Zeile (Jahr · Zugfolge) trägt nichts zur Mindestbreite bei (`flex: 1 1 0; width: 0`),
  die Karten-Tabelle hat `table-layout: fixed` — „Nachspielen“, ⋮ und Ergebnis bleiben ganz im Bild, die Zugfolge kürzt mit „…“
  (d6-l-vereinspartien-handy-ueberlauf). Zahlen über 999 überall mit Tausenderpunkt (`thousands`, auch „Alle Gemeldeten“).
- Tabellen: Namen kürzen nicht mit „…“ (am Touch fehlt der Tooltip) — sie brechen um, Nebenangaben (Elo) in die zweite
  Zeile; gekürzt wird nur Nebeninformation (Zugfolge mit `title`). Am Handy fallen Nebenspalten weg statt Hauptspalten
  rechts aus dem Bild zu schieben (Prognose-ⓘ).
- Brettpaarungen der Prognoseseite (`.pairings` in `.pairings-wrap`, Containerabfrage < 480 px): Namen brechen um
  (`overflow-wrap: anywhere`), Elo in der zweiten Zeile, Abzeichen, Analyse-Symbol und ⋮ gestapelt in einer Spalte, Analyse
  und ⋮ 44 × 44 px; gestapelt ohne die unsichtbaren Abzeichen-Platzhalter `.gb-slot` (d9); die Tabelle bleibt bei 390/360 px
  ganz im Bild (0.869.5, lh-brettmenue). „Gespielt: …“-Zeile: Ergebnis „1 : 0“ als `.res` (`nowrap`, U+00A0), Strich mit
  U+00A0 davor (d9).
- Abzeichen an Partien (0.852.0, ersetzt den Text-Marker „◆ n“/„!! n“ von 0.848.0–0.851.1): `lh-game-badges` — runde
  Knöpfe (20 px hoch, Radius 10 px, 12 px fett), „+“ auf Gold (`MOVE_CLASS_COLORS.inaccuracy`) = interessante Stellungen wie
  der „+“-Knopf, „!!“ auf Türkis (`MOVE_CLASS_COLORS.brilliant`) = brillante Züge; Schrift dunkel auf beiden (≥ 4,5:1, Spec),
  Zahl erst ab 2 („+3“, „!!“). Sie stehen NIE am Ergebnis (Ergebnisspalte bleibt bündig): Prognose-Brettpaarungen vor dem
  Analyse-Symbol, Vereinspartien in der Spalte „Analyse“ vor der Genauigkeit bzw. in der Fußzeile der Handy-Karte,
  Übersicht/Runden hinter „Partie vorhanden“. Klick = Nachspielen aufklappen und zur ersten Marke bzw. zum ersten „!!“.
- Interessante Stellungen (0.848.0): im Nachspielen `lh-game-replay` darunter „Interessante Stellungen:“ als Textknöpfe
  (`.btn-link.mark-jump`, aktiv fett) — Hauptlinie „12…Sf6“, aus einer Variante „Variante: 12…c5“, ohne Ort „in einer
  Variante“, mehrfach markiert „×n“ gedämpft. Züge mit Marke bzw. Abzweig tragen in der Zugliste einen 6-px-Punkt in
  `--rh-success`; eine Variantenstellung auf dem Brett hat darüber eine Zeile „Variante: …“ mit „Zurück zur Partie“. Die Zeile
  „Interessante Stellungen:“ steht unter Brett UND Zugliste, nie im Rollkasten der Zugliste (d8).
- Reiter-Aktivmarke nur nach dem Pfad (`paths: 'exact', queryParams: 'ignored'`) — Prognosen und Vereinspartien tragen
  Query-Parameter (d8). Prozentangaben über `pct()` mit geschütztem Leerzeichen („55 %“ bricht nicht um).
- Partieformular hochladen (Hinzufügen-Seite, Art „Partieformular“, 0.943.0): jede Zeile eines weiteren Fotos heißt „Seite N“
  oder „weiteres Foto von Seite N“ und trägt den Umschalter `.seg.page-kind` „Neue Seite | Dieselbe Seite“ (`aria-pressed`;
  was über 3 Seiten bzw. 4 Fotos je Seite ginge, ist gesperrt — Schrift `--muted`, nicht per Deckkraft) und „↑“ (eine Stelle
  nach vorn). Mehrere Fotos aus der Galerie werden zunächst je Foto die nächste Seite.
- Aufstellungen der Runde (`lh-round-lineups`): eigene Begegnung zuerst, darüber Sprungchips (eigene umrahmt), jede Begegnung
  ein `<details>` mit Kopf + Ergebnis; am Handy (≤ 640 px) fremde zugeklappt (d8-l-fold-long-aufstellungen-handy).

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
  Gruppenfilter als `.chip-btn` statt Auswahlliste; am Handy eine waagrecht wischbare Chipzeile ohne Scrollleiste mit
  28-px-Verlauf (`mask-image`) und gleich breitem Polster am Ende (4.4, d4-c-kartei-filterchips-abgeschnitten).
- Ein gefüllter Knopf je Seite auch bei aufgeklapptem Formular: Hat ein Klappkasten seine eigene Hauptaktion (Gruppe
  ändern → „Änderungen speichern“), verliert der Kopfknopf („Anwesenheit abhaken“) solange `.primary`
  (d4-c-gruppe-zwei-hauptknoepfe).
- `.sheet` (Karteiblatt, Kopflinie `2px solid var(--rule)`), `.cards` (Liste in einer Karte), `.gate`
  (Sperrkarte mit `.gate-actions`), `.empty` (gestrichelter Rahmen), klebende `.form-save` über die volle Breite.
- **Karteizeile und Gruppenkarte sind als Ganzes klickbar** (5.2): die Zeile der Kartei führt zum Karteiblatt (Ziel = der
  Name), die Telefonnummer bleibt eigenes Ziel (wählt); die Gruppenkarte führt zur Gruppenseite, „Anwesenheit abhaken“
  bleibt eigenes Ziel. Fokusring 3 px `--focus`, in der Kartei nach innen gesetzt (`.cards` schneidet ab), Tönung 5 %
  `--ink`.
- Anwesenheit `.matrix`: eigener Scrollkasten, Namensspalte klebt links, Zellen ✓/✗ 26 px.
- Datum TT.MM.JJJJ; der Verknüpfungsstand des Kontos steht in der Fußzeile (r2-c-footer-link).
- Gesperrte `.btn` mit voller Deckkraft: Schrift `--muted`, Rahmen gestrichelt in `--muted`, `.btn.primary` auf 14 %
  Primär-Tönung — nie pauschal `opacity`. Gesperrt bleibt ein Knopf nur, wenn seine Beschriftung den Grund nennt („keine
  spätere ›") oder gerade gespeichert wird; fehlt eine Eingabe, fokussiert der Tipp das Feld und darunter steht, was fehlt
  (`.need`, a1-c-disabled-button-contrast). Eingabefehler, die das Formular selbst findet (Vorname leer, Datum unlesbar),
  stehen ebenso als `.need` unter dem Feld, das Feld bekommt Fokus + `aria-invalid`; die Statuszeile am Formularende
  nur für Serverfehler (b1-c-kind-neu-fehler-unter-falz). Feld und sein `.need` bilden eine Gitterzelle `.field-cell`, damit der
  Hinweis auch im einspaltigen `.grid-2` (Handy) unter SEINEM Feld steht. Zerstörende Aktionen als `.btn.danger`, nicht als
  `.btn-link` — ganze Datensätze (Gruppe, Karteiblatt, Trainingseinheit samt Anwesenheit und Fotos; F4-016); kleine
  Entfernen-Aktionen in Listen und Zeilen (Bild entfernen, Notiz löschen, Verknüpfung trennen) dürfen `.btn-link.danger`
  bleiben. `.btn.danger:hover` füllt in `--bad` mit Schrift `--bad-ink` (dunkel ≥ 4,5:1, F4-017).
- Metazeilen `.kid-meta`: Trenner „·" als absolut gesetztes `::before` ab der zweiten Angabe mitten in der 14-px-Lücke,
  nicht vor Chips; `overflow-x: clip` schneidet ihn an einer umgebrochenen Zeile ab — weder am Zeilenende noch am
  -anfang (5.13, b1-c-meta-trenner-zeilenende). Seitenkopf
  `.page-head`: Titelblock schrumpft (`flex: 1 1 320px`), Knöpfe bleiben daneben. Rechtsseiten-Karte linksbündig an der
  Logo-Kante. Ab 2400 px nur Seiten mit Anwesenheitsmatrix in `--page-max-width-wide` (Verlauf ≤ 46em), Rest 860 px;
  dort die Matrix nur so breit wie ihre Spalten (≥ 828 px), Klappkästen und Statuszeile 828 px.

### 8.6 Geteilte Masken (Anmeldung und Recht)

- Komponenten aus `src/app/features/auth/` und `src/app/features/legal/` (Login, Registrierung, Passwort
  vergessen/zurücksetzen, Datenschutz, Impressum, Konto löschen), in den Hubs per Alias `@rh/*` geroutet.
  Jede App prüft ihre Links mit `checkSharedPageLinks` (`src/app/testing/shared-page-links.ts`).
- **`LEGAL_SITE`** je Oberfläche (`core/legal-site.ts`): Impressum ja/nein, Kontaktadresse, `kind`
  (eigene Datenschutz-Einleitung: rookhub/turnier/kidhub/leaguehub/clubhub), Rückziel `back`, `accountHome`.
- Rücklink „← Zurück“ bzw. zur Startseite statt immer zur Anmeldung (`legalBack`, x-back-login); keine doppelten
  Rechtslinks unter der Anmeldekarte in den Hubs (`.legal-links { display: none }`); kein „Anmelden“-Knopf auf
  der Anmeldeseite selbst (x-login-headbtn, r2-t-login-headbtn).
- **Umgestaltung je Oberfläche — nie die geteilte Komponente für einen Hub ändern**, sondern im globalen
  Stylesheet des Hubs die Material-Variablen auf der Karte überschreiben:

| Oberfläche | Wie |
|---|---|
| RookHub, Turnierseite | Material unverändert; **Datenschutz zuklappen** (RookHub, LeagueHub, ClubHub; vom User freigegeben, Rechtstext unverändert): unter `PrivacyComponent.SECTIONS_OPEN_QUERY` = 1440 × 900 — also auch am Laptop und auf dem Boox — bleiben Verantwortlicher und der zweite Abschnitt offen, alle weiteren stehen als `details.legal-fold` (h2 als `<summary>`); Verzeichnis-Sprung, `#…` und Druck klappen auf (d7-rk-privacy-seite-zu-lang, d7-l-datenschutz-laptop-zu-lang). Turnierseite und KidHub bleiben offen |
| LeagueHub | `lh-root :is(app-login, …, app-account-deletion) .mat-mdc-card { --mat-sys-primary: var(--red); --mat-card-elevated-container-shape: 10px; --mat-button-*-container-shape: 6px; … }`, Schrift Barlow, Rechtsseiten-Köpfe rot (r2-l-legal-style) |
| ClubHub | dieselbe Technik mit `:is(…) .mat-mdc-card`, Primär grün, Köpfe Zilla Slab in `--rule` (r2-c-privacy-style); Knöpfe auf Rechtsseiten wie `.btn` (Radius 8, 44 px); Datenschutz unter 1440 × 900 (Handy, Tablet, Laptop) wie LeagueHub ab dem dritten Abschnitt zugeklappt (`details.legal-fold`, `PrivacyComponent.FOLDING_KINDS`, Linie in `--line`; d6-c-privacy-mobile-zu-lang, d7) |
| KidHub | `KID_AUTH_STYLES` / `KID_PRIVACY_STYLES` in der Hülle + „← Start“-Pille; Rechtsseiten = `/privacy` **und** `/account-deletion` (`isLegalUrl`, Breite `KID_PAGE_WIDTH.legal`); Fachbegriff-Listen für Eltern zugeklappt (`details.kid-parent`), davor ein Kindersatz (`foldPrivacy`, `foldAccountDeletion`: Kindersatz unter „Was entfernt wird“, Löschknopf nach „Was bleibt“); „Passwort vergessen“: beide Eltern-Hinweise in einem `details.kid-parent` (`foldForgotPassword`) |

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
| Kacheln ohne `rh-tile` (5.2) | bewusst nicht umgestellt, weil ohne eindeutiges Hauptziel oder Inhaltskarte: Stellungen merken (`remembered`: Titel-Link → Quelle, Knopf → Analysebrett), Woodpecker-Übersicht (eine Inhaltskarte mit Zyklustabelle; „Zyklus starten“ ändert Daten), Punktepartie-/Katalog-/Meisterpartien-Zeilen (Listen mit Startknopf, der etwas anlegt), Freundeszeilen (mehrere gleichrangige Ziele) | neue und angefasste Kacheln über `rh-tile`; ohne Hauptziel bleibt es beim Knopf |
| Klick am Container statt am Ziel | Analyseaufträge (Kopf `role="button"` mit `stopPropagation` an den Knöpfen), Rekonstruktions-Teile und Endlos-Verlauf am Handy (`(click)` an der Karte, kein Tastaturziel) | beim Anfassen auf ein echtes `<button>`/`<a>` als `rh-tile-target` umstellen |

---

## Änderungsprotokoll

| Datum | Version | Entscheidung | Grund | Quelle |
|---|---|---|---|---|
| 2026-11-26 | 1.030.0 · Turnierseite | Live-Bretter: Schalter „Besten Zug zeigen“ neben „Engine-Balken“ (je Gerät), blauer Pfeil + SAN fett in der Wertzeile (8.2) | Den Engine-Zug in laufenden Partien direkt sehen | Nutzerwunsch 26.11. |
| 2026-11-26 | 1.029.0 · Turnierseite | Live-Bretter: „Link teilen“ teilt einen kurzen, lebenden Link `/live/<token>`, Hinweiszeile für Besitzer/Zuschauer unter der Werkzeugzeile, leiser Hinweis bei unbekanntem Link (8.2) | Ein Link für alle, der Änderungen des Erstellers mitmacht | Nutzerwunsch 26.11. |
| 2026-11-26 | 1.028.0 · Turnierseite | Live-Bretter: alle Bretter in EINEM Raster ohne Abschnitte (`minmax(min(100%, 240px), 1fr)`, Seite bis 1880 px), Herkunft als Zusatz an der Karte, Stand je Begegnung als kompakte Kopfleiste; Zugnummer „Zug n“ im Kartenkopf (8.2) | Acht Bretter auf einen Blick am PC (1920 px: 7 je Reihe, zwei Reihen ohne Scrollen), am Handy eine Spalte | Nutzerwunsch 26.11. |
| 2026-11-26 | 1.027.3 · Turnierseite | Live-Bretter: Engine-Balken genau so hoch wie das gezeichnete Brett (gemessen, Chessground rastet auf Vielfache von acht), 0.0 liegt zwischen 4. und 5. Reihe; nach einem Zug hält er den letzten Wert, bis die neue Stellung bewertet ist (8.2) | Balken als Maßstab am Brett lesbar, kein Ausschlag | Nutzerwunsch 26.11. |
| 2026-11-26 | 1.026.0 · Turnierseite | Live-Bretter: Symbolknopf `biotech` im Kartenkopf öffnet die Partie auf RookHubs Analysebrett (neuer Tab, alle Züge, Kopf, Uhren) (8.2) | Eine laufende Partie selbst durchrechnen, auch als Gast | Nutzerwunsch 26.11. |
| 2026-11-26 | 1.025.0 · Turnierseite | Live-Bretter: „Brett hinzufügen“ (Auswahl-Dialog 560 px mit Suche + Lichess-Link), Abschnitt „Hinzugefügte Bretter“ / „Turnier · Runde“ mit ✕ im Kartenkopf, Elo-Schnitt „Ø n“ unter dem Mannschaftsnamen, Abriss erst nach 10 s anzeigen (8.2) | Mannschaftskampf und daneben einzelne Bretter (auch die WM-Partie) auf einer Seite; „live“ ↔ „unterbrochen“ blinkte | Nutzerwunsch 26.11. |
| 2026-11-26 | 1.024.0 · Turnierseite | Live-Bretter: Knopf „Link teilen“ rechts im Kopf (8.2), unter 480 px nur Symbol; Teilen-Blatt am Handy, sonst Kopieren mit Snackbar | Eine bestimmte Live-Ansicht (Runde, Mannschaft) soll an Gäste gehen und genau so aufgehen | Nutzerwunsch 26.11. |
| 2026-11-25 | 1.021.0 · Zusammenfassung | Roter Faden als abgesetzter Kernsatz ganz oben (8.1): unter dem Titel des Texts, Akzentleiste links + leicht getönte Fläche, 1,05 rem, ≤ 75ch, ohne Überschrift; an der alten Stelle entfällt er | Der rote Faden ist die Kernaussage, stand aber am Ende von „Die Partie“ hinter allen Zügen; eine Überschrift doppelte nur seinen ersten Satz | Wunsch 2026-11-25 |
| 2026-11-25 | 1.015.0 · Partien + Formular-Korrektur | Restzeit an den Zügen (5.3) und Karte „Zeitverbrauch“ (zwei Linien, Weiß voll/Akzent, Schwarz gestrichelt) in der Auswertungsspalte; in der Formular-Werkbank (5.12) ein Feld „Uhr nach dem Zug“ unter dem Zug und die Zeit klein in der Zugliste | Notierte Restzeiten werden jetzt mitgelesen; sie sollen sichtbar und korrigierbar sein, kompakt und ohne zweite Farbe für die Seite | Wunsch 2026-11-25 |
| 2026-11-25 | 1.014.0 · Turnierseite | Live-Bretter `/tournaments/:id/live` (8.2): Bretterraster je Begegnung bzw. Runde, Karte je Brett (Spieler oben/unten, Uhr am Zug fett mit Akzentrahmen, Ergebnis-Chip), Engine-Balken links am Brett schaltbar, Mannschaftsstand als Kopfzeile, Hinweis + Verknüpfen statt Leerzustand; Knopf „Live-Bretter“ auf der Detailseite nur mit Übertragung | Wer einer Mannschaft folgt, will alle ihre Bretter auf einen Blick, auf Handy (eine Spalte) wie am PC (Raster) und am Boox (keine tickende Uhr) | Nutzerwunsch 25.11. |
| 2026-11-25 | 1.013.1 · Tagespuzzle | „Nächstes Puzzle“ nach Lösen und Fehlversuch führt zu den Zufallspuzzles (`/puzzles`); kein Auto-Weiter-Countdown, auch nicht im Brett-Vollbild (5.12) | Der Knopf tat beim Tagespuzzle nichts, ein Countdown lief ins Leere; wegspringen soll man erst auf Wunsch, damit man die Lösung des Tages ansehen kann | Nutzerwunsch 25.11. |
| 2026-11-24 | 1.011.0 · E-Book-Brett Lösen | Lösen wie der Puzzle-Löser: Statuszeile behält Zustand + Auge + Glühbirne (Lösung), die Aktionen stehen als Textknöpfe in einer eigenen, umbrechenden Zeile darunter (Tipp mit `tips_and_updates` + „n/3“, Zurücksetzen, Mausrutscher; am Ende „Wiederholen“ und gefüllter Knopf „Nächstes Diagramm (n)“ rechts); Tipps als nummerierte Liste darunter; kein ✕/Rot nach einem Fehlzug, rot nur das Ergebnis „Falsch“; Vollbild auch im Training mit der Icon-Leiste des Puzzle-Lösers ohne Aufgeben | Puzzle-Modi konsistent halten; die Glühbirne bleibt der Lösung, darum ein anderes Tipp-Symbol; fünf Knöpfe passen in der 300-px-Leiste nicht in die Statuszeile | Nutzerentscheidung 24.11. |
| 2026-11-23 | 1.009.0 · Partien | Spielertitel vor dem Namen (5.13): `.ptitle` 0,85 em, fett, `--rh-accent`, mit U+00A0 an den Namen gebunden, auf Partieseite, Teilen-Link und Partienliste; Link-Vorschau als Text | Titel wie auf Schachseiten üblich vorn und abgehoben, aber ohne Abzeichen, damit die Kopfzeile ruhig bleibt; nicht geregelt, Vorschlag nach Wunsch des Nutzers | Nutzerwunsch 23.11. (Partie 186 „IM Grötz, Harald“) |
| 2026-11-22 | 1.007.0 · Woodpecker | Entscheidungsfrage beim Einstieg „Zyklus N: Zeit ist um“ (5.6): schmaler Dialog (440 px), kurzer Stand, zwei Abschnitte „Verlängern“ (Tage-Feld mit Vorschlag und Vorschau der neuen Frist) und „Zyklus abschließen“ (ein leiser Satz zu den Folgen); Knöpfe „Später“ · „Zyklus abschließen“ (stroked) · „Um n Tage verlängern“ (Hauptknopf). Nicht blockierend: „Später“ fragt in dieser Sitzung nicht wieder, die Karte trägt dann „Verlängern oder abschließen“ (stroked) statt des Zeitbalkens. Im Löser kommt die Frage VOR dem Aufbau der Aufgabe (keine laufende Uhr dahinter) | Beide Wege sind sinnvoll, keiner ist rückgängig zu machen — deshalb ausdrücklich fragen statt still weiterlaufen oder beenden; Verlängern ist die häufigere Wahl und bekommt den Hauptknopf | Nutzerwunsch 22.11. |
| 2026-11-22 | 1.006.0 · Turnierseite | Spielernamen öffnen die Begegnungen (8.2): Name als Knopf in Akzentfarbe ohne Unterstreichung, in der Handy-Karte eigenes Ziel neben dem Stern; Dialog mit einer Zeile je Runde, erste offene Runde getönt mit Chip „Nächste Runde“ | Wer einen Spieler anklickt, will seine Gegner und die nächste Paarung sehen; doppelte Unterstreichung je Zeile wäre unruhig | Nutzerwunsch 22.11. |
| 2026-11-22 | 1.002.0 · Formular-Werkbank | „Partie hier beenden“ als zweiter roter Link vor „Zug löschen“ in der Zeile des Zugs (5.12); Rückfrage über `ConfirmService` mit Zug, SAN und Anzahl Halbzüge, Bestätigen „n Halbzüge löschen“ (Einzahl/Mehrzahl als Schlüsselpaar); Rückgängig in der bestehenden Zeile über dem Zug | Nach dem echten Ende stehen auf Formularen oft Kritzeleien oder Analysezüge, gekürzt wurde bisher per API (Partien 258, 262); die Rückfrage benennt genau, was wegfällt, weil der Name allein nicht sagt, dass Züge gelöscht werden | Nutzerwunsch 22.11. |
| 2026-11-22 | 1.002.2 · Formular-Werkbank | „Partie hier beenden“ auch am Ende der Zugliste (roter Link unter dem Ende-Hinweis, nur solange Formular-Einträge übrig sind; Rückfrage mit Anzahl, Knopf „Rest verwerfen“); Ende-Hinweis „ohne Formular-Eintrag angehängt“ nach dem Beenden | Nicht zuordenbare Kritzeleien am Ende ließen sich sonst nicht loswerden, und nach dem Kürzen kamen sie beim Weiterspielen oder Neuladen zurück | Nutzerwunsch 22.11. |
| 2026-11-22 | 1.001.0 · Kurs-Löser | „Warum diese Aufgabe:“ als leise Textzeile in der Kontext-Karte und in „Linien durchsehen“ (8.1), nur mit Vermerk | Im Kurs „Persönliche Taktiken“ war nicht zu sehen, warum eine Linie zur Partie gehört; die Zeile soll beim Start der Linie da sein, ohne die Bedienung zu verdrängen | Nutzerwunsch 22.11. („kannst du dort immer vermerken, warum du glaubst, dass die Linie relevant ist?“) |
| 2026-11-19 | 0.999.3 · Repertoire-Trainer | Prep-Repertoire gegen den gespeicherten Gegner (8.1): Reihenfolge-Knopf auch dort, Dialog mit sechstem Wert „Nach dem Gegner“ (nur wo er gilt — sonst nicht in der Liste, 5.6); Leerzustand und Abschluss der aufs Fällige beschränkten Abfrage bieten „Alle Linien üben“ statt „Alle jetzt fällig machen“ bzw. „Von vorn“; Hinweis unter der Leiste nennt die geltende Regel | Abfragen brachte dort immer alle Linien; nur Fällige + Untrainierte heißt: es gibt jetzt einen Leerzustand, und „Von vorn“ hätte nach dem Fälligen ins Leere geführt | Nutzerwunsch 19.11. |
| 2026-11-19 | 0.999.1 · Repertoire-Trainer | Kopfleiste bei 600–768 px zweizeilig: Name + Moduswahl oben, Reihenfolge-Knopf links und ⋮ rechts gemeinsam darunter (8.1) | In einer Zeile schrumpfte der Repertoirename dort auf „S…“ (gemessen 28 px bei 620 px; bestand schon mit dem gleich breiten Umschalter „Häufigste Linien zuerst“) | Sichtprüfung 0.996.0 im Browser |
| 2026-11-19 | 0.999.0 · Turnierseite/ClubHub | „Ganze Kachel klickbar“ (5.2) auf den anderen Oberflächen: Turnierkarte in Liste, Karten-Popup und Kalenderfenster sowie Merkliste (8.2); ClubHub-Karteizeilen und -Gruppenkarten mit Fokusring/Tönung aus `--focus`/`--ink` (8.5). KidHub war schon so (Kacheln sind Links), LeagueHub hat keine Kachel mit einem Hauptziel | Die Regel gilt für alle fünf Oberflächen | Nutzerwunsch 19.11. („immer komplett klickbar“) |
| 2026-11-19 | 0.998.0 · RookHub | „Ganze Kachel klickbar“ (5.2) für die übrigen Karten: Kurskarten (Fläche = Titel-Link → Detailseite, aufgeklappter Kapitelblock über der Fläche), E-Book-Regal (nicht bei Mehrfachauswahl), Trainerstunden (Karte und Trefferzeile), Wochenpost (nicht im Formular, Bestenliste über der Fläche), Punktepartie-Durchläufe, Partie-Analysen, Rekonstruktionen, Favoriten, Aufgabenblätter, frühere Vorbereitungen (5.3); Tooltip am Hauptziel verzögert | Dieselbe Regel in allen Bereichen statt nur auf Startseite und Repertoire-Liste; ein sofortiger Tooltip am Hauptziel blitzte sonst bei jeder Mausbewegung über die Karte auf | Nutzerwunsch 19.11. („immer komplett klickbar“) |
| 2026-11-19 | 0.997.0 · alle Oberflächen | Neue allgemeine Regel „Ganze Kachel klickbar“ (5.2) mit dem Baustein `rh-tile` / `rh-tile-target` (`src/_tile.scss`): Fläche = Hauptaktion (Titel-Link, sonst Haupt-/erster Knopf), Nebenelemente bleiben eigene Ziele, nicht klickbar bei gesperrtem Hauptziel und im Bearbeiten-Modus, die Fläche ändert nie Daten, ohne eindeutiges Hauptziel keine Kachel; Tönung beim Überfahren/Drücken und Fokusring um die ganze Kachel (E-Ink 3 px schwarz). Zuerst Dashboard (Kacheln, Kurs-Zeilen, Woodpecker — 8.1) und Repertoire-Karten; „Listenzeile mit zweitem Ziel“ (5.3) ist die Zeilen-Form davon; nicht Umgestelltes in Abschnitt 10 | An vielen Kacheln war nur der Titel oder ein Knopf klickbar; Einzellösungen mit `(click)` am Container verlieren Mittelklick, Tastatur und saubere Nebenziele | Nutzerwunsch 19.11. („hast du oft kacheln wo nur einzelne teile klickbar sind. mach die mal immer komplett klickbar“) |
| 2026-11-19 | 0.996.0 · Repertoire-Trainer | Reihenfolge der Linien als Einstellung je Repertoire: im Trainer ein beschrifteter Rahmenknopf mit der geltenden Reihenfolge statt des Umschalters „Häufigste Linien zuerst“ (8.1); EIN Einstellungs-Dialog mit Optionsgruppe (`mat-radio-group`, je Wert Name + ein Erklärsatz, „Speichern“ erst auf Knopfdruck, Ladefehler statt Auswahl) für Trainer, ⋮ der Linienleiste und ⋮ der Repertoire-Karte (5.6) | Fünf sich ausschließende Reihenfolgen passen nicht in einen Umschalter; die Wahl gilt je Nutzer und Repertoire auf allen Geräten und muss auch außerhalb des Trainers erreichbar sein | Nutzerwunsch 19.11. |
| 2026-11-19 | 0.995.0 · Spielervorbereitung | `/prep`: Abschnitt „Frühere Vorbereitungen“ unter dem Suchfeld, nur solange nicht gesucht wird — Zeilen wie die Treffer (Name, Meta als `.rh-dots`: Datum, eigene Farbe, Partien · Jahre · Elo), zugeordneter Gegner als Link auf die Spielerseite, nicht zugeordneter als `<button>` mit Lupe + „Spieler suchen“ (füllt die Suche), daneben je Zeile ein Symbolknopf zum Trainings-Repertoire; leer → kein Abschnitt, Ladefehler → `app-load-error`; Suchfeld-Hinweis `subscriptSizing="dynamic"`. Neues Muster „Listenzeile mit zweitem Ziel“ (5.3, 8.1) | Man bereitet sich auf dieselben Gegner wieder vor und musste sie jedes Mal neu suchen; zwei Ziele je Zeile ohne Link im Link; ein Eintrag, der nur sucht, darf nicht wie ein Sprung aussehen | Nutzerwunsch 19.11. („zeig dort liste der alten vorbereitungen an und mit klick hol ich den spieler gleich wieder“) |
| 2026-11-18 | 0.994.0 · Woodpecker | Zeitplan als zweiter geteilter Baustein `app-woodpecker-schedule` neben dem Tagesziel: Löser-Kopf (letzte Angabe der Zyklus-Zeile, hinter dem Tagesziel), Dashboard-Kachel (eigene Zeile unter dem Tagesziel), Statistik-Fenster (eigene Zeile der Übersicht, ausführlich mit „Tag k von D“ und „aktuell n %“); im Zeitplan grüner Haken `check_circle` + „Im Zeitplan“ in `--rh-success` + Soll-Anteil klein, dahinter NEUTRAL „Zeitplan: noch 12 bis 57 %“ ohne Warnfarbe; Haken + Text brechen nie auseinander (5.12, 8.1) | Das Tagesziel sagt nur, ob das Minimum von HEUTE geschafft ist; ob man insgesamt dort steht, wo man am heutigen Tag des Zyklus stehen soll, stand nirgends | Nutzerwunsch 18.11.2026 („neben x pro tag für fertig auch noch ein checkmark haben wenn ich % der gesamtpuzzles passend zum aktuellen tag hab (und in statistik oben ausgewiesen noch x puzzle bis z.b. 55% erfüllt)“) |
| 2026-11-18 | 0.993.0 · Woodpecker | Tagesziel ohne Statistik-Fenster: geteilter Baustein `app-woodpecker-daily-goal` im Löser-Kopf (letzte Angabe der Zyklus-Zeile), auf der Dashboard-Kachel (eigene Zeile unter der Frist) und im Statistik-Fenster (ersetzt dort „Nötig je Tag: n (offen ÷ Resttage) · Heute: n“); offen leise „Heute: 23 von 45“, erreicht grüner Haken `check_circle` + „Tagesziel erreicht“ in `--rh-success` + Zahl klein, Zyklus 1 nur „Heute: n“; das Soll ist über den Tag fest (5.12, 8.1) | Ob das heutige Minimum geschafft ist, stand nur im Statistik-Fenster, und die Soll-Zahl dort sank mit jeder gelösten Aufgabe — „erreicht“ gab es nicht | Nutzerwunsch 18.11.2026 („oben auch ohne statistik visualisieren, wenn ich heute das notwendige minimum erledigt habe (sowohl im dashboard als auch wenn ich drinnen bin)“, „grüne checkmark oder so vielleicht“) |
| 2026-11-18 | 0.992.0 · Woodpecker-Löser | Statistik als modaler Inhalts-Dialog (760 px / 94vw) statt aufklappbarem Block in der Kontext-Card (löst die Einträge 0.957.0 und 0.958.1 ab): Knopf „Statistik“ (`bar_chart`) unter der Zyklus-Zeile; oben Übersicht in Lesegröße — Tabelle „Bisher“ gegen dieselben Aufgaben je früherem Zyklus (Zeit, Richtig), „Nötig je Tag: n (offen ÷ Resttage) · Heute: n“; darunter die bisherigen Abschnitte ohne Doppeltes; solange das Fenster offen ist, steht die Lösezeit und das Brett ist unkenntlich (Blur), Hauptknopf „Weiter lösen“ (5.12) | Die wichtigsten Zahlen standen mitten in einem langen Block in der Seitenspalte; beim Lesen lief die Uhr der Aufgabe weiter | Nutzerwunsch 18.11. |
| 2026-11-16 | 0.987.0 · Trainer-Notizbuch | Unterseite `/training-videos/notebook`: Themen „Sagt er immer wieder“ mit Häufigkeit, Eröffnungsempfehlungen mit Mini-Brett + Repertoire-Satz, alle Notizen als Kategorie-Reiter (Chip-Liste mit Anzahl) je Stunde, Aufklapper „▶ anhören“/„Rohzitat“ je Notiz (8.1) | Trainer-Aussagen stundenübergreifend nachschlagen statt Stunde für Stunde | Auftrag der betreuenden Sitzung 16.11. |
| 2026-11-16 | 0.986.0 · Trainerstunden | Neue Seiten `/training-videos` (Liste + Suche), Stunde (Video + Brett + Segmentliste, Liste folgt dem Video), Nachspielen (Zug finden, Hinweis/Auflösen/Überspringen, Ausschnitt-Player „Erklärung hören“ mit „nur Ton“, im E-Ink-Modus vorgewählt) und die Karte „Trainerkommentar“ an der eigenen Partie, getrennt von der Engine-Erklärung (8.1) | Erste Video-Seite der App; Trainer- und Engine-Aussagen dürfen nicht verwechselt werden; E-Ink zeigt bewegtes Bild nur verschmiert | Auftrag der betreuenden Sitzung 16.11. |
| 2026-11-15 | 0.983.0 · Dashboard/Menü | LeagueHub-Kachel (8.1) als dritte Ausnahme in `DEFAULT_VISIBLE`, nur mit `league.view`; Knopf und Menüeintrag „LeagueHub“ (Community, nach „Turniere“) mit `open_in_new` | Wer Zugriff hat, soll LeagueHub ohne „Anpassen“ und ohne Adresse zu kennen erreichen; das Symbol kündigt den Wechsel der Oberfläche an | Nutzerwunsch 15.11. |
| 2026-11-15 | 0.984.0 · Zusammenfassung | ⋮ im Kopf der Zusammenfassung (8.1): „Zur Partie“, „Link teilen“, „Anonym teilen“, „Teilen-Link widerrufen“; unter 600 px ⋮ oben rechts neben den Namen und „Zur Partie“ nur im ⋮; anonymer Link zeigt „Weiß – Schwarz“ ohne Wege zur Partie | Teilen der Zusammenfassung fehlte auf der eigenen Seite; anonym = ohne Spielernamen, serverseitig | Wunsch User 15.11. |
| 2026-11-15 | 0.980.0 · Zusammenfassung | Links im Text (Beispielpartien aus dem Explorer): Akzentfarbe, durchgezogen unterstrichen (Züge bleiben gepunktet), fett/kursiv nur um den ganzen Link; Meisterpartie im selben Tab, lichess.org in neuem Tab. Seite „Meisterpartie“ (`/explorer/game/masters/:id`): eine Spalte ≤ 720 px, Kopfkarte (Spieler mit Elo, Ergebnis, darunter „Meisterpartie · Turnier · Ort · Jahr“ in on-surface-variant) und darunter das Brett der Zusammenfassung unverändert; unbekannte Partie = Leerzustand `link_off` mit Weg zur Startseite, Explorer weg = Ladefehler mit Wiederholen (8, Partie-Zusammenfassung) | Wunsch 15.11.: Beispielpartien „am besten mit einem Link“; lokale Meisterpartien gibt es auf lichess.org nicht | User über die betreuende Sitzung |
| 2026-11-14 | 0.975.0 · E-Book-Leser | Lösen (8, Brett-Leiste): Gegnerzug wird vorgespielt, Brett aus Löser-Sicht, Statuszeile nennt die Farbe; ohne bestimmbare Löserseite kein Modus-Knopf „Lösen“, unter „Ansehen“ eine leise Grundzeile mit Glühbirne für die Buchlinie (5.4) | Meldung 14.11.: Löser spielte die Seite, die mattgesetzt wird („Alekhine: Move by Move“, S. 154) | User über die betreuende Sitzung |
| 2026-11-14 | 0.973.0 · Zusammenfassung | Übersicht „Empfohlene Übungen“ (Überschrift, Stellen, Karten, Buchangaben, Löser, Handy-Blatt) entfällt; „Taktiken trainieren“ bleibt als einzelner Aktionsblock am Textende: Trennlinie `--mat-sys-outline-variant` + 24 px Abstand statt Überschrift, ≤ 75ch (8, Partie-Zusammenfassung) | Wunsch 14.11.: „train am ende der summary gefällt mir, aber übersicht darüber kann weg“; ohne Übersicht klebte der Knopf sonst am letzten Absatz | User über die betreuende Sitzung |
| 2026-11-14 | 0.972.0 · Zusammenfassung | „+“ (Stellung markieren) als sechster Knopf der Brettleiste am Seitenbrett und im Blatt, hinter dem Engine-Knopf (5.12); ≤ 480 px Nebenknöpfe 36 px wie Partieseite/Analysebrett | Wunsch 14.11.: „summary um den (+) knopf erweitern“; gemessen 320/390 px: Leiste = Brettbreite, kein Seiten-Scroll | Nutzerwunsch |
| 2026-11-14 | 0.969.0 · Fehler nachspielen | Partieseite im Training (8.1): nur Brett + Trainer — Zugliste, Auswertung, Repertoires und Meisterpartien ausgeblendet; Trainer ab 1280 px als Spalte rechts neben dem Brett (≤ 680 px, brett-hoch, scrollt in sich), am Handy unter dem Brett; Leertaste = „Nochmal“, Tastenname im Tooltip | Am PC rutschte der Trainer unter dem Brett aus dem Bild, daneben standen drei Spalten, die im Training niemand braucht | Wunsch User 14.11. |
| 2026-11-14 | 0.967.0 · Zusammenfassung | „Taktiken trainieren“ unter den Empfohlenen Übungen (5.12): Sekundärknopf `school` + leise Hinweiszeile, nur angemeldet mit Kurs-Zugang, springt in den Kurs-Löser auf das Kapitel der Partie | Wunsch 14.11.: Link „Taktiken trainieren“, Kurs „Persönliche Taktiken“ mit einem Kapitel je Partie | User über die betreuende Sitzung |
| 2026-11-14 | 0.968.0 · Zusammenfassung | „Taktiken trainieren“ unter den Empfohlenen Übungen (5.12): Sekundärknopf `school` + leise Hinweiszeile, nur angemeldet mit Kurs-Zugang, springt in den Kurs-Löser auf das Kapitel der Partie | Wunsch 14.11.: Link „Taktiken trainieren“, Kurs „Persönliche Taktiken“ mit einem Kapitel je Partie | User über die betreuende Sitzung |
| 2026-11-14 | 0.963.0 · Zusammenfassung | „Die Partie“ als verschmolzener Fließtext (5.12): Partiezüge fett im Text, Varianten in Klammern kleiner/kursiv/gedämpft, Zugzeichen grün/rot aus den Tokens, Zug am Brett mit Akzentfläche; der Abschnitt „Zusammenfassung“ entfällt in neuen Texten, die eingesetzte Zugliste bleibt nur für alte | Wunsch 14.11.: „ich mag nicht einfach die zugliste aufgelistet bekommen sondern es soll die partie mit dem ganzen ersten block gemerged werden“ (Mockup abgenommen) | User über die betreuende Sitzung |
| 2026-11-13 | 0.960.0 · Zusammenfassung | Abschnitt „Empfohlene Übungen“ (5.12): Stellen der Partie mit Zugnummer + Grund, Aufgaben als Karten mit Brett, drei Stellen offen, Lösen im Abschnitt bzw. als Blatt; ohne Stellen kein Abschnitt | Wunsch 13.11.: „am ende eine kleine sammlung von empfohlenen übungen … direkt auf der seite lösbar“; „erzwinge keine motive/aufgaben“ | User über die betreuende Sitzung |
| 2026-11-13 | 0.962.0 · Zusammenfassung | Empfohlene Übungen: Buchbeispiele vor Lichess, Quellzeile (Titel · Autor · Kapitel/Seite) als Link in den Leser, „Beispiel im Buch“ ohne Löser | Wunsch 13.11.: „wenn du in büchern das motiv findest dann sind beispiele aus dem kapitel > allgemeine probleme“; „ja bau den buch-teil“ | User über die betreuende Sitzung |
| 2026-11-13 | 0.959.0 · Zusammenfassung | Block „Die Partie“ hinter dem Abschnitt „Zusammenfassung“ (ganze Zugfolge, Zugpaare ohne inneren Umbruch, Zug am Brett hervorgehoben, Ergebnis am Ende); unter 900 px öffnet ein Zug-Klick das Brett als Blatt (5.6) mit Zugliste und Engine statt zum Seitenende zu scrollen; Engine-Knopf `memory` am Seitenbrett | Wunsch 13.11.: „die zusammenfassung auch die ganze partie abbildet … zum durchklicken“; „am mobile … nicht zum ende springen sondern board modal darüber + zugliste darunter + freizug mit engine“ | User über die betreuende Sitzung |
| 2026-11-10 | 0.958.1 · Woodpecker-Löser | Statistik-Umschalter als kompakter `mat-stroked-button` in eigener Zeile unter der Zyklus-Zeile (Symbol + Zustand: „Statistik anzeigen“/„Statistik ausblenden“, 44 px bei grobem Zeiger) statt Textlink am Ende der `.rh-dots`-Zeile; zweiter Knopf „Statistik ausblenden“ am Ende des aufgeklappten Blocks, danach Fokus + `scrollIntoView` auf den oberen Knopf (5.12) | Nutzer fand den Umschalter nicht: „wie blend ich die statistik wieder aus?“ – „nein da steht nichts“ | User über die Sitzung „woodpecker“ |
| 2026-11-10 | 0.958.0 · Woodpecker | Dashboard-Kachel „bereit“: „Zyklus N starten“ startet direkt und springt in den Löser (Spinner im Knopf, Fehler als Snackbar); Übersicht: genau EIN aktives Training, ⋮ „Archivieren“ mit Rückfrage, „Neues Training“ mit Archiv-Hinweis im Dialog und Knopf „Archivieren und starten“, zugeklappter Abschnitt „Archiv (N)“ mit kompakten Karten, ⋮ „Wiederherstellen“ (gesperrt mit Grund neben einem aktiven) und „Löschen“; kein „+N weitere“ auf der Kachel (8.1) | Nutzerentscheidungen 10.11.: Knopf soll den Zyklus direkt starten; „soll nur ein Woodpecker geben – für einen neuen muss ein alter archiviert werden“ | User über die Sitzung „woodpecker“ |
| 2026-11-10 | 0.957.0 · Woodpecker-Löser | Ganze Statistik standardmäßig eingeklappt, Textknopf „Statistik“ am Ende der Kopfzeile (aria-expanded, je Gerät gemerkt); aufgeklappt die drei Zeilen + ausführliche Statistik in benannten Abschnitten (Dieselben Aufgaben, Wechsel, Hochrechnung, Tipps, Verlauf bzw. Je Tag, Sorgenkinder), Tabellen im eigenen Scrollkasten (5.12) | Wunsch 10.11.: „umfangreiche statistik … die gleichen puzzle haben letztes mal 2h gebraucht“, „soviel mit sovielen hints … muss nicht immer da sein“, „Statistik standardmäßig eingeklappt → ausklappbar“ | User über die Sitzung „woodpecker“ |
| 2026-11-10 | 0.956.0 · Woodpecker-Löser | Statistik des laufenden Zyklus in der Kontext-Card (5.12): Kennzahlzeile gelöst/Quote, ohne/mit Tipp, offen, Ø; Hochrechnung (Restzeit, nötig je Tag, Tempo, voraussichtliches Ende vs. Frist; Zyklus 1: ≈ Satz bis zur Frist); Vorzyklus zum Vergleich — leise, höchstens drei Zeilen unter dem Fortschritt | Wunsch 10.11.: „im Detail … gehen mir die Stats ab – wie viel gelöst, wie viel mit Hint, Hochrechnung“ | User über die betreuende Sitzung |
| 2026-11-10 | 0.955.0 · Dashboard | Woodpecker-Kachel (8.1): laufendes Training mit Zyklus, Fortschritt, Frist und genau einer Aktion je Zustand; zweite Ausnahme in `DEFAULT_VISIBLE` nach dem Muster der angepinnten Kurse (nur mit eigenen Daten sichtbar, sonst nur im Anpassen-Modus) | Wunsch 10.11.: „Woodpecker braucht ein Panel auf dem Dashboard“ | User über die Sitzung „woodpecker“ |
| 2026-11-10 | 0.954.0 · RookHub `/games` | Zusammenfassungs-Symbol je Zeile (fertig = Icon-Link `article`, offen = Sanduhr ohne Knopf, fester Platz, Aktionsspalte 128 px); Turnier als dritte Zeile unter den Namen (gekürzt + Tooltip, alle Laschen außer bei Online-Partien); „Nur meine Partien“ als Häkchen unter dem Suchfeld (`?mine=1`); Partiekopf einer eigenen Partie zeigt den Stand der verbundenen Vereins-Zusammenfassung (Sanduhr/Link) statt „anfordern“ | Stand ohne Öffnen jeder Partie sehen; hochgeladene fremde Vereinspartien ausblenden; Turnier ohne Öffnen | Wunsch 10.11. über die betreuende Sitzung |
| 2026-11-10 | 0.953.0 · Admin „Engines“ | Hinweis-Karte oben nur bei zutreffenden Hinweisen (Symbol + Satz); Engine-Karten je Maschine unter einem Aufklapper-Kopf (Knopf mit `aria-expanded`, ≥ 44 px, Kennzahlen als `.rh-dots`, Zeitplan-Zeile), Zustand je Betrachter in localStorage; Token-Tabelle im Scrollkasten | Wunsch 10.11.: Übersicht Tokens, Zeitplan, kN/s normal/Schonzeit, „was dir sonst noch einfällt“ | User über die betreuende Sitzung |
| 2026-11-10 | 0.951.0 · Admin „Engines“ | Hintergrund-Zuordnung je Engine und Konto umschalten: Zeile mit Zustand als Satz + Symbol, `mat-stroked-button` nennt Handlung und Konto, ohne Rückfrage, Gruppe gesperrt beim Speichern (5.4) | Wunsch 10.11.: „lass mich … hier auch warten ob hintergrund oder nicht“ | User über die betreuende Sitzung |
| 2026-11-10 | 0.950.0 · Runde d10 | 41 Funde des Volllaufs 09.11. (5 Rollen × 4 Breiten × hell/dunkel): „Seite nicht gefunden“ wartet höchstens 6 s auf die Versionsprüfung und prüft den Service Worker nur, wenn einer die Seite steuert (5.10); Merksätze mit automatischer Zeilenhöhe; Partiekopf: Original/Teilen nur im ⋮, „Fehler nachspielen“ als Hauptknopf nach der Analyse, Titelspalte ≥ 280 px, „Ohne Engine“ am Handy nur im ⋮ (5.5, 8.1); Zusammenfassungs-Karte mit 16-px-Polster, `.empty-state`, Sperrkarte bei 403, „Zusammenfassung anfordern“ als Hauptknopf im Leerzustand; SanPipe in Tiefe-Analyse-, Verlauf- und Vergleichs-Dialog sowie E-Book-Zugliste (5.14); E-Book-Partiekopf mit Locale-Datum und „½–½“ statt roher PGN-Werte (5.13); Zugbaum-Hinweis als ? statt Dauertext, Engine-Hilfe als ? statt ⓘ (5.11); Grund für gesperrtes „Vergleichen“ einmal in der Knopfleiste (5.6); Schlagwort-Chips ≥ 44 px Trefferfläche (7); E-Books: Sprach-Chips nur einer gewählt, Karten zählen nur Originaldateien, im Auswahlmodus ein gefüllter Knopf; Abweichungs-Trainer: „deines Gegners“ (Genitiv-Schlüssel), `app-load-error` mit „Erneut versuchen“, Schwelle als `mat-select`; Datum/Uhrzeit-Felder gleich hoch; „(optional)“/„(freiwillig)“ als Hinweis statt im Label (5.6); Favoriten „Merken/Gemerkt“ statt „Lieben“; Woodpecker-Grund in der Knopfleiste, „nicht gefunden“ ohne Brett; LeagueHub: „Link ungültig“ mit Weg zurück, Kandidaten-Raster `align-content: start`, Wartezeile ohne doppelte Abblassung; Turnier: Merken überall gefüllt (8.2), Ergebnisse mit Dezimalkomma, Paarungskarten-Nummer in on-surface-variant, Melden-Dialoge mit einem Satz + ?, typografische Anführungszeichen; KidHub Figurenwahl ≥ 44 px; ClubHub Datum einmal als TT.MM.JJJJ | Design-Durchsicht d10 (Prüfseite, vom User bestätigt; 3 Funde „Nicht beheben“: Zielpunkte dunkle Felder, „+“-Fenster-Grund, Sperrgrund Tablet) | d10-* |
| 2026-11-10 | 0.949.0 · RookHub `/games` | Laschen „Alle / Online / Am Brett“ (`mat-tab-nav-bar`, Wahl in `?kind=`, Zahl je Lasche), leere Lasche als ein Satz statt Leerkarte | Wunsch 10.11.: „unterscheidung … allgemeine saved games, online games, otb games“ | User über die betreuende Sitzung |
| 2026-11-10 | 0.946.0 · Admin „Engines“ | Admin-Reiter mit Zustands-Pillen (Symbol + Text), Karten „Warteschlange“/„Durchsatz“ mit Tabellen, die auch am Handy Tabellen bleiben (`min-width` + `.table-scroll`), darunter eine Karte je Engine im Raster (≥ 420 px Spalte); Offline-Engines eingeklappt; Auffrischen 15 s nur bei sichtbarem Tab | Wunsch 10.11.: Übersicht der registrierten Engines, Hintergrund, Warteschlange, wer rechnet was, Durchsatz | User über die betreuende Sitzung |
| 2026-11-10 | 0.943.0 · LeagueHub | Partieformular hochladen: je weiterem Foto Beschriftung „Seite N“ / „weiteres Foto von Seite N“, Umschalter „Neue Seite \| Dieselbe Seite“ (`.seg`) und „↑“; Galerie-Mehrfachauswahl zuerst „nächste Seite“ | Formular 113: zwei Blätter landeten als eine Seite (verkehrt herum), die Wahl war nach dem Hinzufügen nicht mehr sichtbar | Vorfall 10.11., Auftrag des Users über die betreuende Sitzung |
| 2026-11-09 | 0.941.0 · LeagueHub | Prognose: Umschalter „Gegner \| Eigene Aufstellung“ als `.seg` über dem Prognose-Teil (unter den Paarungen, 44 px am Handy); nur angemeldet, weg auf Teilen-Links, bei spielfrei und gesperrt. In der eigenen Sicht heißt die Farblegende „Eigene Mannschaft hat …“, die Teilen-Knöpfe der Prognose fehlen | Wunsch 09.11.: eigene Prognose je Runde sehen | User |
| 2026-11-09 | 0.938.0 · Link-Vorschau | Vorschaukarten (1200×630) für geteilte Partie, Stellung, Rekonstruktion, Repertoire-Linie, Aufgabenblatt, Puzzles, Wochenpost und LeagueHub-Begegnung/Ligapartie: Diagramm links, rechts Kicker/Titel/Untertitel in Marken-Farben (RookHub 3.1 dunkel, LeagueHub 8.4 mit rotem Balken), Wortmarke unten rechts, Schrift DejaVu Sans; Puzzles aus Löser-Sicht ohne Lösung | Wunsch 09.11.: „für alles, was öfters geteilt wird, passende Info-Vorschaubilder“ | `docs/api/link-vorschau.md`, `OgImageService.Cards.cs` |
| 2026-11-09 | 0.929.0 · Zusammenfassung | „Empfohlene Literatur“ als umrandete Zeilen (Titel, Autor, Begründung, Textknopf „Nicht mehr vorschlagen“ → Zeile bleibt mit „Rückgängig“); wartende Anforderung in der Kopfzeile nur kurz, Fortschritt hinter (i)-Tooltip (auch per Antippen) | Wunsch 09.11.: Literatur je Fehler; langer Satz quetschte die Spielernamen | User |
| 2026-11-09 | 0.917.0 · Merksätze | Merksatz unter Erklärungen kursiv mit fettem „Merksatz:“, dahinter (i) (`info_outline`, 18 px) klappt Buchstellen auf (Autor — Titel, Stelle, Zitat als Blockzitat; Link nur bei Zugriff). Seite `/maxims`: Karte ≤ 860 px, Kategorien als h2, Sätze als `mat-expansion-panel` (multi), Inhalt „Wann er passt“ + „Bücher“ | Wunsch 09.11.: „(i) mit Verweis auf die Bücher, am besten mit direktem Link“; „Link, wo die Merksätze aufgelistet sind, aufklappbar“ | User |
| 2026-11-09 | 0.916.0 · Fehler nachspielen + Zusammenfassung | Züge in der Erklärung des Zugvergleichs als Textknöpfe (`.mv`, Akzentfarbe, gepunktet unterstrichen, Hover-Fläche); Klick lädt die Folge in die Trainings-Analyse der Seite. „Neu anfordern“ der Zusammenfassung nur auf deren Seite, Partie-Kopfzeile nur Link | Wunsch 09.11.: „die lines in der erklärung klickbar wäre toll“; „rewrite summary … muss nicht so prominent sein“ | User |
| 2026-11-08 | 0.908.0 · Fehler nachspielen | Zugvergleich Partiezug ↔ bester Zug unter dem Urteil (8.1): zwei Zeilen mit Bewertung + Engine-Linie, Begründung, Textknopf zur Vergleichsseite; laufend eine leise Zeile, sonst nichts; Vergleichsseite mit `?clubGame=` ohne Verlauf und ohne „geht nicht“ | Wunsch 08.11.: „für diese Fehler auch immer gleich den Fehlzug mit dem besten Zug vergleichen“ | User |
| 2026-11-08 | 0.904.0 · Glocke | Neue Glocken-Typen „Analyse fertig“ (`insights`, Link auf die eigene Partie) und „Formular wartet auf Freigabe“ (`pending_actions`, Kategorie Admin); ein Glocken-Ziel mit absoluter Adresse (LeagueHub) öffnet per Seitenwechsel statt über den Router (`openNotificationLink`) | Wunsch 08.11.: „informier ihn via glocke wenn fertig tief analysiert“, „gib mir einen Glockenping“ bei wartender Freigabe — die Freigabe liegt in LeagueHub, einer anderen Oberfläche | User |
| 2026-11-08 | 0.902.0 · Formular-Leser | Wartende Einlesung zeigt „Platz N in der Warteschlange“ statt des Nachsatzes, sobald andere davor stehen; dann kein Warnhinweis nach 15 min (5.10) | Zwei Formulare zugleich: das zweite zählte nur „wartet“ hoch und sah nach Ausfall aus | Nutzerwunsch 08.11. |
| 2026-11-08 | 0.900.0 · Partieseite + Analysebrett | Partieseite = „Mit Engine“; Kopfzeile dort „Ohne Engine“ (`mat-stroked-button`, `visibility_off`, vorn in `.header-actions`); im Analysebrett führt „Mit Engine“ mit `?game=` zurück zur Partieseite | Wunsch 08.11.: „Mit Engine → club-games/236, Ohne Engine → …?blind=1“ | User |
| 2026-11-08 | 0.898.0 · Analysebrett | Partie immer im Linien-Baum; Kopf-Knopf „Ohne/Mit Engine“ schaltet auch dort den Modus, Dialog = nur Startmodus | Wunsch 08.11.: „zwischen den beiden Modi einfach wechseln können“ | User |
| 2026-11-07 | 0.895.0 · Analysebrett | Kopf-Schalter „Ohne Engine“/„Mit Engine“ (`.header-actions`), führt `?blind=1` in der Adresse mit | Wunsch 07.11.: „einfacher Weg zwischen /analysis?blind=1 und /analysis zu wechseln“ | User |
| 2026-11-07 | 0.893.0 · Repertoire-Trainer | Kommentar-Formen nur noch am Linienende (Schlussstellung + Nachbetrachtung); Schalter „Kommentare zeigen“ (Taste K, je Gerät) in `.trainer-actions`, Kommentare der letzten Züge als Kontextkarte ohne Halt | Nutzerwunsch 07.11.: Formen während des Spiels stören, Kommentare auf Wunsch auch beim Abfragen | Wunsch des Users über die betreuende Sitzung |
| 2026-11-07 | 0.892.0 · Repertoire-Trainer + Spielerkarte | `.opponent-note` (Partien des Gegners zur Stellung, Umschlag warn), Abweichungs-Trainer als eigene Seite im Trainer-Aufbau, Karte: Rahmenknopf „Abweichungen trainieren (N)“ neben dem Anlegen-Knopf + aufklappbare Liste | Nutzerwünsche 07.11.: „trainiere abweichungen“, „meldung ab wann ich von gespielten partien abgewichen bin“ | Wunsch des Users über die betreuende Sitzung |
| 2026-11-07 | 0.889.0/0.890.0 · Repertoire-Trainer | „Linie pausieren“ neben „Überspringen“ (Snackbar mit Rückgängig, Hinweis `.paused-note`); Nachbetrachtung im „Linie fertig“-Kasten: Durchklicken, Engine-Schalter, quere Bewertungsleiste, Analysebrett-Link | Nutzerwünsche 07.11.: „pause line“, „durch die züge klicken und die engine zuschalten“ | Wunsch des Users über die betreuende Sitzung |
| 2026-11-07 | 0.886.0 · Repertoire-Trainer | Fehlzug bleibt rot stehen bis „Nochmal“/Tippen (Brett gesperrt); Zeile „schon n× falsch“ nur mit Daten; Aktionszeile `.trainer-actions` (Rahmenknöpfe „Tipp“ mit Stufenbeschriftung, „Linie überspringen“), Tipp-Markierung grün; Tipp-Zähler in `--rh-hint`; Chessable-Textlink hinter dem Linienlabel (8.1) | Nutzerwünsche 07.11.: „stellung stehen lassen“, „wie oft der schon falsch war“, „hintfunktion“, „button skip line“, „link zur chessable line“ | Wunsch des Users über die betreuende Sitzung |
| 2026-10-30 | 0.881.0 · E-Book-Baum | E-Book-Brett: Modus „Baum“ (8.1) — Schrittleiste + „Als Repertoire speichern“, Weg ab der Grundstellung als Zugfolge, Fortsetzungen als Zeilen mit „n Linien“; Speichern-Dialog 440 px mit Umschalt-Paaren für Umfang und Farbe | Eröffnungsbücher sind keine Aufgabenbücher — Linien durchklicken und als Repertoire mitnehmen | Nutzerwunsch 30.10. (Prod #1071 „Danish Dynamite“) |
| 2026-10-30 | 0.880.0 · Runde d9 · Dialoge, Analyse, LeagueHub, Turnier | Dialogfelder mit `mat-hint` 12 px Abstand zum nächsten Feld, Karten mit Outline-Feld in Zeile 1 oben 20 px (5.2/5.6); „optional“ als Hinweis; Grund am gesperrten Teilen-Knopf; Roast ohne Analyse bietet „Partie analysieren“; QR-Ruhezone 4 Module; ☰-Symbole nur an externen Links, Rechtslinks zuletzt (5.5); Bewertungsleiste fluchtet mit dem Brett; LeagueHub-Paarungen am Handy ohne Abzeichen-Platzhalter, „1 : 0“ geschützt, gesperrter Knopf `not-allowed` + Grund (8.4); Turnier: Region ohne Platzhalter, gekürzte Namen mit „…“, Mannschaftsdialog `autoFocus: 'dialog'` (8.2) | Hinweise klebten am nächsten Label, Sackgassen-Dialoge, gesperrte Knöpfe ohne Grund, QR ohne Ruhezone, 147 px hohe Paarungszeilen, „- (AUT)“ | UI-Sweep d9 (d9-rk-*, d9-l-*, d9-t-*) |
| 2026-10-30 | 0.880.0 · Runde d9 · E-Books | Regal-Titelbild `contain` statt `cover`; Gast-Banner ein Satz, am Handy einzeilig; Leser unter 1100 px: Inhalt/Schrift/Zoom im ⋮, Titel mit Mindestbreite, angedockt für Gäste nur „Anmelden“; Kapitelzeile am Handy ohne Balken: Name `flex: 1 1 0`, ⋮ in Zeile 1 (5.5) | Titel/Autor im Titelbild abgeschnitten; Regal am Handy bei 43 % Vorlauf; Lesertitel am Tablet „Pro…“; d8-Fix brach das ⋮ weiter allein um (Name-Grundbreite calc(100% − 9rem)) | d9-rk-ebook-cover-beschnitten, d9-rk-ebook-anon-handy-banner-vorlauf, d9-rk-reader-titel-abgeschnitten, d9-rk-kapitelzeile-leerraum-handy |
| 2026-10-29 | 0.877.2 | E-Books: Sortierung und „Meine Uploads“ auf jeder Breite hinter „Filter ▾“ (vorher ab 600 px in der Zeile); aktives „Meine Uploads“ zugeklappt als Chip | Nutzerwunsch 29.10.: „my uploads gehört auch in filtercomponente“, „sort by auch in filter“ | Nutzer |
| 2026-10-29 | 0.877.0 | E-Books: Schlagwörter, Sprache und Elo-Spanne auf jeder Breite in EINER Filter-Komponente hinter „Filter ▾“ (Zahl der aktiven Filter, zugeklappt Chips), Sortierung/„Meine Uploads“ ab 600 px weiter in der Zeile; Schlagwörter in der Oberflächensprache beschriftet und danach sortiert; leise Gesamtzeile „N Bücher · M Dateien (Formate)“ über dem Regal | Nutzerwunsch 29.10.: „versteck die tags in einer filterkomponente“, „tags müssen mehrsprachig sein“, Gesamtmenge passend zur Filterung | Nutzer |
| 2026-10-29 | 0.876.0 · Buch-Übersetzung | Leser: Sprachschalter als `translate`-Symbolknopf mit Menü Original/Englisch (aktiv = Primärfarbe), je Gerät gemerkt; Regal: Abzeichen `translate` + „EN“; Verwalter-Übersicht als 560-px-Dialog aus dem Bibliotheks-⋮ (8.1) | Bücher werden ins Englische übersetzt — umschalten muss im Leser ohne neue Leiste gehen | Nutzerwunsch 29.10. |
| 2026-10-30 | 0.879.2 · Leser am Handy | Sprachschalter steht am Handy in der Titelzeile (Zurück · Titel · Sprache · ⋮), nicht allein in einer dritten Zeile; EPUB-Zugtabellen mit Figurinen-Bildern werden kompakt gesetzt wie reine Text-Zugtabellen | Fund UI-Sweep 29.10. | ui_design_interface |
| 2026-10-29 | 0.878.0 | Runde d8, RookHub-Dialoge: Grund eines gesperrten Dialogknopfs global in `styles.scss`, schrumpft statt die Knöpfe umzubrechen, eigene Zeile bei Knopf davor (5.6); Sammlungs- und PGN-Dialog nennen den Grund; Hinweistexte aller Dialoge on-surface-variant (Wache `dialog-hint-contrast.spec`); Serien-Verteiler ein Satz + ?; Zeitpunkte als Datum + Uhrzeit `app-date-time-fields` statt `datetime-local` (5.13); Kapitelzeit-Felder als Outline-Felder (5.8); Teilen-Dialog fokussiert „Kopieren“; Zeitkarte unten 8 px weniger Polster vor Textknöpfen (5.2); Auge im Kalkulieren in einer Flex-Zeile neben dem Hinweis | Hauptknopf allein in Zeile 2, gesperrte Knöpfe ohne Grund, doppelt abgeblasste Hinweise, US-Datum mit 12-h-Uhr in der deutschen Oberfläche, 12,8-px-Zahlenfelder, abgeschnittener Link, unten mehr Luft als oben, Auge auf der Grundlinie | UI-Sweep d8: d8-rk-dialog-aktionen-umbruch, d8-rk-dialog-aktionen-zweite-zeile, d8-rk-ausgabe-dialog-grund-umbruch, d8-rk-sammlung-speichern-ohne-grund, d8-rk-sammlung-dialog-speichern-ohne-grund, d8-rk-pgn-dialog-hochladen-ohne-grund, d8-rk-dialog-hinweis-kontrast, d8-rk-serien-verteiler-hinweis-lang-klein, d8-rk-ausgabe-datetime-us-format, d8-rk-kapitelzeit-eingabefelder-klein, d8-rk-kalk-kapitelzeit-felder-nicht-outline, d8-rk-share-url-feld-abgeschnitten, d8-rk-zeitkarte-polster-unten, d8-rk-hinweiszeile-auge-versetzt |
| 2026-10-29 | 0.878.0 | Runde d8, RookHub: ☰-Untermenü am Handy blendet die Menüs darunter aus (5.5); Leerzustand im Dialog ohne Seitenpolster, linksbündig (5.10); Hilfe-Symbol immer ? (5.11); „Stellung korrigieren“: unter 26 rem Zeilenbreite „Weiß | Schwarz“ statt „… am Zug“ (Langtext als `aria-label`), Werkzeugzeile in einer Zeile; Kapitelzeilen ohne Balken am Handy: Linienzähler vor dem ⋮, 5,5 rem rechtsbündig; „Roast my game“ in de/hr/hu übersetzt („Partie roasten lassen (KI)“); Engine-Tempo in vollen Zahlen „Stellungen pro Sekunde“ statt kN/s | zwei Menüflächen versetzt übereinander, ein Satz in ≈ 90 px Leere, ⓘ neben ?, Drehen und ⋮ allein in Zeile 2 (d7 nicht behoben), ⋮ je Zähler anders versetzt, englischer Menüeintrag, Erklärung mit der Einheit, die sie erklären soll | UI-Sweep d8: d8-rk-konto-menue-doppelt, d8-rk-verlauf-leerzustand-luftig, d8-rk-lochfinder-hilfe-symbole-uneinheitlich, d8-rk-stellung-korrigieren-mobil-symbolzeile, d8-rk-kapitel-menue-spalte-versetzt, d8-rk-partie-menue-roast-englisch, d8-rk-engine-tooltip-kn-jargon |
| 2026-10-29 | 0.878.0 | Runde d8, LeagueHub + Turnier: „Interessante Stellungen:“ unter Brett und Steuerung statt im Rollbereich der Zugliste; Reiter aktiv auch mit Query-Parametern; jedes mat-menu `lh-menu`, „+“-Fenster `panelClass: lh-dialog` (LeagueHub-Fläche); Prozent mit geschütztem Leerzeichen; Turnier-Dialogtitel als `mat-dialog-title`, Teilen-Link aus Tokens mit „…“, Weiterscroll-Schatten `rh-scroll-hint`, ein ⓘ je Zeile | Nachprüf-Lauf 29.10. (d8) | d8-l-*, d8-t-* |
| 2026-10-28 | 0.869.5 | LeagueHub-Brettpaarungen am Handy: eigener Container `.pairings-wrap`, unter 480 px Namen umbrechend, Elo in Zeile 2, Abzeichen/Analyse/⋮ gestapelt, Analyse und ⋮ 44 × 44 px (8.4) | Tabelle 461 px breit, Analyse-Symbol und ⋮ außerhalb des Bildes, Seite scrollte waagerecht | UI-Sweep 20261028_1906_lh-brettmenue |
| 2026-10-28 | 0.869.0 | Runde d7, Stellung korrigieren, Datenschutz, Texte: Dialog „Stellung korrigieren“: Abbrechen (`mat-button`, ohne ✕) und Übernehmen (`mat-flat-button`) in `mat-dialog-actions`, Brett und Figurenleisten an der Fensterhöhe bemessen (`--ps-board-size`), Werkzeugzeile in voller Breite; Stellungs-Editor: „Grundstellung“/„Brett leeren“ (`layers_clear`) beschriftet im ⋮ statt loser Symbole; Datenschutz RookHub/LeagueHub/ClubHub unter 1440 × 900 ab dem dritten Abschnitt zugeklappt (8.6); Studienbuch ohne Startknopf „Sequenziell“ und ohne ▶ je Kapitel; Glossar „Schlagwörter“, „Lc0“ (6); E-Book-Tooltip „Modus – Erklärung“ ohne Quellnamen; Mehrzahl-Paar `ebooks.bulk.forCount/…One`; Markieren-Dialog 440 px, Aktionen in einer Zeile; Glocken-Abzeichen in niedrigen Fenstern mit höherer Spezifität | Knöpfe außer Sicht, ✕ wie Schließen aber destruktiv, Datenschutz am Laptop 12,8 und auf dem Boox 13 Bildschirme, zwei Startknöpfe, uneinheitliche Begriffe, Doppelpunkt-Kette, Klammer-Mehrzahl, Knopf allein in Zeile 2, Abzeichen −8 px | UI-Sweep d7: d7-rk-stellung-korrigieren-dialog-abgeschnitten, d7-rk-stellung-korrigieren-x-knopf-unklar, d7-rk-privacy-seite-zu-lang, d7-l-datenschutz-laptop-zu-lang, d7-rk-studienbuch-start-sequenziell, d7-rk-schlagworte-schlagwoerter-uneinheitlich, d7-rk-lc0-dialog-info-icon-abgeschnitten, d7-rk-ebook-brett-tooltip-jargon, d7-rk-gilt-fuer-ebooks-mehrzahlklammer, d7-rk-markieren-dialog-speichern-eigene-zeile, d7-rk-glocke-abzeichen-angeschnitten, d7-rk-revenge-leer-titelabstand, d7-rk-letzte-analysen-leertext-kontrast, d7-rk-verlauf-leertext-angemeldet, d7-rk-kurs-dialog-fliesstext |
| 2026-10-28 | 0.869.0 | Runde d7, Dialoge: Dialoge (5.6): erstes Formfeld unter dem Titel mit 8 px Luft (global, nur Dialoge mit Formfeld); dunkle Dialogfläche `surface-container-high` + 1 px `outline-variant`; Hinweistext im Dialog in on-surface-variant; gesperrter Knopf mit Grund in der Knopfleiste (Token „Erstellen“, Wochenpost „Anlegen“, Kalkulations-Ausgabe „Speichern“ + Pflichtfeld); Token-Bereich geschlossen als Kurzname (`mat-select-trigger`); Weiterscroll-Schatten als globale Klasse `rh-scroll-hint` (Woodpecker, Puzzle-Einstellungen); Kopfzeile mit ✕ polstert den Inhalt wie der schlichte Titel. Hilfe-? (5.11): U+00A0 + Symbol in `.rh-nowrap` (Installieren, Rekonstruktion, Aufgabenblätter, E-Books, Woodpecker, geteilte Links, Hintergrundjobs) | Label des ersten Felds halb abgeschnitten (Token, E-Book bearbeiten, Wochenpost, Neue Ausgabe, Kapitel hinzufügen), Dialog verschmolz dunkel mit der abgedunkelten Karte, Hinweis ≈ 3,1:1, gesperrte Knöpfe ohne Grund, „Extension (chess.com, lichess.org, Chess…“, ? allein in der Zeile trotz U+00A0 | UI-Sweep d7: d7-rk-dialog-erstes-feld-label-abgeschnitten, d7-rk-dialog-label-oben-abgeschnitten, d7-rk-ebook-dialog-titel-label-abgeschnitten, d7-rk-token-dialog-label-abgeschnitten, d7-rk-dialog-dunkel-kaum-abgesetzt, d7-rk-token-dialog-flaeche, d7-rk-dialog-hint-kontrast, d7-rk-gesperrter-knopf-ohne-grund, d7-rk-ausgabe-speichern-ohne-grund, d7-rk-token-bereich-abgeschnitten, d7-rk-install-hilfesymbol-allein-in-zeile |
| 2026-10-28 | 0.869.0 | Runde d7, Turnierseite + LeagueHub: Kalender am Handy höchstens 3 Einträge je Tag, nach Kalenderwochen gegliedert mit Sprungleiste (44-px-Chips); Mannschaftsfenster zeigt nur echte FIDE-Titel; Melde-Dialog mit kurzer Feldbeschriftung + Frage als Platzhalter, „Quelle vorschlagen“ statt „Hier eintragen“; Suchknopf nennt den Grund, solange er gesperrt ist; E-Ink-Umkreisregler mit sichtbarer Spur; LeagueHub-Übersicht ohne schweren „eigene Begegnung“-Rahmen, Sperrkarte beim Korrigieren spricht vom Korrigieren | Volllauf 28.10. (d7) | d7-t-*, d7-l-uebersicht-begegnungskarte-rahmen, d7-l-gate-korrigieren-text-passt-nicht |
| 2026-10-27 | 0.852.0 | LeagueHub: Marker als Abzeichen `lh-game-badges` („+“ Gold, „!!“ Türkis, dunkle Schrift, Zahl ab 2), eigener Platz statt am Ergebnis, Klick springt beim Nachspielen hin | Text hinter dem Ergebnis las sich wie ein zweiter Spielstand und verschob die Ergebnisspalte; ◆ und !! waren beide grün | Nutzerwunsch 27.10. (Vorschlag A+B+C) |
| 2026-10-26 | 0.848.0 | LeagueHub, interessante Stellungen: Marker „◆ n“ (`--rh-success`) an Partien, Sprungmarken als Textknöpfe unter der Zugliste, Punkt an markierten Zügen, Variantenstellung mit „Zurück zur Partie“; neue Seite „Übersicht“ (`/verein/uebersicht`) in den Aufstellungs-Karten (`lu-*`) | Grün = „+“-Markierung (wie der Knopf), Textknöpfe wie die übrigen Partie-Aktionen | Nutzerwunsch 26.10. |
| 2026-10-25 | 0.837.0 · E-Book-Brett Training | Auge (`visibility`) = aktuelle Stellung am eingefrorenen Brett ansehen (umschaltbar, nur ansehen, beim Lösen und Kalkulieren); Lösung zeigen = Glühbirne (`lightbulb`, aktiv in Primärfarbe) | Das Auge zeigte die Lösung — im Training will man aber zuerst die echte Stellung nach den gespielten Zügen sehen | Nutzerwunsch 25.10. |
| 2026-10-21 | 0.817.0 · Formular-Leser | Wartezeit als zwei Schritte mit Uhr („Wartet auf den Leser“ → ✓ + „Wird gelesen“), geteilt RookHub/LeagueHub (5.10) | Eine Zeile mit einer Uhr ab dem Hochladen sagte nicht, ob der Leser die Einlesung schon hat | Nutzerwunsch 21.10. |
| 2026-10-21 | 0.816.0 · Formular-Werkbank | RookHub-Korrektur (`/games/:id/edit`) und LeagueHub-Prüfseite teilen `app-sheet-workbench` mit der LeagueHub-Aufteilung (Foto mit Seiten-/Fotowahl und markierter Zeile │ Brett, Kurve, Engine │ Ausschnitt + Prüfen, Zugliste); RookHub bekommt Fotowahl, Markierung im Foto, Ausschnitt im Prüfteil, Bewertungskurve der Werkbank und die zuschaltbare Engine statt des Partie-Rückblicks; Texte unter `sheetEdit.*` (en/de/hr/hu) | Zwei Fassungen derselben Korrektur, RookHubs Aufteilung (Foto + Ausschnitt gestapelt, Lesarten rechts) gefiel schlechter | Nutzerwunsch 21.10. (Screenshots 075033 LeagueHub / 075211 RookHub) |
| 2026-10-18 | 0.808.x · Runde d4 · Training | Token `--rh-hint` (hell #8a5300, dunkel #ffb74d, E-Ink #4d4d4d) für „mit Tipp“ im Kursfortschritt (3.1); Repertoire-Trainer: Zusatz „Repertoire-Trainer“ unter 900 px nur für Bildschirmleser, darüber schrumpft er vor dem Namen; Leerzustand ohne „Häufigste Linien zuerst“/„Fortschritt zurücksetzen“; Lernen ohne „spiel …“ solange vorgespielt wird (`whiteToMoveLearn`/`blackToMoveLearn`); Linienliste „⊕ Üben“ als umrandete 44-px-Pille; Woodpecker-Löser: Titel bricht um (1,05 rem < 600 px), Meta über `.rh-dots`; Tipp-Knopf zählt den nächsten Tipp („Tipp (1/3)“ → „Nächster Tipp (2/3)“, auch Eigene Fehler), Vollbild-Abzeichen mit `title` „n von N Tipps gezeigt“; `.board-hint` global 0,875 rem (alle Puzzle-Modi); Endlos: Stufe einmal als „Level n (min–max)“ mit `stairs` in den Quick-Stats (Aktionszeile ohne `levelText`), Tiefen-Regler als `<label>` „Stockfish-Tiefe“ + Spanne im Tooltip, Handy-Seitenrand 1 rem; Tagespuzzle-Chip + Datum; E-Book-Kalkulieren: ein Satz + ? (`calcHelp`); Repertoire-Brett nie unter 300 px, niedrige Fenster `100vh − 300px` (4.6); Statistik: Elo-Kopf mit Lücke/Umbruch, E-Ink-Balken grau + schraffiert, Überlagerung schwarz mit Strichart (3.6); E-Ink: End-Segmente der Umschaltergruppe mit Gruppenrundung | Glühbirne hell kaum vom ✗ zu unterscheiden, Name „Testrepertoire Mar…“ neben vollem Zusatz, zwei wirkungslose Bedienelemente im Leerzustand, Imperativ bei gesperrtem Brett, „+“ las sich als Zustand (40 px), „…“ im Seitenkopf + „· noch 24 Tage“, „Nächster Tipp (1/3)“ mit Tipp 1 sichtbar, zwei Schriftgrößen, „Lv 0“ mit Aufgeben-Fähnchen + „Level 0 (719–759)“ doppelt ([d3 nicht behoben]: 0.807.0 baute nur die Statistik-Kachel ab, die Aktionszeile behielt `levelText`), nackte Zahl „16“, Rand 8 statt 14 px, Tagespuzzle ohne Tagesbezug, Zwei-Satz-Absatz, 160-px-Brett auf dem Boox, 4 px Titel–Feld/2 px Feld–Diagramm, grüne/rote/blaue Diagramme im E-Ink-Modus, abgeschnittener Innenrahmen | UI-Sweep d4 (20261018_1723_local_nach-d3-voll, _1816_…-boox, _1819_…-en): d4-rk-woodpecker-solve-titel-abgeschnitten, d4-rk-progress-bulb-light-red, d4-rk-trainer-titel-handy-gekuerzt, d4-rk-trainer-title-truncated-mobile, d4-rk-trainer-leerzustand-bedienelemente, d4-rk-lernen-anweisung-widerspruch, d4-rk-tipp-zaehler-beschriftung, d4-rk-endless-regler-ohne-beschriftung, d4-rk-endless-level-flagge-doppelt, d4-rk-endless-level-doppelt, d4-rk-kalk-hilfetext-lang, d4-rk-repertoire-brett-winzig, d4-rk-eink-diagramm-farbe, d4-rk-stats-mobile-level-select-crowds-chart, d4-rk-segment-innenrahmen-abgeschnitten, d4-rk-endless-brett-breiter-als-puzzle, d4-rk-line-add-plus-unlabeled, d4-rk-daily-puzzle-ohne-tagesbezug, d4-rk-hint-chip-schriftgroesse-buchmodus |
| 2026-10-18 | 0.808.x · Runde d4 · Apps | ClubHub: Gruppenchips am Handy mit Weiterscroll-Verlauf rechts (28 px, gleich breites Polster), bei aufgeklapptem „Gruppe ändern“ tritt „Anwesenheit abhaken“ als Hauptknopf zurück, Zugriffs-Hinweis ein Satz („Das Konto braucht in RookHub zusätzlich eine Rolle mit dem Recht „ClubHub: Trainer“.“); Turnierseite: aktiver Weg in Kopfleiste und ☰ markiert (Primärfarbe, 600, Unterstrich, `aria-current`), Teams-Tabelle Rang/Punkte 4rem, Name nimmt den Rest | Chip endete hart ohne Hinweis aufs Wischen, zwei gefüllte Knöpfe, drei Sätze samt Versalien-„PERSONEN“, aktuelle Seite nur an der Überschrift erkennbar, Rangspalte 220 px breit | UI-Sweep d4: d4-c-kartei-filterchips-abgeschnitten, d4-c-gruppe-zwei-hauptknoepfe, d4-c-gruppe-trainer-hinweis-jargon, d4-t-navbar-aktiv-marker-fehlt, d4-t-teams-tabelle-spalten-gestreckt |
| 2026-10-20 | 0.813.x · Kurs-Löser-Seitenspalte | Kommentar zwischen Kontext-Karte und Status-Card, lang gekürzt auf ~5 Zeilen; Kurs-Fortschritt hinter ⓘ neben der Sprachwahl; „Überspringen“ neben „Aufgeben“; Spielweise-Knopf nur im ⋮ | Kommentar unten unter der Aktionszeile „rangefriemelt“, Fortschritt und Spielweise-Knopf füllten die Karte | Nutzerwunsch 20.10. (Screenshot Woodpecker-Kurs) |
| 2026-10-18 | 0.808.x · Runde d4 · Partien/Texte | Partie korrigieren 769–1100 px: Raster-Zeilen `auto 1fr` (Zugliste-Überhang nur in Zeile 2, Zug-Karte folgt dem Brett im 12-px-Abstand); Partienliste ohne Engine: eigener Satz `games.listNoEngine` („Genauigkeit … — keine Engine frei“) als Info-Zeile mit Symbol, 12 px unter den Filtern, „–“ der Spalte verweist darauf (aria-describedby + Tooltip), Partieseite-Tooltip `games.analyzeNoEngine`; ⋮ der Partie in Liste und Seite mit gleichem Inhalt (PGN in der Liste, Löschen auf der Seite, 5.5); Rechtsseiten: Rücklink 44 px; Startseite: Trainingsziele `checklist` (Punktepartie behält `track_changes`); Trennpunkt-Muster `.rh-dots`/`.rh-dots--center` global (5.13), Brett-Kopf des E-Book-Lesers und Aufgabenblatt-Liste darauf; E-Books: Hochladen unter 600 px runder 44-px-Symbolknopf mit aria-label/title; U+00A0 vor `app-help-hint` am Satzende (5.11: Installieren, Rekonstruktion, Aufgabenblätter, E-Books); Puzzle-Löser Trefferquote `pct:1`; Analyse-Aufträge: Leerhinweis/Hilfe nennen das ⋮ der Stellung; Englisch: Menüs/Links/Kacheltitel in Satzschreibung, `legal.privacy.link`, britisch „analyse“ (6) | 85-px-Loch, Punktepartie-Fachwort ohne Bezug und klebend unter den Filtern, Löschen nur in der Liste/PGN nur auf der Seite, 24-px-Tippziel, zwei Kacheln mit gleichem Symbol, „· Matt in 3“ am Zeilenanfang, außermittiges Symbol ohne Namen, ? allein in neuer Zeile, „79%“ vs. „78.6%“, Uhr-Symbol gibt es nicht, Title Case trotz d3-Regel (d3 hatte nur Puzzle/Endlos umgestellt, kein Test), „Analyse“/„Analyze“ gemischt | UI-Sweep d4 (20261018_1723_local_nach-d3-voll, _1816_…-boox, _1819_…-en): d4-rk-edit-luecke-brett-zugkarte, d4-rk-games-engine-hinweis-jargon, d4-rk-engine-hinweis-jargon, d4-rk-games-liste-engine-hinweis-verwaist, d4-rk-games-engine-hint-unter-filter, d4-rk-konto-loeschen-textlinks-klein, d4-rk-partie-menue-loeschen-fehlt-partieseite, d4-rk-dashboard-gleiches-symbol-punktepartie-trainingsziele, d4-rk-brett-kopf-punkt-umbruch, d4-rk-ebook-brett-kopf-zeile-punkt-umbruch, d4-rk-ebooks-upload-knopf-aussermittig, d4-rk-hilfe-symbol-umbruch, d4-rk-dashboard-prozent-rundung, d4-rk-beschriftung-uhr-symbol-hinweis, d4-rk-en-title-case-labels, d4-rk-en-analyse-analyze-uneinheitlich |
| 2026-10-18 | 0.808.x · Boox 200 % Falz | Niedrige Fenster (4.6, `(max-width: 768px) and (max-height: 720px)`): gemeinsame Regel in `styles.scss` für RookHub + Turnierseite — App-Kopf 48 px (Abzeichen in der Leiste), Suchfelder 44 px, Reiter 40 px, `h1` 1,5 rem, `h2` 1,125 rem; je Seite Abstände vor der Hauptsache 4–8 px (Partien, Aufgabenblätter, Punktepartie, Rekonstruktion, Kurse, Turnier-Kopf), Aufgabenblatt „Drucken“/„Link kopieren“ und Kursseite „Zurück“ als Symbolknopf mit Namen im `aria-label`, E-Book-Anmeldebanner und „Kurz erzählt“ 0,875 rem, Turnier-Titel 1,125 rem + Sortierknopf 40 px, Lernkarte nach Fensterhöhe | Boox Tab X C mit UI-Größe 200 % = 480 × 640 CSS-px: Vorlauf 42–54 % auf 13 Seiten, Lernkarte ragte 29 px unter die Falz | UI-Sweep 20261018_1816_local_nach-d3-boox: fold-lead rookhub__games, __games-analyzed, __g-analyzed, __worksheets, __worksheets-worksheetId, __ebooks (anon), __guess, __reconstruct, __courses, __courses-study, __friends, __games-mistakes, turnier__tournaments-tournamentId, __t-tournamentId, __tournaments-calendar; fold-below __repertoires-repertoireId-flashcards, __courses-bookId-flashcards |
| 2026-10-18 | 0.807.x · Design-Runde d3 · Apps | ClubHub: Fußlinks am Handy 44 px, `.btn-link` mit unsichtbarer 44-px-Trefferfläche, „Gruppe löschen“ als `.btn.danger`, Feldhinweis in `.field-cell` unter seinem Feld, Seitentitel „Verknüpfung“ bei verknüpftem Konto; KidHub: Kursbalken über die Textspalte und kräftiger, Handy-Kurzformen der Kursaufgabe nur < 600 px, „← Kurse“ mit Abstand (gap), Anmeldung ohne leere Hinweiszeilen, Konto löschen: Kindersatz unter „Was entfernt wird“ + Knopf nach „Was bleibt“, Passwort vergessen: Eltern-Hinweise zugeklappt, Markierungen erst aufs vermessene Brett; LeagueHub: ⓘ mit letztem Wort nicht umbrechend, Korrigieren am Handy Partietitel in Zeile 2 (Basis 10rem); Turnierseite: Profil mit Darstellungskarte (theme-card), Ortshinweis leise mit 8 px Abstand, Spieltermine mit „·“, Stern mittig (20-px-Box), Detail in `--page-max-width`, Gast-Karte mit `.tc-slot`, niedrige Fenster kompakter | Tippziele 17–30 px, Fehler unter dem falschen Feld, Titel widersprach Fußzeile, Balken endete nach 260 px (≈ 1,3:1), Tablet zeigte Handy-Kopf, „←Kurse“, 44–50 px Leerraum, Knopf vor der Erklärung, gestapelte Erwachsenen-Absätze, NaN-Konsolenfehler, ⓘ allein in Zeile 2, 5-zeilige Titelspalte, fehlende E-Ink-/Größen-Wahl, klebender Hinweis, verschwimmende Termine, Stern 5 px zu hoch, 1 100 px statt 1 240 px, rutschende Aktionen, Vorlauf 48–50 % | UI-Sweep d3: d3-c-footer-links-tippziel-klein, d3-c-gruppe-textlinks-tippziel-klein, d3-c-kind-neu-fehler-vor-eingabe, d3-c-verknuepfen-titel-beschriftung, d3-k-kurskachel-balken-feste-breite, d3-k-zurueck-pille-tablet-nur-pfeil, d3-k-kid-back-pill-kurse-pfeil, d3-k-kid-login-feldluecken-ungleich, d3-k-delete-button-before-info, d3-k-forgot-stacked-muted-paragraphs, d3-l-info-icon-allein-handy, d3-l-korrigieren-mobil-kopf-schmal, d3-t-turnier-profil-darstellung-karte-fehlt, d3-t-detail-ortshinweis-ohne-abstand, d3-t-detail-spieltermine-ohne-trenner, d3-t-team-star-vertikal-versetzt, d3-t-detail-schmale-spalte-1920, d3-t-turnierkarte-slot-fehlt-anon, d3-t-kalender-vorlauf-zu-hoch; Konsole KidHub stars-1/levels-1 (NaN-Pfeile) |
| 2026-10-18 | 0.807.x · Design-Runde d3 · Partien | RookHub, Partien: Züge vergleichen zeigt alle Bewertungen (Liste, Antworten, Erwiderungen) aus Sicht der Seite am Zug wie die Begründung, nennt die Sicht einmal über der Liste, der beste Zug nie rot; Ladefehler (≠ 404) auf Zugvergleich, Partie-Analysen und Kursliste = `app-load-error` statt endlosem Balken bzw. „nichts da“, Seitenladen mit `app-loading-spinner` (Balken nur für laufende Aufträge, 5.10); Partie korrigieren 769–1100 px: Zug-Karte unter dem Brett, Zugliste rechts über beide Zeilen; „Seite / Liga“ → „Plattform / Liga“ samt Beispiel-Platzhaltern, Fehler-Sammlung „Fehler mehrerer Partien nachspielen“ mit den Feldnamen; Partieseite: Kopfknöpfe oben bündig, sobald unter „Partie analysieren“ ein Grund steht; Gedankenstrich mit U+00A0 (5.13); fremde Partie „Fehler von {Spieler} nachspielen“ statt „Eigene …“; ⋮ der Partie und der Partienliste in einer Reihenfolge (5.5); Rekonstruktion: Halbzüge-Umschalter am Handy 44 px, ohne gewähltes Teil Vorschau-Brett + nächste Schritte (einspaltig ausgeblendet); Partienliste: leer nur EIN „PGN hochladen“, Handy-Typspalte fest 56 px (Tempo unter dem Symbol), ohne Engine einmal der Grund über der Liste statt grauer Knöpfe; Partieformular: ⓘ im h1; Punktepartie: Punkte erst ab `maxPoints > 0`, en überall „Point game“, Symbol `track_changes` wie 🎯 | Vorzeichen widersprach der Erklärung und färbte den besten Zug rot, Ladefehler sahen wie Laden/Leerzustand aus, 540 px Leerraum, „Seite“ las sich als Farbe, versetzte Knopfzeile, Strich am Zeilenanfang, „Eigene“ bei fremden Fehlern, zwei Menüreihenfolgen, 32-px-Tippziele, leere rechte Spalte, doppelte Aktion, springende Namensspalte, Grund nur im Tooltip, abgesetztes ⓘ, „0 of 0 points“, drei Namen/Symbole | UI-Sweep d3: d3-rk-vergleich-vorzeichen-widerspruch, d3-rk-vergleich-leere-seite-balken, d3-rk-ladefehler-endlos-spinner, d3-rk-edit-tablet-leerraum, d3-rk-edit-seite-liga-beschriftung, d3-rk-mistakes-filter-beschriftung-saison, d3-rk-partie-grund-neben-knopf-versetzt, d3-rk-partie-grund-strich-zeilenanfang, d3-rk-eigene-fehler-fremde-partie, d3-rk-spielmenue-reihenfolge-uneinheitlich, d3-rk-recon-halbzuege-tippziel-klein, d3-rk-recon-detail-leere-rechte-spalte, d3-rk-games-leer-pgn-doppelt, d3-rk-games-liste-mobile-namen-versetzt, d3-rk-games-analyse-symbol-blass, d3-rk-scoresheet-hilfe-icon-abgesetzt-handy, d3-rk-punktepartie-null-von-null, d3-rk-punktepartie-benennung |
| 2026-10-18 | 0.807.x | Design-Runde d3 · Konto: Profil-Titel als `h1`-Seitenkopf über der Karte (5.1); Aktualisieren-Banner mit `--rh-info` (5.11); Aufgabenblatt-Liste: Meta-Trenner absolut in der Lücke + `overflow-x: clip` (wie ClubHub `.kid-meta`), Löschen als letzter roter Eintrag im ⋮ statt Papierkorb neben PDF; Installieren: je Karte ein Satz, Menü-Anleitung hinter ?; Bestenlisten: alles leer → eine Leerkarte mit Zeitraum-Satz + „Puzzles lösen“, einzelne Leerkarte nennt den Zeitraum; Trainingsziele: Untertitel „Tagesziel + Wochenziele“, Feld „Min/Tag“; Konto löschen: Knopf „Konto löschen …“ (führt nur zur Profilkarte), keine Pfadkürzel im Text; Lichess-Token: Speichern nur beim Speichern gesperrt, leer → Grund darunter; Aktivitäts-Raster am Tablet so breit wie die Karte; Passwort-Auge geteilt auf Anmeldung/Registrierung/Zurücksetzen (5.8) | Titel klebte an „Erscheinungsbild“, Hinweis-Banner sah hell wie ein Fehler aus, „· Datum“ am Zeilenanfang, Löschen gleichrangig, Fließtext-Stapel, Leerzustand ohne Grund/Weg, Text versprach Tagesziele je Kategorie, „jetzt löschen“ löschte nicht, gesperrter Knopf ohne Grund, halbleeres Raster, Auge nur beim Registrieren | UI-Sweep d3: d3-rk-profil-titel-ohne-abstand, d3-rk-update-banner-rot-hell, d3-rk-ws-liste-meta-punkt-zeilenanfang, d3-rk-ws-liste-loeschen-direkt-sichtbar, d3-rk-install-karte-fliesstext, d3-rk-bestenlisten-leer-ohne-weg, d3-rk-trainingsziele-tag-vs-woche, d3-rk-konto-jetzt-loeschen-beschriftung, d3-rk-konto-loeschen-url-kuerzel, d3-rk-profil-lichess-speichern-gesperrt-ohne-grund, d3-rk-stats-aktivitaet-raster-halbleer, d3-rk-login-passwort-ohne-auge |
| 2026-10-18 | 0.807.x | Design-Runde d3 · E-Ink (3.6): Snackbar-Aktion schwarz/fett/unterstrichen; Wahlkacheln mit 2-px-Rahmen; Zugrecht-Zeile #333; Aktivitäts-Raster als Graustufentreppe mit Zellrahmen, Elo-Linie schwarz; SVG-Lernkartenbrett weiß/#a3a3a3 mit Rahmen; ☰-Menü der Gäste: „Design“ als Untermenü System/Hell/Dunkel mit Häkchen (statt Umschalter, der den Ist-Zustand nannte), im E-Ink-Modus stattdessen der Hinweis „bleibt immer hell“ | „Schließen“ auf Weiß unsichtbar, Kacheln als Haarlinie, Zugrecht ≈ 4:1, Stufen nur über Grün, Lernkarten in Holzfarben; Menüpunkt nannte den Zustand und schaltete woanders hin, im E-Ink-Modus wirkungslos | UI-Sweep d3: d3-rk-gate-banner-schliessen-blass, d3-rk-eink-wahlkachel-haarlinie, d3-rk-eink-sekundaertext-hellgrau, d3-rk-eink-aktivitaet-heatmap-hellgrau, d3-rk-eink-flashcards-brett-farbig, d3-rk-theme-menu-label-zustand, d3-rk-helles-design-bei-eink |
| 2026-10-15 | 0.799.x | E-Ink-Modus (3.6): je Gerät schaltbar im Profil (Darstellung, erste Karte), ohne Animationen/Ripples, immer hell mit hohem Kontrast und kräftigen Rahmen, Brett ohne Zuganimation in Graustufen mit Rahmen statt Farbflächen, Leser blättert springend mit Tippzonen | Nutzer liest/trainiert auf einem Boox Tab X C (Kaleido 3): Animationen erzeugen Schlieren und Geisterbilder, Grautöne und Farbflächen gehen unter | Nutzerwunsch 15.10. |
| 2026-10-15 | 0.800.x | UI-Skalierung (3.7): je Gerät 100–200 % im Profil (Darstellung, unter E-Ink), Touch über Meta-Viewport, Desktop über `zoom` (Chessground-Container ausgenommen) | Auf dem Boox Tab X C (960 CSS-px) ist die UI viel zu klein; Browser-Zoom gibt es in der TWA nicht | Nutzerwunsch 15.10. |
| 2026-10-15 | 0.786.0 | E-Book-Leser kompakt: EINE Leiste oben (Kopfleiste dockt per `body.nav-docked` 48 px hoch rechts auf der Leser-Leiste an, Glocke/☰/Vollbild dieselben); Brett-Leiste in einer Zeile (Modi als Symbole, Optionen hinter ⚙, Analysieren/Engine/Drehen/FEN/Melden im ⋮), Zugrecht/Bewertung/Herkunft im Brett-Kopf, eine Statuszeile; Lösen startet in „Training“ (gemerkte Wahl gewinnt); Brett ab 900 px bis `100dvh − 220px` | „Die Leiste oben ist viel zu breit“, „rund ums Brett ist so viel los“ — zwei Leisten (≈ 128 px) und sieben Zeilen um das Brett | Nutzerwunsch 15.10. |
| 2026-10-14 | 0.780.2 | E-Book-Brett unter 900 px als Vollbild über dem Buch statt Leiste unten | Leiste war am Handy zu klein zum Lösen | Nutzerwunsch 14.10. |
| 2026-10-14 | 0.777.0 | E-Book-Brett mit Modus-Leiste (Ansehen/Ziehen/Kalkulieren/Lösen/Maia) statt Hand-Knopf + „Lösen“-Knopf; Engine zuschaltbar | Nutzerwunsch: mehrere Arten, mit der Buchstellung zu arbeiten — ein Schalter je Art wäre unübersichtlich | Nutzerwunsch 14.10. |
| 2026-10-14 | 0.775.2 | E-Books (c1): Regal-Hauptknopf `flex: 1`, einzeilig mit „…“, „Weiter“ statt „Weiter auf Seite N“ (Seite im Namen), Download am Handy nur Symbol; Schlagwörter zugeklappt 8 + „Alle Schlagwörter (n)“, am Handy Sortierung/Schalter/Schlagwörter hinter „Filter ▾“ (aktive als Chips); leere Bibliothek ohne Filterzeile; gefiltert leer → „Filter zurücksetzen“; Leerzustands-Symbol nur `> mat-icon`; Regal ab 2400 px in `--page-max-width-wide`; Hochladen-Dialog: Grund neben gesperrtem Knopf, Scrollkante (`--mat-sys-outline-variant`), Autor + Jahr auch am Handy nebeneinander; Leser: 404/403 Klartext + Weg zurück, Fehlerzeile in Seitenbreite, Seitenfeld ohne Drehknöpfe (WebKit), Fußzeile aus (`body.rd-reader-mode`), unter 900 px Diagramm-Knopf, Touch-Zonen dauerhaft gestrichelt, ab 2400 px „Breite füllen“ ≤ 1 400 px, Seite/Zoom neben dem Titel, Brett-Leiste bis 720 px | Knopftext dreizeilig/abgeschnitten, Regal unter der Falz (17 Chip-Zeilen), Sackgassen-Leerzustand, Sperre ohne Grund, „Prüfe die Verbindung“ bei 404, doppeltes Scrollen, Diagramme am Handy unauffindbar, 2 300-px-Seite | UI-Sweep c1: c1-eb-card-action-button, c1-eb-tag-filter-lead, c1-eb-filter-empty-no-reset, c1-eb-upload-disabled-no-reason, c1-eb-reader-404-misleading, c1-eb-ebook-leser-seitenfeld-spinner, c1-eb-ebook-leser-seite-scrollt-um-fusszeile, c1-eb-reader-mobile-diagrams-undiscoverable, c1-eb-reader-ultrawide-layout, c1-eb-empty-upload-icon, c1-eb-empty-lead-mobile, c1-eb-ultrawide-not-wide |
| 2026-10-14 | 0.775.2 | Woodpecker (c1): Neues-Training-Dialog mit EINEM Scrollbereich (Beginn vorn, Kursliste ohne eigenen Kasten, Weiterscroll-Schatten), 560 px / 94vw, `autoFocus: 'dialog'`; ohne Kurs nur Leersatz + „Zu den Kursen“; Aufgabenzahl mit Tausendertrenner und U+00A0; Leersatz im Dialog in `--mat-sys-on-surface-variant`; Hilfe-(?) im Satz; Seitenpolster 16 px ohne 96vw; ab 2400 px Übersicht und Löser in der Breitbild-Stufe (Brett bis Fensterhöhe, Seitenspalte 360–400 px); Löser bei 404/403 „Dieses Training gibt es nicht (mehr).“ + „Zur Übersicht“ statt Verbindungsfehler | Kasten im Kasten mit abgeschnittenem Hinweis, Sackgasse ohne Kurse, „1036 / Aufgaben“, 3,1:1, (?) allein in einer Zeile, 24/32 px Rand, 1 100 px leere Ränder, „Wiederholen“ hilft bei 404 nie | UI-Sweep c1: c1-wp-dialog-kasten-im-kasten, c1-wp-dialog-ohne-kurs-sackgasse, c1-wp-dialog-zahlformat, c1-wp-dialog-leersatz-blass-hell, c1-wp-dialog-fokus-datum, c1-wp-intro-hilfe-eigene-zeile, c1-wp-woodpecker-seitenpolster-96vw, c1-wp-uebersicht-ultrawide, c1-wp-loeser-ultrawide-brett, c1-wp-loeser-404-verbindung |
| 2026-10-14 | 0.773.0 | Woodpecker-Übersicht: Karte je Training, eine Hauptaktion je Zustand, gesperrter Start mit Grund darunter | neuer Bereich; die Pause zwischen Zyklen muss sichtbar begründet sein | Woodpecker-Training |
| 2026-10-14 | 0.772.1 | Kopfleiste fluchtet ab 2400 px mit der Inhaltskante der jeweiligen Seite (gemessenes `--nav-edge`, Rückfall 1 880 px) | Partieseiten 2 510 px, /l /r 1 600, /prep 760 px — Kopf und Inhalt hatten keine gemeinsame Kante | UI-Sweep b1 · b1-rk-kopfleiste-ultrawide-flucht |
| 2026-10-14 | 0.770.0 | Ziehbarer Trenner Buch | Brett (Breite je Gerät gemerkt) | Bretter und Bücher brauchen je nach Bildschirm andere Anteile | Nutzerwunsch |
| 2026-10-14 | 0.766.0 | E-Book-Leser zweispaltig mit Brett rechts; Diagrammzonen nur beim Überfahren sichtbar, Doppelklick stellt auf | Buch bleibt ungestört lesbar, das Brett ist ein Werkzeug daneben | E-Book-Diagramme Stufe 1 |
| 2026-10-13 | 0.764.0 | E-Book-Karten als Regal (Titelbild 2:3, Lesestand-Balken, Abzeichen); Leser als Sonderlayout mit eigenem Scrollkasten | neuer Bereich, Buchtitel allein sagen wenig — das Titelbild ist die Wiedererkennung | E-Book-Bibliothek |
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
| 2026-10-14 | 0.765.0 | RookHub (b1, Repertoire): Trainer — Brett wächst vor der Seitenspalte (`flex-grow` 10 : 1, Basis 480 + 340 px bleibt), Gruppe mittig; der Höhenabzug `100vh - 540px` gilt nur 600–899 px, am Handy Brett volle Inhaltsbreite; Repertoire-Liste: 2400-px-Rasterregel (420 px) HINTER der Grundregel; Karte/Detailkopf ohne „Keine Beschreibung“ (Zeile nur mit Inhalt); Linienliste: ab 21 Linien in mehreren Kapiteln startet nur das erste Kapitel offen, je Kapitel höchstens 15 Linien + „Weitere N Linien anzeigen“ (bei aktivem Stellungsfilter alles offen), Flashcard-Kästchen 18 px in 44-px-Trefferfläche; Stellungsfilter startet in der gemeinsamen Ausgangsstellung der Linien (FEN-Repertoires), ohne gemeinsame Startstellung kein Filter, Zugnummern ab der FEN („1… Kd8“); Lochfinder: Einleitungssatz je Auswahl (Lichess online / lokal / Meister), das 15-Abfragen-Limit hinter dem ⓘ | Brett 696 px bei 1 920 × 1 080 bzw. 305 px am Handy, 300-px-Karten in 3 440 px mit gekürzten Titeln, Füllzeilen, Liste ≈ 14 Bildschirme, 13-px-Tippziel, Filterbrett in der Grundstellung über Endspiel-Linien, Erklärtext zu nicht vorhandenen Bedienelementen | UI-Sweep b1: b1-rk-trainer-brett-1920, b1-rk-trainer-brett-handy-schmal, b1-rk-repertoire-karten-ultrawide-300, b1-rk-keine-beschreibung, b1-rk-repertoire-linien-handy-lang, b1-rk-linienliste-auswahl-klein, b1-rk-repertoire-filterbrett-fen-start, b1-rk-holes-intro-lokal |
| 2026-10-14 | 0.765.0 | RookHub (b1, Anmeldung/Dashboard/Profil/Nachrichten/Admin/Datenschutz): bietet der Fehlerkasten den nächsten Schritt an („Passwort vergessen?“, „Neuen Link anfordern“), entfällt derselbe Link in der Linkzeile darunter; Dashboard `grid-auto-flow: row dense` (nicht beim Anpassen), ab 2400 px feste 5 Kachelspuren; Untertitel mit mehreren Angaben als Teile, Trenner „ · “ als `::before` (Puzzles: „Elo … · … gelöst“, kein „|“ im Text, Teilübersetzungen ohne doppeltes „%“); Profil: API-Tokens ein Satz + Bereiche hinter (?), Externe Engine ein Satz, direkte Einrichtung (Hinweis, Adresse, Anleitung) zugeklappt hinter „Einrichten“, Linktext ohne Scope; Token-/Hintergrundfeld am Handy volle Breite; Nachrichten: „Senden“ nur beim Senden gesperrt, leer → Hinweis „Schreib zuerst eine Nachricht.“ (5.4); Admin-Kartenliste 12 px vor der Blätterleiste; „Wort-⁠/Wort“ mit U+2060 zwischen Strich und Schrägstrich (de/hu, 5.13) | doppelter Link, Loch neben „Wochenpost“, 6 schmale Spalten, „ | “ am Zeilenende, Textwand, 15 px schmaleres Feld, gesperrter Knopf ohne Grund, Seitenwahl klebte, Zeile begann mit „/“ (WebKit) | UI-Sweep b1: b1-rk-login-passwort-vergessen-doppelt, b1-rk-dash-raster-angepinnte-kurse, b1-rk-dash-puzzles-pipe, b1-rk-profil-tokens-engine-textwand, b1-rk-profil-lichess-token-feld-schmal, b1-rk-messages-senden-gesperrt, b1-rk-admin-handy-seitenwahl-klebt, b1-w-privacy-strich-slash |
| 2026-10-14 | 0.765.0 | RookHub (b1, Analyse/Navigation): Partie-Analyse — Zugliste eine Zeile je Vollzug (Nr. \| Weiß \| Schwarz, Bewertung rechtsbündig ≥ 5ch, Nr. 65 %), einspaltig (< 769 px) ohne eigenen Scrollkasten, daneben mit Ausblend-Rand (`.more`); ab 1200 px Brett bis `min(760px, 100vh - 240px)`, Liste so hoch; „Züge vergleichen“ ab 1200 px Brett bis `min(620px, 100vh - 260px)`, „Zum Analysebrett“ als Rücklink (`mat-button`) über dem Titel, Statuszeile nur solange nicht fertig; Zugbaum: Bewertung im gewählten Zug aus `--mat-sys-on-primary` (85 %); Analyse-Kopf trägt 12 px Abstand selbst (h1 ohne Rand); „Partie-Analysen“ und „Gegner vorbereiten“ mit Material-h1; Gast-Leiste Anmelden · Registrieren · ☰; Untermenüs am Handy über dem Hauptmenü (5.5) | Halbzug-Raster mit springender Lesefolge, Scrollkasten im Scrollkasten, Brett 520/376 px bei 1920 × 1080, zwei gleiche Knöpfe, „+0.37“ ≈ 1,6:1 dunkel, „Verlauf“ klebte am Brett (WebKit), Titelgrößen sprangen, ☰ zwischen den Konto-Aktionen, Untermenü schnitt das Hauptmenü an | UI-Sweep b1: b1-rk-analyse-zugliste-halbzuege, b1-rk-analyse-zugliste-scrollkasten, b1-rk-analyse-unterseiten-brett-1920, b1-rk-zuege-vergleichen-zwei-knoepfe, b1-rk-zugbaum-bewertung-aktiv-dunkel, b1-w-analyse-verlauf-klebt, b1-rk-h1-uneinheitlich, b1-rk-navbar-gast-menue-mitte, b1-rk-nav-konto-untermenue-handy |
| 2026-10-14 | 0.765.0 | LeagueHub (b1): ⋮-Menü der Vereinspartien `lh-menu` (Fläche/Linie/Radius 8/Barlow, „Löschen“ rot); ⓘ-Kästen `fit-content`; Prognose-ⓘ am Handy ohne 440-px-Mindestbreite, Spalte „Prognosen“ und Rundenzeile weg, Kopf ohne Versalien; Sperrkarte ≤ 760 px; „Partien hinzufügen“-Einleitung `.info-inline` (ⓘ am Satzende); Vereinspartien am Tablet Namen ungekürzt (Elo zweite Zeile), ab 2400 px Tabelle 1 440 px; Formular-Zugliste einspaltig ohne eigenen Rollbereich; Häkchen-Grundregel 18 px/`--ink`; Partienzahl „(3.115)“ mit Tausenderpunkt und .3em Abstand; Wartezeit „seit 2 Tagen“ mit U+00A0, Halbgeviertstrich; 404 mit LeagueHub-Knopf; „Passwort vergessen“-Hinweis ohne Deckkraft (Spezifität 0,2,3) | Hauptinformation abgeschnitten/gekürzt, leere Flächen, Material-Optik, Browserblau, Kontrast ≈ 4,1:1 | UI-Sweep b1: b1-l-prognose-info-tabellen-handy, b1-l-info-panel-rechts-leer, b1-l-zeilenmenue-material, b1-l-404-knopf-material, b1-l-passwort-mail-deckkraft, b1-l-sperrkarte-breit, b1-l-hinzufuegen-info-abgesetzt, b1-l-vereinspartien-tablet-namen, b1-l-vereinspartien-ultrawide, b1-l-formular-wartezeit-umbruch, b1-l-korrigieren-zugliste-kasten, b1-l-partienzahl-format, b1-l-haekchen-browserblau |
| 2026-10-14 | 0.765.0 | RookHub (b1, Kurse/Kurs-Löser/Flashcards/Brett): Kurs-Löser nach Lösen/Aufgeben nur EIN „Nächstes Puzzle“ (Status-Card), Abzeichen/„Tags anzeigen“ in der Textspalte der Kontext-Card, ab 2400 px Kommentar 1 rem und ≤ 68ch; Kursdetail: Kennzahlen in allen Breiten hinter der Aktionszeile, Studienbuch ohne „0/0“ (Satz der Kurskarte, „Linien durchsehen“ als Hauptknopf), Kapitelzähler 7ch rechtsbündig; Linien durchsehen bis 899.98 px einspaltig, Liste ohne 260-px-Kasten mit klebenden Kapitelköpfen; Flashcards: verdeckte Seite `visibility: hidden` nach halber Drehzeit, Scrollen der Rückseite im Inneren, Vollbild-Knopfzeile in Kartenbreite; Brettkoordinaten brown auf hellen Feldern dunkel wie auf dunklen (≈ 12:1) | doppelter Hauptknopf, Link 34 px aus der Flucht, 85 Zeichen je Zeile, Hauptknopf bei 45–48 %, „0/0“ ohne Aussage, springende Balkenenden, Kasten im Kasten/300-px-Liste am Tablet, Safari zeigte gespiegelte Rückseite, Knopf 240 px neben der Kartenecke, 3,9:1 | UI-Sweep b1: b1-rk-seq-doppelt-naechstes-puzzle, b1-rk-loeser-tags-link-versatz, b1-rk-loeser-kommentar-zeilenlaenge-ultrawide, b1-rk-kursdetail-vorlauf-laptop, b1-rk-studienbuch-fortschritt-0-0, b1-rk-kapitel-balken-versatz, b1-rk-browse-tablet-eng, b1-rk-browse-liste-kasten-einspaltig, b1-w-flashcard-flip, b1-rk-flashcards-vollbild-knopf-verrutscht, b1-rk-brett-koordinaten-helle-felder |
| 2026-10-14 | 0.765.0 | Turnierseite (b1, Kalender/Turnier-Tabellen): Kalender-Filterfeld in Zeilen nach Sinn (Suche allein, Chips, Schalter, Bedenkzeit + „Mind. Teilnehmer“ 14rem + Zurücksetzen), Zeitraum/Umkreis am Handy nur nebeneinander, wenn beide passen, Wertechips nur bei zugeklapptem Filter; ab 2400 px Kartenspalte als Flex-Säule `100vh - 17rem` (Trefferzeile immer im Fenster); Seitenpolster am Handy 16 px (Kalender, Meine Turniere); Team-Spalte „Punkte“ nur mit Werten, Teamname am Handy mit „…“; Spielertabelle 769–1023 px ohne FIDE-ID, Verein ≤ 12rem; leere Paarungen ohne Rundenauswahl, mit „Aktualisieren“ (Detail) bzw. „Volle Ansicht“ (`/t/:id`) | „Mind. Teilneh…“, Suchfeld auf 220 px gedrückt, verwaiste Schalter, doppelte Werte, Trefferzeile halb außerhalb, 8-px-Rand, leere Punktespalte drückte Namen über den Rand, „Bd.“ unerreichbar, Hinweis auf einen fehlenden Knopf | UI-Sweep b1: b1-t-filter-feldbreiten, b1-t-filter-chips-doppelt-offen, b1-t-karte-trefferzeile-abgeschnitten, b1-t-seitenpolster-handy-8px, b1-t-teams-punkte-leer, b1-t-spieler-brettspalte-tablet-en, b1-t-paarungen-leer-ohne-weg |
| 2026-10-14 | 0.765.0 | ClubHub/KidHub (b1): ClubHub-Metazeile — Trenner „·“ absolut in der 14-px-Lücke, am Zeilenanfang per `overflow-x: clip` abgeschnitten (ersetzt das `::after` aus 0.763.1); Gruppenseite ab 2400 px: Matrix `fit-content` (≥ 828 px), Klappkästen + Statuszeile 828 px; „Dazunehmen“/„Zugriff geben“ nicht mehr gesperrt, ohne Eingabe Fokus + `.need`-Hinweis; Kind anlegen: Formularfehler (Vorname, Geburtsdatum, FIDE-Nummer) als `.need` unter dem Feld mit Fokus und `aria-invalid`; kein „Anmelden“ im Kopf auf Registrieren/Passwort-Masken; „…“ typografisch, „E‑Mail“ mit U+2011. KidHub: Kursnamen/-texte bei Sprachwechsel neu geholt (Aufgabe bleibt); Kursaufgabe am Handy Zurück-Pille nur „←“, Titel ≤ 2 Zeilen; ab 2400 px Seitenspalte 480 px mit größerer Schrift (`KID_SIDE_WIDE`), Brett `100vh - 200px`; Registrieren: Feldhinweise an der Feldkante, `#4a6380`, 14 px Abstand; Brett-Koordinaten mit deckendem Hof in der Gegenfarbe | Trenner hing am Zeilenende, Matrix-Spalten 900 px auseinander, Formulare 190 Zeichen breit, gesperrt ohne Grund, Fehler unter der Falz/hinter der Leiste, doppelter Weg zur Anmeldung, gerade Anführungszeichen, Kursname blieb deutsch, „First endga…“, 400-px-Spalte neben 1 240-px-Brett + Fuß 22 px unter der Falz, Hinweise klebten am nächsten Feld, Koordinate 2,8:1 auf Auswahlfeld | UI-Sweep b1: b1-c-meta-trenner-zeilenende, b1-c-ultrawide-matrix-formular-gestreckt, b1-c-gesperrt-ohne-grund, b1-c-kind-neu-fehler-unter-falz, b1-c-passwort-masken-kopf-anmelden, b1-c-gerade-anfuehrung-email-umbruch, b1-k-kursname-sprachwechsel, b1-k-ultrawide-seitenspalte, b1-k-kurstitel-handy-gekuerzt, b1-k-register-hinweise-abstand, b1-k-koordinate-auf-auswahlfeld |
| 2026-10-14 | 0.765.0 | RookHub (b1, Partien/Punktepartie/Rekonstruktion): Partiekopf zeigt die Quelle übersetzt wie die Liste (`game-source.ts`); ⋮ „Löschen“ in Partienliste und Admin-Nutzerliste über `.delete-item` statt `color="warn"`; Lösch-Rückfragen (`….delete…Confirm`) bestätigen mit „Löschen“ statt „OK“ (5.6); Sperr-/Fehlerkarte der Partieseite in `--page-max-width`; Grund unter „Partie analysieren“: Hülle so breit wie der Knopf (≥ 220 px), Satz `text-wrap: balance`; Zugliste der Partieseite mit Weiterscroll-Schatten (5.3); Auswertung: aktive Schalter `--rh-success`, Null-Zähler 55 %; Partie korrigieren: Ergebnis-Spalte 128 px; Datumsfelder als `app-iso-date-field` in der App-Sprache (Partie korrigieren, Rekonstruktion, Trainingsziele, Admin-Tagespuzzle; 5.13); Punktepartie-Brett 600–899 px einspaltig, Brett mittig bis Fensterhöhe; ab 2400 px: Partienliste mit mitwachsenden Zahlenspalten, Punktepartie-Liste zweispaltig, „Letzte Partien“ Datum/Gegner auf Inhaltsbreite, Rekonstruktion Liste + Bearbeiten in 1 880 px (Brett-Editor ≤ 900 px bis Fensterhöhe, 4.1); Rekonstruktion: Leersatz ohne zweites Abblassen, Grund unter gesperrtem „Anlegen“ als `mat-hint` | Rohschlüssel „pgn“, graues Lösch-Symbol, „OK“ als Gefahrknopf, 2 560-px-Sperrkarte, ⋮ ~120 px abgesetzt + Waisenwort, Zugliste endete ohne Hinweis, Kontrast 2,2–3,3:1, „Ergebni“, „10/10/2026“ in Deutsch, 384-px-Brett am Tablet, ~1 000 px Lücken in Listenzeilen, Rekonstruktion bei 1 240 px in 3 440 px, gesperrter Knopf ohne Grund | UI-Sweep b1: b1-rk-partie-quelle-pgn, b1-rk-games-menue-loeschen-rot, b1-rk-games-loeschen-ok, b1-rk-vereinspartie-sperrkarte-ultrawide, b1-rk-partie-grund-unter-knopf, b1-rk-partie-zugliste-schatten, b1-rk-auswertung-hell-kontrast, b1-rk-edit-ergebnis-regression, b1-rk-edit-datum-us-format, b1-rk-guess-brett-tablet, b1-rk-breitstufe-zeilen-auseinander, b1-rk-recon-breitstufe, b1-rk-recon-leer-kontrast, b1-rk-recon-anlegen-ohne-grund |
| 2026-10-14 | 0.765.0 | RookHub (b1, Puzzle-Löser/Endlos/Statistik/Freunde/Bestenlisten/Aufgabenblätter): Löser-Kennzahlen nach Inhaltsbreite (`flex-wrap`, Beschriftung ungekürzt), Karte < 330 px drei Spalten (5.12); „Dumme Tipps“ → „Tipps melden“ in `--mat-sys-on-surface-variant` mit Rahmen `--mat-sys-outline`, ohne Deckkraft, am Handy nur Symbol (alle drei Löser); Tipp-Text ab 900 px in der Seitenspalte unter der Status-Karte; „An Freund schicken“ in schmaler Aktionsleiste (Container `pab` < 420 px) nur Symbol; Endlos: bei offenem Lauf kein zweiter gefüllter Startknopf, Kennzahlen „Stufe · gelöst · Leben · bestes Rating“ mit „ · “ statt „\|“, keine Zeile „Phase 1 (Schritt 63)“; Endlos-Verlauf Dauer über `common.durationMinSec` („21 min 0 s“); Statistik: Seitenpolster 16 px, Kurs-Modus ohne Versuch EINE zentrierte Leerkarte + „Zu den Kursen“ (Leerkarten zentriert, 5.10), Kurs-Zeilen mit Aufgabentitel statt „Puzzle –“ (Wertungsspalte nur mit Wertung, 5.3), ab 2400 px Aktivitätszellen 28 px; Freundesvergleich: Spaltenköpfe einzeilig mit Ellipse + Tooltip, Trennlinie je Zeile in `--mat-sys-outline-variant`; Bestenlisten ab 1 280 px bis 1 400 px breit, Einheit 1 100–1 399 px aus (steht im Kartentitel); Aufgabenblätter: Meta-Angaben als ungeteilte Einheiten mit „·“ als `::before` (5.13) | „Genauigk…“ bzw. „Beste“ allein in Zeile 2, Knopf wirkte gesperrt (hell ≈ 3,5:1), Tipp-Text unter der Falz, ⋮ in eigener Zeile, zwei Hauptknöpfe, Fachjargon, „21m“ = Meter, 12-px-Rand, Sackgassen-Leerkarte, „Puzzle –“ in jeder Zeile, Raster bei 60 % der Karte, Naht in den Zeilenlinien, gekürzte Namen am Laptop, „Oct 11,“ / „2026“ | UI-Sweep b1: b1-rk-puzzle-kennzahlen-raster, b1-rk-puzzle-dumme-tipps-knopf, b1-rk-puzzle-tipptext-unter-falz, b1-rk-puzzle-teilen-eigene-zeile, b1-rk-endless-fortsetzen-kasten, b1-rk-endless-phase-jargon, b1-rk-endless-verlauf-dauer, b1-rk-statistik-rand-12, b1-rk-stats-kurse-leer, b1-rk-stats-kurse-wertung-leer, b1-rk-stats-heatmap-ultrawide, b1-rk-freundesvergleich-kopf-naht, b1-rk-bestenlisten-namen-laptop, b1-rk-aufgabenblaetter-datum-umbruch |
| 2026-10-14 | 0.783.2 | RookHub (c2, E-Books/Woodpecker): Bytegrößen binär (÷ 1024, „kB/MB/GB“) — Upload-Grenze 300 MiB = „300 MB“, der Hinweis ohne Konto nimmt sie als `{{max}}` aus derselben Zahl; randlose Seiten mit eigener Leiste markieren sich mit `data-nav-edge="full"`, die Kopfleiste fluchtet dann ab 2400 px bei 16 px (E-Book-Leser); Leser am Handy rechnet mit der 56-px-Kopfleiste; Brett in der Brett-Leiste ab 900 px höchstens `100dvh − Kopf − 330px` (≥ 280 px), am Handy fünf Modi als Raster (Symbol über Wort); Maia-Zugzeile erst bei bereitem Modell; Leersätze in `--mat-sys-on-surface-variant`; Sortierung 320 px, Suchfeld ≤ 640 px, am Handy kurzer Platzhalter „Bücher suchen“; „1 Seite“/„1 Diagramm“ über *One-Schlüssel; Zahl + Einheit/Zählwort mit geschütztem Leerzeichen („46 MB“, „Zyklus 1“); Jahr-Feld ohne Drehpfeile | zwei Zahlen für dieselbe Grenze, Kopf- und Leser-Leiste auf zwei Kanten, 8-px-Streifen, Bedienzeile unter der Falz, „Maia“ allein in Zeile 2, leere Zeile mit gesperrten Knöpfen, 3,3:1, „Neueste zuerst (E…“, „Schlagw“, „1 Seiten“, Umbrüche „46 / MB“ und „Zyklus / 1“, Safari-Drehpfeile | UI-Sweep c2: c2-eb-upload-limit-315-300, c2-eb-reader-ultrawide-kopfkante, c2-eb-reader-handy-streifen-unten, c2-eb-board-leiste-laptop-falz, c2-eb-board-modus-leiste-maia-allein, c2-eb-brett-maia-leere-zugzeile, c2-eb-calc-leersatz-kontrast, c2-eb-sort-select-truncated, c2-eb-search-placeholder-truncated, c2-eb-plural-1-seiten, c2-eb-maia-46mb-umbruch, c2-wp-dialog-zyklus-zahl-umbruch, c2-eb-upload-jahr-drehpfeile |
| 2026-10-15 | 0.786.1 | E-Book-Leser ab 2400 px: Brett-Spalte `clamp(460px, calc(100dvh - 220px), 46vw)` (Brett 100dvh − 250px) statt höchstens 720 px | Brett 720 px bei 3440 × 1392, darunter ein Drittel leer, 650 px Lücke zwischen Buch und Brett | User-Wunsch 15.10. (Vergleichsbild) |
| 2026-10-15 | 0.790.0 | RookHub, Kurs-Löser: Fortschritt dreigeteilt (ohne Hilfe / mit Tipp oder Neustart / aufgegeben) statt einer Leiste mit „x/y (n %)“ | Nutzerwunsch: „3 %“ sagte nicht, wie sauber gelöst wurde | Nutzer |
| 2026-10-16 | 0.806.x · Falz-Runde 16.10. | ClubHub-Kartei: EIN Trainingsstreifen auch bei mehreren Gruppen (Knopf je Gruppe), Streifen 8 px Polster/Knopf 36 px; am Handy Titel nur für Vorleser, Suche + „+ Kind/+ Trainer“ in Zeile 1, Chips + Archiv + Zähler in Zeile 2, Registerbuchstabe links neben der Karte; Laptop Kopf 40-px-Knöpfe, 10 px Abstände | Vorlauf 54–70 %, zwei Banner über der Liste | UI-Sweep 20261016_1853: clubhub__start (fold-lead, fold-banners) |
| 2026-10-16 | 0.806.x · Falz-Runde 16.10. | RookHub: Repertoire-Baum 600–899 px Brett (≤ 56 %) und Baum nebeneinander, Handy-Titel einzeilig mit „…“ (Name im `title`); „Fehler nachspielen“: Start + Auswahlzahl in einer Zeile, Erklärsatz darunter, Felder am Handy nebeneinander; Partie-Analysen: Einleitung im ⓘ bzw. in der Formularkarte, Leerhinweis vor dem Formular; Punktepartie/Admin/Aufgabenblatt: Abstände vor der Liste 4–16 px statt 8–24 px | Vorlauf 41–73 % | UI-Sweep 20261016_1853: rookhub__repertoires-tree, __games-mistakes, __analysis-games, __guess, __admin, __worksheets-worksheetId |
| 2026-10-16 | 0.806.x · Falz-Runde 16.10. | RookHub: E-Books am Handy als kompakte Zeilen (Titelbild 64 px, Lesen/Herunterladen als 44-px-Symbol + ⋮ übereinander, Name im aria-label), Einleitung im ⓘ am Titel; Kurs-Linien einspaltig mit Kapitel-Aufklappern; Turnierliste am Handy 25 je Abruf; Profil-Darstellungskarte knapper (bleibt oben) | 16–27 Handy-Bildschirme, Darstellungskarte schob das Formular auf 60 % | UI-Sweep 20261016_1853: rookhub__ebooks (fold-too-long, fold-lead), __courses-bookId-browse, turnier__tournaments-calendar--filter, rookhub__profile |
| 2026-10-16 | 0.806.x · Falz-Runde 16.10. | LeagueHub: Formular-Korrektur am Handy „← Überschrift (i)“ / „Partietitel … Speichern ↓“ in zwei Zeilen (Rücklink-Zeile entfällt), Prüf-/Brettkarte 10/12 px Polster; Konto-Vorschläge: (i) an der Überschrift, Erklärsatz im Feld dahinter; Vereinspartien am Handy 8/10 px zwischen Satz-, Such- und Zählerzeile | Vorlauf 41–50 % | UI-Sweep 20261016_1853: leaguehub__verein-partie-clubGameId-korrigieren, __konten, __verein |
| 2026-10-18 | 0.807.x · Design-Runde d3 · E-Books | RookHub, E-Books: Leser am Handy — angedockt belegt ⋮ (letztes Element der Titelzeile) mit `margin-right: var(--rh-nav-w)` die Breite der Kopfleiste, Seitenfeld/Zoom brechen in Zeile 2 um; angedockte Kopfleiste: Abzeichen von Glocke und ☰ `top: 4px` (ganz im Fenster, auch pulsierend); Brett-Kopf: Zugrecht-Zeile bricht um (höchstens 2 Zeilen, `text-wrap: balance`) statt „…“, kurzer Text „Zugrecht unbekannt“; Übersicht ohne Anmeldung ohne Einleitungssatz (Banner + ⓘ am Titel); Suchfeld Basis 360 px (Tablet: Rest bricht um); Formularfeld mit mehrzeiligem Hinweis `subscriptSizing="dynamic"` + 12 px Abstand; Bibliothek: Titel aus dem Dateinamen MIT Endung, Sammelordner „Gemeinfrei“ → Autor aus dem Unterordner, „[Autor, Jahr]“ liefert das Jahr | Seitenfeld unter Anmelden/Glocke, Abzeichen oben abgeschnitten, Herkunftssymbol hinter „…“, Vorlauf 43,5 %, „… Schlagwort such“, Hinweis auf dem Feldrahmen, Titel „Chess Problems [V. M. N“ mit Autor „Gemeinfrei“ | UI-Sweep d3: d3-rk-leser-handy-kopfleiste-ueberlappt, d3-rk-leser-glocken-abzeichen-abgeschnitten, d3-rk-diagram-subtitle-abgeschnitten, d3-rk-ebooks-anon-vorlauf-zu-lang, d3-rk-ebooks-suchfeld-platzhalter-abgeschnitten, d3-rk-ebook-upload-hint-ueberlappt-feld, d3-rk-reader-titel-ohne-ellipse |
| 2026-10-18 | 0.807.x · Design-Runde d3 · Training | RookHub, Training: Repertoire-Trainer mit Repertoirenamen im Kopf, „Häufigste Linien zuerst“ als beschrifteter Umschalter, Übungszyklen + „Fortschritt zurücksetzen“ (rot, zuletzt) im ⋮, Leerzustand mit einer Aktion; Lernkarten ohne Titel „Linie n“ wie die Linienliste; Lochfinder: Seiten-Chip oben, Zeilenbeschriftungen Quelle/Datenbank, „RookHub-Server“, „Lichess-Partien | Meisterpartien“; Lösezeit mit Uhr-Symbol + Namen; Kursdetail ohne Vorgabe-Thema „Taktik“ (gemeinsame Regel mit der Karte); angeklebte Partieangabe mit Jahreszahl („Hastings 2012In …“) wird in der Anzeige abgesetzt (Daten bleiben); Woodpecker: Zeitbalken beschriftet unter der Frist, Löser „Satz: n Aufgaben von N“, Kurszeilen ≥ 44 px, Meta-Zeile bei gleichnamigen Kursen, leere Übersicht ohne Kurs → „Zu den Kursen“, Dialog „Schließen“ ohne Lücken; „Stellungsbewertung anzeigen“; „Tipps melden“ mit Daumen statt Flagge; Endlos-Statistik ohne doppelte Kennzahlen, Zeit = „Sitzung“; „Info“-Abzeichen aus Tokens; Kurs-Chip „Import veraltet“; englische Knöpfe in Satzschreibung; Repertoire-Brett aus Sicht der Kapitelfarbe | nackte Symbole samt offenem Reset, „#1“, Beschriftung las sich falsch zugeordnet, zwei Aktionen im Leerzustand + gleiche Titel, unbeschriftete Zahl, Vorgabe-Abzeichen, angeklebter Satz, Balken widersprach „0“, „0 im Satz“, mehrdeutige „Bewertung“, zwei gleiche Fähnchen, doppelte Kennzahlen, 2,5:1, Imperativ ohne Aktion, Title Case gemischt, Schwarz-Linien verkehrt herum | UI-Sweep d3: d3-rk-trainer-symbolknoepfe-ohne-text, d3-rk-flashcards-karte-titel-nummer, d3-rk-holes-kapitel-mit-schwarz-label, d3-rk-trainer-leer-zwei-aktionen, d3-rk-puzzle-zeitanzeige-ohne-beschriftung, d3-rk-kurs-thema-taktik-vorgabe, d3-rk-kommentar-text-ohne-leerzeichen, d3-rk-woodpecker-balken-zeit-statt-satz, d3-rk-wp-loeser-im-satz-beschriftung, d3-rk-bewertung-anzeigen-beschriftung, d3-rk-flaggen-symbol-doppelt, d3-rk-endless-doppelte-kennzahlen, d3-rk-info-abzeichen-kontrast-dunkel, d3-rk-kurskarte-neu-holen-chip, d3-rk-woodpecker-dialog-kurswahl-tippziel, d3-rk-woodpecker-kursliste-doppelte-namen, d3-rk-woodpecker-leerdialog-luecke-abbrechen, d3-rk-puzzle-button-case-inconsistent, d3-rk-schwarz-repertoire-brett-nicht-gedreht |
| 2026-10-23 | 0.823.2 | RookHub, Buch-/Kurs-Löser: Fehlzug mit Matt-Antwort → Brett bleibt im Matt, Statuskarte zeigt Hinweis + „Nochmal“ / „Lösung zeigen“ statt der Lösungs-Navigation (`solutionHidden`) | Der Sprung ans Ende der Musterlösung versteckte das Matt, das man gerade bekommen hatte | Nutzer |
| 2026-10-23 | 0.825.0 | RookHub, E-Book-Brett: „Training“ friert beim Lösen und Kalkulieren die Buchstellung ein — dasselbe Viz-Brett (`app-puzzle-board`, Stufe 1) wie Kurs-Training/Kalkulations-Modus, letzter Gegenzug als Pfeil; Art (Einfach/Training) auch beim Kalkulieren im ⚙ | Puzzle-Modi konsistent halten | Nutzer |
| 2026-10-23 | 0.824.1 | RookHub, Standard-/Endless-Löser: Matt nach Fehlzug bleibt stehen wie im Buch-/Kurs-Löser (Logik in `BasePuzzleSolver.showFailureOrHoldMate`) | Puzzle-Modi konsistent halten | Nutzer |
| 2026-10-24 | 0.831.0 | RookHub, E-Book-Brett: Modus „Partie“ (Partiensammlungen) — Zugliste der ganzen Partie nach der Regel „Zuglisten“ (eine Zeile je Vollzug, `2.6em 1fr 1fr`, ohne Bewertung), läuft mit der Brett-Spalte ohne eigenen Scrollkasten; aktueller Zug getönt, Diagrammzug unterstrichen; Modus-Knopf nur bei Diagrammen mit Partie | Zuglisten; kein Kasten im Kasten | Nutzer |
| 2026-10-25 | 0.834.0 | RookHub, Repertoire-Baum: Knopf „Pfeile“ unter dem Brett (gemerkt je Gerät) zeigt alle Fortsetzungen der Stellung als grüne Pfeile, Strichstärke 3–18 nach Lichess-Häufigkeit (Wurzel des Anteils am häufigsten Zug, ohne Daten alle 9); im Brett-Vollbild immer an; Figuren ziehen folgt der Fortsetzung, ein Zug außerhalb des Repertoires springt zurück mit kurzer Meldung | Im Vollbild fehlt die Liste — die Pfeile ersetzen sie | Nutzer |
| 2026-10-25 | 0.835.0 | RookHub, Repertoire-Baum im Brett-Vollbild: „Einen Zug zurück“ links oben im schwarzen Balken (Gegenstück zum Vollbild-Knopf rechts oben, 44 px, halbtransparent, an der Wurzel ausgegraut); Pfeil links geht im Baum einen Zug zurück. Brett-Komponente nimmt eigene Vollbild-Bedienelemente über `[boardFsOverlay]` auf | Im Vollbild fehlen Brotkrumen — ohne Knopf gab es keinen Weg zurück | Nutzer |
| 2026-10-25 | 0.836.0 | RookHub, Partieseite: Zug-Klassen als Schild hinter den Zügen der Zugliste (`!! ! ?! ? ??`, `✗`), Schrift nach Kontrast, am Schalter der Brett-Symbole | Zuglisten (Abschnitt 5) |
| 2026-10-26 | 0.847.0 | RookHub, „+“-Knopf (Stellung markieren): das Kreuz ist gezeichnet statt Schriftzeichen (saß auf der Textgrundlinie zu tief im Knopf), Host `align-items: center`; auch ohne Anmeldung sichtbar | Symbole mittig, nie als Glyphe auf der Grundlinie |
| 2026-10-27 | 0.855.0 | RookHub: Auswahlfenster „Partie analysieren — mit/ohne Engine“ (zwei große Wahlflächen mit Symbol + Satz, Kästchen „Entscheidung merken“, Abbrechen unten rechts); Analysebrett „ohne Engine“ blendet alles Bewertende aus (Leiste, Engine, Explorer, Maia, ⋮ der Stellung) und zeigt oben eine Karte mit Fortschritt + „Engine einschalten“, danach den Vergleich als Liste (Klassen-Schild wie in der Zugliste) | Analysebrett (8.x), Dialoge |
| 2026-10-27 | 0.862.0 | RookHub, „+“-Knopf: Klick markiert und öffnet ein kurzes Fenster (420 px) mit Schlagwort-Chips (feste Liste als Umschalter, grün = gesetzt; eigene Wörter über Feld + Enter, mit ×), Kommentarfeld und „Markierung entfernen“ links in Fehlerfarbe; markiert öffnet er nur das Fenster. LeagueHub: „+“ öffnet ein Stellungsfenster (440 px) — Überschrift „Interessante Stellung aus Partie/aus Variante“ zwischen ‹ ›, darunter Partie, wer am Zug (Punkt in Figurfarbe), „nach Zug n“, Zähler „3 / 30“, Diagramm ≤ 360 px, dann derselbe Editor (Schlagworte anderer gestrichelt, Bemerkungen anderer als Zitat), „In der Partie zeigen“ links, „Speichern“ rechts; „+n“-Abzeichen auch an Liga/Runde der Übersicht und oben in den Aufstellungen einer Runde | Stellungen klassifizieren und kommentieren, ohne die Partie aufzuklappen | Nutzer |
| 2026-10-27 | 0.862.1 | RookHub + LeagueHub, Notizen-Editor der „+“-Stellungen: unter der Bemerkung ein leiser Klappknopf „Frühere Fassungen ▾“; aufgeklappt eine Liste (≤ 220 px, scrollt) mit Zeitpunkt (`short`), Schlagworten als kleine Pillen, Bemerkung und „Wiederherstellen“ — übernimmt nur in die Felder, Speichern bleibt ausdrücklich | Gelöschtes zurückholen ohne versehentliches Überschreiben | Nutzer |
| 2026-10-28 | 0.864.0 | Runde d6: ClubHub-Datenschutz am Handy klappt wie LeagueHub ab dem dritten Abschnitt zu (Rechtstext unverändert); LeagueHub-Vereinspartien-Karte: Meta-Zeile ohne Mindestbreite + `table-layout: fixed`; Formular-Leser-Status als Fließtext, Strich mit U+00A0 an der Uhr, Uhr ab einem Tag in Tagen/Stunden; „Alle Gemeldeten“ mit Tausenderpunkt | ClubHub-Datenschutz 14 Handy-Bildschirme; Karten breiter als das Fenster („Nachspi…“, ⋮ außerhalb); „– der Leser …“ am Zeilenanfang + „298:59:11“; „3115“ neben „3.115“ | UI-Sweep 20261028_0657: d6-c-privacy-mobile-zu-lang, d6-l-vereinspartien-handy-ueberlauf, d6-l-wartet-leser-gedankenstrich, d6-l-partien-tausendertrenner |
| 2026-10-28 | 0.864.0 | Runde d6, KidHub: Sternenjagd: Markierungen nur auf ein Brett ≥ 8 Gerätepixel, nach jedem Neuaufbau mit frischen Maßen neu gesetzt; `kidBoardSize` misst nach jedem Neubau von `cg-board`. Registrieren: nur der Kasten „Für Eltern“ (8.3) | Sterne fehlten nach kurzem Mini-Fenster (`translate(NaN,NaN)`), zwei Kästen mit demselben Anfang vor dem ersten Feld | UI-Sweep d6: d6-k-sternenjagd-sterne-fehlen, d6-k-register-doppelter-hinweis |
| 2026-10-28 | 0.865.0 | RookHub + LeagueHub, Notizen-Editor der „+“-Stellungen: öffnet zum Lesen — nur die vergebenen Schlagworte (eigene gefüllt, die anderer gestrichelt), die Bemerkung als Text, Bemerkungen anderer als Zitat; rechts neben „Klassifizierung“ ein kleiner runder Bleistift (28 px, leise), der in die Bearbeitung mit allen Chips und Textfeld schaltet; „Speichern“ erst dann, sonst nur „Schließen“; leere Stellung öffnet gleich in der Bearbeitung | Erst sehen, was hinterlegt ist, ohne Formularwand | Nutzer |
| 2026-10-28 | 0.866.0 | LeagueHub-Vereinsübersicht: jede Liga ein Aufklapper (`details.ov-league`, Kopf = `<h2>` in `summary`, ≥ 44 px, ▸ in `--red`, dreht sich beim Öffnen, Linie in `--line`); zugeklappte Ligen merkt sich der Browser; „+n“ im Kopf öffnet das Stellungsfenster, ohne zu klappen | Am Handy die Ligen einzeln überblicken | Nutzer |
| 2026-11-02 | 0.881.2 | RookHub-Löser: langer Buch-/Kurs-Kommentar scrollt in seiner ~5-Zeilen-Höhe (`overflow-y: auto`, dünner Balken) statt abgeschnitten mit Verlauf | Züge in den unteren Zeilen waren nicht anklickbar, nur über „Ganz lesen“ | Wunsch User |
| 2026-11-02 | 0.882.0 | Woodpecker-Löser = Kurs-Löser: Kommentare, Tipps, Spielweise, Aktionszeile, Sprachwahl wie im Kurs; Plan-Kopf (Chip, Zyklus · Satz/Fortschritt · Resttage, Balken ab Zyklus 2, Wiederholen-Hinweis) und Abschluss-/„gibt es nicht“-Karte in der Kontext-Karte | eigener schlanker Löser ohne Kommentare/Tipps, andere Anordnung als im Kurs | Wunsch User („egal ob über Woodpecker oder Kurs — die UI soll die gleiche sein“) |
| 2026-11-08 | 0.903.0 | Admin „Uploads“: Knopf „Erledigt“ ↔ „Wieder öffnen“ ohne Rückfrage, erledigte Zeile gedämpft mit Vermerk (5.4) | Abgearbeitete Stapel waren von offenen nicht zu unterscheiden, ohne sie zu löschen | Wunsch User 08.11. |
| 2026-11-08 | 0.901.0 | Admin „Uploads“: „Einlesen“ als Knopf mit Menü der Arten (gesperrte Art mit Grund im Eintrag), Rückfrage mit „Einlesen“ als Knopf, Vermerk „eingelesen … → #id“ mit Links in der Zeile (5.4) | Stapel musste zum Einlesen heruntergeladen und neu hochgeladen werden | Wunsch User |
| 2026-11-08 | 0.913.0 | RookHub, Partie-Zusammenfassung (`/club-games/:id/summary`): eine Karte ≤ 860 px, Kopf wie die Partieseite (← Partie, Spieler + Ergebnis, „Zur Partie“), Fließtext Zeilenhöhe 1,6; Diagramme mittig ≤ 360 px mit Unterschrift darunter (Zug fett, Satz, rechts „In der Partie zeigen“), Merksatz als Zitat mit Akzentlinie; Tabellen im eigenen Scrollkasten. In der Partie-Kopfzeile „Zusammenfassung“ als umrandeter Knopf, für Admins „anfordern“ als Textknopf bzw. Wartevermerk mit Sanduhr | Text + Bretter lesbar wie ein Artikel, ohne Analysebrett | Wunsch User 08.11. |
| 2026-11-09 | 0.923.0 | RookHub, eigene Partie: „Zusammenfassung anfordern“ im Kopf wie bei Vereinspartien, dieselbe Seite `/games/:id/summary`; Tageskontingent verbraucht → Textvermerk statt Knopf | Ein Knopf, der nur scheitert, sagt nicht warum | Wunsch 09.11. |
| 2026-11-09 | 0.928.0 | RookHub, Partie-Zusammenfassung: Seitenbrett neben dem Text (5.12), Seite mit Brett `--page-max-width` statt 860 px; unter 900 px Brett UNTER dem Text | Partie neben dem Text durchklicken; Text bleibt über der Falz (4.6) | Wunsch 09.11. |
| 2026-11-09 | 0.932.0 | RookHub, Partie-Zusammenfassung: Züge im Text wie `.mv` (Akzentfarbe, gepunktet unterstrichen), Fett/Kursiv bleiben; Partiezug springt aufs Seitenbrett, eine Variante erscheint dort samt ←/→; eigene Züge auf dem Seitenbrett ergeben eine Variante (Leiste „Variante“ mit Zügen und „Zurück zur Partie“, ← am Anfang führt ebenfalls zurück). Nichts wird gespeichert; unter 900 px rollt das Brett ins Bild | Varianten aus dem Text direkt nachspielen und eigene Ideen probieren | Wunsch 09.11. |
| 2026-11-15 | 0.982.0 | RookHub, neue Seite Besucher-Herkunft `/visitors` (8.1): Flächenkarte in fünf Akzent-Stufen mit Skala, Kennzahlenreihe, Rangliste als Tabelle mit Balken, Kontinent-Wahl und Admin-Filter als Chips mit URL-Zustand, zweispaltig per Containerabfrage, E-Ink-Helligkeitstreppe | Erste Karte dieser Art — Stufen, Tastaturweg und „Farbe nie allein“ festgelegt | Wunsch User 15.11. (Vorlage „Vorschlag A“) |
| 2026-11-17 | Trainerstunden teilen | RookHub, Stundenseite: Abschnitt „Teilen“ (Frist-Umschalter, Liste der aktiven Links mit Kopieren/Widerrufen) und Gast-Ansicht `/tl/:token` samt Seite „Link abgelaufen“ (8.1) | Einzelne Stunden zeitlich begrenzt ohne Konto zeigen; der Gast darf nicht in private Bereiche abbiegen | Wunsch User 17.11. |
| 2026-11-17 | Nächster Sprecher | RookHub, Stundenseite der Trainerstunden (auch Gast): Sprecherleiste unter dem Video, Legende, „Zum Anfang des Redebeitrags“ und „Nächster Sprecher“ (8.1) | Im Video gezielt zum Trainer bzw. zur eigenen Frage springen | Wunsch User 17.11. |
| 2026-11-22 | 1.000.0 | RookHub, Eigene Fehler nachspielen (5.12): Live-Zeile „Schwache Analyse im Browser: … (Tiefe n) · Antwort …“ unter „gehört nicht zu den n besten Zügen“ | Fehlversuch außerhalb der Kandidaten hatte nur eine Schranke, keinen Wert | Wunsch User 22.11. (Vereinspartie 260) |
| 2026-11-25 | 1.012.0 | LeagueHub + Spielervorbereitung, Trainingslinien: Linienzahl als natives Auswahlfeld „Linien“ hinter dem Repertoire-Menü (8.4), „… und n weitere“ mit umrandetem Knopf zur nächsten Stufe | Wie viele Linien man bekommt, war fest 50 | Wunsch User 25.11. |
