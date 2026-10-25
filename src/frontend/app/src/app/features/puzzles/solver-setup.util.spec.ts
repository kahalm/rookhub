import { Chess } from 'chess.js';
import { clampStartPly, previewPosition, solverOrientation } from './solver-setup.util';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('solver-setup.util', () => {
  it('klemmt den Vorspiel-Halbzug', () => {
    expect(clampStartPly(0, 4)).toBe(0);
    expect(clampStartPly(2, 4)).toBe(2);
    expect(clampStartPly(3, 4)).toBe(0);   // kein Löserzug mehr übrig
    expect(clampStartPly(-5, 4)).toBe(-1);
    expect(clampStartPly(-1, 4)).toBe(-1);
  });

  it('Blickrichtung: Gegenseite des Setup-Zugs, bei -1 die Seite am Zug', () => {
    const moves = ['e2e4', 'e7e5'];
    expect(solverOrientation(new Chess(START), moves, 0)).toBe('black');
    expect(solverOrientation(new Chess(START), moves, -1)).toBe('white');
  });

  it('Vorschau spielt das Vorspiel, lehnt kaputte FENs ab', () => {
    const p = previewPosition(START, 'e2e4 e7e5 g1f3 b8c6', 1)!;
    expect(p.fen.startsWith('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b')).toBeTrue();
    expect(p.orientation).toBe('white');    // Schwarz spielt den Setup-Zug e7e5 → Weiß löst
    expect(p.turnColor).toBe('black');
    expect(previewPosition('kaputt', 'e2e4 e7e5', 0)).toBeNull();
  });
});
