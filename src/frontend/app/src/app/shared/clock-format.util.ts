/**
 * Sekunden als Uhr: `m:ss` unter einer Stunde, `h:mm:ss` darueber; negativ/ungueltig → `0:00`,
 * Bruchteile abgeschnitten. Die EINE Quelle fuer Wochenpost-, Kurs-, Kalkulations- und
 * Timer-Zeiten (vorher sechs gleiche Kopien unter vier Namen, Codereview F5-025).
 *
 * Nicht zu verwechseln mit `formatClock` (features/games/scoresheet-timing.ts, nur `m:ss` ohne
 * Stunden) und `formatPuzzleTime` (puzzle-format.util.ts, „45s" unter einer Minute).
 */
export function formatSecondsClock(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds || 0));
  const sec = s % 60, m = Math.floor(s / 60) % 60, h = Math.floor(s / 3600);
  const p2 = (n: number) => n.toString().padStart(2, '0');
  return h > 0 ? `${h}:${p2(m)}:${p2(sec)}` : `${m}:${p2(sec)}`;
}
