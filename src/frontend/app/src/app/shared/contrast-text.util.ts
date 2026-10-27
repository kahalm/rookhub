/** Schrift auf einer Farbfläche (#rrggbb): Schwarz oder Weiß, was mehr Kontrast hat (WCAG-Leuchtdichte) — Gelb → Schwarz,
 *  Rot → Weiß. Geteilt von den Zug-Klassen-Schildern der Zugliste und der Karte „Ohne Engine". */
export function contrastText(color: string): string {
  const m = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(color);
  if (!m) return '#fff';
  const lin = (h: string) => { const c = parseInt(h, 16) / 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; };
  const l = 0.2126 * lin(m[1]) + 0.7152 * lin(m[2]) + 0.0722 * lin(m[3]);
  return 1.05 / (l + 0.05) >= (l + 0.05) / 0.05 ? '#fff' : '#000';
}
