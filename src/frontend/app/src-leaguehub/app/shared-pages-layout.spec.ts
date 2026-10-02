import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ForgotPasswordComponent } from '@rh/features/auth/forgot-password.component';
import { LoginComponent } from '@rh/features/auth/login.component';
import { RegisterComponent } from '@rh/features/auth/register.component';
import { ResetPasswordComponent } from '@rh/features/auth/reset-password.component';
import { AccountDeletionComponent } from '@rh/features/legal/account-deletion.component';
import { ImpressumComponent } from '@rh/features/legal/impressum.component';
import { LEGAL_SITE } from '@rh/features/legal/legal-site';
import { PrivacyComponent } from '@rh/features/legal/privacy.component';
import { leaguehubConfig } from './app.config';

/**
 * RookHubs geteilte Masken in der LeagueHub-Hülle (UX-068). Gemessen wurde vorher: alle vier Anmeldeseiten 919 px hoch
 * (80vh-Zentrierung unter Wortmarke und Werbesatz, darüber bis 255 px leer), die Rechtsseiten mit doppeltem Rand (Karte
 * am Handy 294 statt 350 px breit), und unter der Anmeldekarte dieselben Rechtslinks wie in der Fußzeile, anders benannt.
 *
 * Die Seiten werden so eingehängt wie in der App: in `<lh-root><main class="wrap">` — die Regeln in leaguehub.scss gelten
 * nur dort (die globalen Styles laufen im Test mit, angular.json → test.styles).
 */
describe('LeagueHub: geteilte Anmelde- und Rechtsseiten in der Hülle (UX-068)', () => {
  let shell: HTMLElement;

  beforeEach(() => {
    shell = document.createElement('lh-root');
    shell.style.display = 'block';
    shell.style.width = '390px';
    shell.innerHTML = '<main class="wrap"></main>';
    document.body.appendChild(shell);
    const legalSite = leaguehubConfig.providers.filter(p => (p as { provide?: unknown })?.provide === LEGAL_SITE);
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'de' }), ...legalSite],
    });
  });

  afterEach(() => shell.remove());

  function mount(page: Type<unknown>): HTMLElement {
    const fixture = TestBed.createComponent(page);
    fixture.detectChanges();
    shell.querySelector('main')!.appendChild(fixture.nativeElement);
    return fixture.nativeElement as HTMLElement;
  }

  for (const page of [LoginComponent, RegisterComponent, ForgotPasswordComponent, ResetPasswordComponent] as Type<unknown>[]) {
    it(`${page.name}: keine 80vh-Zentrierung — die Karte steht oben, die Seite wird nicht künstlich hoch`, () => {
      const box = mount(page).querySelector('.auth-container') as HTMLElement;
      expect(getComputedStyle(box).minHeight).toBe('0px');
      expect(box.getBoundingClientRect().height).toBeLessThan(window.innerHeight * 0.8);
    });
  }

  for (const page of [PrivacyComponent, ImpressumComponent, AccountDeletionComponent] as Type<unknown>[]) {
    it(`${page.name}: kein zweiter Rand in der .wrap — die Karte nutzt die Breite (390 px − 2 × 16 px)`, () => {
      const el = mount(page);
      const box = el.querySelector('.legal-container') as HTMLElement;
      expect(getComputedStyle(box).paddingLeft).toBe('0px');
      expect(getComputedStyle(box).paddingRight).toBe('0px');
      expect(Math.round((el.querySelector('mat-card') as HTMLElement).getBoundingClientRect().width)).toBe(358);
    });
  }

  it('Anmeldung: keine zweiten Rechtslinks unter der Karte — die Fußzeile der Hülle zeigt sie immer', () => {
    const links = mount(LoginComponent).querySelector('.legal-links') as HTMLElement;
    expect(getComputedStyle(links).display).toBe('none');
  });
});
