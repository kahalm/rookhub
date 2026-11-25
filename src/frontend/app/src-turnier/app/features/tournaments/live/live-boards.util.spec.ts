import { evalText, lastMovePair, shownClock, whiteShare } from './live-boards.util';
import { LiveGame } from './live-boards.models';

describe('live-boards.util', () => {
  it('whiteShare: 50 bei 0, Matt ganz, ohne Bewertung null', () => {
    expect(whiteShare({ cp: 0, mate: null, source: 'cloud', depth: null })).toBe(50);
    expect(whiteShare({ cp: 300, mate: null, source: 'cloud', depth: null })!).toBeGreaterThan(70);
    expect(whiteShare({ cp: null, mate: -2, source: 'cloud', depth: null })).toBe(0);
    expect(whiteShare(null)).toBeNull();
  });

  it('evalText', () => {
    expect(evalText({ cp: 42, mate: null, source: 'cloud', depth: 30 })).toBe('+0.4');
    expect(evalText({ cp: null, mate: -3, source: 'broadcast', depth: null })).toBe('#−3');
  });

  it('lastMovePair', () => {
    expect(lastMovePair('e7e8q')).toEqual(['e7', 'e8']);
    expect(lastMovePair(null)).toBeUndefined();
  });

  it('shownClock: nur die Seite am Zug läuft, fertige Partie steht', () => {
    const g = {
      white: { clockSeconds: 600 }, black: { clockSeconds: 500 }, whiteToMove: true, finished: false, plies: 10,
    } as unknown as LiveGame;
    expect(shownClock(g, true, 0, 5000)).toBe(595);
    expect(shownClock(g, false, 0, 5000)).toBe(500);
    expect(shownClock({ ...g, finished: true }, true, 0, 5000)).toBe(600);
    expect(shownClock(g, true, 5000, 1000)).toBe(600);   // „jetzt“ vor dem Eintreffen: keine Sekunde dazu
  });
});
