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
    // Weiterscroll-Schatten auf dem einzigen Scrollbereich: globale Klasse rh-scroll-hint (styles.scss, DESIGN 5.6) —
    // local-Abdeckung + scroll-Schatten, gemessen am berechneten Stil.
    const content = el.querySelector<HTMLElement>('mat-dialog-content')!;
    expect(content.classList).toContain('rh-scroll-hint');
    const cs = getComputedStyle(content);
    expect(cs.backgroundImage).toContain('linear-gradient');
    expect(cs.backgroundImage).toContain('radial-gradient');
    expect(cs.backgroundAttachment).toBe('local, scroll');
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

describe('WoodpeckerCreateDialogComponent (Design-Runde d3)', () => {
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
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => document.querySelectorAll('app-woodpecker-create-dialog').forEach(e => e.remove()));

  it('d3-rk-woodpecker-leerdialog-luecke-abbrechen: ohne Kurs heißt der Knopf „Close“, der Leerzustand ohne 2rem-Polster', () => {
    const { el } = render([]);
    expect(el.querySelector('mat-dialog-actions button')!.textContent!.trim()).toBe('Close');
    expect(compactCss()).toContain('.wp-content.empty-state{padding:8px00;}');
  });

  it('mit Kursen bleibt es „Cancel“', () => {
    const { el } = render([course(1, 'A', 10)]);
    expect(el.querySelector('mat-dialog-actions button')!.textContent!.trim()).toBe('Cancel');
  });

  it('d3-rk-woodpecker-dialog-kurswahl-tippziel: jede Kurszeile ist ganzzeilig ≥ 44 px hoch', () => {
    const { el } = render([course(1, 'Basic Endgames', 190), course(2, 'Other', 183)]);
    const rows = Array.from(el.querySelectorAll('.courses mat-checkbox')) as HTMLElement[];
    const list = el.querySelector('.courses') as HTMLElement;
    expect(rows.length).toBe(2);
    for (const r of rows) {
      const field = r.querySelector('.mdc-form-field') as HTMLElement;
      expect(field.getBoundingClientRect().height).toBeGreaterThanOrEqual(44);
      expect(Math.round(field.getBoundingClientRect().width)).toBe(Math.round(list.getBoundingClientRect().width));
    }
  });

  it('d3-rk-woodpecker-kursliste-doppelte-namen: nur gleichnamige Kurse bekommen eine unterscheidende Meta-Zeile', () => {
    const own = { ...course(1, '100 Tactical Patterns', 448), isOwned: true } as CourseListItem;
    const shared = { ...course(2, '100 Tactical Patterns', 438), isShared: true, sharedByUsername: 'noel', lastActivityAt: '2026-10-01T10:00:00Z' } as CourseListItem;
    const { el } = render([own, shared, course(3, 'Basic Endgames', 190)]);
    const metas = Array.from(el.querySelectorAll('.courses mat-checkbox')).map(r => r.querySelector('.c-meta')?.textContent?.trim() ?? null);
    expect(metas[0]).toBe('Your course');
    expect(metas[1]).toContain('from noel');
    expect(metas[1]).toContain('last practised');
    expect(metas[2]).toBeNull();
  });
});

describe('Woodpecker-Texte halten Zyklus und Zahl zusammen (c2-wp-dialog-zyklus-zahl-umbruch)', () => {
  const load = async (lang: string) =>
    (await (await fetch(`/i18n/${lang}.json`).then(r => r.ok ? r : fetch(`/base/i18n/${lang}.json`))).json()) as
      { woodpecker: { rules: string; create: { coursesHint: string; start: string } } };
  // Wort + Zahl (bzw. hu „1. ciklus“) mit geschütztem Leerzeichen U+00A0 — nie „Zyklus“ am Zeilenende, „1“ in der nächsten.
  const PAIR: Record<string, RegExp> = { de: /Zyklus 1/, en: /[Cc]ycle 1/, hr: /[Cc]iklusa? 1/, hu: /1\. ciklus/ };
  for (const lang of ['de', 'en', 'hr', 'hu']) {
    it(`${lang}: Hinweis, Beginn und Regeln ohne normales Leerzeichen zwischen Zyklus und Zahl`, async () => {
      const wp = (await load(lang)).woodpecker;
      for (const text of [wp.create.coursesHint, wp.create.start, wp.rules]) {
        expect(text).withContext(text).not.toMatch(PAIR[lang]);
        expect(text).withContext(text).toMatch(/ /);
      }
    });
  }
});
