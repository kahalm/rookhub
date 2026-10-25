import { Observable, Subject, of, throwError } from 'rxjs';
import { CalcReviewController } from './calc-review.controller';
import { CalcReviewPatch } from './calc-review.util';
import { CalcPosition, CalcPositionListItem, CalcReviewSaved } from './calculation.service';

function item(id: number, over: Partial<CalcPositionListItem> = {}): CalcPositionListItem {
  return { id, round: String(id), title: null, chapter: null, hasTree: false,
    chosenSan: null, chosenUci: null, secondsSpent: 0, grade: null, ...over };
}

function pos(id: number, over: Partial<CalcPosition> = {}): CalcPosition {
  return { id, bookId: 1, round: String(id), title: null, chapter: null, fen: '8/8/8/8/8/8/8/K6k w - - 0 1',
    setupMoves: '', comment: null, treeJson: null, treeUpdatedAt: null, ...over } as CalcPosition;
}

describe('CalcReviewController', () => {
  function make(send: (id: number, p: CalcReviewPatch) => Observable<CalcReviewSaved>) {
    const items = [item(7, { secondsSpent: 10, grade: 2 }), item(8)];
    let current: number | null = 7;
    const log: string[] = [];
    const ctrl = new CalcReviewController({
      items: () => items, currentId: () => current, send,
      onChanged: () => log.push('changed'), onSaved: () => log.push('saved'), onSaveFailed: () => log.push('failed'),
    });
    return { ctrl, items, log, setCurrent: (id: number | null) => { current = id; } };
  }
  const echo = (id: number, p: CalcReviewPatch): Observable<CalcReviewSaved> =>
    of({ bookPuzzleId: id, chosenSan: p.chosenSan ?? null, chosenUci: p.chosenUci ?? null, secondsSpent: 99, grade: p.grade ?? null });

  it('load nimmt Werte der Einzelstellung vor denen der Zeile und spiegelt sie zurück', () => {
    const { ctrl, items } = make(echo);
    ctrl.load(pos(7, { grade: 4 }));
    expect(ctrl.review).toEqual({ chosenSan: null, chosenUci: null, secondsSpent: 10, grade: 4 });
    expect(items[0].grade).toBe(4);
  });

  it('set zeigt optimistisch, meldet die Änderung und übernimmt die Server-Antwort', () => {
    const { ctrl, items, log } = make(echo);
    ctrl.load(pos(7));
    ctrl.set({ grade: 3 });
    expect(log).toEqual(['changed', 'saved', 'changed']);
    expect(ctrl.review.grade).toBe(3);
    expect(ctrl.review.secondsSpent).toBe(99);
    expect(items[0].secondsSpent).toBe(99);
  });

  it('ohne angezeigte Stellung passiert nichts', () => {
    const sent: number[] = [];
    const { ctrl, setCurrent } = make((id, p) => { sent.push(id); return echo(id, p); });
    setCurrent(null);
    ctrl.set({ grade: 1 });
    expect(sent).toEqual([]);
  });

  it('Zeit an einer nicht angezeigten Stellung: geht mit Marke raus, die Anzeige bleibt', () => {
    const sent: CalcReviewPatch[] = [];
    const { ctrl } = make((id, p) => { sent.push(p); return echo(id, p); });
    ctrl.load(pos(7));
    ctrl.creditSeconds(8, 30, 'tok');
    expect(sent).toEqual([{ secondsDelta: 30, secondsToken: 'tok' }]);
    expect(ctrl.review.secondsSpent).toBe(10);
  });

  it('eine noch wartende Änderung wird von der Antwort nicht überschrieben', () => {
    const first = new Subject<CalcReviewSaved>();
    let n = 0;
    const { ctrl } = make((id, p) => (++n === 1 ? first : echo(id, p)));
    ctrl.load(pos(7));
    ctrl.set({ grade: 1 });
    ctrl.set({ grade: 4 });                        // wartet
    first.next({ bookPuzzleId: 7, chosenSan: null, chosenUci: null, secondsSpent: 0, grade: 1 });
    // Antwort auf grade 1 kam, grade 4 wartete → Anzeige blieb bei 4, dann ging 4 raus und kam zurück.
    expect(ctrl.review.grade).toBe(4);
  });

  it('Fehlschlag: meldet und schickt beim nächsten flush denselben Stand', () => {
    let fail = true;
    const sent: CalcReviewPatch[] = [];
    const { ctrl, log } = make((id, p) => {
      sent.push(p);
      if (fail) { fail = false; return throwError(() => new Error('x')); }
      return echo(id, p);
    });
    ctrl.load(pos(7));
    ctrl.set({ grade: 2 });
    expect(log).toContain('failed');
    ctrl.flush();
    expect(sent).toEqual([{ grade: 2 }, { grade: 2 }]);
  });
});
