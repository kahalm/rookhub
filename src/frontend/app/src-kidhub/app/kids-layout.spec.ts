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
import { KidsApiService, KidsCourseLine, KidsLevelDetail, KidsTheme } from './core/kids-api.service';

/** Echte Texte einer Sprache (public/i18n) — die Kopfzeile bricht mit den (laengeren) Schluesseln anders um. */
async function texts(lang: string): Promise<TranslationObject> {
  for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
    const res = await fetch(url);
    if (res.ok) return res.json();
  }
  throw new Error(`${lang}.json nicht gefunden`);
}

interface Box { left: number; top: number; right: number; bottom: number; width: number; height: number }

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

interface Layout {
  board: Box;
  task: Box;
  head: Box;
  /** Die Kinder der Titelzeile (Zurueck, Titel, Punkte/Herzen/Zaehler) in Dokumentreihenfolge. */
  headItems: { name: string; box: Box }[];
  /** Ein Fortschrittspunkt der Stufe (falls vorhanden) — gequetscht wird er zum Oval. */
  dot: Box | null;
  /** Trifft ein Tipp 4 px ueber bzw. unter dem Rueckweg-Knopf noch den Knopf? (Tippflaeche groesser als die Pille) */
  backReach: { above: boolean; below: boolean };
}

/**
 * Kopiert die gerenderte Seite samt Stilen in einen iframe von `w`×`h` — dort gelten Media-Queries,
 * vh und vw DIESES Fensters. Scrollbalken liegen wie am Handy/Tablet ueber dem Inhalt. Krumme Masse (599,5 px)
 * rundet der Testbrowser (Pixeldichte 1) auf ganze Pixel; mit `zoom: 2` am iframe bleibt die halbe Stelle stehen.
 */
function layoutAt(page: Page, w: number, h: number): Layout {
  const frame = document.createElement('iframe');
  const zoom = Number.isInteger(w) && Number.isInteger(h) ? 1 : 2;
  frame.style.cssText = `position: fixed; left: ${-w - 50}px; top: 0; width: ${w}px; height: ${h}px; border: 0; zoom: ${zoom};`;
  document.body.appendChild(frame);
  try {
    const doc = frame.contentDocument!;
    doc.open();
    doc.write(`<!doctype html><html><head><style>${page.css}</style><style>html { scrollbar-width: none; }</style></head>`
      + `<body>${page.html}</body></html>`);
    doc.close();
    const rect = (el: Element): Box => {
      const r = el.getBoundingClientRect();
      return { left: r.left, top: r.top, right: r.right, bottom: r.bottom, width: r.width, height: r.height };
    };
    const box = (selector: string): Box => rect(doc.querySelector(selector)!);
    const head = doc.querySelector('.head')!;
    const dot = doc.querySelector('.dots li');
    const back = doc.querySelector('.head .back')!;
    const b = rect(back);
    const hits = (y: number) => doc.elementFromPoint(b.left + b.width / 2, y)?.closest('.back') === back;
    return {
      board: box('.board'), task: box('.task-slot'), head: rect(head),
      headItems: Array.from(head.children).map(el => ({ name: el.className || el.tagName.toLowerCase(), box: rect(el) })),
      dot: dot ? rect(dot) : null,
      backReach: { above: hits(b.top - 4), below: hits(b.bottom + 4) },
    };
  } finally {
    frame.remove();
  }
}

interface Page { html: string; css: string }

/** Echte Stufen haben zehn Aufgaben (`KidsCurriculum.PuzzlesPerLevel`) — zehn Punkte in der Kopfzeile. */
const PUZZLES_PER_LEVEL = 10;

const PUZZLES = [
  { fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: 'g4g3 b8h8' },
  { fen: '6k1/8/6K1/8/8/8/1R6/R7 b - - 0 1', moves: 'g8h8 a1a8' },
];

function levelDetail(level: number, theme: KidsTheme): KidsLevelDetail {
  return {
    level, theme,
    puzzles: Array.from({ length: PUZZLES_PER_LEVEL }, (_, i) => ({ id: i + 1, ...PUZZLES[i % 2] })),
  };
}

/** Kurslinien mit einem langen Buchtitel (der h1 der Kurs-Kopfzeile). */
function courseLines(bookTitle: string, count: number): KidsCourseLine[] {
  return Array.from({ length: count }, (_, i) => ({
    id: i + 1, bookTitle, ...PUZZLES[i % 2], startPly: 0, title: `Aufgabe ${i + 1}`, chapter: 'Kapitel 1',
    comment: null, moveComments: null, altMoves: null,
  }));
}

/**
 * Rendert die echte Huelle mit der Seite `url` in `lang` (Stufe `level` freigeschaltet) und gibt HTML und
 * Stile fuer `layoutAt` zurueck.
 */
async function renderPage(url: string, lang: string,
  data: { level?: KidsLevelDetail; course?: KidsCourseLine[] }): Promise<Page> {
  localStorage.setItem('rookhub_lang', lang);
  // Die Stufe davor geschafft — sonst zeigt eine spaete Stufe nur das Schloss.
  const before = (data.level?.level ?? 1) - 1;
  localStorage.setItem('rh-kids-progress-v1', JSON.stringify({
    levels: before > 0 ? { [before]: { stars: 1, runIndex: 0, runMistakes: 0 } } : {}, courses: {},
  }));
  const api = jasmine.createSpyObj<KidsApiService>('KidsApiService',
    ['level', 'levels', 'languageHint', 'coursePuzzles', 'endlessBatch']);
  const level = data.level ?? levelDetail(1, 'mate1');
  api.level.and.returnValue(of(level));
  api.levels.and.returnValue(of([{ level: level.level, theme: level.theme, puzzleCount: level.puzzles.length }]));
  api.languageHint.and.returnValue(of({ country: null, language: null }));
  api.coursePuzzles.and.returnValue(of(data.course ?? []));
  api.endlessBatch.and.callFake((windows: unknown[]) =>
    of(windows.map((_, i) => ({ id: 100 + i, ...PUZZLES[i % 2], rating: 700 }))));
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
  TestBed.inject(TranslateService).setTranslation(lang, await texts(lang));
  TestBed.inject(TranslateService).use(lang);
  const f = TestBed.createComponent(KidHubAppComponent);
  f.detectChanges();
  await TestBed.inject(Router).navigateByUrl(url);
  f.detectChanges();
  f.detectChanges();
  const html = (f.nativeElement as HTMLElement).outerHTML;
  expect(html).toContain('class="board"');
  return { html, css: pageCss() };
}

/**
 * Brett und Aufbau der Aufgabenseite je Fenstergroesse (Codereview 2026-09-29, UX-028). Gerendert wird
 * die echte Seite (Huelle + Stufe/Endlos/Kurs); `layoutAt` misst sie in Fenstern der gaengigen Geraete.
 * Gemeldet: am iPad hochkant (768–834 px) stand das Brett mit 300 px neben einer halbleeren Seite
 * (760 px bekam 640 px), am Handy quer lief die unterste Reihe aus dem Bild.
 */
describe('KidHub: Aufgabenseite je Fenstergroesse', () => {
  afterEach(() => { localStorage.removeItem('rookhub_lang'); localStorage.removeItem('rh-kids-progress-v1'); });

  /** Kopfzeile (Zurueck, Titel, Punkte) beginnt buendig mit dem Brett — in jeder Ansicht. */
  function expectHeadAligned(l: Layout, label: string): void {
    expect(Math.abs(l.head.left - l.board.left)).withContext(`${label}: Kopfzeile ${l.head.left} / Brett ${l.board.left}`)
      .toBeLessThanOrEqual(2);
  }

  /** Handy quer: Aufgabe neben dem Brett, die unterste Reihe im Bild, nichts ragt rechts hinaus. */
  function expectLandscape(l: Layout, w: number, h: number, label: string): void {
    expect(l.task.left).withContext(`${label}: Aufgabe rechts neben dem Brett`).toBeGreaterThanOrEqual(l.board.right);
    // Das Brett ist quadratisch: Oberkante + Breite = Unterkante der untersten Reihe.
    expect(l.board.top + l.board.width).withContext(`${label}: Unterkante des Bretts`).toBeLessThanOrEqual(h);
    expect(l.board.width).withContext(`${label}: Brettbreite`).toBeGreaterThanOrEqual(240);
    expect(l.board.right).withContext(`${label}: nichts ragt rechts hinaus`).toBeLessThanOrEqual(w);
    expectHeadAligned(l, label);
  }

  /**
   * Die Titelzeile bleibt quer EINZEILIG — das Brett rechnet mit genau einer Zeile darueber. Ein zu langer Titel
   * wird gekuerzt; Zurueck, Punkte, Herzen und Zaehler bleiben ganz, ueberlappen nicht und stehen in der Zeile.
   */
  function expectHeadOneLine(l: Layout, label: string): void {
    expect(l.head.height).withContext(`${label}: Titelzeile ${l.head.height} px hoch`).toBeLessThanOrEqual(36);
    let prevRight = l.head.left;
    for (const { name, box } of l.headItems) {
      expect(box.left).withContext(`${label}: ${name} ueberlappt seinen Vorgaenger`).toBeGreaterThanOrEqual(prevRight - 0.5);
      expect(box.right).withContext(`${label}: ${name} ragt aus der Titelzeile`).toBeLessThanOrEqual(l.head.right + 0.5);
      prevRight = box.right;
    }
    const title = l.headItems.find(i => i.name === 'h1');
    expect(title?.box.width ?? 0).withContext(`${label}: vom Titel bleibt etwas zu lesen`).toBeGreaterThanOrEqual(90);
    // Die Pille ist hier flacher (die Zeile darf nicht hoeher werden) — getippt wird sie trotzdem auf 44 px
    // (Codereview 2026-09-29, F7-012).
    const back = l.headItems.find(i => i.name.split(' ').includes('back'));
    expect(back?.box.height ?? 0).withContext(`${label}: Rueckweg-Pille`).toBeGreaterThanOrEqual(34);
    expect(l.backReach).withContext(`${label}: Tippflaeche des Rueckwegs ober-/unterhalb der Pille`)
      .toEqual({ above: true, below: true });
    if (l.dot) expect(l.dot.width).withContext(`${label}: Punkt rund statt gequetscht`).toBeGreaterThanOrEqual(l.dot.height - 0.5);
  }

  for (const [w, h] of [[768, 1024], [810, 1080], [820, 1180], [834, 1194], [1024, 1366]] as const) {
    it(`Tablet hochkant ${w}×${h}: Aufgabe ueber dem Brett, das Brett ist gross`, async () => {
      const l = layoutAt(await renderPage('/levels/1', 'de', {}), w, h);
      expect(l.task.bottom).withContext('Aufgabe ueber dem Brett').toBeLessThanOrEqual(l.board.top);
      expect(l.board.width).withContext('Brettbreite').toBeGreaterThanOrEqual(600);
      expectHeadAligned(l, `${w}×${h}`);
    });
  }

  for (const [w, h] of [[390, 844], [760, 1024]] as const) {
    it(`Handy/schmal hochkant ${w}×${h}: untereinander wie bisher`, async () => {
      const l = layoutAt(await renderPage('/levels/1', 'de', {}), w, h);
      expect(l.task.bottom).toBeLessThanOrEqual(l.board.top);
      expect(l.board.width).toBeCloseTo(Math.min(0.92 * w, 0.7 * h, 640), 0);
      expectHeadAligned(l, `${w}×${h}`);
    });
  }

  /** Gaengige Handys quer: iPhone 6/7/8/SE, X/11 Pro/12 mini, Androids mit 360 dp, die Geraete aus dem Fund. */
  const PHONES_LANDSCAPE = [[667, 375], [812, 375], [740, 360], [720, 360], [780, 360], [640, 360], [844, 390], [915, 412]] as const;

  /**
   * Die laengsten Titelzeilen der echten Leiter (`KidsCurriculum.Levels`) je Sprache, mit zehn Punkten daneben:
   * auf Ungarisch schon Stufe 1. Vorher brach die Kopfzeile um und schob das Brett 18 px unter den Fensterrand.
   */
  const LONG_TITLES: [string, number, KidsTheme][] = [
    ['de', 1, 'mate1'],       // „Stufe 1 · Matt in 1" — der kurze Fall
    ['hu', 1, 'mate1'],       // „1. szint · Matt 1 lépésben"
    ['hu', 25, 'discovered'], // „25. szint · Felfedett támadás"
    ['hu', 40, 'mate2'],      // „40. szint · Matt 2 lépésben"
    ['en', 25, 'discovered'], // „Level 25 · Discovered attack"
    ['hr', 23, 'capture'],    // „Razina 23 · Slobodna figura"
    ['hr', 33, 'discovered'], // „Razina 33 · Otkriveni napad"
    ['de', 31, 'capture'],    // „Stufe 31 · Freie Figur"
    ['de', 29, 'promote'],    // „Stufe 29 · Umwandeln"
  ];
  for (const [lang, level, theme] of LONG_TITLES) {
    it(`Handy quer, Stufe ${level} (${theme}) auf ${lang} mit ${PUZZLES_PER_LEVEL} Punkten: einzeilige Kopfzeile, das ganze Brett im Bild`, async () => {
      const page = await renderPage(`/levels/${level}`, lang, { level: levelDetail(level, theme) });
      for (const [w, h] of PHONES_LANDSCAPE) {
        const l = layoutAt(page, w, h);
        expectLandscape(l, w, h, `${lang} ${w}×${h}`);
        expectHeadOneLine(l, `${lang} ${w}×${h}`);
      }
    });
  }

  for (const lang of ['de', 'hu', 'hr']) {
    it(`Handy quer, Endlos auf ${lang}: einzeilige Kopfzeile, das ganze Brett im Bild`, async () => {
      const page = await renderPage('/endless', lang, {});
      for (const [w, h] of PHONES_LANDSCAPE) {
        const l = layoutAt(page, w, h);
        expectLandscape(l, w, h, `${lang} ${w}×${h}`);
        expectHeadOneLine(l, `${lang} ${w}×${h}`);
      }
    });
  }

  it('Handy quer, Kurs mit langem Titel: einzeilige Kopfzeile, das ganze Brett im Bild', async () => {
    const page = await renderPage('/courses/7', 'hr', {
      course: courseLines('Prvi koraci u šahu – taktika za mlade igrače, svezak 2: viljuška, vezivanje i ražanj', 24),
    });
    for (const [w, h] of PHONES_LANDSCAPE) {
      const l = layoutAt(page, w, h);
      expectLandscape(l, w, h, `${w}×${h}`);
      expectHeadOneLine(l, `${w}×${h}`);
    }
  });

  for (const [w, h, board] of [[1024, 768, 524], [1280, 800, 630], [1440, 900, 730]] as const) {
    it(`PC/Tablet quer ${w}×${h}: nebeneinander, Brett ${board} px wie bisher`, async () => {
      const l = layoutAt(await renderPage('/levels/1', 'de', {}), w, h);
      expect(l.task.left).toBeGreaterThanOrEqual(l.board.right);
      expect(l.board.width).toBeCloseTo(board, 0);
      expectHeadAligned(l, `${w}×${h}`);
    });
  }

  /**
   * Gebrochene Fenstermasse (Zoom, krumme Pixeldichte) zwischen den Grenzen der beiden Ansichten: vorher griff dort
   * die PC-Formel mit der 300-px-Untergrenze (599 < Breite < 600, 500 < Hoehe < 501), bei 599,5×360 lief das Raster
   * waagrecht ueber.
   */
  for (const [w, h] of [[599.5, 360], [700, 500.5]] as const) {
    it(`Zwischen den Grenzen ${w}×${h}: untereinander, nichts laeuft waagrecht ueber`, async () => {
      const l = layoutAt(await renderPage('/levels/1', 'de', {}), w, h);
      expect(l.task.bottom).withContext('Aufgabe ueber dem Brett').toBeLessThanOrEqual(l.board.top);
      expect(l.board.right).withContext('Brett rechts im Bild').toBeLessThanOrEqual(w);
      expect(Math.max(l.task.right, l.head.right)).withContext('nichts ragt rechts hinaus').toBeLessThanOrEqual(w + 0.5);
    });
  }
});
