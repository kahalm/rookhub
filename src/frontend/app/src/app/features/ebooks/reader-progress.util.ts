/**
 * Reine Rechnungen des PDF-Lesers — ohne pdf.js, ohne DOM, damit sie sich prüfen lassen.
 */

/** Zoom-Stufen des Lesers (Faktor auf die 72-dpi-Seitenbreite); „Breite" liegt dazwischen und ist kein fester Schritt. */
export const ZOOM_STEPS: readonly number[] = [0.5, 0.65, 0.8, 1, 1.25, 1.5, 2, 2.5, 3, 4];

/** Nächste Stufe über bzw. unter dem aktuellen Faktor (am Rand bleibt es stehen). */
export function nextZoom(current: number, direction: 1 | -1): number {
  if (direction > 0) return ZOOM_STEPS.find(z => z > current + 1e-6) ?? current;
  for (let i = ZOOM_STEPS.length - 1; i >= 0; i--) if (ZOOM_STEPS[i] < current - 1e-6) return ZOOM_STEPS[i];
  return current;
}

/** Faktor, mit dem eine Seite von `pageWidth` (bei Faktor 1) genau `containerWidth` breit wird — abzüglich Rand. */
export function fitWidthZoom(containerWidth: number, pageWidth: number, padding = 16): number {
  if (pageWidth <= 0 || containerWidth <= 0) return 1;
  const z = (containerWidth - 2 * padding) / pageWidth;
  return Math.max(0.2, Math.min(6, Math.round(z * 1000) / 1000));
}

/**
 * Die „aktuelle" Seite beim Scrollen: die Seite, die die Mitte des Sichtfelds enthält — und solange die erste
 * Seite noch die obere Hälfte füllt, bleibt es die erste. `tops` sind die Oberkanten der Seiten im Scrollbereich
 * (aufsteigend), `heights` ihre Höhen; 1-basiert.
 */
export function currentPage(scrollTop: number, viewportHeight: number, tops: readonly number[], heights: readonly number[]): number {
  if (tops.length === 0) return 1;
  const probe = scrollTop + Math.min(viewportHeight, heights[0] ?? viewportHeight) / 2;
  let page = 1;
  for (let i = 0; i < tops.length; i++) {
    if (tops[i] <= probe) page = i + 1;
    else break;
  }
  return page;
}

/** Welche Seiten gerendert sein sollen: sichtbare plus `around` davor/danach (Vorlauf beim Scrollen). */
export function pagesToRender(scrollTop: number, viewportHeight: number, tops: readonly number[], heights: readonly number[], around = 1): Set<number> {
  const result = new Set<number>();
  const from = scrollTop - around * viewportHeight;
  const to = scrollTop + viewportHeight * (1 + around);
  for (let i = 0; i < tops.length; i++) {
    const top = tops[i];
    const bottom = top + (heights[i] ?? 0);
    if (bottom >= from && top <= to) result.add(i + 1);
  }
  if (result.size === 0 && tops.length > 0) result.add(currentPage(scrollTop, viewportHeight, tops, heights));
  return result;
}

/** Seiteneingabe des Nutzers: Zahl in 1…count, sonst `null`. */
export function parsePageInput(raw: string, count: number): number | null {
  const n = Number.parseInt(raw.trim(), 10);
  if (!Number.isFinite(n) || n < 1 || n > Math.max(1, count)) return null;
  return n;
}
