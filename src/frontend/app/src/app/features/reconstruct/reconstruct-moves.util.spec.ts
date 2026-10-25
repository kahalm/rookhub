import { PartKind, ReconstructionPart } from './reconstruct.service';
import {
  canCloseGap, fenMoveNumber, firstGap, gapMessageKey, gapTargetAt, hasProposalsBefore, moveNo, moveTokens, partState,
  partSummary, plyCountOf, positionOrientation, previewFen, recordedEndBefore, replayMoves, targetAfter, tokensUpTo,
  withSideToMove,
} from './reconstruct-moves.util';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const MIDDLE = 'r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3';

function part(over: Partial<ReconstructionPart>): ReconstructionPart {
  return {
    id: 1, ordinal: 0, kind: PartKind.Moves, moves: 'e4 e5', fen: null, fromPly: null,
    continuesPrevious: false, certain: true, blackToMove: false, generated: false, note: null, anchored: true, valid: true,
    startFen: START, endFen: START, plyCount: 2, startPly: 0, firstBadMove: null, mismatch: false, ...over,
  };
}

describe('reconstruct-moves.util', () => {
  it('withSideToMove stellt die Seite um und loescht das en-passant-Feld', () => {
    const ep = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2';
    expect(withSideToMove(ep, true)).toBe('rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2');
    expect(withSideToMove(START, false)).toBe(START);
    expect(withSideToMove('kaputt', true)).withContext('zu kurz: unveraendert').toBe('kaputt');
  });

  const tokenCases: [string, string[]][] = [
    ['', []],
    ['e4 e5', ['e4', 'e5']],
    ['1. e4 e5 2. Nf3', ['e4', 'e5', 'Nf3']],
    ['1.e4 e5 2.Nf3 Nc6 1-0', ['e4', 'e5', 'Nf3', 'Nc6']],
    ['12...Bxf3  *', ['Bxf3']],
    ['  d4\nd5 1/2-1/2 ', ['d4', 'd5']],
    ['1.d4 d5 2.c4 1/2', ['d4', 'd5', 'c4']],
  ];
  for (const [text, want] of tokenCases) {
    it(`moveTokens(${JSON.stringify(text)})`, () => expect(moveTokens(text)).toEqual(want));
  }

  it('tokensUpTo schneidet am Blaetter-Stand ab', () => {
    const t = ['e4', 'e5', 'Nf3'];
    expect(tokensUpTo(t, null)).toEqual(t);
    expect(tokensUpTo(t, 0)).toEqual([]);
    expect(tokensUpTo(t, 2)).toEqual(['e4', 'e5']);
    expect(tokensUpTo(t, 9)).toEqual(t);
    expect(tokensUpTo(t, -1)).toEqual([]);
  });

  it('replayMoves spielt nach und nennt den ersten unmoeglichen Zug', () => {
    expect(replayMoves(START, ['e4', 'e5']).bad).toBeNull();
    const bad = replayMoves(START, ['e4', 'Ke2']);
    expect(bad.bad).toBe('Ke2');
    expect(bad.fen).withContext('Stellung vor dem Fehler').toContain('4P3');
    expect(replayMoves('keine fen', ['e4'])).toEqual({ fen: START, bad: null });
  });

  it('Anzeige-Helfer', () => {
    expect([0, 1, 2, 9].map(moveNo)).toEqual([1, 1, 2, 5]);
    expect(plyCountOf(part({ plyCount: 7 }))).toBe(7);
    expect(plyCountOf(part({ generated: true, moves: 'e4  e5 Nf3', plyCount: 0 }))).toBe(3);
    expect(partSummary(part({ kind: PartKind.Position, fen: MIDDLE }))).toBe(MIDDLE);
    expect(partSummary(part({ moves: 'a b c d e f g h i j k l m' }))).toBe('a b c d e f g h i j k l …');
    expect(partState(part({ mismatch: true }))).toBe('mismatch');
    expect(partState(part({ anchored: false }))).toBe('floating');
    expect(partState(part({ valid: false }))).toBe('broken');
    expect(partState(part({}))).toBe('ok');
    expect(positionOrientation(part({ fen: MIDDLE }))).toBe('black');
    expect(positionOrientation(part({ fen: START }))).toBe('white');
    expect(fenMoveNumber(MIDDLE)).toBe(3);
    expect(fenMoveNumber(null)).toBe(1);
  });

  describe('Luecken', () => {
    const moves = part({ id: 1, ordinal: 0, endFen: MIDDLE });
    const gen1 = part({ id: 9, ordinal: 1, generated: true, endFen: null });
    const gen2 = part({ id: 10, ordinal: 2, generated: true, endFen: null });
    const target = part({ id: 2, ordinal: 3, kind: PartKind.Position, fen: MIDDLE, moves: null });
    const all = [moves, gen1, gen2, target];

    it('canCloseGap nur vor einer Stellung mit Luecke', () => {
      expect(canCloseGap(target, 0)).toBeFalse();
      expect(canCloseGap(target, 1)).toBeTrue();
      expect(canCloseGap({ ...target, continuesPrevious: true }, 1)).toBeFalse();
      expect(canCloseGap({ ...target, kind: PartKind.Moves }, 1)).toBeFalse();
    });

    it('Vorschlaege haengen am Teil danach und am Ende davor', () => {
      expect(targetAfter(all, gen1)).toBe(target);
      expect(recordedEndBefore(all, gen2)).toBe(MIDDLE);
      expect(recordedEndBefore(all, moves)).toBeNull();
      expect(hasProposalsBefore(all, target)).toBeTrue();
      expect(hasProposalsBefore(all, gen1)).toBeFalse();
    });

    it('gapTargetAt: die Zeile steht vor dem ERSTEN Vorschlag', () => {
      expect(gapTargetAt(all, all, 0)).toBeNull();
      expect(gapTargetAt(all, all, 1)).withContext('erster Vorschlag').toBe(target);
      expect(gapTargetAt(all, all, 2)).withContext('mitten in den Vorschlaegen').toBeNull();
      expect(gapTargetAt(all, all, 3)).toBeNull();
      const visible = [moves, target];
      expect(gapTargetAt(visible, all, 1)).withContext('Vorschlaege ausgeblendet').toBe(target);
      expect(gapTargetAt(visible, all, 5)).toBeNull();
      expect(firstGap(visible, all)).toBe(target);
      expect(firstGap([moves, { ...target, continuesPrevious: true }], all)).toBeNull();
    });

    it('previewFen nimmt das Ende des letzten aufgezeichneten Teils', () => {
      expect(previewFen([])).toBeNull();
      expect(previewFen([moves, gen1])).toBe(MIDDLE);
    });
  });

  it('gapMessageKey: je Grund ein eigener Satz', () => {
    const r = (reason: string | null, solutions = 0) =>
      ({ partId: 1, maxPlies: 4, nodes: 0, budgetExhausted: false, reason, solutions: new Array(solutions) });
    expect(gapMessageKey(null)).toBeNull();
    expect(gapMessageKey(r(null))).toBeNull();
    expect(gapMessageKey(r('budget', 1))).withContext('es gibt Wege').toBeNull();
    const table: [string, string][] = [
      ['unreachable', 'reconstruct.gap.unreachable'], ['too-far', 'reconstruct.gap.tooFar'],
      ['same-position', 'reconstruct.gap.samePosition'], ['budget', 'reconstruct.gap.budget'],
      ['no-anchor', 'reconstruct.gap.noAnchor'], ['no-gap', 'reconstruct.gap.noGap'],
      ['no-previous', 'reconstruct.gap.noPrevious'], ['target-not-a-position', 'reconstruct.gap.targetNotAPosition'],
      ['invalid-from', 'reconstruct.gap.invalidPosition'], ['invalid-to', 'reconstruct.gap.invalidPosition'],
      ['was-anderes', 'reconstruct.gap.none'],
    ];
    for (const [reason, key] of table) expect(gapMessageKey(r(reason))).withContext(reason).toBe(key);
  });
});
