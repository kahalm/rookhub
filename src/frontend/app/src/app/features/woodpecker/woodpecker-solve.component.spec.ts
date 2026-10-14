import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { LongSolveService } from '../puzzles/long-solve.service';
import { BookPuzzleDto } from '../puzzles/puzzle.service';
import { StockfishService } from '../puzzles/stockfish.service';
import { WoodpeckerSolveComponent } from './woodpecker-solve.component';
import { WoodpeckerNext, WoodpeckerPlan, WoodpeckerService } from './woodpecker.service';

const plan = (over: Partial<WoodpeckerPlan> = {}): WoodpeckerPlan => ({
  id: 3, name: 'WP', createdAt: '', completedAt: null, books: [], setSize: 10, availableCount: 100,
  currentCycle: 2, maxCycles: 7, status: 'solving', nextCycleAt: null,
  cycles: [
    { number: 1, targetDays: 28, startedAt: '2026-09-01T00:00:00Z', deadline: '2026-09-29T00:00:00Z', finishedAt: '2026-09-20T00:00:00Z', attempted: 10, solved: 8, timeSeconds: 600 },
    { number: 2, targetDays: 14, startedAt: new Date().toISOString(), deadline: new Date(Date.now() + 14 * 86_400_000).toISOString(), finishedAt: null, attempted: 4, solved: 4, timeSeconds: 100 },
  ],
  ...over,
});

const puzzle: BookPuzzleDto = {
  id: 501, lineId: 'l', bookFileName: 'wp.pgn', bookTitle: 'WP', round: '1',
  fen: '6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1', moves: 'a1a8', startPly: -1,
};

describe('WoodpeckerSolveComponent', () => {
  let service: jasmine.SpyObj<WoodpeckerService>;

  function render(next: WoodpeckerNext) {
    service = jasmine.createSpyObj<WoodpeckerService>('WoodpeckerService', ['next', 'recordAttempt']);
    service.next.and.returnValue(of(next));
    service.recordAttempt.and.returnValue(of(plan({ cycles: [plan().cycles[0], { ...plan().cycles[1], attempted: 5 }] })));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [WoodpeckerSolveComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ planId: '3' }) } } },
        { provide: WoodpeckerService, useValue: service },
        { provide: StockfishService, useValue: { init: () => Promise.resolve(), getEval: () => Promise.resolve(''), stop: () => {} } },
        { provide: PreferencesService, useValue: { boardTheme: 'brown', pieceSet: 'cburnett', offPathWarnMoves: 0 } },
        { provide: LongSolveService, useValue: { resolve: (s: number) => of(s) } },
        { provide: SnackbarService, useValue: { warn: () => {}, info: () => {} } },
      ],
    });
    const fixture = TestBed.createComponent(WoodpeckerSolveComponent);
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
  }

  it('lädt die nächste Aufgabe des Plans und stellt sie aufs Brett', () => {
    const { c, el } = render({ puzzle, plan: plan() });
    expect(service.next).toHaveBeenCalledWith(3);
    expect(c.phase()).toBe('solving');
    expect(c.state).toBe('AWAITING_USER_MOVE');
    expect(el.querySelector('app-puzzle-board')).not.toBeNull();
  });

  it('Aufgeben meldet genau einen Fehlversuch, Wiederholen keinen weiteren', () => {
    const { c } = render({ puzzle, plan: plan() });
    c.giveUp();
    expect(service.recordAttempt).toHaveBeenCalledOnceWith(3, 501, false, jasmine.any(Number));
    c.retry();
    expect(c.repeatHint()).toBeTrue();
    c.giveUp();
    expect(service.recordAttempt).toHaveBeenCalledTimes(1);
  });

  it('richtiger Zug löst die Aufgabe und meldet sie als gelöst', () => {
    const { c } = render({ puzzle, plan: plan() });
    c.onMoveMade({ orig: 'a1', dest: 'a8' });
    expect(c.state).toBe('SOLVED');
    expect(service.recordAttempt).toHaveBeenCalledOnceWith(3, 501, true, jasmine.any(Number));
    expect(c.cycle()?.attempted).toBe(5);
  });

  it('keine Aufgabe mehr: Abschlusskarte mit Weg zur Übersicht', () => {
    const { el } = render({ puzzle: null, plan: plan({ status: 'break' }) });
    expect(el.querySelector('.done a')?.getAttribute('href')).toBe('/woodpecker');
    expect(el.querySelector('app-puzzle-board')).toBeNull();
  });
});
