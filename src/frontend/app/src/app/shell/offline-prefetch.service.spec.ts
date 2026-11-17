import { of } from 'rxjs';
import { OfflinePrefetchService } from './offline-prefetch.service';
import { PUZZLE_POOL_KEY } from '../core/offline.service';
import { ENDLESS_CHAIN_BLOCK } from '../features/puzzles/endless-prefetch.util';

function make(opts: { loggedIn?: boolean; puzzleCount?: number; endlessCached?: number; config?: any } = {}) {
  const puzzleService = {
    getStats: jasmine.createSpy('getStats').and.returnValue(of({ puzzleElo: 1500 })),
    getAnonymousStats: jasmine.createSpy('getAnonymousStats').and.returnValue(of({ puzzleElo: 1200 })),
    getRatingRange: jasmine.createSpy('getRatingRange').and.returnValue(of({ min: 500, max: 2800 })),
    getRandomBatch: jasmine.createSpy('getRandomBatch').and.returnValue(of([{ id: 1 }])),
  } as any;
  const prefs = { visualization: 0, puzzleDifficulty: 'normal' } as any;
  const auth = { isLoggedIn: opts.loggedIn ?? false } as any;
  const offline = { puzzleCount: opts.puzzleCount ?? 0 } as any;
  const endlessStorage = {
    loadConfig: (d: any) => ({ ...d, ...(opts.config ?? {}) }),
    loadSessionHistory: () => [],
    loadOfflinePool: () => new Array(opts.endlessCached ?? (ENDLESS_CHAIN_BLOCK + 5)).fill({}),
    saveOfflinePool: jasmine.createSpy('saveOfflinePool'),
    saveChainSeed: jasmine.createSpy('saveChainSeed'),
  } as any;
  return { svc: new OfflinePrefetchService(puzzleService, prefs, auth, offline, endlessStorage), puzzleService, endlessStorage };
}

describe('OfflinePrefetchService', () => {
  it('does nothing when offline', () => {
    spyOnProperty(navigator, 'onLine', 'get').and.returnValue(false);
    const { svc, puzzleService } = make({ puzzleCount: 10 });
    svc.prefetchAll();
    expect(puzzleService.getRatingRange).not.toHaveBeenCalled();
    expect(puzzleService.getRandomBatch).not.toHaveBeenCalled();
  });

  it('skips the standard pool when enough is already cached', () => {
    spyOnProperty(navigator, 'onLine', 'get').and.returnValue(true);
    spyOn(localStorage, 'getItem').and.returnValue(JSON.stringify(new Array(10).fill({})));
    const { svc, puzzleService } = make({ puzzleCount: 10 });
    svc.prefetchAll();
    expect(puzzleService.getRandomBatch).not.toHaveBeenCalled();
  });

  it('fills the standard pool online when under target (anonymous → getAnonymousStats) and saves it', () => {
    spyOnProperty(navigator, 'onLine', 'get').and.returnValue(true);
    spyOn(localStorage, 'getItem').and.returnValue('[]');
    const setItem = spyOn(localStorage, 'setItem');
    const { svc, puzzleService } = make({ loggedIn: false, puzzleCount: 5 });
    svc.prefetchAll();
    expect(puzzleService.getAnonymousStats).toHaveBeenCalled();
    expect(puzzleService.getStats).not.toHaveBeenCalled();
    expect(puzzleService.getRandomBatch).toHaveBeenCalled();
    expect(setItem).toHaveBeenCalledWith(PUZZLE_POOL_KEY, jasmine.any(String));
  });

  it('refills the endless pool when below one gauntlet block', () => {
    spyOnProperty(navigator, 'onLine', 'get').and.returnValue(true);
    spyOn(localStorage, 'getItem').and.returnValue(JSON.stringify(new Array(999).fill({}))); // standard already full
    const { svc, endlessStorage } = make({ puzzleCount: 1, endlessCached: 0 });
    svc.prefetchAll();
    expect(endlessStorage.saveOfflinePool).toHaveBeenCalled();
  });

  it('sends the endless themes as themesAny (OR, like the live run), never as themes (AND)', () => {
    spyOnProperty(navigator, 'onLine', 'get').and.returnValue(true);
    spyOn(localStorage, 'getItem').and.returnValue(JSON.stringify(new Array(999).fill({})));
    const { svc, puzzleService } = make({ puzzleCount: 1, endlessCached: 0, config: { themes: ' fork pin skewer ' } });
    svc.prefetchAll();
    const args = puzzleService.getRandomBatch.calls.mostRecent().args;
    expect(args[1]).toBeUndefined();            // themes (UND)
    expect(args[3]).toBe('fork pin skewer');    // themesAny (ODER)
  });

  it('falls back to config.themes as themesAny when "weakest themes" is on (no list at hand)', () => {
    spyOnProperty(navigator, 'onLine', 'get').and.returnValue(true);
    spyOn(localStorage, 'getItem').and.returnValue(JSON.stringify(new Array(999).fill({})));
    const { svc, puzzleService } = make({ puzzleCount: 1, endlessCached: 0, config: { themes: 'mateIn2 fork', worstTags: true } });
    svc.prefetchAll();
    const args = puzzleService.getRandomBatch.calls.mostRecent().args;
    expect(args[1]).toBeUndefined();
    expect(args[3]).toBe('mateIn2 fork');
  });

  it('sends no theme filter when the endless config has none', () => {
    spyOnProperty(navigator, 'onLine', 'get').and.returnValue(true);
    spyOn(localStorage, 'getItem').and.returnValue(JSON.stringify(new Array(999).fill({})));
    const { svc, puzzleService } = make({ puzzleCount: 1, endlessCached: 0 });
    svc.prefetchAll();
    const args = puzzleService.getRandomBatch.calls.mostRecent().args;
    expect(args[1]).toBeUndefined();
    expect(args[3]).toBeUndefined();
  });
});
