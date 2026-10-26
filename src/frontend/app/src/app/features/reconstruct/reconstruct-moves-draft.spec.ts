import { ReconstructMovesDraft } from './reconstruct-moves-draft';
import { START_FEN } from '../analysis/position-setup.component';

describe('ReconstructMovesDraft', () => {
  it('spielt die Züge nach und meldet den ersten unmöglichen', () => {
    const d = new ReconstructMovesDraft();
    d.setText('1. e4 e5 2. Nf3');
    expect(d.plyTotal()).toBe(3);
    expect(d.boardFen()).toContain('5N2');
    expect(d.badMove()).toBeNull();
    expect(d.playable()).toBeTrue();
    d.setText('e4 Qh9');
    expect(d.badMove()).toBe('Qh9');
    expect(d.playable()).toBeFalse();
  });

  it('blättern, abschneiden, anhängen, zurücknehmen', () => {
    const d = new ReconstructMovesDraft();
    d.setText('e4 e5 Nf3');
    d.ply.set(1);
    expect(d.plyShown()).toBe(1);
    expect(d.atEnd()).toBeFalse();
    d.truncateHere();
    expect(d.text).toBe('e4');
    expect(d.atEnd()).toBeTrue();
    d.append('c5');
    expect(d.text).toBe('e4 c5');
    d.undoMove();
    expect(d.text).toBe('e4');
  });

  it('Seite am Zug umstellen löst den Anker', () => {
    const d = new ReconstructMovesDraft();
    expect(d.startFen()).toBe(START_FEN);
    expect(d.sideBlack()).toBeFalse();
    d.setSideBlack(true);
    expect(d.sideBlack()).toBeTrue();
    expect(d.anchored()).toBeFalse();
  });
});
