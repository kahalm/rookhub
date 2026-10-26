import { NO_ERRORS_SCHEMA, Type } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { provideTranslateService, TranslatePipe } from '@ngx-translate/core';
import { of } from 'rxjs';
import { BookPuzzleComponent } from './book-puzzle.component';
import { BookCommentBlockComponent } from './book-comment-block.component';
import { shallowBookPuzzle } from '../../testing/book-puzzle-shallow';
import { PuzzleService } from './puzzle.service';
import { StockfishService } from './stockfish.service';
import { PreferencesService } from '../../core/preferences.service';
import { CourseService } from '../courses/course.service';
import { WeeklyService } from '../weekly/weekly.service';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { ChallengeService } from '../../core/challenge.service';
import { LongSolveService } from './long-solve.service';
import { FavoritesService } from '../../core/favorites.service';
import { SolveModeService } from '../../core/solve-mode.service';
import { WorksheetService } from '../worksheets/worksheet.service';

/**
 * UI-Sweep b1, Kurs-Löser (Kontext-Card + Kommentar): eigene Datei, damit die über 2 000 Zeilen lange
 * book-puzzle.component.spec.ts nicht weiter wächst. Gerendert mit ausgeblendeten Kindkomponenten — Eingaben landen
 * als DOM-Eigenschaft am Element.
 */
const FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

function render(setup: (c: any) => void): HTMLElement {
  const prefs: any = {
    boardTheme: 'green', pieceSet: 'cburnett', themeMode: 'fixed', stockfishDepth: 12, bookStockfishDepth: 12,
    visualization: 0, vizArrow: true, offPathWarnMoves: 3, enPassantForced: true,
  };
  const route: any = {
    snapshot: { paramMap: { get: () => null }, queryParamMap: { get: () => null } },
    paramMap: { subscribe: () => ({ unsubscribe() {} }) },
  };
  const solveMode: any = {
    get: () => 'training', set: () => {}, clear: () => {}, ensure: () => of('training'),
    levelFor: () => 1, modeForLevel: () => 'training',
  };
  TestBed.configureTestingModule({
    imports: [BookPuzzleComponent],
    providers: [
      provideTranslateService({ fallbackLang: 'en' }),
      { provide: PuzzleService, useValue: {} },
      { provide: StockfishService, useValue: { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } },
      { provide: PreferencesService, useValue: prefs },
      { provide: ActivatedRoute, useValue: route },
      { provide: MatDialog, useValue: {} },
      { provide: CourseService, useValue: {} },
      { provide: WeeklyService, useValue: {} },
      { provide: Router, useValue: { navigate: jasmine.createSpy('navigate'), url: '/courses/7/sequential' } },
      { provide: AuthService, useValue: { isLoggedIn: true } },
      { provide: SnackbarService, useValue: { info: () => {} } },
      { provide: OfflineQueueService, useValue: { enqueue: () => {} } },
      { provide: ChallengeService, useValue: {} },
      { provide: LongSolveService, useValue: { resolve: (s: number) => of(s) } },
      { provide: FavoritesService, useValue: { contains: () => of(false), add: () => of(true), remove: () => of(false), count: () => of(0), list: () => of([]) } },
      { provide: SolveModeService, useValue: solveMode },
      { provide: WorksheetService, useValue: {} },
    ],
  });
  shallowBookPuzzle();
  const fixture = TestBed.createComponent(BookPuzzleComponent);
  const c: any = fixture.componentInstance;
  c.puzzle = { id: 42, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
  setup(c);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const nextButtons = (el: Element) =>
  Array.from(el.querySelectorAll('button')).filter(b => b.textContent?.includes('solver.actions.nextPuzzle'));

// b1-rk-seq-doppelt-naechstes-puzzle (DESIGN 5.4): nach Lösen/Aufgeben stand „Nächstes Puzzle" als gefüllter Knopf in
// der Kontext-Card UND in der Statuskarte. Jetzt bringt ihn nur die Statuskarte.
describe('BookPuzzleComponent Kontext-Card: ein „Nächstes Puzzle" (b1)', () => {
  beforeEach(() => spyOn(BookPuzzleComponent.prototype, 'ngOnInit'));
  afterEach(() => TestBed.resetTestingModule());

  for (const state of ['SOLVED', 'FAILED'] as const) {
    it(`Kurs ${state}: kein zweiter „Nächstes Puzzle" in der Kontext-Card, die Statuskarte bringt ihn`, () => {
      const el = render(c => { c.inCourse = true; c.course.bookId = 7; c.state = state; });
      const card = el.querySelector('.context-card')!;
      expect(card).not.toBeNull();
      expect(nextButtons(card).length).toBe(0);
      expect(card.querySelector('.ctx-nav')).withContext('leere Navigationszeile nicht gerendert').toBeNull();
      expect(card.querySelector('.skip-quiet')).withContext('kein „Überspringen" nach dem Ergebnis').toBeNull();
      const status = el.querySelector('app-puzzle-status-card') as any;
      expect(status).not.toBeNull();
      expect(status.showNext).toBeTrue();
    });

    it(`Wochenpost ${state}: kein „Nächstes Puzzle" in der Kontext-Card`, () => {
      const el = render(c => { c.inWeekly = true; c.state = state; });
      expect(nextButtons(el.querySelector('.context-card')!).length).toBe(0);
    });
  }

  it('Tagespuzzle gelöst: Datums-Navigation bleibt in der Kontext-Card', () => {
    const el = render(c => { c.daily.date = '20260714'; c.state = 'SOLVED'; });
    const nav = el.querySelector('.context-card .ctx-nav');
    expect(nav).not.toBeNull();
    expect(nav!.textContent).toContain('book.actions.prevDaily');
  });

  it('Buch-Puzzle aufgegeben: „Nächstes im Buch"/„Zufällig" bleiben als Rahmenknöpfe', () => {
    const el = render(c => { c.state = 'FAILED'; });
    const nav = el.querySelector('.context-card .ctx-nav')!;
    expect(nav.textContent).toContain('book.actions.nextInBook');
    expect(nav.querySelector('[mat-flat-button]')).toBeNull();
  });

  it('während des Lösens steht „Überspringen" neben „Aufgeben" in der Status-Card, nicht in der Kontext-Card (2026-10-20)', () => {
    const el = render(c => { c.inCourse = true; c.course.bookId = 7; c.state = 'AWAITING_USER_MOVE'; });
    expect(el.querySelector('.context-card .skip-quiet')).toBeNull();
  });
});

// b1-rk-loeser-tags-link-versatz (DESIGN 3.2/5.2): Abzeichen und „Tags anzeigen" fluchten mit Titel/Kapitel in der
// Textspalte neben dem Zurück-Pfeil, statt am Kartenrand unter dem Pfeil zu beginnen.
describe('BookPuzzleComponent Kontext-Card: Abzeichen in der Textspalte (b1)', () => {
  beforeEach(() => spyOn(BookPuzzleComponent.prototype, 'ngOnInit'));
  afterEach(() => TestBed.resetTestingModule());

  it('setzt .ctx-badges in .ctx-text', () => {
    const el = render(c => {
      c.inCourse = true; c.course.bookId = 7; c.state = 'AWAITING_USER_MOVE';
      c.puzzle = { ...c.puzzle, difficulty: 'Medium', tags: 'endgame' };
    });
    const badges = el.querySelector('.context-card .ctx-badges');
    expect(badges).not.toBeNull();
    expect(badges!.parentElement!.classList).toContain('ctx-text');
    expect(el.querySelector('.context-card .ctx-head > .ctx-text app-puzzle-tags')).not.toBeNull();
  });
});

const css = (cmp: Type<unknown>) =>
  ((((cmp as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n'))
    .replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, '');

// b1-rk-loeser-kommentar-zeilenlaenge-ultrawide (DESIGN 4.6 Regel 4): ab 2400 px Kommentar 1 rem, Zeile ≤ 68 Zeichen.
describe('BookPuzzleComponent Kurs-Kommentar ab 2400 px (b1)', () => {
  it('größere Schrift und begrenzte Zeilenlänge nur in der Breitbild-Stufe', () => {
    const s = css(BookCommentBlockComponent);   // Kommentar-Block ist seit Z18 eine eigene Komponente
    const at = s.indexOf('@media(min-width:2400px)');
    expect(at).toBeGreaterThan(-1);
    const wide = s.slice(at);
    expect(wide).toContain('.ctx-commentp{font-size:1rem;line-height:1.5');
    expect(wide).toContain('.ctx-comment.ctx-comment-body{max-width:68ch');
    expect(s.slice(0, at)).not.toContain('68ch');
  });
});

// d4-rk-tipp-zaehler-beschriftung: die Klammer im Tipp-Knopf nennt die Stufe, die der Klick aufdeckt — vorher stand
// „Nächster Tipp (1/3)“, während Tipp 1 schon zu sehen war.
describe('BookPuzzleComponent Tipp-Knopf zählt den nächsten Tipp (d4)', () => {
  beforeEach(() => spyOn(BookPuzzleComponent.prototype, 'ngOnInit'));
  afterEach(() => TestBed.resetTestingModule());

  const tip = (el: Element) => el.querySelector('.board-hint-tip')!.textContent!.replace(/\s+/g, ' ');

  const withHints = (level: number) => (c: any) => {
    c.state = 'AWAITING_USER_MOVE';
    c.hintLevel = level;
    Object.defineProperty(c, 'availableHints', { get: () => ['a', 'b', 'c'] });
  };

  it('vor dem ersten Klick „(1/3)“', () => {
    expect(tip(render(withHints(0)))).toContain('(1/3)');
  });

  it('nach einem gezeigten Tipp „solver.hints.next (2/3)“', () => {
    const t = tip(render(withHints(1)));
    expect(t).toContain('solver.hints.next');
    expect(t).toContain('(2/3)');
  });
});

// d4-rk-daily-puzzle-ohne-tagesbezug: Chip „Tagespuzzle“ + Datum wie der Wochenpost-Chip.
describe('BookPuzzleComponent Kontext-Card: Tagespuzzle nennt sich und den Tag (d4)', () => {
  beforeEach(() => spyOn(BookPuzzleComponent.prototype, 'ngOnInit'));
  afterEach(() => TestBed.resetTestingModule());

  it('isDaily: Chip „book.dailyChip“ und das Datum des Tages', () => {
    const el = render(c => { c.daily.date = '20260714'; c.state = 'AWAITING_USER_MOVE'; });
    const row = el.querySelector('.context-card .daily-chip-row');
    expect(row).not.toBeNull();
    expect(row!.querySelector('.weekly-chip')!.textContent!.trim()).toBe('book.dailyChip');
    expect(row!.querySelector('.daily-date')!.textContent).toContain('2026');
    expect(row!.querySelector('.daily-date')!.textContent).toContain('14');
  });

  it('Buch-Puzzle ohne Datum: kein Tagespuzzle-Chip', () => {
    const el = render(c => { c.state = 'AWAITING_USER_MOVE'; });
    expect(el.querySelector('.daily-chip-row')).toBeNull();
  });
});
