import { Type, NO_ERRORS_SCHEMA } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslatePipe } from '@ngx-translate/core';
import { of } from 'rxjs';
import { BookPuzzleComponent } from './book-puzzle.component';
import { PuzzleComponent } from './puzzle.component';
import { EndlessPuzzleComponent } from './endless-puzzle.component';
import { PuzzleActionBarComponent } from './puzzle-action-bar.component';
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
 * UI-Sweep a1, Gruppe Löser (Kurs/Tagespuzzle/Buch/Wochenpost, Standard-Puzzle, Endlos): Layout-Regeln.
 * Karma läuft 1400 px breit — Media-Query-Regeln werden als CSS-Text der KOMPILIERTEN Stile geprüft
 * (Muster `features/page-width.spec.ts`). Sass löst `calc()` innerhalb von `min()` auf — erwartet wird die kompilierte Form.
 */
const css = (cmp: Type<unknown>) =>
  ((((cmp as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n'))
    .replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, ' ');

/** Rumpf der ersten @media-Regel, deren Bedingung `cond` enthält (Klammern ausgezählt). */
function mediaBlock(style: string, cond: string): string | null {
  const i = style.indexOf(cond);
  if (i < 0) return null;
  const open = style.indexOf('{', i);
  let depth = 0;
  for (let j = open; j < style.length; j++) {
    if (style[j] === '{') depth++;
    else if (style[j] === '}' && --depth === 0) return style.slice(open + 1, j);
  }
  return null;
}

const SOLVERS: [string, Type<unknown>, string, string][] = [
  ['Buch/Kurs/Tagespuzzle/Wochenpost', BookPuzzleComponent, '.puzzle-page', '.puzzle-layout'],
  ['Standard-Puzzle', PuzzleComponent, '.puzzle-page', '.puzzle-layout'],
  ['Endlos', EndlessPuzzleComponent, '.endless-page:has(.play-screen)', '.play-screen'],
];

describe('Löser-Layout (UI-Sweep a1)', () => {
  for (const [name, cmp, page, layout] of SOLVERS) {
    it(`${name}: ab 2400 px breite Seite, Brett ohne 820-px-Deckel bis Fensterhöhe (a1-rk-loeser-breitstufe, a1-rk-puzzle-brett-ultrawide)`, () => {
      const wide = mediaBlock(css(cmp), '(min-width: 2400px)');
      expect(wide).withContext('Breitbild-Regel vorhanden').not.toBeNull();
      const w = wide!.replace(/\s/g, '');
      expect(w).toContain(`${page.replace(/\s/g, '')}{max-width:min(var(--page-max-width-wide,1880px),96vw)`);
      expect(w).toContain('.board-section{width:min(100vh-180px,100%-360px-1.5rem)');
      expect(w).not.toContain('820px');
    });

    it(`${name}: unter 2400 px bleibt die gemeinsame Seitenbreite und der 820-px-Deckel`, () => {
      const s = css(cmp).replace(/\s/g, '');
      expect(s).toContain('.board-section{flex:00auto;width:min(60vw,820px,100vh-180px)');
    });

    it(`${name}: 769–899 px einspaltig, Brett mittig bis Fensterhöhe (a1-rk-tablet-seitenspalte-eng)`, () => {
      const tab = mediaBlock(css(cmp), '(min-width: 769px) and (max-width: 899.98px)');
      expect(tab).withContext('Tablet-Regel vorhanden').not.toBeNull();
      const t = tab!.replace(/\s/g, '');
      expect(t).toContain(`${layout}{flex-direction:column`);
      expect(t).toContain('.board-section{width:min(100%,100vh-180px);align-self:center');
      expect(t).toContain('.info-section{width:100%');
    });
  }

  it('„Am Zug“-Punkt Schwarz kommt nur noch aus styles.scss (keine Komponenten-Überschreibung, a1-rk-am-zug-punkt-dunkel)', () => {
    for (const [, cmp] of SOLVERS) expect(css(cmp)).not.toContain('.tomove-dot.black');
  });

  it('„Überspringen“ ohne Deckkraft, Farbe aus der Rolle on-surface-variant (a1-rk-skip-kontrast-hell)', () => {
    const m = /\.skip-quiet\s*\{([^}]*)\}/.exec(css(BookPuzzleComponent));
    expect(m).not.toBeNull();
    expect(m![1]).not.toContain('opacity');
    expect(m![1].replace(/\s/g, '')).toContain('color:var(--mat-sys-on-surface-variant)');
  });

  it('Wochenpost: 8 px zwischen Fortschrittszeile und Umschaltknopf (a1-rk-wochenpost-fortschritt-klebt)', () => {
    const s = css(BookPuzzleComponent).replace(/\s/g, '');
    expect(s).toContain('.context-card.ctx-progress.ctx-mode-btn{margin-top:8px');
    expect(s).toMatch(/\.context-card\.ctx-stats\{[^}]*margin:8px00/);
  });

  it('Statistik-Kennzahlen brechen um statt überzulaufen, Abstand zum Titel (a1-rk-puzzle-statistik-tablet-ueberlauf)', () => {
    const s = css(PuzzleComponent).replace(/\s/g, '');
    expect(s).toContain('grid-template-columns:repeat(auto-fit,minmax(64px,1fr))');
    expect(s).toMatch(/\.stats-grid\{[^}]*margin-top:8px/);
    expect(s).toMatch(/\.stat-label\{[^}]*text-overflow:ellipsis/);
    expect(s).not.toContain('.stats-grid{grid-template-columns:repeat(2,1fr)');
  });
});

describe('Statistik-Karte schmal (a1-rk-puzzle-statistik-tablet-ueberlauf)', () => {
  it('fünf Kennzahlen in 250 px ragen nicht über den Rand', () => {
    const host = document.createElement('div');
    host.style.width = '250px';
    const style = document.createElement('style');
    style.textContent = css(PuzzleComponent);
    host.innerHTML = `<div class="stats-grid">${['1400|Elo', '11/14|Gelöst', '79 %|Genauigkeit', '2|Serie', '3|Beste']
      .map(p => `<div class="stat"><span class="stat-value">${p.split('|')[0]}</span><span class="stat-label">${p.split('|')[1]}</span></div>`)
      .join('')}</div>`;
    document.head.appendChild(style);
    document.body.appendChild(host);
    try {
      const grid = host.querySelector('.stats-grid') as HTMLElement;
      expect(grid.scrollWidth).toBeLessThanOrEqual(grid.clientWidth);
      const right = host.getBoundingClientRect().right;
      host.querySelectorAll('.stat').forEach(s => expect(s.getBoundingClientRect().right).toBeLessThanOrEqual(right + 0.5));
    } finally {
      host.remove(); style.remove();
    }
  });
});

describe('Puzzle-Aktionsleiste schmal (a1-rk-puzzle-menue-allein-tablet)', () => {
  it('Teilen und ⋮ bleiben in derselben Zeile, auch wenn die Leiste umbricht', () => {
    TestBed.configureTestingModule({
      imports: [PuzzleActionBarComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), provideNoopAnimations()],
    });
    const fixture = TestBed.createComponent(PuzzleActionBarComponent);
    fixture.componentRef.setInput('rating', 1745);
    fixture.componentRef.setInput('tags', 'fork pin skewer');
    const el = fixture.nativeElement as HTMLElement;
    el.style.display = 'block';
    el.style.width = '200px';
    document.body.appendChild(el);
    try {
      fixture.detectChanges();
      const share = el.querySelector('.pab-share') as HTMLElement;
      const more = el.querySelector('.pab-more') as HTMLElement;
      expect(share.parentElement).toBe(more.parentElement);
      expect(share.parentElement!.classList).toContain('pab-actions');
      expect(Math.abs(share.getBoundingClientRect().top - more.getBoundingClientRect().top)).toBeLessThan(2);
      expect(el.querySelector('.pab-spacer')).toBeNull();
    } finally {
      el.remove();
    }
  });
});

/** Rendert die Löser-Seite mit ausgeblendeten Kindkomponenten (wie `renderBookPuzzle` in book-puzzle.component.spec). */
function renderBookPuzzle(setup: (c: any) => void, url = '/courses/7/solve'): HTMLElement {
  const prefs: any = {
    boardTheme: 'green', pieceSet: 'cburnett', themeMode: 'fixed', stockfishDepth: 12, bookStockfishDepth: 12,
    visualization: 0, vizArrow: true, offPathWarnMoves: 3, enPassantForced: true,
  };
  const route: any = {
    snapshot: { paramMap: { get: () => null }, queryParamMap: { get: () => null } },
    paramMap: { subscribe: () => ({ unsubscribe() {} }) },
  };
  const solveMode: any = { get: () => 'training', set: () => {}, clear: () => {}, ensure: () => of('training'),
    levelFor: () => 0, modeForLevel: () => 'easy' };
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
      { provide: Router, useValue: { navigate: jasmine.createSpy('navigate'), url } },
      { provide: AuthService, useValue: { isLoggedIn: false } },
      { provide: SnackbarService, useValue: { info: () => {} } },
      { provide: OfflineQueueService, useValue: { enqueue: () => {} } },
      { provide: ChallengeService, useValue: {} },
      { provide: LongSolveService, useValue: { resolve: (s: number) => of(s) } },
      { provide: FavoritesService, useValue: { contains: () => of(false), add: () => of(true), remove: () => of(false), count: () => of(0), list: () => of([]) } },
      { provide: SolveModeService, useValue: solveMode },
      { provide: WorksheetService, useValue: {} },
    ],
  });
  TestBed.overrideComponent(BookPuzzleComponent, { set: { imports: [CommonModule, TranslatePipe], schemas: [NO_ERRORS_SCHEMA] } });
  const fixture = TestBed.createComponent(BookPuzzleComponent);
  const c: any = fixture.componentInstance;
  c.puzzle = { id: 42, fen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', moves: 'e2e4 e7e5', bookFileName: 'b' };
  setup(c);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('Kurs-Löser: langer Kommentar hinter der Bedienung (a1-rk-kommentar-schiebt-aktionen)', () => {
  beforeEach(() => spyOn(BookPuzzleComponent.prototype, 'ngOnInit'));

  it('Kommentar steht NACH Kontext-Card (Fortschritt, „Überspringen“), Status-Card und Aktionsleiste — ungekürzt', () => {
    const long = 'Sehr langer Kommentar. '.repeat(80);
    const el = renderBookPuzzle(c => {
      c.inCourse = true; c.courseTotal = 10; c.courseSolved = 0;
      c.state = 'AWAITING_USER_MOVE';
      spyOnProperty(c, 'commentLines', 'get').and.returnValue([long]);
      spyOnProperty(c, 'commentBlocks', 'get').and.returnValue([[{ text: long }]]);
    });
    const info = el.querySelector('.info-section') as HTMLElement;
    const comment = info.querySelector('.ctx-comment') as HTMLElement;
    const card = info.querySelector('.context-card') as HTMLElement;
    const status = info.querySelector('app-puzzle-status-card') as HTMLElement;
    const bar = info.querySelector('app-puzzle-action-bar') as HTMLElement;
    const skip = info.querySelector('.skip-quiet') as HTMLElement;
    expect(comment).withContext('Kommentar gerendert').not.toBeNull();
    expect(card.contains(comment)).withContext('nicht mehr in der Kontext-Card').toBeFalse();
    expect(card.contains(skip)).toBeTrue();
    expect(card.querySelector('.ctx-progress')).not.toBeNull();
    const after = (a: Element, b: Element) => !!(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);
    expect(after(card, comment)).toBeTrue();
    expect(after(status, comment)).toBeTrue();
    expect(after(bar, comment)).toBeTrue();
    expect(comment.textContent!.length).toBeGreaterThanOrEqual(long.trim().length);
  });

  it('ohne Kommentar kein leerer Block', () => {
    const el = renderBookPuzzle(c => {
      c.state = 'AWAITING_USER_MOVE';
      spyOnProperty(c, 'commentLines', 'get').and.returnValue([]);
    });
    expect(el.querySelector('.ctx-comment-block')).toBeNull();
  });
});
