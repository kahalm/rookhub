import { Component } from '@angular/core';
import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AccountDeletionComponent } from './account-deletion.component';
import { ImpressumComponent } from './impressum.component';
import { LEGAL_SITE, LegalSite, legalBackLink } from './legal-site';
import { PrivacyComponent } from './privacy.component';
import { AuthService } from '../../core/auth.service';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';

@Component({ standalone: true, template: '' })
class StubComponent {}

/**
 * Der Ruecklink der Rechtsseiten (Codereview UX-017): Er fuehrte immer mit „Zurueck zur Anmeldung" nach /login, egal
 * woher man kam — wer eingeloggt aus der Fusszeile oder dem ☰-Menue kam, landete ueber den guestGuard auf dem
 * Dashboard statt dort, wo er war. Jetzt: aus der App gekommen → ein Schritt zurueck; direkt aufgerufen → Ersatzziel.
 */
describe('legalBackLink (UX-017)', () => {
  function setup(site?: LegalSite) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'dashboard', component: StubComponent }, { path: 'privacy', component: StubComponent },
          { path: 'login', component: StubComponent }]),
        provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' }),
        ...(site ? [{ provide: LEGAL_SITE, useValue: site }] : []),
      ],
    });
    return spyOn(TestBed.inject(Location), 'back');
  }
  const click = (init: Partial<MouseEvent> = {}) =>
    ({ button: 0, ctrlKey: false, metaKey: false, shiftKey: false, altKey: false,
      preventDefault: jasmine.createSpy('preventDefault'), ...init }) as unknown as MouseEvent & { preventDefault: jasmine.Spy };

  it('direkt aufgerufen (kein Schritt zurueck in der App): Ersatzziel wie bisher', () => {
    const back = setup();
    const link = TestBed.runInInjectionContext(() => legalBackLink('legal.privacy.back'));
    expect(link).toEqual(jasmine.objectContaining({ history: false, link: '/login', label: 'legal.privacy.back', href: '/login' }));
    const e = click();
    link.go(e);
    expect(back).not.toHaveBeenCalled();
    expect(e.preventDefault).not.toHaveBeenCalled();
  });

  it('KidHub ohne Verlauf: zur Startseite', () => {
    setup({ contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/' });
    const link = TestBed.runInInjectionContext(() => legalBackLink('legal.privacy.back'));
    expect(link).toEqual(jasmine.objectContaining({ history: false, link: '/', label: 'legal.backHome' }));
  });

  it('aus der App gekommen: „Zurueck" geht einen Schritt zurueck, Strg-Klick oeffnet das Ersatzziel', async () => {
    const back = setup();
    await TestBed.inject(Router).navigateByUrl('/dashboard');       // die Seite davor
    const link = TestBed.runInInjectionContext(() => legalBackLink('legal.privacy.back'));
    expect(link).toEqual(jasmine.objectContaining({ history: true, label: 'common.back', href: '/login' }));

    const ctrl = click({ ctrlKey: true });
    link.go(ctrl);
    expect(back).not.toHaveBeenCalled();
    expect(ctrl.preventDefault).not.toHaveBeenCalled();

    const plain = click();
    link.go(plain);
    expect(back).toHaveBeenCalledTimes(1);
    expect(plain.preventDefault).toHaveBeenCalled();
  });

  it('angemeldet ohne Verlauf: zur Startseite statt zur Anmeldung (x-back-login)', () => {
    setup();
    spyOnProperty(TestBed.inject(AuthService), 'isLoggedIn').and.returnValue(true);
    const link = TestBed.runInInjectionContext(() => legalBackLink('legal.privacy.back'));
    expect(link).toEqual(jasmine.objectContaining({ history: false, link: '/', label: 'legal.backHome', href: '/' }));
  });

  it('eigenes Ziel der Oberflaeche gilt auch angemeldet (Turnierseite: Kalender)', () => {
    setup({ contactEmail: 'x@y.z', imprint: true, back: '/tournaments/calendar' });
    spyOnProperty(TestBed.inject(AuthService), 'isLoggedIn').and.returnValue(true);
    const link = TestBed.runInInjectionContext(() => legalBackLink('legal.privacy.back'));
    expect(link).toEqual(jasmine.objectContaining({ link: '/tournaments/calendar', label: 'legal.backHome' }));
  });

  it('von der Anmeldung gekommen: „Zurueck zur Anmeldung", sonst nur „Zurueck" (x-back-login)', async () => {
    const back = setup();
    await TestBed.inject(Router).navigateByUrl('/login?returnUrl=%2Fx');
    const link = TestBed.runInInjectionContext(() => legalBackLink('legal.privacy.back'));
    expect(link).toEqual(jasmine.objectContaining({ history: true, label: 'legal.privacy.back' }));
    link.go(click());
    expect(back).toHaveBeenCalledTimes(1);

    await TestBed.inject(Router).navigateByUrl('/dashboard');
    expect(TestBed.runInInjectionContext(() => legalBackLink('legal.privacy.back')).label).toBe('common.back');
  });

  for (const [name, page] of [['Datenschutz', PrivacyComponent], ['Impressum', ImpressumComponent], ['Konto loeschen', AccountDeletionComponent]] as const) {
    it(`${name}: Ruecklink rendert je nach Herkunft „Zurueck" oder das Ersatzziel`, async () => {
      const back = setup();
      const plain = TestBed.createComponent(page as never);
      plain.detectChanges();
      const fallback = (plain.nativeElement as HTMLElement).querySelector('.back a')!;
      expect(fallback.getAttribute('href')).toBe('/login');
      expect(fallback.textContent).not.toContain('common.back');
      plain.destroy();

      await TestBed.inject(Router).navigateByUrl('/dashboard');
      const fromApp = TestBed.createComponent(page as never);
      fromApp.detectChanges();
      const a = fromApp.debugElement.query(By.css('.back a'));
      expect(a.nativeElement.textContent).toContain('common.back');
      expect(a.nativeElement.getAttribute('href')).toBe('/login');   // neuer Tab: Ersatzziel
      // Ueber den Angular-Listener, ohne echtes DOM-Ereignis: ein nicht abgefangener Klick verliesse die Testseite.
      const e = click();
      a.triggerEventHandler('click', e);
      expect(back).toHaveBeenCalledTimes(1);
      expect(e.preventDefault).toHaveBeenCalled();
    });
  }
});
