import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService, TranslationObject } from '@ngx-translate/core';
import { SwUpdate } from '@angular/service-worker';
import { of, Subject } from 'rxjs';
import { KidHubAppComponent } from './app.component';
import { routes } from './app.routes';
import { KidsApiService, KidsLevelDetail } from './core/kids-api.service';

async function germanTexts(): Promise<TranslationObject> {
  for (const url of ['/i18n/de.json', '/base/i18n/de.json']) {
    const res = await fetch(url);
    if (res.ok) return res.json();
  }
  throw new Error('de.json nicht gefunden');
}

interface Box { left: number; top: number; right: number; bottom: number; width: number }

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

/**
 * Kopiert die gerenderte Seite samt Stilen in einen iframe von `w`×`h` — dort gelten Media-Queries,
 * vh und vw DIESES Fensters. Scrollbalken liegen wie am Handy/Tablet ueber dem Inhalt.
 */
function layoutAt(html: string, css: string, w: number, h: number): { board: Box; task: Box; head: Box } {
  const frame = document.createElement('iframe');
  frame.style.cssText = `position: fixed; left: ${-w - 50}px; top: 0; width: ${w}px; height: ${h}px; border: 0;`;
  document.body.appendChild(frame);
  try {
    const doc = frame.contentDocument!;
    doc.open();
    doc.write(`<!doctype html><html><head><style>${css}</style><style>html { scrollbar-width: none; }</style></head>`
      + `<body>${html}</body></html>`);
    doc.close();
    const box = (selector: string): Box => {
      const r = doc.querySelector(selector)!.getBoundingClientRect();
      return { left: r.left, top: r.top, right: r.right, bottom: r.bottom, width: r.width };
    };
    return { board: box('.board'), task: box('.task-slot'), head: box('.head') };
  } finally {
    frame.remove();
  }
}

/**
 * Brett und Aufbau der Aufgabenseite je Fenstergroesse (Codereview 2026-09-29, UX-028). Gerendert wird
 * die echte Seite (Huelle + Stufe 1); `layoutAt` misst sie in Fenstern der gaengigen Geraete.
 * Gemeldet: am iPad hochkant (768–834 px) stand das Brett mit 300 px neben einer halbleeren Seite
 * (760 px bekam 640 px), am Handy quer lief die unterste Reihe aus dem Bild.
 */
describe('KidHub: Aufgabenseite je Fenstergroesse', () => {
  const detail: KidsLevelDetail = {
    level: 1, theme: 'mate1',
    puzzles: [
      { id: 1, fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8' },
      { id: 2, fen: '6k1/8/6K1/8/8/8/1R6/R7 b - - 0 1', moves: 'g8h8 a1a8' },
    ],
  };
  let html = '';
  let css = '';

  beforeEach(async () => {
    localStorage.setItem('rookhub_lang', 'de');
    localStorage.removeItem('rh-kids-progress-v1');
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['level', 'levels', 'languageHint']);
    api.level.and.returnValue(of(detail));
    api.levels.and.returnValue(of([{ level: 1, theme: 'mate1', puzzleCount: 2 }]));
    api.languageHint.and.returnValue(of({ country: null, language: null }));
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
    // Echte deutsche Texte: die Kopfzeile bricht mit den (laengeren) Schluesseln frueher um als in der App.
    TestBed.inject(TranslateService).setTranslation('de', await germanTexts());
    TestBed.inject(TranslateService).use('de');
    const f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
    await TestBed.inject(Router).navigateByUrl('/levels/1');
    f.detectChanges();
    f.detectChanges();
    html = (f.nativeElement as HTMLElement).outerHTML;
    css = pageCss();
    expect(html).toContain('class="board"');
  });

  afterEach(() => { localStorage.removeItem('rookhub_lang'); localStorage.removeItem('rh-kids-progress-v1'); });

  /** Kopfzeile (Zurueck, Titel, Punkte) beginnt buendig mit dem Brett — in jeder Ansicht. */
  function expectHeadAligned(l: { board: Box; head: Box }, label: string): void {
    expect(Math.abs(l.head.left - l.board.left)).withContext(`${label}: Kopfzeile ${l.head.left} / Brett ${l.board.left}`)
      .toBeLessThanOrEqual(2);
  }

  for (const [w, h] of [[768, 1024], [810, 1080], [820, 1180], [834, 1194], [1024, 1366]] as const) {
    it(`Tablet hochkant ${w}×${h}: Aufgabe ueber dem Brett, das Brett ist gross`, () => {
      const l = layoutAt(html, css, w, h);
      expect(l.task.bottom).withContext('Aufgabe ueber dem Brett').toBeLessThanOrEqual(l.board.top);
      expect(l.board.width).withContext('Brettbreite').toBeGreaterThanOrEqual(600);
      expectHeadAligned(l, `${w}×${h}`);
    });
  }

  for (const [w, h] of [[390, 844], [760, 1024]] as const) {
    it(`Handy/schmal hochkant ${w}×${h}: untereinander wie bisher`, () => {
      const l = layoutAt(html, css, w, h);
      expect(l.task.bottom).toBeLessThanOrEqual(l.board.top);
      expect(l.board.width).toBeCloseTo(Math.min(0.92 * w, 0.7 * h, 640), 0);
      expectHeadAligned(l, `${w}×${h}`);
    });
  }

  for (const [w, h] of [[844, 390], [915, 412], [667, 375], [740, 360]] as const) {
    it(`Handy quer ${w}×${h}: Aufgabe neben dem Brett, die unterste Reihe bleibt im Bild`, () => {
      const l = layoutAt(html, css, w, h);
      expect(l.task.left).withContext('Aufgabe rechts neben dem Brett').toBeGreaterThanOrEqual(l.board.right);
      // Das Brett ist quadratisch: Oberkante + Breite = Unterkante der untersten Reihe.
      expect(l.board.top + l.board.width).withContext('Unterkante des Bretts').toBeLessThanOrEqual(h);
      expect(l.board.width).withContext('Brettbreite').toBeGreaterThanOrEqual(240);
      expect(l.board.right).withContext('nichts ragt rechts hinaus').toBeLessThanOrEqual(w);
      expectHeadAligned(l, `${w}×${h}`);
    });
  }

  for (const [w, h, board] of [[1024, 768, 524], [1280, 800, 630], [1440, 900, 730]] as const) {
    it(`PC/Tablet quer ${w}×${h}: nebeneinander, Brett ${board} px wie bisher`, () => {
      const l = layoutAt(html, css, w, h);
      expect(l.task.left).toBeGreaterThanOrEqual(l.board.right);
      expect(l.board.width).toBeCloseTo(board, 0);
      expectHeadAligned(l, `${w}×${h}`);
    });
  }
});
