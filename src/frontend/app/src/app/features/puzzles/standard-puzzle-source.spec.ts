import { of } from 'rxjs';
import { StandardPuzzleSource, StandardPuzzleFilters } from './standard-puzzle-source';
import { PUZZLE_POOL_KEY } from '../../core/offline.service';

const P = (id: number, rating = 1500) => ({ id, fen: '8/8/8/8/8/8/8/8 w - - 0 1', moves: 'a1a2', rating }) as any;

function make(filters: Partial<StandardPuzzleFilters> = {}) {
  const svc: any = {
    getById: jasmine.createSpy('getById').and.callFake((id: number) => of(P(id))),
    getRandom: jasmine.createSpy('getRandom').and.returnValue(of(P(99))),
    getRandomBatch: jasmine.createSpy('getRandomBatch').and.returnValue(of([P(5)])),
    getWorstThemes: jasmine.createSpy('getWorstThemes').and.returnValue(of(['fork', 'pin'])),
  };
  const offline: any = { puzzleCount: 0 };
  const f: StandardPuzzleFilters = {
    elo: () => 1500, difficulty: () => 'normal', excludeSolved: () => false,
    worstTagsEnabled: () => false, isLoggedIn: () => true, ...filters,
  };
  return { src: new StandardPuzzleSource(svc, offline, f), svc, offline };
}

describe('StandardPuzzleSource', () => {
  let onLine: PropertyDescriptor | undefined;
  beforeEach(() => {
    onLine = Object.getOwnPropertyDescriptor(navigator, 'onLine');
    try { localStorage.removeItem(PUZZLE_POOL_KEY); } catch { /* ignore */ }
  });
  afterEach(() => {
    if (onLine) Object.defineProperty(navigator, 'onLine', onLine);
    else Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => true });
    try { localStorage.removeItem(PUZZLE_POOL_KEY); } catch { /* ignore */ }
  });
  const setOnline = (v: boolean) => Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => v });

  it('pick: Deep-Link-Id vor vorab geladenem Puzzle', () => {
    const { src, svc } = make();
    src.nextPuzzle = P(7);
    let got: any;
    src.pick(42)!.subscribe(p => got = p);
    expect(svc.getById).toHaveBeenCalledWith(42);
    expect(got.id).toBe(42);
    expect(src.nextPuzzle?.id).toBe(7);
  });

  it('pick: vorab geladenes Puzzle wird genau einmal verbraucht', () => {
    const { src, svc } = make();
    src.nextPuzzle = P(7);
    let got: any;
    src.pick(null)!.subscribe(p => got = p);
    expect(got.id).toBe(7);
    expect(src.nextPuzzle).toBeNull();
    expect(svc.getRandom).not.toHaveBeenCalled();
  });

  it('pick offline: aus dem Pool, leerer Pool → null', () => {
    setOnline(false);
    const { src } = make();
    src.offlinePuzzlePool = [P(3, 1500)];
    let got: any;
    src.pick(null)!.subscribe(p => got = p);
    expect(got.id).toBe(3);
    expect(src.offlinePuzzlePool.length).toBe(0);
    expect(src.pick(null)).toBeNull();
  });

  it('pick online: Zufallspuzzle im Rating-Fenster mit den Filtern', () => {
    setOnline(true);
    const { src, svc } = make({ excludeSolved: () => true });
    src.pick(null);
    expect(svc.getRandom).toHaveBeenCalledWith(1400, 1600, undefined, true, undefined);
  });

  it('discardForFilterChange verwirft Vorab-Puzzle, Pool und laufende Vorab-Antworten', () => {
    setOnline(true);
    const { src, svc } = make();
    let emit: (p: any) => void = () => {};
    svc.getRandom.and.returnValue({ subscribe: (h: any) => { emit = h.next; } });
    src.prefetchNext();
    src.nextPuzzle = P(1);
    src.offlinePuzzlePool = [P(2)];
    src.discardForFilterChange();
    emit(P(3));
    expect(src.nextPuzzle).toBeNull();
    expect(src.offlinePuzzlePool).toEqual([]);
  });

  it('schwächste Themen: nur aktiv + angemeldet geladen, dann als Parameter', () => {
    let enabled = false;
    const { src, svc } = make({ worstTagsEnabled: () => enabled });
    const cb = jasmine.createSpy('cb');
    src.ensureWorstThemes(cb);
    expect(svc.getWorstThemes).not.toHaveBeenCalled();
    expect(src.worstThemesParam).toBeUndefined();
    enabled = true;
    src.ensureWorstThemes(cb);
    expect(cb).toHaveBeenCalledTimes(2);
    expect(src.worstThemesParam).toBe('fork pin');
    expect(src.activeWorstThemes).toEqual(['fork', 'pin']);
  });
});
