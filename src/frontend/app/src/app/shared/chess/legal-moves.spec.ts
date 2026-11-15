import { Chess } from 'chess.js';
import { MAX_POSITIONS, clearLegalMoveCache, legalMove, legalMoveByCoords, legalMoves, legalSans } from './legal-moves';

/** Ohne die Überladungen von chess.js — der Spion soll nur zählen. */
const PROTO = Chess.prototype as unknown as { moves: (...a: unknown[]) => unknown };
const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
/** Weißer Bauer auf e7 vor der Umwandlung, schwarzer Turm auf d8 zum Schlagen. */
const PROMO = '3r3k/4P3/8/8/8/8/8/4K3 w - - 0 1';

describe('legal-moves (gemerkte legale Züge je Stellung, 0.978.1)', () => {
  beforeEach(() => clearLegalMoveCache());

  it('SAN-Liste wie chess.js; jeder Zug stimmt mit der ausführlichen Liste von chess.js überein', () => {
    for (const fen of [START, PROMO, 'r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4']) {
      const verbose = new Chess(fen).moves({ verbose: true });
      expect(legalSans(fen)).toEqual(verbose.map(m => m.san));
      expect(legalMoves(fen)!.map(m => [m.san, m.from, m.to, m.piece, m.promotion, m.after]))
        .toEqual(verbose.map(m => [m.san, m.from, m.to, m.piece, m.promotion, m.after]));
    }
  });

  it('ein Zug nach SAN und nach Feldern; Umwandlung nur mit der Figur', () => {
    expect(legalMove(START, 'Nf3')).toEqual(jasmine.objectContaining({ from: 'g1', to: 'f3', piece: 'n',
      after: 'rnbqkbnr/pppppppp/8/8/8/5N2/PPPPPPPP/RNBQKB1R b KQkq - 1 1' }));
    expect(legalMove(START, 'Nf6')).toBeNull();
    expect(legalMoveByCoords(START, 'e2', 'e4')?.san).toBe('e4');
    expect(legalMoveByCoords(START, 'e2', 'e5')).toBeNull();
    expect(legalMoveByCoords(PROMO, 'e7', 'd8', 'n')?.san).toBe('exd8=N');
    expect(legalMoveByCoords(PROMO, 'e7', 'e8')).toBeNull();
  });

  it('unlesbare FEN: null statt Ausnahme', () => {
    expect(legalSans('kein fen')).toBeNull();
    expect(legalMove('kein fen', 'e4')).toBeNull();
    expect(legalMoves('kein fen')).toBeNull();
    expect(legalMoveByCoords('kein fen', 'e2', 'e4')).toBeNull();
  });

  it('erzeugt die Züge einer Stellung nur einmal und nie die ausführliche Liste', () => {
    const moves = spyOn(PROTO, 'moves').and.callThrough();
    legalSans(START); legalSans(START); legalMove(START, 'e4'); legalMove(START, 'e4'); legalMoveByCoords(START, 'e2', 'e4');
    expect(moves.calls.count()).toBe(1);
    expect(moves.calls.allArgs().some(a => a.length > 0)).toBeFalse();
  });

  it('der Speicher ist gedeckelt: voll heißt leeren, die Antworten bleiben richtig', () => {
    const moves = spyOn(PROTO, 'moves').and.callThrough();
    for (let i = 0; i <= MAX_POSITIONS; i++) legalSans(`8/8/8/8/8/8/8/K6k w - - ${i} 1`);
    expect(moves.calls.count()).toBe(MAX_POSITIONS + 1);
    expect(legalSans('8/8/8/8/8/8/8/K6k w - - 0 1')).toEqual(['Ka2', 'Kb2', 'Kb1']);   // war verdrängt → neu erzeugt
    expect(moves.calls.count()).toBe(MAX_POSITIONS + 2);
  });
});
