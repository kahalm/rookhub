import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { PdfEngineLoader } from './pdf-engine';
import { MAX_CANVAS_PIXELS, PDF_PAGE_CACHE_SIZE, PdfViewerComponent, cappedCanvasRatio, evictOldestPage } from './pdf-viewer.component';

describe('PdfViewerComponent', () => {
  it('„Breite einpassen“ passt jede Seite für sich ein (Querformat-Doppelseiten ragen nicht hinaus); Hand-Zoom gilt für alle', () => {
    TestBed.configureTestingModule({
      imports: [PdfViewerComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }),
        { provide: PdfEngineLoader, useValue: { load: () => new Promise(() => {}), documentParams: (url: string) => ({ url }) } }],
    });
    const c = TestBed.createComponent(PdfViewerComponent).componentInstance;
    const v = c as unknown as { fitZoom(w: number): number; scaleFor(s: { width: number }): number };
    v.fitZoom = (w: number) => 800 / w;
    c.zoom = null;
    expect(v.scaleFor({ width: 400 })).toBe(2);      // Hochformat
    expect(v.scaleFor({ width: 800 })).toBe(1);      // Doppelseite: kleiner, passt trotzdem
    c.zoom = 1.5;
    expect(v.scaleFor({ width: 400 })).toBe(1.5);
    expect(v.scaleFor({ width: 800 })).toBe(1.5);
  });

  it('eine Seite scheitert nach dem Öffnen (z. B. Ausweis abgelaufen): pageFailed statt still weiß (F2-005)', async () => {
    TestBed.configureTestingModule({
      imports: [PdfViewerComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }),
        { provide: PdfEngineLoader, useValue: { load: () => new Promise(() => {}), documentParams: (url: string) => ({ url }) } }],
    });
    const c = TestBed.createComponent(PdfViewerComponent).componentInstance;
    c.zoom = 1;
    const v = c as unknown as { doc: unknown; renderPage(s: unknown, cv: HTMLCanvasElement): Promise<void> };
    v.doc = { getPage: () => Promise.reject({ status: 401 }) };
    const seen: unknown[] = [];
    c.pageFailed.subscribe(e => seen.push(e));
    const slot = { index: 2, width: 100, height: 100, rendered: 0, rendering: null };
    await v.renderPage(slot, document.createElement('canvas'));
    expect(seen).toEqual([{ status: 401 }]);
    expect(slot.rendered).toBe(0);
  });
});

describe('evictOldestPage (F2-015)', () => {
  it('verdrängt bei vollem Cache die älteste Seite und gibt sie mit cleanup() frei; die gerade geholte bleibt', () => {
    const cleaned: number[] = [];
    const cache = new Map<number, { cleanup(): boolean }>();
    for (let i = 1; i <= PDF_PAGE_CACHE_SIZE; i++) cache.set(i, { cleanup: () => { cleaned.push(i); return true; } });
    evictOldestPage(cache, PDF_PAGE_CACHE_SIZE);
    expect(cleaned).toEqual([]);                        // noch nicht voll
    cache.set(99, { cleanup: () => { cleaned.push(99); return true; } });
    evictOldestPage(cache, 99);
    expect(cleaned).toEqual([1]);
    expect(cache.has(1)).toBeFalse();
    expect(cache.size).toBe(PDF_PAGE_CACHE_SIZE);
  });
});

describe('cappedCanvasRatio (F2-017)', () => {
  it('lässt kleine Leinwände unverändert und deckelt große auf MAX_CANVAS_PIXELS', () => {
    expect(cappedCanvasRatio(595, 842, 2)).toBe(2);                  // A4 bei Zoom 1, dpr 2: 2 Mio. px
    const css = { w: 595 * 4, h: 842 * 4 };                           // Zoom 4
    const r = cappedCanvasRatio(css.w, css.h, 3);                     // dpr 3 → ungedeckelt 72 Mio. px
    expect(r).toBeLessThan(3);
    expect(Math.floor(css.w * r) * Math.floor(css.h * r)).toBeLessThanOrEqual(MAX_CANVAS_PIXELS);
    expect(cappedCanvasRatio(0, 0, 3)).toBe(3);                       // noch ohne Maße
  });

  it('kein Zeichenkontext: als Seitenfehler gemeldet statt still weiß', async () => {
    TestBed.configureTestingModule({
      imports: [PdfViewerComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }),
        { provide: PdfEngineLoader, useValue: { load: () => new Promise(() => {}), documentParams: (url: string) => ({ url }) } }],
    });
    const c = TestBed.createComponent(PdfViewerComponent).componentInstance;
    c.zoom = 1;
    const page = { getViewport: ({ scale }: { scale: number }) => ({ width: 100 * scale, height: 100 * scale }), render: () => ({ promise: Promise.resolve() }) };
    const v = c as unknown as { doc: unknown; renderPage(s: unknown, cv: HTMLCanvasElement): Promise<void> };
    v.doc = { getPage: () => Promise.resolve(page) };
    const canvas = document.createElement('canvas');
    spyOn(canvas, 'getContext').and.returnValue(null);
    const seen: unknown[] = [];
    c.pageFailed.subscribe(e => seen.push(e));
    await v.renderPage({ index: 2, width: 100, height: 100, rendered: 0, rendering: null }, canvas);
    expect(seen.length).toBe(1);
  });
});
