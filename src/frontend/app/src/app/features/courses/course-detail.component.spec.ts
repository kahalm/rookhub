import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { Observable, of, throwError } from 'rxjs';
import { CourseDetailComponent } from './course-detail.component';
import { CourseDetail, CourseLine, CourseManageChapter, CourseService } from './course.service';
import { CourseLanguageService } from './course-language.service';
import { saveBookOffline } from '../puzzles/book-offline.util';

/** Echte Sprachwahl mit fester Oberflächensprache — `getDetail` bekommt damit ein `lang`. */
function courseLang(ui = 'de'): CourseLanguageService {
  return new CourseLanguageService({ currentLang: () => ui, getFallbackLang: () => 'en' } as never);
}

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
  /** Rückfrage (ConfirmService): antwortet sofort; Vorgabe „ja", ein Test stellt `answer` um. */
  const confirm: { answer: boolean; ask: () => Observable<boolean> } = { answer: true, ask: () => of(confirm.answer) };
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
  const calc = { getBook: () => of({ chapters: [] }) };
  const calcEditions = { visible: () => of([]), manage: () => of([]), upsert: () => of({}), remove: () => of(undefined) };
  const component = new CourseDetailComponent(
    { snapshot: { paramMap: { get: () => '58' } } } as never,
    { navigate: () => Promise.resolve(true) } as never,
    courses as never,
    calc as never,
    calcEditions as never,
    dialog as never,
    { warn: (m: string) => warnings.push(m), quick: () => undefined } as never,
    { instant: (k: string) => k } as never,
    { sendAndNotify: (target: number | null, items: unknown[]) => calls.push(`worksheet:${target}:${items.length}`) } as never,
    courseLang(),
    confirm as never,
    { has: (perm: string) => perm === 'weeklyposts.manage' } as never,
    { getAll: () => { calls.push('weeklyList'); return of([{ scheduledAt: '2026-10-09T17:00:00Z' }]); } } as never,
  );
  return { component, calls, warnings, confirm };
}

describe('CourseDetailComponent', () => {
  it('Wochenpost aus dem Kapitel: holt den letzten Termin und öffnet den Dialog; ohne Solver-Index nichts', async () => {
    const { component, calls } = make();
    expect(component.canCreateWeekly).toBeTrue();
    await component.createWeeklyFromChapter({ name: 'Pins', solverIndex: 2 } as never);
    expect(calls).toContain('weeklyList');
    expect(calls).toContain('dialog');
    const before = calls.length;
    await component.createWeeklyFromChapter({ name: 'Stellungen', solverIndex: null } as never);
    expect(calls.length).toBe(before);
  });

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
    const { component, calls, confirm } = make();
    component.ngOnInit();

    confirm.answer = false;
    component.deleteChapter(chapter());
    expect(calls).not.toContain('deleteChapter');

    confirm.answer = true;
    component.deleteChapter(chapter());
    expect(calls).toContain('deleteChapter');
  });

  it('löscht eine Linie und zieht Zähler + Kapitel-Linien frisch nach', () => {
    const { component, calls } = make();
    component.ngOnInit();
    const ch = chapter();
    component.toggleChapter(ch);
    const before = calls.filter(c => c === 'getChapterLines').length;

    component.deleteLine(ch, line({ id: 11 }));

    expect(calls).toContain('deleteLine');
    // Neuladen aktualisiert das Detailbild UND das aufgeklappte Kapitel.
    expect(calls.filter(c => c === 'getDetail').length).toBe(2);
    expect(calls.filter(c => c === 'getChapterLines').length).toBe(before + 1);
    expect(component.busy).toBeFalse();
  });

  it('fragt vor dem Löschen einer Linie nach', () => {
    const { component, calls, confirm } = make();
    component.ngOnInit();
    confirm.answer = false;
    component.deleteLine(chapter(), line());
    expect(calls).not.toContain('deleteLine');
  });

  it('setzt den Kapitel-Fortschritt nur nach Bestätigung zurück', () => {
    const { component, calls, confirm } = make();
    component.ngOnInit();
    confirm.answer = false;
    component.resetChapter(chapter());
    expect(calls).not.toContain('resetChapter');

    confirm.answer = true;
    component.resetChapter(chapter());
    expect(calls).toContain('resetChapter');
  });

  it('setzt den ganzen Kurs nur nach Bestätigung zurück', () => {
    const { component, calls } = make();
    component.ngOnInit();
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

  it('stellt den Schalter nach einem Fehlschlag zurück — das Nachladen allein schreibt [checked] nicht neu', () => {
    const { component } = make(
      { setCalculation: () => throwError(() => new Error('nope')) }, false, { isCalculation: false });
    component.ngOnInit();
    const toggle = { checked: true };   // mat-slide-toggle ist beim Klick schon selbst umgekippt

    component.setCalculation(true, toggle);

    expect(toggle.checked).toBeFalse();
  });
});

describe('CourseDetailComponent — PGN je Kapitel/Linie', () => {
  it('lädt das Kapitel mit seinem Namen und benennt die Datei danach', () => {
    const asked: unknown[][] = [];
    const click = spyOn(HTMLAnchorElement.prototype, 'click');
    const { component } = make({
      downloadChapterPgn: (...a: unknown[]) => { asked.push(a); return of(new Blob(['pgn'])); },
    });
    component.ngOnInit();

    component.downloadChapterPgn(chapter({ name: 'Kapitel 1' }));
    component.downloadChapterPgn(chapter({ name: null }));

    expect(asked).toEqual([[58, 'Kapitel 1'], [58, null]]);
    const names = click.calls.all().map(c => (c.object as HTMLAnchorElement).download);
    expect(names).toEqual(['TestNoel_Kapitel_1.pgn', 'TestNoel_no_chapter.pgn']);
  });

  it('lädt eine Linie über ihre Id', () => {
    const asked: unknown[][] = [];
    const click = spyOn(HTMLAnchorElement.prototype, 'click');
    const { component } = make({
      downloadLinePgn: (...a: unknown[]) => { asked.push(a); return of(new Blob(['pgn'])); },
    });
    component.ngOnInit();

    component.downloadLinePgn(line({ id: 16867, round: '004.002', title: '1A | 2.Sf3 | Weiß spielt 6.Lxa3' }));

    expect(asked).toEqual([[58, 16867]]);
    expect((click.calls.mostRecent().object as HTMLAnchorElement).download)
      .toBe('TestNoel_004_002_1A_2_Sf3_Weiß_spielt_6_Lxa3.pgn');
  });

  it('meldet einen fehlgeschlagenen Download', () => {
    const { component, warnings } = make({
      downloadChapterPgn: () => throwError(() => new Error('nope')),
      downloadLinePgn: () => throwError(() => new Error('nope')),
    });
    component.ngOnInit();

    component.downloadChapterPgn(chapter());
    component.downloadLinePgn(line());

    expect(warnings).toEqual(['courses.downloadFailed', 'courses.downloadFailed']);
  });
});

/** Kurs-Übersetzung (Stufe C, 0.549.0): Sprache der Seite, Kapitel-Labels, Offline-Hinweis. */
describe('CourseDetailComponent Sprache der Kommentare', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('fragt das Detail in der Oberflächensprache an (ohne gemerkte Wahl)', () => {
    const langs: (string | null | undefined)[] = [];
    const { component } = make({ getDetail: (_id: number, lang?: string | null) => { langs.push(lang); return of(detail()); } });
    component.ngOnInit();
    expect(langs).toEqual(['de']);
  });

  it('zeigt Kapitel mit ihrem Label, Schlüssel bleibt der Name', () => {
    const { component } = make();
    const ch = chapter({ name: "King's Indian", label: 'Königsindisch' });
    expect(component.chapterLabel(ch)).toBe('Königsindisch');
    expect(component.key(ch)).toBe("King's Indian");   // Umbenennen/Löschen/PGN adressieren das Original
    expect(component.chapterLabel(chapter({ name: 'A', label: null }))).toBe('A');
  });

  it('eine Sprachwahl wird gemerkt und lädt die Seite in ihr neu', () => {
    const langs: (string | null | undefined)[] = [];
    const { component } = make({ getDetail: (_id: number, lang?: string | null) => { langs.push(lang); return of(detail()); } });
    component.ngOnInit();
    component.pickLanguage('en');
    expect(langs).toEqual(['de', 'en']);
    expect(component.courseLang.choice({ bookId: 58 })).toBe('en');
  });

  it('meldet eine Offline-Kopie in anderer Sprache und holt sie auf Wunsch neu', () => {
    // Kopie auf Deutsch geholt, der Kurs kennt en (Quelle) + de; gewählt wird dann das Original.
    saveBookOffline('testnoel.pgn', [{ id: 1, bookFileName: 'testnoel.pgn', commentLanguages: ['en', 'de'] } as never], 58, 'de');
    const got: (string | null | undefined)[] = [];
    const { component } = make({
      getDetail: () => of(detail({ isCalculation: false })),
      getBookPuzzles: (_id: number, lang?: string | null) => {
        got.push(lang);
        return of([{ id: 1, bookFileName: 'testnoel.pgn', commentLanguages: ['en', 'de'] }]);
      },
    });
    component.ngOnInit();
    expect(component.offlineStale).toBeFalse();   // Oberfläche 'de' = Sprache der Kopie
    component.pickLanguage('en');
    expect(component.offlineStale).toBeTrue();
    component.redownloadOffline();
    expect(got).toEqual(['en']);
    expect(component.offlineStale).toBeFalse();
  });

  it('Kalkulationsbücher haben keine Offline-Kopie und damit keinen Hinweis', () => {
    saveBookOffline('testnoel.pgn', [{ id: 1, bookFileName: 'testnoel.pgn' } as never], 58, 'fr');
    const { component } = make();   // detail() ist ein Kalkulationsbuch
    component.ngOnInit();
    component.pickLanguage('en');
    expect(component.offlineStale).toBeFalse();
  });
});

/** UI-Sweep z2 (z2-rk-kursdetail-handy-vorlauf) + b1 (b1-rk-kursdetail-vorlauf-laptop): die Aktionszeile steht in
 *  ALLEN Breiten vor den Kennzahlen — am Laptop lag der Hauptknopf sonst bei 45–48 % der Höhe. */
describe('CourseDetailComponent Layout Kopf', () => {
  const css = ((CourseDetailComponent as any).ɵcmp.styles as string[]).join('\n').replace(/\s+/g, ' ');
  const beforeMedia = css.slice(0, css.indexOf('@media'));
  it('setzt die Kennzahlen hinter die Aktionszeile — außerhalb jeder Media-Query', () => {
    expect(beforeMedia).toMatch(/\.head-card[^{]* mat-card-content[^{]*\{ ?display: flex; flex-direction: column/);
    expect(beforeMedia).toMatch(/\.head-card[^{]* \.meta[^{]*\{ ?order: 1/);
  });

  it('Kapitelzähler in fester, rechtsbündiger Breite (b1-rk-kapitel-balken-versatz)', () => {
    expect(css).toMatch(/\.chapter-label[^{]*\{[^}]*min-width: 7ch;[^}]*text-align: right/);
  });
});

/** UI-Sweep v1: lange Beschreibung mit „Mehr" im Satz (v1-rk-kursseite-beschreibung-vorlauf-handy),
 *  Schlagwörter als Abzeichen (v1-rk-kursseite-schlagwoerter-ohne-leerzeichen). */
describe('CourseDetailComponent Kopf (gerendert)', () => {
  const LONG = 'Zehn vollständig kommentierte Meisterpartien aus Capablancas „Chess Fundamentals" (1921, gemeinfrei). '
    + 'Die Kommentare stammen aus dem Buch und wurden behutsam übertragen; jede Partie zeigt ein typisches Motiv.';
  afterEach(() => TestBed.resetTestingModule());

  async function render(over: Partial<CourseDetail>, width = 358): Promise<{ el: HTMLElement; detect: () => void }> {
    await TestBed.configureTestingModule({
      imports: [CourseDetailComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: CourseService, useValue: { getDetail: () => of(detail({ isCalculation: false, ...over })), getTranslations: () => throwError(() => ({ status: 404 })) } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(CourseDetailComponent);
    const host = fixture.nativeElement as HTMLElement;
    // Im Dokument und in Handybreite: die Beschreibung wird GEMESSEN gekürzt (ClampMoreDirective).
    host.style.display = 'block';
    host.style.width = `${width}px`;
    document.body.appendChild(host);
    fixture.detectChanges();
    return { el: host, detect: () => fixture.detectChanges() };
  }

  afterEach(() => document.querySelectorAll('app-course-detail').forEach(e => e.remove()));

  function lines(p: Element): number {
    const lh = parseFloat(getComputedStyle(p).lineHeight);
    return Math.round(p.getBoundingClientRect().height / lh);
  }

  it('klappt eine lange Beschreibung zu und per „Mehr" (aria-expanded) auf', async () => {
    const { el, detect } = await render({ description: LONG });
    const p = el.querySelector('p.description')!;
    const more = p.querySelector('button.desc-more') as HTMLButtonElement;
    expect(more).withContext('Knopf steht im Absatz').not.toBeNull();
    expect(more.hidden).toBeFalse();
    expect(more.getAttribute('aria-expanded')).toBe('false');
    expect(p.textContent).not.toContain('typisches Motiv');
    more.click();
    detect();
    expect(more.getAttribute('aria-expanded')).toBe('true');
    expect(el.querySelector('p.description')!.textContent).toContain('typisches Motiv');
    expect(more.hidden).withContext('„Weniger" bleibt sichtbar').toBeFalse();
  });

  // Nachrunde v1-rk-kursseite-beschreibung-vorlauf-handy: am Handy waren es mit 180 Zeichen noch 5 Zeilen.
  for (const width of [322, 358]) {
    it(`zugeklappt höchstens drei Zeilen samt „Mehr" (${width} px)`, async () => {
      const { el } = await render({ description: LONG }, width);
      const p = el.querySelector('p.description')!;
      expect(lines(p)).toBeLessThanOrEqual(3);
      expect(lines(p)).toBeGreaterThanOrEqual(2);
      const text = p.querySelector('.clamp-text')!.textContent!;
      expect(text.endsWith(' …')).toBeTrue();
      expect(LONG.startsWith(text.slice(0, -2))).withContext('am Wortende gekürzt').toBeTrue();
      // „Mehr" steht in der letzten Zeile hinter dem Text
      const more = p.querySelector('.desc-more')!.getBoundingClientRect();
      expect(more.bottom).toBeLessThanOrEqual(p.getBoundingClientRect().bottom + 1);
    });
  }

  it('breit: passt der Text in drei Zeilen, bleibt er ganz', async () => {
    const { el } = await render({ description: LONG }, 900);
    const p = el.querySelector('p.description')!;
    expect(lines(p)).toBeLessThanOrEqual(3);
    expect(p.querySelector('.clamp-text')!.textContent).toBe(LONG);
    expect((p.querySelector('.desc-more') as HTMLElement).hidden).toBeTrue();
  });

  it('kurze Beschreibung ohne „Mehr"', async () => {
    const { el } = await render({ description: 'Kurz und knapp.' });
    expect(el.querySelector('p.description .clamp-text')!.textContent!.trim()).toBe('Kurz und knapp.');
    expect((el.querySelector('.desc-more') as HTMLElement).hidden).toBeTrue();
  });

  it('zeigt Schlagwörter einzeln als Abzeichen, nicht als Rohtext', async () => {
    const { el } = await render({ tags: 'Meisterpartien,kommentiert,gemeinfrei' });
    const chips = Array.from(el.querySelectorAll('dd.tag-list .badge')).map(b => b.textContent!.trim());
    expect(chips).toEqual(['Meisterpartien', 'kommentiert', 'gemeinfrei']);
    expect(el.textContent).not.toContain('Meisterpartien,kommentiert');
  });

  // a1-rk-kennzahl-beschriftung-umbruch: bricht eine Beschriftung um, bleiben die Werte der Zeile auf einer Linie.
  it('hält die Kennzahl-Werte auf einer Linie, auch wenn eine Beschriftung umbricht', async () => {
    const { el } = await render({}, 600);
    const cells = Array.from(el.querySelectorAll('dl.meta > div')).slice(0, 3) as HTMLElement[];
    expect(cells.length).toBe(3);
    cells[0].querySelector('dt')!.textContent = 'Lines';
    cells[1].querySelector('dt')!.textContent = 'Positions without a solution';
    cells[2].querySelector('dt')!.textContent = 'Chapters';
    const dt = cells.map(c => c.querySelector('dt')!.getBoundingClientRect());
    expect(dt[1].height).withContext('die lange Beschriftung bricht um').toBeGreaterThan(dt[0].height * 1.5);
    const tops = cells.map(c => Math.round(c.querySelector('dd')!.getBoundingClientRect().top));
    expect(tops[0]).toBe(tops[1]);
    expect(tops[2]).toBe(tops[1]);
  });

  it('nur technische Schlagwörter → keine Zeile „Schlagwörter"', async () => {
    const { el } = await render({ tags: 'personal' });
    expect(el.querySelector('dd.tag-list')).toBeNull();
  });

  // b1-rk-kursdetail-vorlauf-laptop: am Laptop steht der Hauptknopf über den Kennzahlen.
  it('Laptop: Aktionszeile über den Kennzahlen', async () => {
    const { el } = await render({ description: LONG }, 1240);
    const actions = el.querySelector('.head-card .actions')!.getBoundingClientRect();
    const meta = el.querySelector('.head-card dl.meta')!.getBoundingClientRect();
    expect(actions.bottom).toBeLessThanOrEqual(meta.top);
  });

  // b1-rk-studienbuch-fortschritt-0-0: Studienbuch (nur Partien) ohne „0/0", „Linien durchsehen" ist der Hauptknopf.
  it('Studienbuch ohne Aufgaben: kein Fortschritt, leiser Satz, Durchsehen gefüllt', async () => {
    const { el } = await render({
      puzzleCount: 0, solvedCount: 0, progressPercent: 0, totalLines: 10, infoLineCount: 10,
      chapters: [chapter({ quizCount: 0, lineCount: 6, solvedCount: 0, progressPercent: 0 })],
    });
    expect(el.querySelector('.head-card .progress-row')).toBeNull();
    expect(el.querySelector('.head-card .progress-note')).toBeNull();
    expect(el.querySelector('.head-card .empty-note')!.textContent).toContain('courses.emptyCourse.gamesOnly');
    expect(el.querySelector('.chapter-progress')).withContext('kein Kapitelbalken').toBeNull();
    const primary = el.querySelectorAll('.head-card .actions [mat-flat-button]');
    expect(primary.length).withContext('genau ein Hauptknopf').toBe(1);
    expect(primary[0].classList).toContain('browse-btn');
    expect(el.querySelector('.head-card .actions .start-btn')!.hasAttribute('mat-flat-button')).toBeFalse();
  });

  it('Kurs mit Aufgaben: Fortschritt und „Sequenziell" als Hauptknopf bleiben', async () => {
    const { el } = await render({ chapters: [chapter({ quizCount: 3 }), chapter({ name: 'Info', quizCount: 0 })] });
    expect(el.querySelector('.head-card .progress-label')!.textContent).toContain('1/3');
    expect(el.querySelector('.head-card .empty-note')).toBeNull();
    const primary = el.querySelectorAll('.head-card .actions [mat-flat-button]');
    expect(primary.length).toBe(1);
    expect(primary[0].classList).toContain('start-btn');
    // Kapitel ohne Aufgaben: leerer Platzhalter statt „0/0"
    expect(el.querySelectorAll('.chapter-progress mat-progress-bar').length).toBe(1);
    expect(el.querySelectorAll('.chapter-progress--none').length).toBe(1);
  });

  it('Kapitelbalken sind gleich lang, egal wie breit der Zähler ist (b1-rk-kapitel-balken-versatz)', async () => {
    const { el } = await render({
      chapters: [chapter({ quizCount: 6, solvedCount: 0 }), chapter({ name: 'Lang', quizCount: 119, solvedCount: 112 })],
    }, 820);
    const bars = Array.from(el.querySelectorAll('.chapter-progress mat-progress-bar')).map(b => b.getBoundingClientRect());
    expect(bars.length).toBe(2);
    expect(Math.round(bars[0].width)).toBe(Math.round(bars[1].width));
    expect(Math.round(bars[0].right)).toBe(Math.round(bars[1].right));
  });
});

