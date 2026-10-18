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
import { KID_HOME_WIDTH, KID_MAP_WIDTH } from './shared/kids-layout';
import { LEGAL_SITE } from '@rh/features/legal/legal-site';
import { PrivacyComponent } from '@rh/features/legal/privacy.component';
import { AccountDeletionComponent } from '@rh/features/legal/account-deletion.component';
import { ForgotPasswordComponent } from '@rh/features/auth/forgot-password.component';
import { HandoffService } from '@rh/core/handoff.service';
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
          { path: 'login', component: BlankComponent },
          { path: 'privacy', component: PrivacyComponent },
          { path: 'account-deletion', component: AccountDeletionComponent },
          { path: 'forgot-password', component: ForgotPasswordComponent },
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
    // z2-k-karten-breit: die Karte wächst mit dem Fenster (mindestens 980 px, ≈ 75 vw, höchstens 1480 px).
    const vw = window.innerWidth;
    expect(parseFloat(getComputedStyle(top).maxWidth)).toBeCloseTo(Math.min(Math.max(980, 0.75 * vw), 1480), 0);
    expect(top.style.getPropertyValue('--kid-page-width')).toBe(KID_MAP_WIDTH);

    await TestBed.inject(Router).navigateByUrl('/');
    f.detectChanges();
    expect(top.style.getPropertyValue('--kid-page-width')).toBe(KID_HOME_WIDTH);

    // z2-k-auth-logo: auf den Anmelde-Masken so breit wie „← Start" und Karte.
    await TestBed.inject(Router).navigateByUrl('/login');
    f.detectChanges();
    expect(top.style.getPropertyValue('--kid-page-width')).toBe('432px');

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

  // UI-Sweep 2026-10-11 (d1-k-account-deletion-style, d1-k-account-deletion-jargon)
  it('Konto löschen: Kinderkarte, „← Start" oben, Kopfzeile so breit wie die Karte, Eltern-Liste zu, Löschknopf groß und rot', async () => {
    // Auf localhost gibt es kein RookHub zum Löschen — wie auf kidhub.* soll der Knopf erscheinen.
    spyOnProperty(HandoffService.prototype, 'accountHomeUrl', 'get').and.returnValue('https://rookhub.example');
    const f = await at('/account-deletion');
    await new Promise(r => setTimeout(r));
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;

    const back = el.querySelector<HTMLAnchorElement>('.legal-top a.back')!;
    expect(back).withContext('„← Start" oben').not.toBeNull();
    expect(back.compareDocumentPosition(el.querySelector('app-account-deletion')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(el.querySelector<HTMLElement>('header.top')!.style.getPropertyValue('--kid-page-width')).toBe('620px');

    const card = getComputedStyle(el.querySelector<HTMLElement>('app-account-deletion mat-card')!);
    expect(card.borderTopLeftRadius).toBe('22px');
    expect(card.backgroundColor).toBe('rgb(255, 255, 255)');
    expect(card.maxWidth).toBe('588px');                          // z1-k-legal-line
    const h1 = getComputedStyle(el.querySelector<HTMLElement>('app-account-deletion h1')!);
    expect(h1.fontWeight).toBe('800');
    expect(h1.color).toBe('rgb(29, 79, 145)');                        // --kid-title
    expect(getComputedStyle(el.querySelector<HTMLElement>('app-account-deletion mat-card-content')!).fontSize).toBe('16px');

    // Fachbegriffe für Eltern zugeklappt, davor der Kindersatz.
    const details = el.querySelector('app-account-deletion details.kid-parent:has(ul.removed)')!;
    expect(details).not.toBeNull();
    expect(details.hasAttribute('open')).toBeFalse();
    expect(el.querySelector('app-account-deletion .kid-short')).not.toBeNull();

    // a1-k-deletion-fachjargon: auch die Anleitung (☰ → Konto → Profil, PGN, Repertoires) ist für Eltern zugeklappt,
    // der Kindersatz steht direkt unter der Einleitung, der Knopf bleibt sichtbar.
    const content = el.querySelector('app-account-deletion mat-card-content')!;
    const howTo = content.querySelector('details.kid-parent:has(.kid-parent-howto-label)')!;
    expect(howTo).withContext('Anleitung zugeklappt').not.toBeNull();
    expect(howTo.hasAttribute('open')).toBeFalse();
    expect(howTo.querySelector('h2')).not.toBeNull();
    expect(howTo.querySelector('p.backup')).not.toBeNull();
    for (const sel of ['p.backup', 'p.backup-links']) {
      const n = content.querySelector<HTMLElement>(sel);
      if (n) expect(n.checkVisibility()).withContext(`${sel} zu`).toBeFalse();
    }
    // d3-k-delete-button-before-info: Kindersatz unter „Was entfernt wird" (vor dem Eltern-Aufklapper), Knopf erst nach
    // „Was anonym erhalten bleibt" — erst was verloren geht, dann die Aktion.
    const short = content.querySelector('.kid-short')!;
    expect(short.nextElementSibling).withContext('Kindersatz vor der Eltern-Liste').toBe(details);
    expect(short.previousElementSibling!.tagName).toBe('H2');
    const heads = Array.from(content.querySelectorAll(':scope > h2'));
    const action = content.querySelector(':scope > p.action')!;
    expect(heads[1].compareDocumentPosition(action) & Node.DOCUMENT_POSITION_FOLLOWING).withContext('Knopf nach „Was bleibt"').toBeTruthy();
    expect(action.nextElementSibling).withContext('danach Kontakt').toBe(heads[2]);
    expect(el.querySelector('app-account-deletion details .delete-now')).withContext('Knopf nicht zugeklappt').toBeNull();
    expect(howTo.compareDocumentPosition(el.querySelector('app-account-deletion p.action')!) & Node.DOCUMENT_POSITION_FOLLOWING)
      .withContext('Knopf unter der Anleitung').toBeTruthy();

    // Löschknopf: Gefahrfarbe --kid-bad-fg, weiße Schrift ≥ 4,5:1, Hauptknopf-Form (≥ 52 px, Radius 18, fett).
    const btn = el.querySelector<HTMLElement>('app-account-deletion .delete-now')!;
    const b = getComputedStyle(btn);
    expect(b.backgroundColor).toBe('rgb(155, 28, 28)');
    expect(contrast(parseColor(b.color), parseColor(b.backgroundColor))).toBeGreaterThanOrEqual(4.5);
    expect(btn.getBoundingClientRect().height).toBeGreaterThanOrEqual(52);
    expect(b.borderTopLeftRadius).toBe('18px');
    expect(b.fontWeight).toBe('800');
  });
  // d3-k-forgot-stacked-muted-paragraphs: zwei leise Eltern-Absätze offen unter „Link senden" — jetzt EIN zugeklappter Aufklapper.
  it('Passwort vergessen: beide Eltern-Hinweise in einem geschlossenen „Für Eltern"-Aufklapper', async () => {
    const f = await at('/forgot-password');
    await new Promise(r => setTimeout(r));
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const content = el.querySelector('app-forgot-password mat-card-content')!;
    const details = content.querySelectorAll(':scope > details.kid-parent');
    expect(details.length).toBe(1);
    expect(details[0].hasAttribute('open')).toBeFalse();
    expect(details[0].querySelector('p.kids-mail')).not.toBeNull();
    expect(details[0].querySelector('p.no-email a[href^="mailto:"]')).not.toBeNull();
    expect(content.querySelector(':scope > p.kids-mail, :scope > p.no-email')).withContext('nichts mehr offen').toBeNull();
    expect(details[0].querySelector('summary .kid-parent-label')!.textContent).toBeTruthy();
    expect((details[0].querySelector('summary') as HTMLElement).getBoundingClientRect().height).toBeGreaterThanOrEqual(44);
    expect((content.querySelector('p.no-email') as HTMLElement).checkVisibility()).toBeFalse();
  });
});
