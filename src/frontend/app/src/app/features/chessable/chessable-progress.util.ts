import { TranslateService } from '@ngx-translate/core';
import { ChessableImport } from './chessable.service';

/**
 * Hol-Durchsatz (Prod-Messung 2026-06-15, inkl. VPN-Rotationspausen): grob ~15–20 Zeilen/min.
 * Für Schätzungen konservativ die Faustregel 500 Zeilen ≈ 30 Min (≈ 16,7/min) verwenden.
 */
// Durchsatz echter (nicht-gecachter) Linien-Abrufe für die Rest-Zeit-Schätzung. Auf 40 Linien/Min
// gesetzt (nach dem Rotate-on-Block-Speedup; vorher gemessen ~26). Gecachte Linien sind quasi sofort.
export const CHESSABLE_LINES_PER_MIN = 40;

/** Kompakte Dauer aus Millisekunden: "1 h 5 min", "12 min", "45 s"; "—" bei ungültig/negativ. */
export function formatDuration(ms: number): string {
  if (!isFinite(ms) || ms < 0) return '—';
  const s = Math.floor(ms / 1000);
  if (s >= 3600) return `${Math.floor(s / 3600)} h ${Math.floor((s % 3600) / 60)} min`;
  if (s >= 60) return `${Math.floor(s / 60)} min`;
  return `${s} s`;
}

/**
 * Gesamt-Zeilenzahl: bevorzugt den EXAKTEN Wert (`linesTotal`, aus getCourse?includeVariations);
 * fällt auf die lineare Hochrechnung aus dem Kapitel-Fortschritt zurück, solange der exakte Wert
 * (noch) nicht vorliegt (0). 0 = (noch) nicht bestimmbar.
 */
export function effectiveTotalLines(linesDone: number, chaptersDone: number, chaptersTotal: number, linesTotal = 0): number {
  if (linesTotal > 0) return linesTotal;
  if (linesDone <= 0 || chaptersDone <= 0 || chaptersTotal <= 0) return 0;
  return Math.round((linesDone * chaptersTotal) / chaptersDone);
}

/**
 * Geschätzte Rest-Holzeit in Minuten: (Gesamt − geholt) / Durchsatz, aufgerundet. Nutzt die exakte
 * Gesamtzahl, sobald bekannt, sonst die Hochrechnung. 0 = nicht schätzbar.
 */
export function estimateRemainingMinutes(linesDone: number, chaptersDone: number, chaptersTotal: number, linesTotal = 0): number {
  const total = effectiveTotalLines(linesDone, chaptersDone, chaptersTotal, linesTotal);
  if (total <= 0) return 0;
  const remaining = Math.max(0, total - linesDone);
  return Math.ceil(remaining / CHESSABLE_LINES_PER_MIN);
}

/**
 * Statuslabel eines Imports: Phase + (beim Holen) Kapitel/Linien-Fortschritt + Rest-Zeit-Schätzung.
 * Erzeugt genau den Text „hole Kurs… Kapitel 7/36 · 82/1000 Linien · noch ca. 23 Min".
 * Reine Funktion (statt Komponenten-Methode), damit Chessable-Tab UND Kursseite denselben Text bauen.
 */
export function chessableStatusLabel(imp: ChessableImport, t: TranslateService): string {
  let s = t.instant('chessable.phase_' + (imp.phase || 'queued'));
  if (imp.phase === 'fetching' && imp.chaptersTotal > 0) {
    // Mit bekannter Gesamt-Linienzahl „Linien X/Gesamt" zeigen, sonst nur die geholten.
    s += ' ' + (imp.linesTotal > 0
      ? t.instant('chessable.fetchProgressTotal',
          { ch: imp.chaptersDone, total: imp.chaptersTotal, lines: imp.linesDone, linesTotal: imp.linesTotal })
      : t.instant('chessable.fetchProgress',
          { ch: imp.chaptersDone, total: imp.chaptersTotal, lines: imp.linesDone }));
    // Restzeit: exakte Gesamtzahl nutzen, sobald bekannt; sonst Hochrechnung.
    const eta = estimateRemainingMinutes(imp.linesDone, imp.chaptersDone, imp.chaptersTotal, imp.linesTotal);
    if (eta > 0) s += ' · ' + t.instant('chessable.etaRemaining', { min: eta });
  }
  return s;
}

/** Status für Warteschlange/Zeile: pausiert / globale Position / Hol-Fortschritt. */
export function chessableQueueLabel(imp: ChessableImport, t: TranslateService): string {
  if (imp.status === 'paused') return t.instant('chessable.statusPaused');
  if (imp.phase === 'queued') return t.instant('chessable.queuePos', { pos: imp.queuedAhead + 1 });
  return chessableStatusLabel(imp, t);
}

/**
 * Sortiert die Import-Warteschlange nach „#" (globale Abarbeitungs-Position): zuerst nach
 * `queuedAhead` aufsteigend (in Arbeit = 0 → ganz oben, dann Warteposition 2, 3, …), bei
 * Gleichstand nach Anlegezeitpunkt (älter zuerst). Stabil & rein → in `.sort()` verwendbar.
 */
export function compareImportsByQueue(a: ChessableImport, b: ChessableImport): number {
  if (a.queuedAhead !== b.queuedAhead) return a.queuedAhead - b.queuedAhead;
  return Date.parse(a.createdAt) - Date.parse(b.createdAt);
}
