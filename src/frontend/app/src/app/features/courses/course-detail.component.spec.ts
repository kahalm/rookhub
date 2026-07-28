import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { CourseDetailComponent } from './course-detail.component';
import { CourseDetail, CourseLine, CourseManageChapter } from './course.service';

function chapter(over: Partial<CourseManageChapter> = {}): CourseManageChapter {
  return {
    name: 'Kapitel 1', lineCount: 3, quizCount: 0, solvedCount: 1, progressPercent: 33,
    solverIndex: null, firstLineId: 11, ...over,
  };
}

function detail(over: Partial<CourseDetail> = {}): CourseDetail {
  return {
    bookId: 58, fileName: 'testnoel.pgn', displayName: 'TestNoel', description: null,
    difficulty: null, rating: null, minElo: null, maxElo: null, tags: null, themes: ['tactics'],
    kind: 'Puzzle', isCalculation: true, isPublic: false, publicSlug: null,
    isOwned: true, isShared: false, sharedByUsername: null, isPinned: false, canManage: true,
    puzzleCount: 3, solvedCount: 1, progressPercent: 33, totalLines: 3, infoLineCount: 3,
    lastMode: null, lastActivityAt: null, linkedBookId: null, linkedDisplayName: null,
    chapters: [chapter()], createdAt: '2026-07-28T08:00:00Z', updatedAt: '2026-07-28T08:00:00Z',
    ...over,
  };
}

function line(over: Partial<CourseLine> = {}): CourseLine {
  return {
    id: 11, lineId: 'testnoel.pgn:1', round: '1', title: null, chapter: 'Kapitel 1',
    fen: '8/8/8/4k3/8/8/4K3/8 w - - 0 1', comment: null, isInfoOnly: true, moveCount: 0, ...over,
  };
}

/**
 * Komponente ohne Template, mit Stub-Abhängigkeiten. `detailOver` verstellt das Detailbild des
 * mitzählenden Standard-Stubs — nötig, wo ein Test die Zahl der `getDetail`-Aufrufe prüft (ein
 * eigener `getDetail`-Stub in `api` protokolliert nicht mit).
 */
function make(api: Record<string, unknown> = {}, dialogResult: unknown = false,
              detailOver: Partial<CourseDetail> = {}) {
  const calls: string[] = [];
  const warnings: string[] = [];
  const courses = {
    getDetail: () => { calls.push('getDetail'); return of(detail(detailOver)); },
    getChapterLines: () => { calls.push('getChapterLines'); return of([line()]); },
    addLines: () => of({ added: 1, chapter: 'K', issues: [], totalLines: 1 }),
    deleteLine: () => { calls.push('deleteLine'); return of(undefined); },
    renameChapter: () => { calls.push('renameChapter'); return of({ updated: 2 }); },
    deleteChapter: () => { calls.push('deleteChapter'); return of({ deleted: 3 }); },
    resetChapter: () => { calls.push('resetChapter'); return of({ cleared: 1 }); },
    setCalculation: (_id: number, v: boolean) => { calls.push(`setCalculation:${v}`); return of({ isCalculation: v }); },
    reset: () => { calls.push('reset'); return of({}); },
    pinCourse: () => { calls.push('pinCourse'); return of(undefined); },
    unpinCourse: () => { calls.push('unpinCourse'); return of(undefined); },
    downloadPgn: () => of(new Blob(['x'])),
    ...api,
  };
  const dialog = {
    open: () => { calls.push('dialog'); return { afterClosed: () => of(dialogResult) }; },
  };
  const component = new CourseDetailComponent(
    { snapshot: { paramMap: { get: () => '58' } } } as never,
    { navigate: () => Promise.resolve(true) } as never,
    courses as never,
    dialog as never,
    { warn: (m: string) => warnings.push(m), quick: () => undefined } as never,
    { instant: (k: string) => k } as never,
  );
  return { component, calls, warnings };
}

describe('CourseDetailComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [CourseDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CourseDetailComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });
});

describe('CourseDetailComponent Starten', () => {
  it('führt beim Kalkulationsbuch in den Kalkulations-Modus', () => {
    const { component } = make();
    component.ngOnInit();
    expect(component.startLink).toEqual(['/courses', 58, 'calc']);
    expect(component.startLabel).toBe('courses.calculate');
  });

  it('nimmt sonst den zuletzt genutzten Modus', () => {
    const { component } = make({ getDetail: () => of(detail({ isCalculation: false, lastMode: 'random' })) });
    component.ngOnInit();
    expect(component.startLink).toEqual(['/courses', 58, 'random']);
    expect(component.startLabel).toBe('courses.random');

    const plain = make({ getDetail: () => of(detail({ isCalculation: false, lastMode: null })) });
    plain.component.ngOnInit();
    expect(plain.component.startLink).toEqual(['/courses', 58, 'sequential']);
    expect(plain.component.startLabel).toBe('courses.sequential');
  });

  it('springt beim Kapitel-Start des Kalkulationsbuchs auf die erste Stellung', () => {
    const { component } = make();
    component.ngOnInit();
    const ch = chapter({ firstLineId: 42 });
    expect(component.chapterStartLink(ch)).toEqual(['/courses', 58, 'calc']);
    expect(component.chapterStartParams(ch)).toEqual({ pos: 42 });
  });

  it('nutzt sonst den Solver-Kapitelindex — ohne Quiz-Linien gibt es keinen Start', () => {
    const { component } = make({ getDetail: () => of(detail({ isCalculation: false, lastMode: 'sequential' })) });
    component.ngOnInit();
    expect(component.chapterStartLink(chapter({ solverIndex: 2, quizCount: 5 })))
      .toEqual(['/courses', 58, 'chapter', 2, 'sequential']);
    expect(component.chapterStartLink(chapter({ solverIndex: null }))).toBeNull();
    expect(component.chapterStartParams(chapter({ solverIndex: 2 }))).toBeNull();
  });
});

describe('CourseDetailComponent Kapitel', () => {
  it('nutzt „" als Schlüssel für „ohne Kapitel" und übersetzt das Label', () => {
    const { component } = make();
    expect(component.key(chapter({ name: null }))).toBe('');
    expect(component.key(chapter({ name: 'A' }))).toBe('A');
    expect(component.chapterLabel(chapter({ name: null }))).toBe('courses.noChapter');
    expect(component.chapterLabel(chapter({ name: 'A' }))).toBe('A');
  });

  it('lädt die Linien erst beim Aufklappen und nur einmal', () => {
    const { component, calls } = make();
    component.ngOnInit();
    const ch = chapter();

    component.toggleChapter(ch);
    expect(component.expanded['Kapitel 1']).toBeTrue();
    expect(component.linesByChapter['Kapitel 1'].length).toBe(1);
    expect(calls.filter(c => c === 'getChapterLines').length).toBe(1);

    component.toggleChapter(ch);                 // zuklappen
    component.toggleChapter(ch);                 // wieder auf → aus dem Cache
    expect(calls.filter(c => c === 'getChapterLines').length).toBe(1);
  });

  it('meldet einen Ladefehler der Linien', () => {
    const { component, warnings } = make({ getChapterLines: () => throwError(() => new Error('x')) });
    component.ngOnInit();
    component.toggleChapter(chapter());
    expect(warnings).toEqual(['courses.detail.linesLoadFailed']);
  });

  it('bricht das Umbenennen ohne Änderung ab', () => {
    const { component, calls } = make();
    component.ngOnInit();
    const ch = chapter({ name: 'Alt' });

    component.startRename(ch);
    expect(component.renamingKey).toBe('Alt');
    expect(component.renameDraft).toBe('Alt');

    component.commitRename(ch);                  // unverändert → kein Request
    expect(calls).not.toContain('renameChapter');
    expect(component.renamingKey).toBeNull();
  });

  it('benennt um und lädt neu', () => {
    const { component, calls } = make();
    component.ngOnInit();
    const ch = chapter({ name: 'Alt' });
    component.startRename(ch);
    component.renameDraft = 'Neu';
    component.commitRename(ch);
    expect(calls).toContain('renameChapter');
    expect(component.renamingKey).toBeNull();
    expect(component.busy).toBeFalse();
  });

  it('warnt, wenn der Zielname belegt ist', () => {
    const { component, warnings } = make({
      renameChapter: () => throwError(() => ({ error: { message: 'schon belegt' } })),
    });
    component.ngOnInit();
    const ch = chapter({ name: 'Alt' });
    component.startRename(ch);
    component.renameDraft = 'Belegt';
    component.commitRename(ch);
    expect(warnings).toEqual(['schon belegt']);
  });
});

describe('CourseDetailComponent Löschen & Zurücksetzen (mit Rückfrage)', () => {
  it('löscht ein Kapitel nur nach Bestätigung', () => {
    const { component, calls } = make();
    component.ngOnInit();

    spyOn(window, 'confirm').and.returnValue(false);
    component.deleteChapter(chapter());
    expect(calls).not.toContain('deleteChapter');

    (window.confirm as jasmine.Spy).and.returnValue(true);
    component.deleteChapter(chapter());
    expect(calls).toContain('deleteChapter');
  });

  it('löscht eine Linie und zieht Zähler + Kapitel-Linien frisch nach', () => {
    const { component, calls } = make();
    component.ngOnInit();
    const ch = chapter();
    component.toggleChapter(ch);
    const before = calls.filter(c => c === 'getChapterLines').length;
    spyOn(window, 'confirm').and.returnValue(true);

    component.deleteLine(ch, line({ id: 11 }));

    expect(calls).toContain('deleteLine');
    // Neuladen aktualisiert das Detailbild UND das aufgeklappte Kapitel.
    expect(calls.filter(c => c === 'getDetail').length).toBe(2);
    expect(calls.filter(c => c === 'getChapterLines').length).toBe(before + 1);
    expect(component.busy).toBeFalse();
  });

  it('fragt vor dem Löschen einer Linie nach', () => {
    const { component, calls } = make();
    component.ngOnInit();
    spyOn(window, 'confirm').and.returnValue(false);
    component.deleteLine(chapter(), line());
    expect(calls).not.toContain('deleteLine');
  });

  it('setzt den Kapitel-Fortschritt nur nach Bestätigung zurück', () => {
    const { component, calls } = make();
    component.ngOnInit();
    spyOn(window, 'confirm').and.returnValue(false);
    component.resetChapter(chapter());
    expect(calls).not.toContain('resetChapter');

    (window.confirm as jasmine.Spy).and.returnValue(true);
    component.resetChapter(chapter());
    expect(calls).toContain('resetChapter');
  });

  it('setzt den ganzen Kurs nur nach Bestätigung zurück', () => {
    const { component, calls } = make();
    component.ngOnInit();
    spyOn(window, 'confirm').and.returnValue(true);
    component.resetCourse();
    expect(calls).toContain('reset');
  });
});

describe('CourseDetailComponent Stellungen einfügen', () => {
  it('öffnet den Memo-Dialog und lädt nach dem Einfügen neu', () => {
    const { component, calls } = make({}, true);        // Dialog meldet „geändert"
    component.ngOnInit();
    const before = calls.filter(c => c === 'getDetail').length;

    component.addChapter();

    expect(calls).toContain('dialog');
    expect(calls.filter(c => c === 'getDetail').length).toBe(before + 1);
  });

  it('lädt nicht neu, wenn der Dialog nichts eingefügt hat', () => {
    const { component, calls } = make({}, false);
    component.ngOnInit();
    const before = calls.filter(c => c === 'getDetail').length;
    component.addLines(chapter());
    expect(calls.filter(c => c === 'getDetail').length).toBe(before);
  });
});

describe('CourseDetailComponent Anpinnen', () => {
  it('schaltet den Pin optimistisch um', () => {
    const { component, calls } = make();
    component.ngOnInit();
    expect(component.detail!.isPinned).toBeFalse();

    component.togglePin();
    expect(calls).toContain('pinCourse');
    expect(component.detail!.isPinned).toBeTrue();

    component.togglePin();
    expect(calls).toContain('unpinCourse');
    expect(component.detail!.isPinned).toBeFalse();
  });
});

describe('CourseDetailComponent Kalkulations-Modus', () => {
  it('schaltet um und lädt die Detailseite neu (Start-Knopf/Zählung hängen am Flag)', () => {
    const { component, calls } = make({}, false, { isCalculation: false });
    component.ngOnInit();
    const before = calls.filter(c => c === 'getDetail').length;

    component.setCalculation(true);

    expect(calls).toContain('setCalculation:true');
    expect(calls.filter(c => c === 'getDetail').length).toBe(before + 1);
  });

  it('ignoriert den unveränderten Zustand', () => {
    const { component, calls } = make();                 // Fixture ist bereits Kalkulationsbuch
    component.ngOnInit();
    component.setCalculation(true);
    expect(calls).not.toContain('setCalculation:true');
  });

  it('meldet einen Fehlschlag und holt den echten Stand zurück', () => {
    const { component, calls, warnings } = make(
      { setCalculation: () => throwError(() => new Error('nope')) }, false, { isCalculation: false });
    component.ngOnInit();
    const before = calls.filter(c => c === 'getDetail').length;

    component.setCalculation(true);

    expect(warnings).toContain('courses.detail.calcToggleFailed');
    expect(calls.filter(c => c === 'getDetail').length).toBe(before + 1);
    expect(component.busy).toBeFalse();
  });
});
