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
});
