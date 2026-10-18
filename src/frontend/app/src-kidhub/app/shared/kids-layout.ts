import { isAuthUrl } from './kids-auth';

/**
 * Aufbau der Aufgabenseite (Stufe, Endlos, Kurs) je Fenster — EINE Stelle fuer alle Komponenten, die daran
 * haengen: die App-Huelle setzt dazu die Groessen (`--kid-board`, `--kid-row`), `KidsPuzzleComponent` das Raster,
 * die drei Seiten ihre Kopfzeile. Wandert eine Grenze nur in einer Datei, sitzen Kopfzeile und Brett schief.
 *
 * <p>Vorher hing alles an einer festen Breite (bis 760 px untereinander): am iPad hochkant (768–834 px) stand das
 * Brett mit 300 px neben einer halbleeren Seite, und am Handy quer lief seine unterste Reihe aus dem Bild
 * (Codereview 2026-09-29, UX-028).</p>
 */

/**
 * Untereinander (Aufgabe, Brett, Eule): schmal, hochkant — daneben bliebe fuers Brett nur ein Streifen — oder bis
 * 760 px Breite mit genug Hoehe. Schliesst {@link KID_SHORT} aus; bis 760 px Breite ergaenzen sich die beiden genau.
 * Bereichs-Schreibweise (`<`, `>`) statt `max-width: 599px`/`min-height: 501px`: dazwischen lagen sonst 1-px-Luecken,
 * in denen bei krummen Fenstermassen (Zoom, Pixeldichte 2,625 …) die PC-Formel mit 300 px Untergrenze griff.
 */
export const KID_STACKED = '(width < 600px), (max-aspect-ratio: 1/1), (width <= 760px) and (height > 500px)';

/**
 * Handy quer: wenig Hoehe — das Brett nimmt die Hoehe ganz, die Aufgabe steht daneben. Die App-Huelle rechnet dafuer
 * mit genau EINER Titelzeile ueber dem Brett; die drei Seiten (Stufe, Endlos, Kurs) halten sie deshalb einzeilig
 * und kuerzen einen langen Titel, statt ihn umzubrechen.
 */
export const KID_SHORT = '(width >= 600px) and (height <= 500px)';

/**
 * Rueckweg-Knopf („← Start", „← Stufen", „← Kurse") aller Unterseiten — EINE Regel statt fuenf Kopien. Vorher ein
 * Textlink von ~21 px Hoehe ohne Polsterung, am Handy schwer zu treffen; jetzt eine Pille mit mindestens 44 px
 * Tipphoehe (Codereview 2026-09-29, F7-012). Handy quer muss die Titelzeile einzeilig und hoechstens 36 px hoch
 * bleiben (damit rechnet das Brett, siehe {@link KID_SHORT}): dort ist die Pille flacher, und eine unsichtbare Flaeche
 * ober- und unterhalb haelt die Tipphoehe bei 44 px, ohne die Zeile hoeher zu machen.
 * `gap: .3em` (d3-k-kid-back-pill-kurse-pfeil): Pfeil und Wort als eigene Spannen („←" + „Kurse") bekommen ihren Abstand
 * hier — ein fuehrendes Leerzeichen faellt im Flex-Block weg; einteiliger Text („← Start") bleibt unberuehrt.
 */
export const KID_BACK = `
  .back {
    position: relative; display: inline-flex; align-items: center; gap: .3em; box-sizing: border-box; min-height: 44px;
    padding: 6px 16px; border-radius: 999px; background: var(--kid-card); box-shadow: 0 3px 0 var(--kid-shadow);
    font-size: 1.1rem; font-weight: 800; line-height: 1.2; text-decoration: none; color: inherit; white-space: nowrap;
    flex-shrink: 0;
  }
  .back:active { transform: translateY(2px); box-shadow: 0 1px 0 var(--kid-shadow); }
  @media ${KID_SHORT} {
    .back { min-height: 34px; padding: 3px 14px; }
    .back::after { content: ''; position: absolute; left: 0; right: 0; top: -5px; bottom: -5px; }
  }
`;

/**
 * Brett-Koordinaten der Kinderseite (UI-Sweep 2026-10-10, k-coords): RookHub setzt sie seit 0.740.0 global auf 11/12 px
 * halbfett (`src/styles.scss`) — auf dem bis zu 820 px grossen Kinderbrett bleibt das winzig. Hier ~2,2 % der Brettbreite
 * (`--kid-board`, mindestens 11 px am Handy), fett und deckend in der GEGENFARBE des Felds: dunkles Blau auf den hellen,
 * Weiss auf den dunklen Feldern des Themas „blue" (#d4e3ed / #5882a1). Welche Ziffer auf welchem Feld steht, regelt
 * styles.scss (UX-060: Raenge links, Linien unten) — die Reihenfolge der Faerbung ist dieselbe, nur die Toene sind
 * kraeftiger. Die Leisten wachsen mit der Schrift (styles.scss legt sie fest auf 14 px). Nur fuer `.board` der
 * Kinderseiten — RookHubs Bretter bleiben unberuehrt.
 */
export const KID_COORD_LIGHT_TEXT = '#ffffff';
export const KID_COORD_DARK_TEXT = '#2f4f6c';
/**
 * Hof um die Koordinaten (b1-k-koordinate-auf-auswahlfeld, DESIGN 5.12): auf dem grünen Auswahlfeld (≈ #749c85), dem
 * letzten Zug und den Zielpunkten hatte die dunkle Schrift nur ≈ 2,8:1, ein Figurenfuß berührte sie zusätzlich.
 * Ein deckender Hof in der Gegenfarbe hält sie auf jeder Feldfarbe lesbar — dunkle Schrift mit hellem, weiße mit
 * dunklem Hof (wie RookHub im Thema brown, a1-rk-brett-koordinaten-dunkle-felder). Ersetzt den dünnen globalen Hof.
 */
export const KID_COORD_DARK_HALO = '#ffffff';
export const KID_COORD_LIGHT_HALO = '#1d3a52';
export const KID_COORDS = `
  .board ::ng-deep .cg-wrap coords {
    font-size: max(11px, calc(var(--kid-board, 640px) * 0.022)); font-weight: 700; line-height: 1; opacity: 1;
  }
  .board ::ng-deep .cg-wrap coords.ranks { width: 1.6em !important; }
  .board ::ng-deep .cg-wrap coords.files { height: 1.4em !important; }
  .board ::ng-deep .board-theme-blue .orientation-white .ranks :nth-child(even),
  .board ::ng-deep .board-theme-blue .orientation-white .files :nth-child(even),
  .board ::ng-deep .board-theme-blue .orientation-black .ranks :nth-child(odd),
  .board ::ng-deep .board-theme-blue .orientation-black .files :nth-child(odd) {
    color: ${KID_COORD_DARK_TEXT};
    text-shadow: 0 0 2px ${KID_COORD_DARK_HALO}, 0 0 3px ${KID_COORD_DARK_HALO}, 0 0 4px rgba(255, 255, 255, .8);
  }
  .board ::ng-deep .board-theme-blue .orientation-white .ranks :nth-child(odd),
  .board ::ng-deep .board-theme-blue .orientation-white .files :nth-child(odd),
  .board ::ng-deep .board-theme-blue .orientation-black .ranks :nth-child(even),
  .board ::ng-deep .board-theme-blue .orientation-black .files :nth-child(even) {
    color: ${KID_COORD_LIGHT_TEXT};
    text-shadow: 0 0 2px ${KID_COORD_LIGHT_HALO}, 0 0 3px ${KID_COORD_LIGHT_HALO};
  }
`;

/**
 * Seitenspalte neben dem Brett im Breitbild (b1-k-ultrawide-seitenspalte, DESIGN 4.1/8.3): ab 2400 px wächst das Brett
 * auf bis zu 1240 px, die Spalte daneben auf 480 px (`--kid-side`, App-Hülle) — Aufgabe, Eule, Knöpfe und Kommentar
 * wachsen hier mit, sonst wirkte sie mit Laptop-Schrift winzig neben dem Brett. Nur nebeneinander (quer, nicht flach):
 * hochkant ({@link KID_STACKED}) und Handy quer ({@link KID_SHORT}) bleiben, wie sie sind. Steht in der Komponente NACH
 * deren Grundregeln (gleiche Spezifität, die spätere gewinnt).
 */
export const KID_WIDE_SIDE = '(width >= 2400px) and (height > 500px) and (aspect-ratio > 1/1)';
export const KID_SIDE_WIDE = `
  @media ${KID_WIDE_SIDE} {
    .bubble { font-size: 1.5rem; padding: 18px 20px; }
    .owl { font-size: 3rem; }
    .big { font-size: 1.55rem; padding: 18px 22px; }
    .comment { font-size: 1.25rem; }
    .task { font-size: 1.8rem; }
  }
`;

/**
 * Breite der Inhaltsspalte je Seite (UI-Sweep 2026-10-10, k-logo): die Kopfzeile mit dem Logo steht in derselben
 * zentrierten Spalte wie der Inhalt — vorher klebte das Logo am PC ganz links (x = 16), der Inhalt begann erst bei
 * x = 336. Die Seiten setzen ihre `max-width` aus diesen Werten (Polsterung 16 px eingerechnet), die App-Huelle
 * gibt der Kopfzeile dieselbe Breite ({@link kidPageWidth}).
 */
// legal 620 (z1-k-legal-line): Karte 588 px, Text ≈ 556 px ≈ 75 Zeichen bei 16 px — mit 840 waren es rund 105.
export const KID_PAGE_WIDTH = { home: 1160, map: 980, list: 820, legal: 620 } as const;

/**
 * Startseite und Karten wachsen am großen Bildschirm mit (DESIGN 4.6 Regel 4): bis zum Laptop gelten die festen Werte
 * oben, bei 1920 px wird die Startseite 1520 px breit (vier Kacheln ≈ 360 px, z2-k-start-kacheln-breit) und die
 * Stufen-/Sternenkarte ≈ 1440 px (9 Spalten, 40 Stufen in 5 Reihen statt 7, z2-k-karten-breit). Ab 2400 px hebt die
 * App-Huelle die Obergrenze ueber `--kid-wide-max` auf 1880 px (Breitbild-Stufe, DESIGN 4.1, a1-k-ultrawide-raster);
 * darunter gilt der Rueckfallwert, also pixelgleich wie bisher.
 */
export const KID_HOME_WIDTH = `clamp(${KID_PAGE_WIDTH.home}px, calc(100vw - 400px), var(--kid-wide-max, 1520px))`;
export const KID_MAP_WIDTH = `clamp(${KID_PAGE_WIDTH.map}px, 75vw, var(--kid-wide-max, 1480px))`;
/** Anmelde-Masken: Kopfzeile so breit wie „← Start" und die 400-px-Karte darunter (z2-k-auth-logo). */
export const KID_AUTH_WIDTH = 432;

/**
 * Breite der Kopfzeile fuer die Seite unter `url` (CSS-Wert). Aufgabenseiten (Stufe, Endlos, Sternenjagd, Kurs)
 * richten ihre Titelzeile an `--kid-row` aus (Brett + Spalte daneben), dazu die 16 px Polsterung je Seite.
 */
export function kidPageWidth(url: string): string {
  const path = url.split(/[?#]/)[0].replace(/\/+$/, '') || '/';
  if (/^\/(levels|stars|courses)\/[^/]+$/.test(path) || path === '/endless') return 'calc(var(--kid-row, 1068px) + 32px)';
  if (path === '/') return KID_HOME_WIDTH;
  if (path === '/courses') return `${KID_PAGE_WIDTH.list}px`;
  if (isAuthUrl(path)) return `${KID_AUTH_WIDTH}px`;
  // Rechtsseiten (Datenschutz, Konto loeschen: d1-k-account-deletion-style) — Kopfzeile so breit wie ihre Karte.
  if (path === '/privacy' || path === '/account-deletion') return `${KID_PAGE_WIDTH.legal}px`;
  return KID_MAP_WIDTH;
}

/**
 * Zähler im Seitenkopf (UI-Sweep 2026-10-11, r2-k-counter): Stufen „★ 14", Endlos „✓ 3", Sternenjagd „✓ 0/20" und das
 * freie Spiel „✓ 0" hatten vier verschiedene Formen (dunkler Text, blauer Text, grüne Pille). Jetzt EINE grüne Pille
 * mit Zeichen + Zahl — ohne Schatten und ohne Zeiger, damit sie nicht wie ein Knopf aussieht. Klasse `kid-counter`.
 */
export const KID_COUNTER = `
  .kid-counter {
    display: inline-flex; align-items: center; gap: 4px; box-sizing: border-box; padding: 4px 12px; border-radius: 999px;
    background: var(--kid-good-bg); color: var(--kid-good-fg); font-size: 1.1rem; font-weight: 800; line-height: 1.3;
    white-space: nowrap; flex-shrink: 0; cursor: default; user-select: none;
  }
`;
