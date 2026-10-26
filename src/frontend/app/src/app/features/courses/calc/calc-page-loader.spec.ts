import { Subject, of, throwError } from 'rxjs';
import { CalcPageLoader, CalcPageLoaderHost, calcStartFen } from './calc-page-loader';
import { CalcChapterModel } from './calc-chapter-model';
import { CalcPosition } from './calculation.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const pos = (over: Partial<CalcPosition> = {}) => ({
  id: 7, bookId: 1, round: '1', title: null, chapter: null, fen: START, setupMoves: '', comment: null,
  treeJson: null, treeUpdatedAt: null, ...over,
}) as CalcPosition;

describe('calcStartFen', () => {
  it('spielt den Vorlauf ein; unspielbar oder illegal → Header-FEN und gesperrt', () => {
    expect(calcStartFen(pos({ setupMoves: 'e2e4' })).fen).toContain('4P3');
    expect(calcStartFen(pos({ setupMoves: 'e2e5' }))).toEqual({ fen: START, illegal: true });
    expect(calcStartFen(pos({ fen: 'kein fen' })).illegal).toBeTrue();
    expect(calcStartFen(pos()).illegal).toBeFalse();
  });
});

describe('CalcPageLoader', () => {
  function make(backend: Record<string, unknown>) {
    const applied: CalcPosition[] = [];
    const host: CalcPageLoaderHost = {
      backend: () => backend as any, bookId: () => 1, lang: () => 'de', chapters: new CalcChapterModel(),
      enterChapter: jasmine.createSpy('enterChapter'), applyPosition: p => applied.push(p),
      stopTraining: jasmine.createSpy('stopTraining'), track: () => {},
    };
    return { loader: new CalcPageLoader(host), host, applied };
  }

  it('leeres Buch: fertig geladen, kein Kapitel', () => {
    const { loader, host } = make({ getBook: () => of({ bookId: 1, displayName: 'B', isCalculation: true, positions: [] }) });
    loader.loadBook(null);
    expect(loader.loading).toBeFalse();
    expect(loader.book?.bookId).toBe(1);
    expect(host.enterChapter).not.toHaveBeenCalled();
  });

  it('eine überholte Antwort wird verworfen; Fehler ohne Stellung stoppt das Training', () => {
    const first = new Subject<CalcPosition>();
    const queue: unknown[] = [first, of(pos({ id: 8 }))];
    const { loader, applied, host } = make({ getPosition: () => queue.shift() });
    loader.loadPosition(7);
    loader.loadPosition(8);
    first.next(pos({ id: 7 }));
    expect(applied.map(p => p.id)).toEqual([8]);
    expect(loader.position?.id).toBe(8);

    const failing = make({ getPosition: () => throwError(() => new Error('502')) });
    failing.loader.loadPosition(9);
    expect(failing.loader.loadError).toBeTrue();
    expect(failing.loader.position).toBeNull();
    expect(failing.host.stopTraining).toHaveBeenCalled();
    expect(host.stopTraining).not.toHaveBeenCalled();
  });
});
