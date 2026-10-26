import { of, throwError } from 'rxjs';
import { sendStandardAttempt, StandardAttemptDeps } from './standard-attempt';
import { PuzzleRevengeRound } from './puzzle-revenge-round';

describe('sendStandardAttempt', () => {
  const attempt = { id: 5, solved: true, seconds: 12, moveLog: [], visualizationLevel: 1, evalShown: false, vizShowCount: 0, hintsUsed: 2 };

  function deps(over: Partial<StandardAttemptDeps> = {}): StandardAttemptDeps & { stats: unknown[] } {
    const stats: unknown[] = [];
    return {
      stats,
      puzzleService: {
        ensureSessionId: () => 'sess', recordAnonymousAttempt: jasmine.createSpy('anon').and.returnValue(of({})),
        getAnonymousStats: () => of({ puzzleElo: 1500 }),
      } as any,
      offlineQueue: { enqueue: jasmine.createSpy('enqueue') } as any,
      challengeService: {} as any, revengeService: {} as any,
      loggedIn: false, revenge: new PuzzleRevengeRound({} as any),
      visualizationMode: () => 1, onEloChange: () => {}, onStats: s => stats.push(s),
      ...over,
    };
  }

  it('anonym: meldet mit Tipp-Stufe und lädt die anonyme Statistik', () => {
    const d = deps();
    sendStandardAttempt(d, attempt);
    expect((d.puzzleService.recordAnonymousAttempt as jasmine.Spy).calls.mostRecent().args).toEqual([5, true, 12, undefined, 1, false, 0, 2]);
    expect(d.stats.length).toBe(1);
  });

  it('anonym mit Fehler: Versuch samt Sitzung in die Offline-Schlange', () => {
    const d = deps();
    (d.puzzleService.recordAnonymousAttempt as jasmine.Spy).and.returnValue(throwError(() => new Error('x')));
    sendStandardAttempt(d, attempt);
    const [method, url, body] = (d.offlineQueue.enqueue as jasmine.Spy).calls.mostRecent().args;
    expect(method).toBe('POST');
    expect(url).toBe('/api/puzzles/5/attempt/anonymous');
    expect(body.sessionId).toBe('sess');
    expect(body.hintsUsed).toBe(2);
  });
});
