import { of } from 'rxjs';
import { BookCommentHost, BookCommentView } from './book-comment-view';

const FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('BookCommentView (Zerlegung Z18)', () => {
  function host(over: Record<string, unknown> = {}): BookCommentHost {
    return {
      puzzle: { id: 1, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b', comment: 'Einleitung', moveComments: { '0': 'Gut.' } } as any,
      state: 'AWAITING_USER_MOVE', reviewMode: false, reviewIndex: 0, moveComment: null, onSolutionPath: true,
      moveIndex: 0, startPly: 0, titleDisplay: 'T', chapterDisplay: 'K', displayBookName: 'B',
      dialog: { open: jasmine.createSpy('open').and.returnValue({ afterClosed: () => of({ fen: 'f', from: 'e2', to: 'e4' }) }) } as any,
      variationPreview: null,
      ...over,
    } as BookCommentHost;
  }

  it('vor dem ersten Zug die Einleitung, im Review der Kommentar der Stelle', () => {
    expect(new BookCommentView(host()).lines).toEqual(['Einleitung']);
    expect(new BookCommentView(host({ reviewMode: true, reviewIndex: 1, moveComment: 'Gut.' })).display).toBe('Gut.');
    expect(new BookCommentView(host({ puzzle: null })).lines).toEqual([]);
  });

  it('Zug-Chip setzt die Brett-Vorschau; das Fenster spielt den gewählten Zug ebenso vor', () => {
    const h = host();
    const v = new BookCommentView(h);
    v.previewMove({ text: '', move: 'e4', fen: 'x', from: 'e2', to: 'e4' } as any);
    expect(h.variationPreview).toEqual({ fen: 'x', lastMove: ['e2', 'e4'] });
    v.exitPreview();
    expect(h.variationPreview).toBeNull();
    v.openDialog();
    expect((h.dialog.open as jasmine.Spy).calls.mostRecent().args[1].data.subtitle).toBe('K · B');
    expect(h.variationPreview!.fen).toBe('f');
  });
  // Woodpecker, Aufgabe 334 (Prod BookPuzzles 35193, gemeldet 2026-11-27): Spoilerschutz + Drohung als Nullzug.
  describe('Spoilerschutz und Drohung (Aufgabe 334)', () => {
    const P334 = {
      id: 35193, fen: '5r1k/p2q2p1/2n5/3r3p/1bQ2B2/1P3P2/P3R1PP/4RN1K w - - 0 1', moves: 'e2e8 d5d4 e8f8 b4f8 c4e6',
      bookFileName: 'b', moveComments: {},
      comment: '334. Jose Raul Capablanca - Boris Kostic, Havana (3) 1919 White already has two pawns, but Black '
        + 'threatens 1...Bxe1 and 1...Rd4. There is a solution to both of these threats:',
    } as any;
    const chips = (v: BookCommentView) => v.blocks.flat().filter(x => x.move);

    it('beim Lösen nur bisher gespielte Halbzüge, in Review/nach dem Lösen die ganze Linie', () => {
      expect(new BookCommentView(host({ puzzle: P334, startPly: -1, moveIndex: 0 })).spoilerPlayed).toBe(0);
      expect(new BookCommentView(host({ puzzle: P334, startPly: -1, moveIndex: 2, state: 'THINKING' })).spoilerPlayed).toBe(2);
      expect(new BookCommentView(host({ puzzle: P334, reviewMode: true })).spoilerPlayed).toBeUndefined();
      for (const state of ['SOLVED', 'FAILED', 'INFO', 'COURSE_DONE']) {
        expect(new BookCommentView(host({ puzzle: P334, state })).spoilerPlayed).withContext(state).toBeUndefined();
      }
    });

    it('Einleitung beim Lösen: beide Drohzüge ohne den Lösungszug 1.Re8; die Leiste kennt die Drohung', () => {
      const h = host({ puzzle: P334, startPly: -1, moveIndex: 0 });
      const v = new BookCommentView(h);
      const moves = chips(v);
      expect(moves.map(x => x.move)).toEqual(['1...Bxe1', '1...Rd4']);
      expect(moves.every(x => x.threat && x.fen!.startsWith('5r1k/'))).toBeTrue();   // e8 leer: kein 1.Re8
      v.previewMove(moves[1]);
      expect(h.variationPreview?.threat).toBeTrue();
    });

    it('Review an der Einleitung (volle Linie): weiterhin Drohung statt Lösungszug', () => {
      const v = new BookCommentView(host({ puzzle: P334, startPly: -1, reviewMode: true, reviewIndex: 0 }));
      expect(chips(v).every(x => x.threat && x.fen!.startsWith('5r1k/'))).toBeTrue();
    });

    it('Spoilerschutz: ein Kommentar-Zug, der nur hinter einem ungespielten Lösungszug geht, bleibt Text', () => {
      const p = { ...P334, comment: 'Then 2.Rxf8+ wins.', moveComments: { '1': 'Then 2.Rxf8+ wins.' } };
      expect(chips(new BookCommentView(host({ puzzle: p, startPly: -1, moveIndex: 0 })))).toEqual([]);
      // Nach 1.Re8 Rd4 (moveIndex 2) ist 2.Rxf8+ verankerbar.
      expect(chips(new BookCommentView(host({ puzzle: p, startPly: -1, moveIndex: 2, state: 'AWAITING_USER_MOVE' })))
        .map(x => x.move)).toEqual(['2.Rxf8+']);
      expect(chips(new BookCommentView(host({ puzzle: p, startPly: -1, state: 'SOLVED' }))).map(x => x.move)).toEqual(['2.Rxf8+']);
    });
  });
});
