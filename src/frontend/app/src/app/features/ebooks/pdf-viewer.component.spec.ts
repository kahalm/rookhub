import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { PdfEngineLoader } from './pdf-engine';
import { PdfViewerComponent } from './pdf-viewer.component';

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
