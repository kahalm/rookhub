import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { PositionSetupComponent } from './position-setup.component';

describe('PositionSetupComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [PositionSetupComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PositionSetupComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('d7: „Brett leeren“/„Grundstellung“ stehen beschriftet im ⋮ statt als lose ✕; hideActions blendet die Knopfzeile aus', async () => {
    await TestBed.configureTestingModule({
      imports: [PositionSetupComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(PositionSetupComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const toolIcons = Array.from(el.querySelectorAll('.ps-tools mat-icon')).map(i => i.textContent?.trim());
    expect(toolIcons).not.toContain('clear');
    expect(toolIcons).toContain('more_vert');
    expect(el.querySelector('.ps-actions')).withContext('Knopfzeile ohne hideActions').toBeTruthy();
    (el.querySelector('.ps-more') as HTMLElement).click();
    fixture.detectChanges();
    const clear = document.querySelector('.cdk-overlay-container .ps-clear') as HTMLElement;
    expect(clear?.textContent).toContain('analysis.setup.clear');
    expect(clear.querySelector('mat-icon')?.textContent?.trim()).toBe('layers_clear');
    expect(document.querySelector('.cdk-overlay-container .ps-startpos')?.textContent).toContain('analysis.setup.startpos');
    clear.click();
    fixture.componentRef.setInput('hideActions', true);
    fixture.detectChanges();
    expect(el.querySelector('.ps-actions')).toBeNull();
  });

  // d8-rk-stellung-korrigieren-mobil-symbolzeile [d7 nicht behoben]: im Handy-Dialog bleiben ≈ 318 px — Papierkorb,
  // „Weiß | Schwarz“, Drehen und ⋮ stehen in EINER Zeile (Kurztexte), breit bleiben die Langtexte.
  for (const lang of ['de', 'en', 'hr', 'hu']) {
    it(`d8 (${lang}): Werkzeugzeile bricht bei 318 px nicht um, Langtext als aria-label`, async () => {
      await TestBed.configureTestingModule({
        imports: [PositionSetupComponent],
        providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: lang })],
      }).compileComponents();
      const res = await fetch(`/i18n/${lang}.json`).then(r => r.ok ? r : fetch(`/base/i18n/${lang}.json`));
      const tr = TestBed.inject(TranslateService);
      tr.setTranslation(lang, await res.json());
      tr.use(lang);
      const fixture = TestBed.createComponent(PositionSetupComponent);
      const el = fixture.nativeElement as HTMLElement;
      el.style.display = 'block';
      el.style.width = '318px';
      document.body.appendChild(el);
      try {
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
        const items = Array.from(el.querySelectorAll<HTMLElement>('.ps-tools > button, .ps-tools > mat-button-toggle-group'));
        expect(items.length).toBe(4);
        const tops = items.map(i => i.getBoundingClientRect().top);
        expect(Math.max(...tops) - Math.min(...tops)).withContext(`Zeilen-Oberkanten ${tops.join(', ')}`).toBeLessThan(8);
        const toggles = Array.from(el.querySelectorAll<HTMLElement>('mat-button-toggle'));
        expect(toggles[0].querySelector<HTMLElement>('.ps-short')!.offsetWidth).toBeGreaterThan(0);
        expect(toggles[0].querySelector<HTMLElement>('.ps-long')!.offsetWidth).toBe(0);
        expect(toggles[0].querySelector('button')!.getAttribute('aria-label')).toBe(tr.instant('common.whiteToMove'));
        expect(toggles[1].querySelector('button')!.getAttribute('aria-label')).toBe(tr.instant('common.blackToMove'));

        el.style.width = '520px';
        await new Promise(r => requestAnimationFrame(() => r(null)));
        expect(toggles[0].querySelector<HTMLElement>('.ps-long')!.offsetWidth).withContext('breit: Langtext').toBeGreaterThan(0);
        expect(toggles[0].querySelector<HTMLElement>('.ps-short')!.offsetWidth).toBe(0);
      } finally {
        el.remove();
      }
    });
  }
});
