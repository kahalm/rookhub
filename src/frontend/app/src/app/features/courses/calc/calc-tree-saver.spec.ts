import { Observable, Subject, of, throwError } from 'rxjs';
import { CalcSaveClock, CalcTreeSaver } from './calc-tree-saver';
import { CalcTree, addMove, createTree } from './calc-tree.util';
import { CalcPosition } from './calculation.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

function pos(id: number, over: Partial<CalcPosition> = {}): CalcPosition {
  return { id, bookId: 1, round: '1', title: null, chapter: null, fen: START, setupMoves: '', comment: null,
    treeJson: null, treeUpdatedAt: null, ...over } as CalcPosition;
}

function fakeClock() {
  let run: (() => void) | null = null;
  const clock: CalcSaveClock & { fire(): void; pending(): boolean } = {
    setTimeout: fn => { run = fn; return 1; },
    clearTimeout: () => { run = null; },
    fire: () => { const r = run; run = null; r?.(); },
    pending: () => run !== null,
  };
  return clock;
}

describe('CalcTreeSaver', () => {
  function make(saveTree: (id: number, json: string) => Observable<{ bookPuzzleId: number; updatedAt: string }>) {
    const log: string[] = [];
    let tree: CalcTree = createTree(START);
    let current: number | null = 7;
    const clock = fakeClock();
    const saver = new CalcTreeSaver({
      backend: () => ({ saveTree, deleteTree: (id: number) => { log.push('delete:' + id); return of(undefined); } }),
      currentId: () => current,
      tree: () => tree,
      markDone: (id, done) => log.push(`done:${id}:${done}`),
      onSaved: () => log.push('saved'),
      onFailed: () => log.push('failed'),
      clock,
    });
    return {
      saver, log, clock,
      setTree: (t: CalcTree) => { tree = t; },
      setCurrent: (id: number | null) => { current = id; },
    };
  }
  const ok = (id: number) => of({ bookPuzzleId: id, updatedAt: '2026-07-28T10:00:00Z' });
  function withMove(): CalcTree {
    const t = createTree(START);
    addMove(t, t.rootId, { san: 'e4', uci: 'e2e4', fen: 'x' });
    return t;
  }

  it('Autosave: entprellt, speichert nach dem Takt und setzt savedAt', () => {
    const sent: number[] = [];
    const { saver, log, clock, setTree } = make((id) => { sent.push(id); return ok(id); });
    saver.reset(pos(7));
    setTree(withMove());
    saver.markDirty();
    saver.markDirty();
    expect(sent).toEqual([]);
    clock.fire();
    expect(sent).toEqual([7]);
    expect(log).toEqual(['saved', 'done:7:true']);
    expect(saver.savedAt?.toISOString()).toBe('2026-07-28T10:00:00.000Z');
    expect(saver.saving).toBeFalse();
  });

  it('ohne Änderung wird nichts gesendet; geleerter gespeicherter Baum wird gelöscht', () => {
    const { saver, log } = make(ok);
    saver.reset(pos(7, { treeJson: '{}', treeUpdatedAt: '2026-07-27T10:00:00Z' }));
    saver.flush();
    expect(log).toEqual([]);
    saver.markDirty();
    saver.flush();
    expect(log).toEqual(['done:7:false', 'delete:7']);
  });

  it('Fehlschlag: meldet, kein sofortiger Neuversuch, nächster flush schickt denselben Stand', () => {
    let fail = true;
    const sent: string[] = [];
    const { saver, log, setTree } = make((id, json) => {
      sent.push(json);
      if (fail) { fail = false; return throwError(() => new Error('x')); }
      return ok(id);
    });
    saver.reset(pos(7));
    setTree(withMove());
    saver.markDirty();
    saver.flush();
    expect(log).toEqual(['failed']);
    saver.flush();
    expect(sent.length).toBe(2);
    expect(sent[1]).toBe(sent[0]);
  });

  it('späte Antwort der alten Stellung lässt die neue Anzeige in Ruhe', () => {
    const late = new Subject<{ bookPuzzleId: number; updatedAt: string }>();
    const { saver, setTree, setCurrent } = make(() => late);
    saver.reset(pos(7));
    setTree(withMove());
    saver.markDirty();
    saver.flush();
    expect(saver.saving).toBeTrue();
    setCurrent(8);
    saver.reset(pos(8));
    late.next({ bookPuzzleId: 7, updatedAt: '2026-07-28T10:00:00Z' });
    expect(saver.savedAt).toBeNull();
    expect(saver.saving).toBeFalse();
  });
});
