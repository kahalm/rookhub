import { fensOf, pliesOfPgn } from '@rh/features/games/game-edit.util';
import { GameEvals } from '@rh/features/games/game-review.util';
import { correctionCurve } from './scan-eval-curve';

describe('correctionCurve', () => {
  // 1.e4 e5 2.Qh5 Nc6 3.Qxf7#? — hier nur als Zickzack: 2…Nc6 verschenkt (+9), 3.Bc4 gibt es zurück (0).
  const pgn = '1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 *';
  const plies = pliesOfPgn(pgn);
  const saved = plies.map(p => p.san);
  const fens = fensOf(plies);
  const cps = [20, 30, 25, 20, 900, 0];
  const evals: GameEvals = {
    status: 'done', analyzed: 6, total: 6, targetDepth: 20,
    plies: cps.map((cp, ply) => ({ ply, cp, mate: null, depth: 20, playedUci: plies[ply].uci })),
    final: { cp: 0, mate: null },
  };

  it('zeichnet die ganze gespeicherte Partie und markiert den Fehler', () => {
    const c = correctionCurve(evals, saved, plies, fens)!;
    expect(c.curve.length).toBe(plies.length + 1);
    expect(c.curve.every(v => v != null)).toBeTrue();
    expect(c.same).toBe(6);
    expect(c.marks.some(m => m.ply === 3)).toBeTrue();      // 2…Nc6 verschenkt
  });

  it('bricht ab der ersten Änderung ab — die Analyse gehört zur gespeicherten Partie', () => {
    const changed = pliesOfPgn('1. e4 e5 2. Qh5 g6 *');
    const c = correctionCurve(evals, saved, changed, fensOf(changed))!;
    expect(c.same).toBe(3);
    expect(c.curve.length).toBe(changed.length + 1);
    expect(c.curve.slice(0, 3).every(v => v != null)).toBeTrue();
    expect(c.curve[4]).toBeNull();
  });

  it('ohne Analyse keine Kurve', () => {
    expect(correctionCurve(null, saved, plies, fens)).toBeNull();
    expect(correctionCurve({ ...evals, plies: [] }, saved, plies, fens)).toBeNull();
  });
});
