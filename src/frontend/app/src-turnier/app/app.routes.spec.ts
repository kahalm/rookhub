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
    expect(report.problems).toEqual([]);
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
