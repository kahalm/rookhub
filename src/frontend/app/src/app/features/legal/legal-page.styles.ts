/**
 * Gemeinsame Seitenform der Rechtsseiten (Datenschutz, Impressum, Konto löschen) — UI-Sweep 2026-10-11, Runde d2:
 *
 * - Karte überall gleich breit (vorher „Konto löschen“ 720 px: im LeagueHub 14 px eingerückt neben Kopf und Fuß,
 *   d2-l-kontoloeschen-flucht).
 * - Fließtext höchstens 68ch (seit z2 33em, s. u.) ≈ 75 Zeichen je Zeile (vorher 90–100, d2-rk-legal-zeilenlaenge, DESIGN 4.6 Regel 4).
 *   Nicht in KidHub (`.legal-kid`): die Kinderkarte hat eigene Breite und Schrift (KID_PRIVACY_STYLES).
 * - Am Handy (< 600 px) 12 px statt 2rem Rand und 12 px Kartenpolster, Listen mit 1rem statt ~40 px Einzug — sonst
 *   blieben dem Text von 390 px nur ~295 (d2-t-privacy-toc-mobile, d2-l-datenschutz-lang; wie tournament-detail).
 *
 * - Runde z1: Die Karte ist so breit wie die Textspalte (68ch + 2 × 16 px Polster) statt 760 px — vorher blieben
 *   rechts in der Karte ≈ 130 px leer (z1-t-privacy-karte-rechts-leer). Fließtext mit 1,55 Zeilenhöhe und Abstand
 *   zwischen den Aufzählungspunkten (z1-rk-privacy-zeilen; vorher ≈ 1,2 ohne Abstand, Punkte liefen ineinander) —
 *   nicht im Inhaltsverzeichnis (eigene Zeilen) und nicht in KidHub (eigene Schrift).
 *
 * - Runde z2 (z2-rk-legal-zeilenlaenge): 33em statt 68ch — `ch` ist die Breite der Ziffer 0, in Roboto deutlich breiter
 *   als ein mittlerer Buchstabe; mit 68ch passten noch 80–85 Zeichen in eine Zeile. 33em ≈ 72–75 Zeichen (gemessen ≈ 75 im Test-Chrome) (DESIGN 4.6
 *   Regel 4), in jeder Schrift der Hubs (die Breite wächst mit ihrer Schriftgröße mit).
 *
 * Die Hubs überschreiben Rand und Karte in ihren globalen Stylesheets (DESIGN 8.6); deren Selektoren sind spezifischer.
 */
export const LEGAL_PAGE_STYLES = `
  .legal-container { padding: 2rem; display: flex; justify-content: center; }
  /* Bewusst schwach (ein Element + Attribut): KidHub, LeagueHub und ClubHub setzen ihre eigene Kartenbreite. */
  mat-card { max-width: calc(33em + 32px); width: 100%; }
  .legal-container:not(.legal-kid) mat-card-content { max-width: 33em; }
  .legal-container:not(.legal-kid) mat-card-content > p,
  .legal-container:not(.legal-kid) mat-card-content > ul li { line-height: 1.55; }
  .legal-container:not(.legal-kid) mat-card-content > ul > li + li { margin-top: 0.5rem; }
  @media (max-width: 599px) {
    .legal-container { padding: 0.75rem; }
    mat-card-header, mat-card-content { padding-left: 12px; padding-right: 12px; }
    mat-card-content ul { padding-left: 1rem; }
  }
`;
