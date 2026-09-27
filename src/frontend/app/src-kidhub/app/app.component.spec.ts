import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { SwUpdate } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { KidHubAppComponent, KIDS_LANGUAGES } from './app.component';
import { FORMAT_LOCALES, LocaleService } from '@rh/core/locale.service';

describe('KidHubAppComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [KidHubAppComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        {
          provide: SwUpdate,
          useValue: {
            isEnabled: false, versionUpdates: new Subject<unknown>(),
            unrecoverable: new Subject<unknown>(), checkForUpdate: () => Promise.resolve(false),
          },
        },
      ],
    });
  });

  afterEach(() => localStorage.removeItem('rookhub_lang'));

  function selected(f: { nativeElement: HTMLElement }): string {
    return (f.nativeElement.querySelector('footer select') as HTMLSelectElement).value;
  }

  /**
   * Gemeldet 2026-09-27: unten stand „Deutsch", die Seite sprach Englisch — die Liste zeigte ihren
   * ERSTEN Eintrag statt der aktiven Sprache, und ein Klick auf Deutsch aenderte nichts.
   */
  it('die Auswahl zeigt die Sprache, in der die Seite wirklich steht', async () => {
    localStorage.setItem('rookhub_lang', 'en');
    const f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    expect(TestBed.inject(TranslateService).currentLang()).toBe('en');
    expect(selected(f)).toBe('en');
  });

  it('eine Wahl gilt sofort und wird gespeichert', async () => {
    localStorage.setItem('rookhub_lang', 'en');
    const f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
    f.componentInstance.setLang('de');
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    expect(TestBed.inject(TranslateService).currentLang()).toBe('de');
    expect(localStorage.getItem('rookhub_lang')).toBe('de');
    expect(selected(f)).toBe('de');
  });

  it('eine Sprache ohne Kindertexte zeigt Deutsch, ohne die Wahl zu ueberschreiben', async () => {
    localStorage.setItem('rookhub_lang', 'fr');
    const f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    expect(TestBed.inject(TranslateService).currentLang()).toBe('de');
    expect(localStorage.getItem('rookhub_lang')).toBe('fr');
    expect(TestBed.inject(LocaleService).current).toBe('de');
  });

  it('bietet nur die vollstaendig uebersetzten Sprachen an', () => {
    expect(KIDS_LANGUAGES.map(l => l.code as string).sort()).toEqual([...FORMAT_LOCALES].sort());
  });

  it('verlinkt Impressum und Datenschutz', () => {
    const f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
    const hrefs = Array.from(f.nativeElement.querySelectorAll('footer a') as NodeListOf<HTMLAnchorElement>)
      .map(a => a.getAttribute('href'));
    expect(hrefs).toContain('/impressum');
    expect(hrefs).toContain('/privacy');
  });
});
