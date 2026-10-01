import { Chessground } from 'chessground';
import { Api } from 'chessground/api';
import { readCoords } from '../../testing/board-coords';

/**
 * Farbe der Brett-Koordinaten (Codereview 2026-09-29, UX-060). RookHub schiebt die Rangziffern in styles.scss auf
 * die LINKE Randspalte (Weiss unten: a-Linie, Schwarz unten: h-Linie), chessground und die Themen-Regeln faerbten
 * sie aber fuer die rechte — dort liegen helle und dunkle Felder umgekehrt. Im Thema „blue" standen 2/4/6/8 hell auf
 * hell (1,0:1, unsichtbar) und 1/3/5/7 dunkel auf dunkel (1,9:1); dasselbe Muster in allen Themen.
 *
 * Gerendert wird ein echtes chessground-Brett je Thema und Orientierung; jede Ziffer und jeder Buchstabe wird gegen
 * das Feld gemessen, auf dem sie laut Lage im Brett steht.
 */
describe('Brett-Koordinaten: Farbe passend zum Feld darunter (UX-060)', () => {
  /** Feldfarben, wie sie gemalt werden (brown: chessground.brown.css, #f0d9b5 mit 20 % Schwarz auf den dunklen). */
  const THEMES: Record<string, [light: string, dark: string]> = {
    brown: ['#f0d9b5', '#c0ae91'],
    blue: ['#d4e3ed', '#5882a1'],
    green: ['#eeeed2', '#769656'],
    gray: ['#f0f0f0', '#8a8a8a'],
    wood: ['#e6d1a0', '#8b5e3c'],
  };
  let host: HTMLElement;
  let ground: Api | undefined;

  afterEach(() => {
    ground?.destroy();
    ground = undefined;
    host.remove();
  });

  function mount(theme: string, orientation: 'white' | 'black'): HTMLElement {
    host = document.createElement('div');
    host.className = `board-theme-${theme} piece-set-cburnett`;
    const el = document.createElement('div');
    el.style.width = el.style.height = '400px';
    host.appendChild(el);
    document.body.appendChild(host);
    ground = Chessground(el, { fen: '8/8/8/8/8/8/8/8', orientation, coordinates: true });
    return el;
  }

  for (const [theme, [light, dark]] of Object.entries(THEMES)) {
    for (const orientation of ['white', 'black'] as const) {
      it(`${theme}, ${orientation === 'white' ? 'Weiss' : 'Schwarz'} unten: jede Koordinate hebt sich von IHREM Feld ab`, () => {
        const readings = readCoords(mount(theme, orientation), light, dark);
        expect(readings.filter(r => r.kind === 'rank').length).toBe(8);
        expect(readings.filter(r => r.kind === 'file').length).toBe(8);
        for (const r of readings) {
          const what = `${r.kind === 'rank' ? 'Rang' : 'Linie'} ${r.label} auf ${r.square} (${r.onDark ? 'dunkel' : 'hell'}): `
            + `${r.own.toFixed(2)}:1, zur anderen Feldfarbe ${r.other.toFixed(2)}:1`;
          expect(r.own).withContext(`Farbe fuer das falsche Feld — ${what}`).toBeGreaterThan(r.other);
          expect(r.own).withContext(`kaum sichtbar — ${what}`).toBeGreaterThanOrEqual(1.5);
        }
      });
    }
  }
});
