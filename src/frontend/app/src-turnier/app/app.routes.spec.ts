import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ForgotPasswordComponent } from '@rh/features/auth/forgot-password.component';
import { PrivacyComponent } from '@rh/features/legal/privacy.component';
import { routes } from './app.routes';
import { checkSharedPageLinks } from '@rh/testing/shared-page-links';
import { turnierConfig } from './app.config';
import { LEGAL_SITE, LegalSite } from '@rh/features/legal/legal-site';
import { AUTH_INTRO, AuthIntro } from '@rh/features/auth/auth-intro';

/**
 * Die Turnierseite benutzt RookHubs Anmeldemaske und Rechtsseiten (`@rh/*`). Deren Links muessen
 * hier eigene Wege haben — sonst fing sie '**' ab, der Kalender verlangte eine Anmeldung und schickte
 * zurueck auf /login: „Passwort vergessen?" und „Datenschutz" drehten sich im Kreis (W3 F6-001).
 */
describe('Turnier-Routen', () => {
  /** Was login/register/forgot-password/reset-password/privacy/impressum/account-deletion verlinken. */
  const linkedBySharedPages = ['login', 'register', 'forgot-password', 'reset-password', 'privacy', 'impressum', 'account-deletion'];

  it('hat fuer jeden Link der geteilten Anmelde- und Rechtsseiten einen eigenen Weg', () => {
    const paths = routes.map(r => r.path);
    for (const p of linkedBySharedPages) expect(paths).withContext(p).toContain(p);
  });

  it('jeder GERENDERTE Link der geteilten Seiten trifft seinen eigenen Weg (UX-003)', async () => {
    // Die Liste oben ist von Hand; das hier liest die Links aus den Templates und faengt damit auch neue.
    const report = await checkSharedPageLinks(routes, turnierConfig);
    expect(report.mounted).toEqual(jasmine.arrayWithExactContents(['login', 'register', 'forgot-password', 'reset-password', 'privacy', 'impressum', 'account-deletion']));
    expect(report.links).not.toContain('/account-deletion → /profile');
    expect(report.problems).toEqual([]);
  });

  it('Konto loeschen verweist auf RookHub — das Profil hier hat keine Loesch-Karte (UX-023)', () => {
    const legal = turnierConfig.providers.find(p => (p as { provide?: unknown }).provide === LEGAL_SITE) as
      { useFactory: () => LegalSite } | undefined;
    expect(legal?.useFactory()).toEqual(jasmine.objectContaining({ imprint: true, accountHome: 'rookhub' }));
  });

  it('die Anmeldemaske (hier die Startseite) erklaert das Angebot, die Registrierung das RookHub-Konto (UX-027)', () => {
    const intro = turnierConfig.providers.find(p => (p as { provide?: unknown }).provide === AUTH_INTRO) as
      { useValue: AuthIntro } | undefined;
    expect(intro?.useValue).toEqual({ login: 'turnier.authIntro.login', register: 'turnier.authIntro.register' });
  });

  it('laesst Passwort-Reset und Rechtsseiten ohne Anmeldung zu', () => {
    for (const p of ['forgot-password', 'reset-password', 'privacy', 'impressum', 'account-deletion']) {
      expect(routes.find(r => r.path === p)?.canActivate).withContext(p).toBeUndefined();
    }
  });

  describe('abgemeldet', () => {
    beforeEach(() => {
      localStorage.removeItem('rookhub_user');
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(), provideHttpClientTesting(), provideRouter(routes),
          provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        ],
      });
    });

    it('oeffnet „Passwort vergessen" statt zurueck auf die Anmeldung zu springen', async () => {
      const harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/forgot-password', ForgotPasswordComponent);
      expect(TestBed.inject(Router).url).toBe('/forgot-password');
    });

    it('oeffnet die Datenschutzerklaerung', async () => {
      const harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/privacy', PrivacyComponent);
      expect(TestBed.inject(Router).url).toBe('/privacy');
    });
  });
});
