import { TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { SwUpdate } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { KidHubAppComponent } from './app.component';
import { kidhubConfig } from './app.config';
import { LEGAL_SITE } from '@rh/features/legal/legal-site';
import { PrivacyComponent } from '@rh/features/legal/privacy.component';
import { contrast, parseColor } from '@rh/testing/contrast';

@Component({ standalone: true, template: '<p class="page">Seite</p>' })
class BlankComponent {}

/**
 * Die Huelle der Kinderseite nach dem UI-Sweep 2026-10-10: Kopfzeile in der Spalte des Inhalts (k-logo),
 * Sprachwahl als Pille (k-lang-select), Datenschutz in der Kinder-Fassung (k-privacy).
 */
describe('KidHub-Huelle (UI-Sweep 2026-10-10)', () => {
  beforeEach(() => {
    localStorage.setItem('rookhub_lang', 'de');
    const legal = kidhubConfig.providers.find(p => (p as { provide?: unknown }).provide === LEGAL_SITE);
    TestBed.configureTestingModule({
      imports: [KidHubAppComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideRouter([
          { path: '', pathMatch: 'full', component: BlankComponent },
          { path: 'levels', component: BlankComponent },
          { path: 'levels/:level', component: BlankComponent },
          { path: 'privacy', component: PrivacyComponent },
        ]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        legal!,
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

  async function at(url: string) {
    const f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
    await TestBed.inject(Router).navigateByUrl(url);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  it('k-logo: die Kopfzeile ist so breit wie die Spalte der Seite und mittig', async () => {
    const f = await at('/levels');
    const top = (f.nativeElement as HTMLElement).querySelector<HTMLElement>('header.top')!;
    expect(getComputedStyle(top).maxWidth).toBe('980px');
    expect(top.style.getPropertyValue('--kid-page-width')).toBe('980px');

    await TestBed.inject(Router).navigateByUrl('/');
    f.detectChanges();
    expect(top.style.getPropertyValue('--kid-page-width')).toBe('1160px');

    await TestBed.inject(Router).navigateByUrl('/levels/2');
    f.detectChanges();
    expect(top.style.getPropertyValue('--kid-page-width')).toContain('--kid-row');
  });

  it('k-lang-select: Pille mit Globus und eigenem Pfeil, das native select bleibt bedienbar', async () => {
    const f = await at('/');
    const el = f.nativeElement as HTMLElement;
    const pill = el.querySelector<HTMLElement>('footer label.lang')!;
    const select = pill.querySelector('select')!;
    expect(pill.querySelector('.globe')?.textContent).toContain('🌐');
    expect(pill.querySelector('.caret')?.textContent).toContain('▾');
    expect(getComputedStyle(select).appearance).toBe('none');
    expect(parseFloat(getComputedStyle(pill).borderTopLeftRadius)).toBeGreaterThan(40);
    expect(pill.getBoundingClientRect().height).toBeGreaterThanOrEqual(44);
    const text = parseColor(getComputedStyle(pill).color);
    const bg = parseColor(getComputedStyle(pill).backgroundColor);
    expect(contrast(text, bg)).toBeGreaterThanOrEqual(4.5);
    expect(select.options.length).toBe(4);
  });

  it('k-privacy: „← Start" oben, Kinder-Teil hervorgehoben, Eltern-Abschnitte zugeklappt, Fliesstext 16 px', async () => {
    const f = await at('/privacy');
    await new Promise(r => setTimeout(r));
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;

    const back = el.querySelector<HTMLAnchorElement>('.legal-top a.back')!;
    expect(back).withContext('Ruecklink oben').not.toBeNull();
    expect(back.getAttribute('href')).toBe('/');
    expect(back.compareDocumentPosition(el.querySelector('app-privacy')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    expect(el.querySelector('.kid-summary h2#privacy-kid')).withContext('Kinder-Teil als Karte').not.toBeNull();
    const sections = el.querySelectorAll('app-privacy details.kid-parent');
    expect(sections.length).toBeGreaterThan(5);
    expect(el.querySelector('details.kid-parent[open]')).toBeNull();
    expect(getComputedStyle(el.querySelector<HTMLElement>('app-privacy .toc')!).display).toBe('none');
    expect(getComputedStyle(el.querySelector<HTMLElement>('app-privacy mat-card-content')!).fontSize).toBe('16px');

    // Auf anderen Seiten kein zweiter Ruecklink in der Huelle.
    await TestBed.inject(Router).navigateByUrl('/levels');
    f.detectChanges();
    expect(el.querySelector('.legal-top')).toBeNull();
  });
});
