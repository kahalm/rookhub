import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialog } from '@angular/material/dialog';
import { BreakpointObserver } from '@angular/cdk/layout';
import { of } from 'rxjs';
import { provideRouter } from '@angular/router';
import { SummaryExercisesComponent, SummaryExerciseSheetComponent, exerciseMoveLabel } from './summary-exercises.component';
import { SummaryExerciseSolverComponent } from './summary-exercise-solver.component';
import { SummaryExercise, SummaryExerciseGroup } from './club-game-summary.service';
import { PuzzleService } from '../../puzzles/puzzle.service';
import { AuthService } from '../../../core/auth.service';
import { PreferencesService } from '../../../core/preferences.service';

// Weiß setzt nach a7-a6 mit Dg7# matt (Bauer f6 deckt). Grundreihe mit zwei Türmen: Td8# ist die Lösung, Te8# ebenfalls Matt.
const ex = (id: number): SummaryExercise =>
  ({ id, fen: '6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1', moves: ['a7a6', 'h6g7'], rating: 1500 + id, themes: ['mateIn1'], match: 'strict' });
const backRank: SummaryExercise = { id: 99, fen: '6k1/p4ppp/8/8/8/8/8/K2RR3 b - - 0 1', moves: ['a7a6', 'd1d8'], rating: 1400, themes: ['mateIn1'], match: 'family' };
// Buchdiagramme (0.962.0): Weiß am Zug setzt mit Dg7# (Bauer f6 deckt) matt — lösbar mit bestätigter Buchlösung bzw. nur Beispiel.
const bookEx = (id: number, solvable: boolean, extra: Partial<SummaryExercise['book']> = {}): SummaryExercise => ({
  id, fen: '6k1/5p1p/5P1Q/8/8/8/8/K7 w - - 0 1', moves: solvable ? ['h6g7'] : [], rating: 0, themes: [], match: 'family', source: 'book',
  book: { ebookId: 683, title: 'The Chess Toolbox', author: 'Thomas Willemze', chapter: null, page: 24, pdfPage: true, label: null, solvable, ...extra },
});
const group = (ply: number, n = 2): SummaryExerciseGroup =>
  ({ ply, white: true, reason: 'missedMate', mateIn: 1, alsoPlies: [], exercises: Array.from({ length: n }, (_, i) => ex(ply * 10 + i)) });

function setup(opts: { narrow?: boolean; loggedIn?: boolean } = {}) {
  const record = jasmine.createSpy('recordAttempt').and.returnValue(of({}));
  const dialog = { open: jasmine.createSpy('open').and.returnValue({ afterClosed: () => of(undefined) }) };
  TestBed.configureTestingModule({
    imports: [SummaryExercisesComponent],
    providers: [
      provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }), provideRouter([]),
      { provide: PuzzleService, useValue: { recordAttempt: record } },
      { provide: AuthService, useValue: { isLoggedIn: !!opts.loggedIn } },
      { provide: PreferencesService, useValue: { boardTheme: 'brown', pieceSet: 'cburnett' } },
      { provide: BreakpointObserver, useValue: { isMatched: () => !!opts.narrow } },
      { provide: MatDialog, useValue: dialog },
    ],
  });
  return { record, dialog };
}

describe('SummaryExercisesComponent', () => {
  it('Zugnummer einer Stellung: weiß „15.“, schwarz „15…“', () => {
    expect(exerciseMoveLabel(28)).toBe('15.');
    expect(exerciseMoveLabel(29)).toBe('15…');
  });

  it('drei Stellen offen, der Rest hinter „Weitere Stellen“', () => {
    setup();
    const f = TestBed.createComponent(SummaryExercisesComponent);
    f.componentRef.setInput('groups', [group(10), group(20), group(30), group(40), group(50)]);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelectorAll('.group').length).toBe(3);
    (el.querySelector('button.more') as HTMLButtonElement).click();
    f.detectChanges();
    expect(el.querySelectorAll('.group').length).toBe(5);
  });

  it('Karte zeigt die Stellung NACH dem Gegnerzug aus Sicht des Lösers', () => {
    setup();
    const f = TestBed.createComponent(SummaryExercisesComponent);
    f.componentRef.setInput('groups', [group(10, 1)]);
    f.detectChanges();
    const board = f.debugElement.query(d => d.name === 'app-chess-board').componentInstance;
    expect(board.fen.startsWith('6k1/5p1p/p4P1Q/8/')).toBeTrue();
    expect(board.flipped).toBeFalse();
    expect(board.lastMove).toEqual(['a7', 'a6']);
  });

  it('Desktop: Klick löst im Abschnitt, Ergebnis wird an der Karte markiert', () => {
    setup();
    const f = TestBed.createComponent(SummaryExercisesComponent);
    f.componentRef.setInput('groups', [group(10)]);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    (el.querySelectorAll('.card-hit')[1] as HTMLElement).click();
    f.detectChanges();
    const solver = f.debugElement.query(d => d.componentInstance instanceof SummaryExerciseSolverComponent).componentInstance as SummaryExerciseSolverComponent;
    expect(solver.index()).toBe(1);
    solver.onMove({ orig: 'h6', dest: 'g7' });
    f.detectChanges();
    expect(solver.status()).toBe('solved');
    expect(el.querySelectorAll('.card .mark.ok').length).toBe(1);
  });

  it('taktische Stelle (0.961.0): Grund mit Thema, Karten „gleiches Motiv“', () => {
    setup();
    const f = TestBed.createComponent(SummaryExercisesComponent);
    const tactic: SummaryExerciseGroup = { ply: 21, white: false, reason: 'missedTactic', mateIn: null, alsoPlies: [], theme: 'fork',
      exercises: [{ ...ex(1), match: 'strict' }, { ...ex(2), match: 'family' }] };
    f.componentRef.setInput('groups', [tactic]);
    f.detectChanges();
    const c = f.componentInstance;
    expect(c.matchKey('strict', tactic)).toBe('strictTactic');
    expect(c.matchKey('family', tactic)).toBe('sameTheme');
    expect(c.matchKey('theme', tactic)).toBe('sameTheme');
    expect(c.matchKey('family', group(10))).toBe('family');
    expect((f.nativeElement as HTMLElement).querySelector('h3')?.textContent).toContain('11…');
  });

  it('Handy: Klick öffnet das Blatt statt des Lösers im Abschnitt', () => {
    const { dialog } = setup({ narrow: true });
    const f = TestBed.createComponent(SummaryExercisesComponent);
    f.componentRef.setInput('groups', [group(10)]);
    f.detectChanges();
    ((f.nativeElement as HTMLElement).querySelector('.card-hit') as HTMLElement).click();
    f.detectChanges();
    expect(dialog.open).toHaveBeenCalledWith(SummaryExerciseSheetComponent, jasmine.objectContaining({ data: jasmine.objectContaining({ index: 0 }) }));
    expect((f.nativeElement as HTMLElement).querySelector('app-summary-exercise-solver')).toBeNull();
    history.back();
  });

  it('Buchbeispiele (0.962.0): Diagrammstellung ohne Gegnerzug, Quelle mit Kapitel bzw. Seite, Sprung in den Leser', () => {
    setup();
    const f = TestBed.createComponent(SummaryExercisesComponent);
    const g: SummaryExerciseGroup = { ...group(10, 0), exercises: [bookEx(5, true, { chapter: 'Kapitel 3 Mattbilder' }), bookEx(6, false), ex(5)] };
    f.componentRef.setInput('groups', [g]);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const boards = f.debugElement.queryAll(d => d.name === 'app-chess-board').map(d => d.componentInstance);
    expect(boards[0].fen).toBe('6k1/5p1p/5P1Q/8/8/8/8/K7 w - - 0 1');
    expect(boards[0].lastMove).toBeUndefined();
    const sources = Array.from(el.querySelectorAll('a.book')).map(a => a.textContent ?? '');
    expect(sources.length).toBe(2);
    expect(sources[0]).toContain('The Chess Toolbox');
    expect(sources[0]).toContain('Kapitel 3 Mattbilder');
    expect(el.querySelector('a.book')?.getAttribute('href')).toBe('/ebooks/683?page=24&fmt=pdf&diagram=5');
    // Nur-Beispiel: die Karte selbst führt in den Leser, kein Löser.
    const example = el.querySelectorAll('.card')[1];
    expect(example.querySelector('a.card-hit')?.getAttribute('href')).toBe('/ebooks/683?page=24&fmt=pdf&diagram=6');
    expect(example.querySelector('button.card-hit')).toBeNull();
    // Die Lichess-Aufgabe ist im Löser die zweite (das Beispiel zählt nicht mit).
    (el.querySelectorAll('.card')[2].querySelector('.card-hit') as HTMLElement).click();
    f.detectChanges();
    const solver = f.debugElement.query(d => d.componentInstance instanceof SummaryExerciseSolverComponent).componentInstance as SummaryExerciseSolverComponent;
    expect(solver.exercises().length).toBe(2);
    expect(solver.index()).toBe(1);
  });

  it('EPUB-Diagramm ohne Kapitel: Abschnitt statt Seite, Sprung nur mit Diagramm', () => {
    setup();
    const f = TestBed.createComponent(SummaryExercisesComponent);
    f.componentRef.setInput('groups', [{ ...group(10, 0), exercises: [bookEx(7, true, { pdfPage: false, page: 13 })] }]);
    f.detectChanges();
    const a = (f.nativeElement as HTMLElement).querySelector('a.book');
    expect(a?.getAttribute('href')).toBe('/ebooks/683?diagram=7');
    expect(a?.textContent).toContain('games.summary.exercises.bookPart');
  });
});

describe('SummaryExerciseSolverComponent', () => {
  function solver(loggedIn = false, list: SummaryExercise[] = [ex(1), ex(2)]) {
    const { record } = setup({ loggedIn });
    const f = TestBed.createComponent(SummaryExerciseSolverComponent);
    f.componentRef.setInput('exercises', list);
    f.detectChanges();
    return { f, c: f.componentInstance, record };
  }

  it('startet nach dem Gegnerzug, Löser am Zug', () => {
    const { c } = solver();
    expect(c.fen().startsWith('6k1/5p1p/p4P1Q/8/')).toBeTrue();
    expect(c.orientation()).toBe('white');
    expect(c.dests().get('h6')).toContain('g7');
  });

  it('richtiger Zug → gelöst; angemeldet einmal wie im normalen Löser verbucht', () => {
    const { c, record } = solver(true);
    c.onMove({ orig: 'h6', dest: 'g7' });
    expect(c.status()).toBe('solved');
    expect(record).toHaveBeenCalledOnceWith(1, true, jasmine.any(Number));
  });

  it('anderes Matt im letzten Zug zählt auch (wie Lichess)', () => {
    const { c } = solver(false, [backRank]);
    c.onMove({ orig: 'e1', dest: 'e8' });
    expect(c.status()).toBe('solved');
  });

  it('falscher Zug → falsch, wird zurückgenommen; anonym nichts verbucht; Lösung zeigen', () => {
    jasmine.clock().install();
    try {
      const { c, record } = solver(false);
      c.onMove({ orig: 'h6', dest: 'h5' });
      expect(c.status()).toBe('wrong');
      jasmine.clock().tick(800);
      expect(c.status()).toBe('play');
      expect(c.fen().startsWith('6k1/5p1p/p4P1Q/8/')).toBeTrue();
      c.showSolution();
      jasmine.clock().tick(1000);
      expect(c.status()).toBe('shown');
      expect(c.fen().startsWith('6k1/5pQp/')).toBeTrue();
      expect(record).not.toHaveBeenCalled();
    } finally { jasmine.clock().uninstall(); }
  });

  it('„Weiter“ geht zur nächsten Aufgabe der Stelle', () => {
    const { f, c } = solver();
    c.next();
    f.detectChanges();
    expect(c.index()).toBe(1);
    expect(c.current()?.id).toBe(2);
    expect(c.status()).toBe('play');
  });

  it('Buchdiagramm (0.962.0): Löser zieht zuerst, gelöst wird nicht als Puzzle verbucht', () => {
    const { c, record } = solver(true, [bookEx(5, true)]);
    expect(c.fen()).toBe('6k1/5p1p/5P1Q/8/8/8/8/K7 w - - 0 1');
    expect(c.orientation()).toBe('white');
    c.onMove({ orig: 'h6', dest: 'g7' });
    expect(c.status()).toBe('solved');
    expect(record).not.toHaveBeenCalled();
  });
});
