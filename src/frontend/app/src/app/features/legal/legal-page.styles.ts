/**
 * Gemeinsame Seitenform der Rechtsseiten (Datenschutz, Impressum, Konto löschen) — UI-Sweep 2026-10-11, Runde d2:
 *
 * - Karte überall 760 px (vorher „Konto löschen“ 720 px: im LeagueHub 14 px eingerückt neben Kopf und Fuß,
 *   d2-l-kontoloeschen-flucht).
 * - Fließtext höchstens 68ch ≈ 75 Zeichen je Zeile (vorher 90–100, d2-rk-legal-zeilenlaenge, DESIGN 4.6 Regel 4).
 *   Nicht in KidHub (`.legal-kid`): die Kinderkarte hat eigene Breite und Schrift (KID_PRIVACY_STYLES).
 * - Am Handy (< 600 px) 12 px statt 2rem Rand und 12 px Kartenpolster, Listen mit 1rem statt ~40 px Einzug — sonst
 *   blieben dem Text von 390 px nur ~295 (d2-t-privacy-toc-mobile, d2-l-datenschutz-lang; wie tournament-detail).
 *
 * Die Hubs überschreiben Rand und Karte in ihren globalen Stylesheets (DESIGN 8.6); deren Selektoren sind spezifischer.
 */
export const LEGAL_PAGE_STYLES = `
  .legal-container { padding: 2rem; display: flex; justify-content: center; }
  mat-card { max-width: 760px; width: 100%; }
  .legal-container:not(.legal-kid) mat-card-content { max-width: 68ch; }
  @media (max-width: 599px) {
    .legal-container { padding: 0.75rem; }
    mat-card-header, mat-card-content { padding-left: 12px; padding-right: 12px; }
    mat-card-content ul { padding-left: 1rem; }
  }
`;
