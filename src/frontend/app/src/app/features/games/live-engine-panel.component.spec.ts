import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { LiveEnginePanelComponent } from './live-engine-panel.component';
import { LiveEngineSession } from './live-engine-session';

describe('LiveEnginePanelComponent', () => {
  // Gemeldet 2026-10-06: beim Zugwechsel leert die Sitzung die Linien — ohne feste Zeilenzahl sprang die Seite.
  it('zeigt immer drei Zeilen, auch solange noch keine Linie da ist', () => {
    const lines = signal<{ evalText: string; san: string; positive: boolean }[]>([]);
    const session = {
      fallback: () => false, engineName: () => null, depth: () => 0, isLc0: () => false, nodes: () => 0,
      variation: () => [], canRedo: () => false, variationSan: () => '', lines, fen: (g: string) => g,
    } as unknown as LiveEngineSession;
    TestBed.configureTestingModule({ imports: [LiveEnginePanelComponent], providers: [provideTranslateService({ fallbackLang: 'de' }), provideHttpClient(), provideHttpClientTesting()] });
    const f = TestBed.createComponent(LiveEnginePanelComponent);
    f.componentRef.setInput('session', session);
    f.componentRef.setInput('gameFen', 'start');
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelectorAll('.lines li').length).toBe(3);
    expect(el.querySelectorAll('.lines li.empty').length).toBe(3);
    lines.set([{ evalText: '+0.3', san: '1. e4 e5', positive: true }]);
    f.detectChanges();
    expect(el.querySelectorAll('.lines li').length).toBe(3);
    expect(el.querySelectorAll('.lines li.empty').length).toBe(2);
  });

  // Nebenbefund Nachprüfung v1: Engine-Linien in der Oberflächensprache (DESIGN 5.14), nur Anzeige.
  it('zeigt die Linien mit deutschen Figurenbuchstaben', () => {
    const lines = signal([{ evalText: '+0.3', san: '1. Nf3 d5 2. g3 Bb4+', positive: true }]);
    const session = {
      fallback: () => false, engineName: () => null, depth: () => 0, isLc0: () => false, nodes: () => 0,
      variation: () => [], canRedo: () => false, variationSan: () => '', lines, fen: (g: string) => g,
    } as unknown as LiveEngineSession;
    TestBed.configureTestingModule({ imports: [LiveEnginePanelComponent], providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(TranslateService).use('de');
    const f = TestBed.createComponent(LiveEnginePanelComponent);
    f.componentRef.setInput('session', session);
    f.componentRef.setInput('gameFen', 'start');
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelector('.lines .line-san')!.textContent!.trim()).toBe('1. Sf3 d5 2. g3 Lb4+');
  });
});
