import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { SwUpdate } from '@angular/service-worker';
import { of, Subject } from 'rxjs';
import { KidHubAppComponent } from './app.component';
import { routes } from './app.routes';
import { KidsApiService, KidsCourseLine, KidsLevelDetail } from './core/kids-api.service';

/** Prüfkatalog des Codereviews (PLAN.md 3.3): KidHub hat „große Ziele" — jedes Bedienziel mindestens 44 px. */
const MIN_TARGET = 44;

const PUZZLE = { fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8' };

/** Alle Stile der Testseite (global + jede gerenderte Komponente) als ein Text. */
function pageCss(): string {
  return Array.from(document.styleSheets).map(sheet => {
    try {
      return Array.from(sheet.cssRules).map(r => r.cssText).join('\n');
    } catch {
      return '';
    }
  }).join('\n');
}

/** Rendert die echte Huelle mit der Seite `url` und gibt HTML und Stile zurueck. */
async function renderPage(url: string): Promise<{ html: string; css: string }> {
  localStorage.setItem('rookhub_lang', 'de');
  localStorage.removeItem('rh-kids-progress-v1');
  const detail: KidsLevelDetail = { level: 1, theme: 'mate1', puzzles: [{ id: 1, ...PUZZLE }] };
  const line: KidsCourseLine = {
    id: 1, bookTitle: 'Matt in einem Zug', ...PUZZLE, startPly: 0, title: null, chapter: null, comment: null,
    moveComments: null, altMoves: null,
  };
  const api = jasmine.createSpyObj<KidsApiService>('KidsApiService',
    ['level', 'levels', 'languageHint', 'courses', 'coursePuzzles', 'endlessBatch']);
  api.level.and.returnValue(of(detail));
  api.levels.and.returnValue(of([{ level: 1, theme: 'mate1', puzzleCount: 1 }, { level: 2, theme: 'promote', puzzleCount: 1 }]));
  api.languageHint.and.returnValue(of({ country: null, language: null }));
  api.courses.and.returnValue(of([{ bookId: 7, title: 'Matt in einem Zug', description: null, puzzleCount: 1 }]));
  api.coursePuzzles.and.returnValue(of([line]));
  api.endlessBatch.and.callFake((windows: unknown[]) => of(windows.map((_, i) => ({ id: 100 + i, ...PUZZLE, rating: 700 }))));
  TestBed.configureTestingModule({
    imports: [KidHubAppComponent],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter(routes),
      provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      { provide: KidsApiService, useValue: api },
      {
        provide: SwUpdate,
        useValue: {
          isEnabled: false, versionUpdates: new Subject<unknown>(),
          unrecoverable: new Subject<unknown>(), checkForUpdate: () => Promise.resolve(false),
        },
      },
    ],
  });
  const f = TestBed.createComponent(KidHubAppComponent);
  f.detectChanges();
  await TestBed.inject(Router).navigateByUrl(url);
  f.detectChanges();
  f.detectChanges();
  return { html: (f.nativeElement as HTMLElement).outerHTML, css: pageCss() };
}

/** Kopiert die Seite in einen iframe von `w`×`h` (dort gelten Media-Queries DIESES Fensters) und misst `selector`. */
function boxesAt(page: { html: string; css: string }, w: number, h: number, selector: string): DOMRect[] {
  const frame = document.createElement('iframe');
  frame.style.cssText = `position: fixed; left: ${-w - 50}px; top: 0; width: ${w}px; height: ${h}px; border: 0;`;
  document.body.appendChild(frame);
  try {
    const doc = frame.contentDocument!;
    doc.open();
    doc.write(`<!doctype html><html><head><style>${page.css}</style></head><body>${page.html}</body></html>`);
    doc.close();
    return Array.from(doc.querySelectorAll(selector)).map(el => el.getBoundingClientRect());
  } finally {
    frame.remove();
  }
}

/**
 * Tippziele der Kinderseite (Codereview 2026-09-29, F7-012): der einzige sichtbare Rueckweg jeder Unterseite war ein
 * Textlink von ~21 px Hoehe ohne Polsterung (gemessen 61×22 bzw. 75×21 px bei 390×844).
 */
describe('KidHub: Tippziele', () => {
  afterEach(() => { localStorage.removeItem('rookhub_lang'); localStorage.removeItem('rh-kids-progress-v1'); });

  for (const url of ['/levels', '/levels/1', '/endless', '/courses', '/courses/7']) {
    it(`${url}: der Rueckweg-Knopf ist mindestens ${MIN_TARGET} px hoch (Handy hochkant und PC)`, async () => {
      const page = await renderPage(url);
      for (const [w, h] of [[390, 844], [1280, 800]] as const) {
        const [back] = boxesAt(page, w, h, '.head .back');
        expect(back).withContext(`${url} ${w}×${h}: Rueckweg fehlt`).toBeDefined();
        expect(back.height).withContext(`${url} ${w}×${h}: Hoehe`).toBeGreaterThanOrEqual(MIN_TARGET);
        expect(back.width).withContext(`${url} ${w}×${h}: Breite`).toBeGreaterThanOrEqual(MIN_TARGET);
      }
    });
  }
});
