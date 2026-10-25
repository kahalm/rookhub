import { of, throwError } from 'rxjs';
import { TrainerSrSync } from './repertoire-trainer-sr-sync';
import { LineStateDto } from './repertoire-training.service';
import { REPERTOIRE_OFFLINE_PREFIX } from '../../core/offline.service';

const NOW = Date.parse('2026-10-25T10:00:00Z');
const KEY = 'k1';
const OFFLINE_KEY = REPERTOIRE_OFFLINE_PREFIX + '5';

function st(extra: Partial<LineStateDto> = {}): LineStateDto {
  return { lineKey: KEY, level: 1, reps: 1, lapses: 0, dueAt: new Date(NOW - 1000).toISOString(), lastReviewedAt: null, inPool: true, paused: false, ...extra };
}

function make(training: any, offline = false) {
  const enqueue = jasmine.createSpy('enqueue');
  const sync = new TrainerSrSync({
    repertoireId: () => 5, training: () => training, queue: () => ({ enqueue }), isOffline: () => offline, now: () => NOW,
  });
  return { sync, enqueue };
}

function cachedStates(): LineStateDto[] {
  return JSON.parse(localStorage.getItem(OFFLINE_KEY)!).states;
}

describe('TrainerSrSync', () => {
  beforeEach(() => localStorage.setItem(OFFLINE_KEY, JSON.stringify({
    meta: { id: 5, name: 'R' }, pgn: '1. e4 *', states: [], config: null, savedAt: '2026-10-01T00:00:00Z',
  })));
  afterEach(() => localStorage.removeItem(OFFLINE_KEY));

  it('setStates/stateOf/clear', () => {
    const { sync } = make({});
    sync.setStates([st()]);
    expect(sync.stateOf(KEY)?.level).toBe(1);
    sync.clear();
    expect(sync.stateOf(KEY)).toBeUndefined();
  });

  it('review online: Server-Zustand übernehmen, in die Kopie spiegeln, Fälligkeit melden', () => {
    const answer = st({ level: 2, dueAt: '2026-10-27T10:00:00.000Z' });
    const reviewLine = jasmine.createSpy('reviewLine').and.returnValue(of(answer));
    const { sync, enqueue } = make({ reviewLine });
    const due = jasmine.createSpy('due');
    sync.review({ lineKey: KEY, label: 'L', correct: true }, due);
    expect(reviewLine).toHaveBeenCalledWith(5, { lineKey: KEY, label: 'L', correct: true });
    expect(due).toHaveBeenCalledWith('2026-10-27T10:00:00.000Z');
    expect(sync.stateOf(KEY)).toEqual(answer);
    expect(cachedStates()).toEqual([answer]);
    expect(enqueue).not.toHaveBeenCalled();
  });

  it('review scheitert: lokal rechnen (Stufe 1 → 2), Kopie, Offline-Queue, Fälligkeit', () => {
    const { sync, enqueue } = make({ reviewLine: () => throwError(() => new Error('504')) });
    sync.setStates([st()]);
    const due = jasmine.createSpy('due');
    const req = { lineKey: KEY, label: 'L', correct: true };
    sync.review(req, due);
    expect(sync.stateOf(KEY)!.level).toBe(2);
    expect(Date.parse(due.calls.mostRecent().args[0])).toBeGreaterThan(NOW);
    expect(enqueue).toHaveBeenCalledWith('POST', '/api/repertoires/5/training/line-review', req);
    expect(cachedStates()[0].level).toBe(2);
  });

  it('review offline: kein Server-Aufruf, ein falsches Urteil setzt auf Stufe 1', () => {
    const reviewLine = jasmine.createSpy('reviewLine');
    const { sync, enqueue } = make({ reviewLine }, true);
    sync.setStates([st({ level: 4 })]);
    sync.review({ lineKey: KEY, label: '', correct: false }, () => {});
    expect(reviewLine).not.toHaveBeenCalled();
    expect(sync.stateOf(KEY)!.level).toBe(1);
    expect(enqueue).toHaveBeenCalledTimes(1);
  });

  it('promoteLearned online: Zustand lokal sofort im Pool, Server-Aufruf, keine Queue', () => {
    const promote = jasmine.createSpy('promote').and.returnValue(of({ affected: 1 }));
    const { sync, enqueue } = make({ promote });
    sync.promoteLearned(KEY);
    expect(promote).toHaveBeenCalledWith(5, [KEY]);
    expect(sync.stateOf(KEY)!.inPool).toBeTrue();
    expect(cachedStates().length).toBe(1);
    expect(enqueue).not.toHaveBeenCalled();
  });

  it('promoteLearned: Fehler oder offline → Offline-Queue', () => {
    const failing = make({ promote: () => throwError(() => new Error('500')) });
    failing.sync.promoteLearned(KEY);
    expect(failing.enqueue).toHaveBeenCalledWith('POST', '/api/repertoires/5/training/promote', { lineKeys: [KEY] });
    const promote = jasmine.createSpy('promote');
    const off = make({ promote }, true);
    off.sync.promoteLearned(KEY);
    expect(promote).not.toHaveBeenCalled();
    expect(off.enqueue).toHaveBeenCalledWith('POST', '/api/repertoires/5/training/promote', { lineKeys: [KEY] });
  });

  it('eigene Intervalle der Offline-Kopie gehen vor die Vorgabe', () => {
    const { sync } = make({ reviewLine: () => throwError(() => new Error('x')) });
    sync.setSrLevels([{ value: 1, unit: 'h' }, { value: 3, unit: 'h' }, { value: 5, unit: 'h' }]);
    sync.setStates([st()]);
    let due = '';
    sync.review({ lineKey: KEY, label: '', correct: true }, d => due = d);
    expect(Date.parse(due) - NOW).toBe(3 * 3_600_000);
  });
});
