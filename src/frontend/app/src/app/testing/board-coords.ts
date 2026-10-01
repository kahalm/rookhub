import { contrast, parseColor, resolveColor } from './contrast';

/** Eine Brett-Koordinate, gemessen gegen das Feld, auf dem sie steht, und gegen die andere Feldfarbe. */
export interface CoordReading {
  kind: 'rank' | 'file';
  label: string;
  /** Feld unter der Ziffer/dem Buchstaben, z. B. „a1". */
  square: string;
  onDark: boolean;
  /** Kontrast zum eigenen Feld. */
  own: number;
  /** Kontrast zur anderen Feldfarbe — groesser als `own` heisst: die Farbe ist fuer das falsche Feld gewaehlt. */
  other: number;
}

const FILES = 'abcdefgh';

/**
 * Misst jede Koordinate eines gerenderten chessground-Bretts (`.cg-wrap`) gegen das Feld, auf dem sie WIRKLICH
 * steht — ermittelt aus ihrer Lage im Brett, nicht aus der Theorie (Codereview UX-060: RookHub schiebt die
 * Rangziffern auf die a-Linie, chessground faerbt sie fuer die h-Linie). `light`/`dark` sind die Feldfarben des
 * Brettthemas; die Deckkraft der `coords`-Leiste (chessground: 0,8) rechnet mit.
 */
export function readCoords(wrap: HTMLElement, light: string, dark: string): CoordReading[] {
  const board = wrap.querySelector('cg-board')!.getBoundingClientRect();
  const white = wrap.classList.contains('orientation-white');
  const size = board.width / 8;
  const [lightBg, darkBg] = [resolveColor(light), resolveColor(dark)];
  const out: CoordReading[] = [];
  for (const coords of Array.from(wrap.querySelectorAll<HTMLElement>('coords.ranks, coords.files'))) {
    const opacity = Number(getComputedStyle(coords).opacity);
    for (const coord of Array.from(coords.querySelectorAll<HTMLElement>('coord'))) {
      const r = coord.getBoundingClientRect();
      const col = Math.min(7, Math.max(0, Math.floor((r.left + r.width / 2 - board.left) / size)));
      const row = Math.min(7, Math.max(0, Math.floor((r.top + r.height / 2 - board.top) / size)));
      const file = white ? col : 7 - col;
      const rank = white ? 7 - row : row;
      const onDark = (file + rank) % 2 === 0;                 // a1 ist dunkel
      const [cr, cg, cb, ca] = parseColor(getComputedStyle(coord).color);
      const fg: [number, number, number, number] = [cr, cg, cb, ca * opacity];
      out.push({
        kind: coords.classList.contains('ranks') ? 'rank' : 'file',
        label: coord.textContent ?? '',
        square: `${FILES[file]}${rank + 1}`,
        onDark,
        own: contrast(fg, onDark ? darkBg : lightBg),
        other: contrast(fg, onDark ? lightBg : darkBg),
      });
    }
  }
  return out;
}
