import { of } from 'rxjs';
import { BookCommentHost, BookCommentView } from './book-comment-view';

const FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('BookCommentView (Zerlegung Z18)', () => {
  function host(over: Record<string, unknown> = {}): BookCommentHost {
    return {
      puzzle: { id: 1, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b', comment: 'Einleitung', moveComments: { '0': 'Gut.' } } as any,
      state: 'AWAITING_USER_MOVE', reviewMode: false, reviewIndex: 0, moveComment: null, onSolutionPath: true,
      solverMoveIndex: 0, solverStartPly: 0, titleDisplay: 'T', chapterDisplay: 'K', displayBookName: 'B',
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
});
