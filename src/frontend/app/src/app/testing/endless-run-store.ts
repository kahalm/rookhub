import { Injector, runInInjectionContext } from '@angular/core';
import { AuthService } from '../core/auth.service';
import { EndlessChainService } from '../features/puzzles/endless-chain.service';
import { ENDLESS_RUN_CLOCK, EndlessRunStore } from '../features/puzzles/endless-run.store';
import { EndlessRunClock, realRunClock } from '../features/puzzles/endless-run-timer';
import { EndlessStorageService } from '../features/puzzles/endless-storage.service';
import { PuzzleService } from '../features/puzzles/puzzle.service';

/** {@link EndlessRunStore} ohne TestBed aus Test-Doppeln (eigener Injector je Aufruf). Der Ketten-Dienst ist echt und
 *  läuft über dieselbe `puzzleService`-Instanz — spätere Neuzuweisungen von `getRandomBatch` wirken durch ihn hindurch. */
export function makeEndlessRunStore(deps: {
  storage: unknown; puzzleService: unknown; auth: unknown; clock?: EndlessRunClock; chainService?: EndlessChainService;
}): EndlessRunStore {
  const chainService = deps.chainService ?? new EndlessChainService(deps.puzzleService as PuzzleService);
  const injector = Injector.create({
    providers: [
      { provide: EndlessStorageService, useValue: deps.storage },
      { provide: PuzzleService, useValue: deps.puzzleService },
      { provide: EndlessChainService, useValue: chainService },
      { provide: AuthService, useValue: deps.auth },
      { provide: ENDLESS_RUN_CLOCK, useValue: deps.clock ?? realRunClock },
    ],
  });
  return runInInjectionContext(injector, () => new EndlessRunStore());
}
