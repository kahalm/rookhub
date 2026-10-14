import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService, TranslationObject } from '@ngx-translate/core';
import { of } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { CourseListItem, CourseService } from '../courses/course.service';
import { WoodpeckerCreateDialogComponent } from './woodpecker-create-dialog.component';
import { WoodpeckerService } from './woodpecker.service';

const course = (bookId: number, displayName: string, puzzleCount: number): CourseListItem => ({
  bookId, fileName: `${bookId}.pgn`, displayName, difficulty: null, rating: null, tags: null, description: null,
  puzzleCount, solvedCount: 0, progressPercent: 0, lastMode: null, lastActivityAt: null,
} as CourseListItem);

/** Kompiliertes CSS ohne Leerraum und ohne %NS%-Platzhalter (Muster features/page-width.spec.ts). */
const compactCss = () =>
  (((WoodpeckerCreateDialogComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n')
    .replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s/g, '');

/** Karma serviert `public/` als Assets (Muster i18n-parity.spec). */
type I18n = { woodpecker: Record<string, string> };
const loadI18n = async (lang: string): Promise<I18n> => (await fetch(`/i18n/${lang}.json`)).json();

describe('WoodpeckerCreateDialogComponent (UI-Sweep c1)', () => {
  let en: I18n;
  beforeAll(async () => { en = await loadI18n('en'); });

  function render(courses: CourseListItem[]) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [WoodpeckerCreateDialogComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { mode: 'create' } },
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: CourseService, useValue: { getCourses: () => of(courses) } },
        { provide: WoodpeckerService, useValue: {} },
        { provide: SnackbarService, useValue: { warn: () => {} } },
      ],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', en as unknown as TranslationObject);
    translate.use('en');
    const fixture = TestBed.createComponent(WoodpeckerCreateDialogComponent);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('c1-wp-dialog-ohne-kurs-sackgasse: ohne Kurs nur Leersatz + „Zu den Kursen“, kein Datum, kein Start, kein Grund', () => {
    const { el } = render([course(1, 'Kalkulation', 0)]);
    const link = el.querySelector<HTMLAnchorElement>('.empty-state a');
    expect(link?.getAttribute('href')).toBe('/courses');
    expect(link?.hasAttribute('mat-dialog-close')).toBeTrue();
    expect(el.querySelector('app-iso-date-field')).toBeNull();
    expect(el.querySelectorAll('mat-dialog-actions button').length).toBe(1); // nur „Abbrechen“
    expect(el.querySelector('.reason')).toBeNull();
  });

  it('c1-wp-dialog-kasten-im-kasten: Beginn steht vor der Kursliste, die Liste hat keinen eigenen Scrollkasten', () => {
    const { el } = render([course(1, 'A', 448), course(2, 'B', 1036)]);
    const date = el.querySelector('app-iso-date-field')!;
    const list = el.querySelector('.courses')!;
    expect(date.compareDocumentPosition(list) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const css = compactCss();
    const courses = /\.courses\{[^}]*\}/.exec(css)![0];
    expect(courses).not.toContain('max-height');
    expect(courses).not.toContain('overflow');
    // Weiterscroll-Schatten auf dem einzigen Scrollbereich (local-Abdeckung + scroll-Schatten, DESIGN 5.3).
    const content = /\.wp-content\{[^}]*\}/.exec(css)![0];
    expect(content).toContain('var(--mat-dialog-container-color,var(--mat-sys-surface))70%)centerbottom/100%32pxno-repeatlocal');
    expect(content).toContain('100%14pxno-repeatscroll');
  });

  it('c1-wp-dialog-zahlformat: Aufgabenzahl mit Tausendertrenner, Zahl + Einheit mit U+00A0 und nowrap', async () => {
    const { el } = render([course(2, 'B', 1036)]);
    expect(el.querySelector('.c-count')?.textContent?.trim()).toBe('1,036 puzzles');
    expect(compactCss()).toMatch(/\.c-count\{[^}]*white-space:nowrap/);
    for (const lang of ['de', 'en', 'hr', 'hu']) {
      const wp = (await loadI18n(lang)).woodpecker;
      expect(wp['puzzles']).withContext(lang).toMatch(/^\{\{count\}\} \S/);
      expect(wp['puzzlesOne']).withContext(lang).toMatch(/^\{\{count\}\} \S/);
    }
  });

  it('c1-wp-dialog-leersatz-blass-hell: Leersatz im Dialog in on-surface-variant statt 60 % von currentColor', () => {
    expect(compactCss()).toMatch(/\.empty-state\{color:var\(--mat-sys-on-surface-variant\)/);
  });
});
