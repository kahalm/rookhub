import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { KidsHomeComponent } from './kids-home.component';
import { KidsProgressStore } from '../../core/kids-progress.store';

/**
 * Codereview 2026-09-29, A10-003: 20 Kinder öffnen im Vereinsraum hinter EINER NAT-Adresse KidHub — ab dem 16. kam
 * auf `levels` ein 429, und die Startseite zeigte das Fehlerbild statt der Stufen. Mit dem ECHTEN KidsApiService.
 */
describe('KidsHomeComponent', () => {
  const KEY = 'rh-kids-progress-v1';
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.removeItem(KEY);
    TestBed.configureTestingModule({
      imports: [KidsHomeComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    localStorage.removeItem(KEY);
  });

  it('holt die Stufen nach einem 429 einmal nach, statt das Fehlerbild zu zeigen', fakeAsync(() => {
    const f = TestBed.createComponent(KidsHomeComponent);
    f.detectChanges();
    http.expectOne(r => r.url === '/api/kids/courses').flush([]);
    http.expectOne('/api/kids/levels').flush(null, { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '5' } });
    f.detectChanges();
    expect(f.componentInstance.failed()).toBeFalse();

    tick(5000);
    http.expectOne('/api/kids/levels').flush([{ level: 1, theme: 'mate1', puzzleCount: 10 }]);
    f.detectChanges();

    expect(f.componentInstance.failed()).toBeFalse();
    expect(f.componentInstance.current()).toBe(1);
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('a.go')).not.toBeNull();
    expect(el.querySelector('kid-error')).toBeNull();
  }));

  it('ein zweites 429 zeigt das Fehlerbild (nur EIN Nachholversuch)', fakeAsync(() => {
    const f = TestBed.createComponent(KidsHomeComponent);
    f.detectChanges();
    http.expectOne(r => r.url === '/api/kids/courses').flush([]);
    http.expectOne('/api/kids/levels').flush(null, { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '1' } });
    tick(1000);
    http.expectOne('/api/kids/levels').flush(null, { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '1' } });
    f.detectChanges();
    expect(f.componentInstance.failed()).toBeTrue();
    expect((f.nativeElement as HTMLElement).querySelector('kid-error')).not.toBeNull();
  }));

  /** Codereview 2026-09-29, F7-011: im Fehlerfall stand nur ein Satz da — ohne Knopf, mit dem das Kind weiterkommt. */
  it('Fehlerbild mit „Nochmal": holt die Stufen neu, danach steht der Startknopf da', fakeAsync(() => {
    const f = TestBed.createComponent(KidsHomeComponent);
    f.detectChanges();
    http.expectOne(r => r.url === '/api/kids/courses').flush([]);
    http.expectOne('/api/kids/levels').flush(null, { status: 500, statusText: 'Server Error' });
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const again = el.querySelector<HTMLButtonElement>('kid-error button.again');
    expect(again).withContext('Knopf „Nochmal"').not.toBeNull();

    again!.click();
    // Die Kurse kamen schon — die liefert der Zwischenspeicher, neu geholt werden nur die Stufen.
    http.expectOne('/api/kids/levels').flush([{ level: 1, theme: 'mate1', puzzleCount: 10 }]);
    f.detectChanges();

    expect(f.componentInstance.failed()).toBeFalse();
    expect(el.querySelector('kid-error')).toBeNull();
    expect(el.querySelector('a.go')).not.toBeNull();
  }));

  /** Codereview 2026-09-29, F7-014: der Stand behaelt Stufen, die ein Neuaufbau mit weniger Stufen nicht mehr kennt — die
   *  Kachel zeigte „4 von 3 Stufen geschafft". */
  it('zaehlt nur die geschafften Stufen der aktuellen Leiter', fakeAsync(() => {
    const progress = TestBed.inject(KidsProgressStore);
    for (const l of [1, 2, 3, 4]) progress.completeRun(l);
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { kids: { home: { levelsDone: '{{done}} of {{total}} levels' } } });
    translate.use('en');

    const f = TestBed.createComponent(KidsHomeComponent);
    f.detectChanges();
    http.expectOne(r => r.url === '/api/kids/courses').flush([]);
    http.expectOne('/api/kids/levels').flush([
      { level: 1, theme: 'mate1', puzzleCount: 10 }, { level: 2, theme: 'mate1', puzzleCount: 10 },
      { level: 3, theme: 'capture', puzzleCount: 10 },
    ]);
    f.detectChanges();

    expect(f.componentInstance.done()).toBe(3);
    expect((f.nativeElement as HTMLElement).querySelector('.tile.puzzles .meta')!.textContent).toContain('3 of 3 levels');
  }));

  /** UI-Sweep 2026-10-11 (r2-k-course-tile): die Kurs-Kachel nennt den Stand wie die Puzzles-Kachel. */
  it('ein Kurs: „Titel · 8 von 287" mit Balken; mehrere: wie viele angefangen sind', fakeAsync(() => {
    const progress = TestBed.inject(KidsProgressStore);
    for (let id = 1; id <= 8; id++) progress.recordCourseSolved(5, id);
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { kids: { home: {
      courseProgress: '{{title}} · {{done}} of {{total}}', coursesStarted: '{{started}} of {{count}} started', courseCount: '{{count}} courses',
    } } });
    translate.use('en');

    const f = TestBed.createComponent(KidsHomeComponent);
    f.detectChanges();
    http.expectOne(r => r.url === '/api/kids/courses').flush([{ bookId: 5, title: 'First endgames', description: null, puzzleCount: 287 }]);
    http.expectOne('/api/kids/levels').flush([]);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('.tile.courses .meta')!.textContent).toContain('First endgames · 8 of 287');
    expect(el.querySelector<HTMLElement>('.tile.courses .tile-bar span')!.style.width).toBe('2.8%');

    f.componentInstance.courses.set([
      { bookId: 5, title: 'First endgames', description: null, puzzleCount: 287 },
      { bookId: 6, title: 'Mates', description: null, puzzleCount: 20 },
    ]);
    f.detectChanges();
    expect(el.querySelector('.tile.courses .meta')!.textContent).toContain('1 of 2 started');
    expect(el.querySelector('.tile.courses .tile-bar')).toBeNull();
  }));

  /** UI-Sweep 2026-10-11 (r2-k-level-run): der Startknopf nennt bei einem angefangenen Durchgang die nächste Aufgabe. */
  it('angefangene Stufe: „Weiter mit Stufe n · Aufgabe m"', fakeAsync(() => {
    const progress = TestBed.inject(KidsProgressStore);
    progress.completeRun(1);
    for (let i = 0; i < 4; i++) progress.recordSolved(2, 0);
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { kids: { home: { continueAt: 'Level {{level}} · puzzle {{n}}', continue: 'Level {{level}}' } } });
    translate.use('en');

    const f = TestBed.createComponent(KidsHomeComponent);
    f.detectChanges();
    http.expectOne(r => r.url === '/api/kids/courses').flush([]);
    http.expectOne('/api/kids/levels').flush([
      { level: 1, theme: 'mate1', puzzleCount: 10 }, { level: 2, theme: 'mate1', puzzleCount: 10 },
    ]);
    f.detectChanges();

    expect((f.nativeElement as HTMLElement).querySelector('a.go')!.textContent).toContain('Level 2 · puzzle 5');
  }));

  /** Codereview 2026-09-29, UX-062: ohne Stufen fehlte der Startknopf ersatzlos, der Fehlersatz stand klein als letzte
   *  Zeile unter dem Speicherhinweis (am Handy bei y ≈ 680 px). */
  it('das Fehlerbild steht an der Stelle des Startknopfs, nicht unter dem Speicherhinweis', fakeAsync(() => {
    const f = TestBed.createComponent(KidsHomeComponent);
    f.detectChanges();
    http.expectOne(r => r.url === '/api/kids/courses').flush([]);
    http.expectOne('/api/kids/levels').flush(null, { status: 500, statusText: 'Server Error' });
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('.hero kid-error')).withContext('Fehlerkachel im Kopfbereich').not.toBeNull();
    expect(el.querySelectorAll('kid-error').length).toBe(1);
    const saved = el.querySelector('.saved')!;
    expect(saved.nextElementSibling).withContext('nichts mehr unter dem Speicherhinweis').toBeNull();
  }));

  /** Die gerenderte Seite samt aller Stile in einem iframe von `w`×`h` — dort gelten dessen Media-Queries. */
  function measureAt(host: HTMLElement, w: number, h: number) {
    const css = Array.from(document.styleSheets).map(sheet => {
      try { return Array.from(sheet.cssRules).map(r => r.cssText).join('\n'); } catch { return ''; }
    }).join('\n');
    const frame = document.createElement('iframe');
    frame.style.cssText = `position: fixed; left: ${-w - 50}px; top: 0; width: ${w}px; height: ${h}px; border: 0;`;
    document.body.appendChild(frame);
    try {
      const doc = frame.contentDocument!;
      doc.open();
      doc.write(`<!doctype html><html><head><style>${css}</style></head><body style="margin:0">${host.outerHTML}</body></html>`);
      doc.close();
      const tile = doc.querySelector('.tile')!;
      const r = tile.getBoundingClientRect();
      const win = frame.contentWindow!;
      return {
        tile: { width: r.width, height: r.height },
        icon: parseFloat(win.getComputedStyle(doc.querySelector('.tile .icon')!).fontSize),
        name: parseFloat(win.getComputedStyle(doc.querySelector('.tile .name')!).fontSize),
        hostHeight: doc.body.firstElementChild!.getBoundingClientRect().height,
      };
    } finally {
      frame.remove();
    }
  }

  /** UI-Sweep 2026-10-10 (k-start-space): am PC endeten die Kacheln bei y ≈ 550, darunter fast 500 px leer. */
  it('am PC: Inhalt senkrecht mittig, Kacheln ~360 × 260 mit 64-px-Symbol und 32-px-Titel; am Handy wie bisher', () => {
    const f = TestBed.createComponent(KidsHomeComponent);
    f.detectChanges();
    http.expectOne(r => r.url === '/api/kids/courses').flush([]);
    http.expectOne('/api/kids/levels').flush([{ level: 1, theme: 'mate1', puzzleCount: 10 }]);
    f.detectChanges();
    const host = f.nativeElement as HTMLElement;

    const pc = measureAt(host, 1440, 900);
    expect(pc.tile.height).toBeGreaterThanOrEqual(220);
    expect(pc.tile.width).toBeGreaterThanOrEqual(240);
    expect(pc.icon).toBe(64);
    expect(pc.name).toBe(32);
    expect(pc.hostHeight).withContext('fuellt die Hoehe zwischen Kopf- und Fusszeile').toBeGreaterThanOrEqual(900 - 140 - 1);

    const phone = measureAt(host, 390, 844);
    expect(phone.icon).toBeLessThan(64);
    expect(phone.tile.height).toBeLessThan(220);
  });

  /** UI-Sweep d2-k-home-tiles: mit Kurskachel vier Kacheln in EINER Reihe, am Laptop alle über der Falz. */
  it('am Laptop stehen alle vier Kacheln (mit Kurs) in einer Reihe über der Falz', () => {
    const f = TestBed.createComponent(KidsHomeComponent);
    f.detectChanges();
    http.expectOne(r => r.url === '/api/kids/courses').flush([{ bookId: 7, title: 'Erste Endspiele', description: null, puzzleCount: 287 }]);
    http.expectOne('/api/kids/levels').flush([{ level: 1, theme: 'mate1', puzzleCount: 10 }]);
    f.detectChanges();
    const host = f.nativeElement as HTMLElement;
    expect(host.querySelectorAll('.tile').length).toBe(4);

    const css = Array.from(document.styleSheets).map(sheet => {
      try { return Array.from(sheet.cssRules).map(r => r.cssText).join('\n'); } catch { return ''; }
    }).join('\n');
    const frame = document.createElement('iframe');
    frame.style.cssText = 'position: fixed; left: -1500px; top: 0; width: 1366px; height: 768px; border: 0;';
    document.body.appendChild(frame);
    try {
      const doc = frame.contentDocument!;
      doc.open();
      doc.write(`<!doctype html><html><head><style>${css}</style></head><body style="margin:0">${host.outerHTML}</body></html>`);
      doc.close();
      const rects = Array.from(doc.querySelectorAll('.tile')).map(t => t.getBoundingClientRect());
      expect(new Set(rects.map(r => Math.round(r.top))).size).withContext('eine Reihe').toBe(1);
      expect(Math.max(...rects.map(r => r.bottom))).withContext('über der Falz (ohne Kopf ~70 px)').toBeLessThanOrEqual(768 - 70);
    } finally {
      frame.remove();
    }
  });
});
