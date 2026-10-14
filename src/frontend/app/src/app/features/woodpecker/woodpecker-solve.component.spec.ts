import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
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

  function render(next: WoodpeckerNext | Observable<WoodpeckerNext>) {
    service = jasmine.createSpyObj<WoodpeckerService>('WoodpeckerService', ['next', 'recordAttempt']);
    service.next.and.returnValue(next instanceof Observable ? next : of(next));
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

  // c1-wp-loeser-404-verbindung: fremdes/gelöschtes Training ist kein Verbindungsfehler.
  for (const status of [404, 403]) {
    it(`${status}: „gibt es nicht (mehr)“ + „Zur Übersicht“, kein „Erneut versuchen“`, () => {
      const { c, el } = render(throwError(() => new HttpErrorResponse({ status })));
      expect(c.phase()).toBe('missing');
      expect(el.querySelector('.missing p')?.textContent).toContain('woodpecker.notFound');
      expect(el.querySelector('.missing a')?.getAttribute('href')).toBe('/woodpecker');
      expect(el.querySelector('app-load-error')).toBeNull();
    });
  }

  it('Netz/5xx bleibt Ladefehler mit „Erneut versuchen“', () => {
    const { c, el } = render(throwError(() => new HttpErrorResponse({ status: 0 })));
    expect(c.phase()).toBe('error');
    expect(el.querySelector('app-load-error')).not.toBeNull();
  });
});

/** Kompiliertes CSS ohne Leerraum und ohne %NS%-Platzhalter (Muster features/page-width.spec.ts). */
const solveCss = () =>
  (((WoodpeckerSolveComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n')
    .replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s/g, '');

describe('WoodpeckerSolveComponent — Layout (UI-Sweep c1)', () => {
  it('c1-wp-loeser-ultrawide-brett: ab 2400 px breite Seite, Brett bis Fensterhöhe, Seitenspalte 360–400 px', () => {
    const css = solveCss();
    const at = css.indexOf('@media(min-width:2400px)');
    expect(at).toBeGreaterThan(-1);
    const wide = css.slice(at);
    expect(wide).toContain('.page{max-width:min(var(--page-max-width-wide,1880px),96vw)');
    expect(wide).toMatch(/\.solver\{justify-content:center;grid-template-columns:minmax\(0,(calc\()?100vh-160px\)?\)minmax\(360px,400px\)/);
    expect(wide).toMatch(/\.board-section\{width:min\(100%,(calc\()?100vh-160px\)?\)/);
    // Darunter ändert sich nichts: das breite Token kommt außerhalb der Media-Query nicht vor.
    expect(css.slice(0, at)).not.toContain('--page-max-width-wide');
  });

  it('c1-wp-woodpecker-seitenpolster-96vw: Seite ohne 96vw, Rand = 16-px-Polster', () => {
    const page = /\.page\{[^}]*\}/.exec(solveCss())![0];
    expect(page).toContain('max-width:var(--page-max-width)');
    expect(page).not.toContain('96vw');
    expect(page).toContain('padding:8px16px16px');
  });
});
