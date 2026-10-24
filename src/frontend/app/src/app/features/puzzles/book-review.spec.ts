import { BookReviewController } from './book-review';

const FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
/** Muster-Diagramm ohne Könige — chess.js verwirft es. */
const ILLEGAL = '8/8/8/8/8/8/4P3/8 w - - 0 1';

describe('BookReviewController (Zerlegung Z18)', () => {
  const line = { fen: FEN, moves: 'e2e4 e7e5 g1f3', moveComments: { '0': 'Eröffnung', '2': 'Springer' } };

  it('total: ganze Partie vs. Lösung ab Trainingsstart', () => {
    const r = new BookReviewController();
    expect(r.total(null, false, 0)).toBe(0);
    expect(r.total(line, false, 1)).toBe(3);
    expect(r.total(line, true, 1)).toBe(2);
  });

  it('fullGameFrame: Stellung, letzter Zug und der zuletzt gesehene Kommentar', () => {
    const r = new BookReviewController();
    const f = r.fullGameFrame(line, 2);
    expect(f.index).toBe(2);
    expect(f.lastMove).toEqual(['e7', 'e5']);
    expect(f.turnColor).toBe('white');
    expect(f.moveComment).toBe('Eröffnung');   // Zug 2 ohne Kommentar → der von Zug 1 bleibt
    expect(r.fullGameFrame(line, 99).index).toBe(3);   // geklemmt
  });

  it('solutionFrame: Vorspiel still, index zählt ab dem Trainingsstart', () => {
    const r = new BookReviewController();
    const f = r.solutionFrame(line, 1, 2);
    expect(f.index).toBe(2);
    expect(f.lastMove).toEqual(['g1', 'f3']);
    expect(f.fen).toContain('5N2');
    expect(f.moveComment).toBe('Springer');
    expect(r.solutionFrame(line, 1, 0).moveComment).toBeNull();   // vor dem ersten Lösungszug kein Zug-Kommentar
  });

  it('illegale Diagramm-FEN: per Koordinaten nachgespielt, absoluter Index', () => {
    const r = new BookReviewController();
    const f = r.solutionFrame({ fen: ILLEGAL, moves: 'e2e4', moveComments: {} }, 0, 1);
    expect(f.index).toBe(1);
    expect(f.fen.split(' ')[0]).toBe('8/8/8/8/4P3/8/8/8');
    expect(f.isCheck).toBeFalse();
    expect(r.fullGameFrame({ fen: ILLEGAL, moves: 'e2e4', moveComments: {} }, 1).fen.split(' ')[0]).toBe('8/8/8/8/4P3/8/8/8');
  });

  it('Pfeile je gespieltem Halbzug aus moveShapes', () => {
    const r = new BookReviewController();
    r.setShapes(JSON.stringify({ '1': [{ o: 'd8', d: 'g5', b: 'green' }] }));
    expect(r.fullGameFrame(line, 2).shapes.length).toBe(1);
    expect(r.fullGameFrame(line, 1).shapes).toEqual([]);
    r.setShapes(null);
    expect(r.shapesForPlyPlayed(1)).toEqual([]);
  });
});
