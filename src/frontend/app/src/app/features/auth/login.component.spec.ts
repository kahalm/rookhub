import { of, throwError } from 'rxjs';
import { LoginComponent, loginErrorOf, loginRetryAfterSeconds } from './login.component';
import { AuthPrefillService } from '../../core/auth-prefill.service';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { LEGAL_SITE } from '../legal/legal-site';

function make(queryParams: Record<string, string> = {}, prefill = new AuthPrefillService()) {
  const auth: any = { login: jasmine.createSpy('login').and.returnValue(of({})) };
  const router: any = { navigateByUrl: jasmine.createSpy('navigateByUrl') };
  const route: any = { snapshot: { queryParams } };
  // Schluessel samt Parametern zurueck, damit die Specs auch die Wartezeit sehen.
  const translate: any = { instant: (k: string, p?: object) => (p ? `${k} ${JSON.stringify(p)}` : k) };
  return { c: new LoginComponent(auth, prefill, router, route, translate), auth, router, prefill };
}

describe('LoginComponent', () => {
  it('defaults returnUrl to /dashboard when absent', () => {
    expect(make().c.returnUrl).toBe('/dashboard');
  });

  it('keeps a safe local returnUrl', () => {
    expect(make({ returnUrl: '/courses/5/sequential' }).c.returnUrl).toBe('/courses/5/sequential');
  });

  it('rejects open-redirect returnUrls (protocol-relative / absolute / no leading slash)', () => {
    expect(make({ returnUrl: '//evil.com' }).c.returnUrl).toBe('/dashboard');
    expect(make({ returnUrl: 'https://evil.com' }).c.returnUrl).toBe('/dashboard');
    expect(make({ returnUrl: 'dashboard' }).c.returnUrl).toBe('/dashboard');
  });

  it('sets authRequired only for the "1" flag', () => {
    expect(make({ authRequired: '1' }).c.authRequired).toBeTrue();
    expect(make({ authRequired: '0' }).c.authRequired).toBeFalse();
  });

  it('navigates to returnUrl on successful login', () => {
    const { c, auth, router } = make({ returnUrl: '/stats' });
    c.username = 'u'; c.password = 'p'; c.rememberMe = true;
    c.onSubmit();
    expect(auth.login).toHaveBeenCalledWith('u', 'p', true);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/stats');
  });

  it('seeds fields from the shared prefill (carried over from register)', () => {
    const prefill = new AuthPrefillService();
    prefill.username = 'carried'; prefill.password = 'pw';
    const { c } = make({}, prefill);
    expect(c.username).toBe('carried');
    expect(c.password).toBe('pw');
  });

  it('writes typed values back into the shared prefill', () => {
    const { c, prefill } = make();
    c.username = 'typed'; c.password = 'secret';
    expect(prefill.username).toBe('typed');
    expect(prefill.password).toBe('secret');
  });

  it('clears the prefill on successful login', () => {
    const { c, prefill } = make({ returnUrl: '/stats' });
    c.username = 'u'; c.password = 'p';
    c.onSubmit();
    expect(prefill.username).toBe('');
    expect(prefill.password).toBe('');
  });

  it('shows the error in the form, never the raw server text, and clears loading', () => {
    const { c, auth, router } = make();
    auth.login.and.returnValue(throwError(() => ({ status: 500, error: { message: 'nope' } })));
    c.onSubmit();
    expect(c.error()).toEqual({ kind: 'failed', text: 'auth.login.failed' });
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(c.loading).toBeFalse();
  });

  it('shows the error in the UI language when the server sends a code (F5-019)', () => {
    const { auth, router, prefill } = make();
    const translate: any = {
      instant: (k: string) => k === 'apiErrors.login_invalid' ? 'Benutzername oder Passwort ist falsch.' : k,
    };
    const c = new LoginComponent(auth, prefill, router, { snapshot: { queryParams: {} } } as any, translate);
    auth.login.and.returnValue(throwError(() => ({
      status: 401, error: { message: 'Invalid username or password.', code: 'login_invalid' },
    })));
    c.onSubmit();
    expect(c.error()).toEqual({ kind: 'credentials', text: 'Benutzername oder Passwort ist falsch.' });
  });

  describe('Fehlerarten (UX-019)', () => {
    function failWith(err: object) {
      const made = make();
      made.auth.login.and.returnValue(throwError(() => err));
      made.c.onSubmit();
      return made.c.error();
    }

    it('429 des IP-Limiters: eigener Text mit der Wartezeit statt „Anmeldung fehlgeschlagen“', () => {
      expect(failWith({ status: 429, error: { code: 'rate_limited', retryAfterSeconds: 42 } }))
        .toEqual({ kind: 'rateLimited', text: 'auth.login.rateLimited {"seconds":42}' });
      // Ohne Rumpf (aeltere API) bzw. ohne Angabe: das Fenster des Limiters.
      expect(failWith({ status: 429, error: null })!.text).toBe('auth.login.rateLimited {"seconds":60}');
    });

    it('429 der Konto-Bremse behält seinen Code-Text (wenige Sekunden, nicht das IP-Fenster)', () => {
      const { auth, router, prefill } = make();
      const translate: any = { instant: (k: string) => k === 'apiErrors.login_throttled' ? 'Konto-Bremse' : k };
      const c = new LoginComponent(auth, prefill, router, { snapshot: { queryParams: {} } } as any, translate);
      auth.login.and.returnValue(throwError(() => ({ status: 429, error: { code: 'login_throttled' } })));
      c.onSubmit();
      expect(c.error()).toEqual({ kind: 'rateLimited', text: 'Konto-Bremse' });
    });

    it('401 ohne Code: übersetzter Text statt der englischen Servermeldung', () => {
      expect(failWith({ status: 401, error: { message: 'Invalid username or password.' } }))
        .toEqual({ kind: 'credentials', text: 'apiErrors.login_invalid' });
    });

    it('ohne Verbindung (Status 0): eigener Hinweis', () => {
      expect(failWith({ status: 0, error: new ProgressEvent('error') }))
        .toEqual({ kind: 'offline', text: 'auth.login.offline' });
    });

    it('ein neuer Versuch räumt die alte Meldung ab', () => {
      const { c, auth } = make();
      auth.login.and.returnValue(throwError(() => ({ status: 401 })));
      c.onSubmit();
      expect(c.error()).not.toBeNull();
      auth.login.and.returnValue(of({}));
      c.onSubmit();
      expect(c.error()).toBeNull();
    });

    it('loginErrorOf / loginRetryAfterSeconds', () => {
      expect(loginErrorOf({ status: 400 })).toBe('credentials');
      expect(loginErrorOf({ status: 403 })).toBe('failed');
      expect(loginErrorOf(undefined)).toBe('failed');
      expect(loginRetryAfterSeconds({ error: { retryAfterSeconds: 1.2 } })).toBe(2);
      expect(loginRetryAfterSeconds({ headers: { get: (h: string) => (h === 'Retry-After' ? '17' : null) } })).toBe(17);
      expect(loginRetryAfterSeconds({})).toBe(60);
    });
  });
});

/** UX-019: die Meldung steht im Formular (role=alert), beim falschen Passwort mit dem Weg „Passwort vergessen?“. */
describe('LoginComponent Template (Fehlermeldung im Formular, UX-019)', () => {
  async function renderWith(err: object): Promise<HTMLElement> {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { login: () => throwError(() => err) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    fixture.componentInstance.onSubmit();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('falsches Passwort: Meldung mit Link zu „Passwort vergessen?“', async () => {
    const el = await renderWith({ status: 401, error: { code: 'login_invalid' } });
    const alert = el.querySelector('.form-error[role="alert"]');
    expect(alert).not.toBeNull();
    expect(alert!.querySelector('a')!.getAttribute('href')).toBe('/forgot-password');
  });

  it('Rate-Limit: Meldung ohne Passwort-Link', async () => {
    const el = await renderWith({ status: 429, error: { code: 'rate_limited', retryAfterSeconds: 30 } });
    const alert = el.querySelector('.form-error[role="alert"]');
    expect(alert).not.toBeNull();
    expect(alert!.querySelector('a')).toBeNull();
  });
});

/**
 * Gerendertes Template: die Mobil-Attribute der Eingabefelder (Handy-Tastatur ohne Grossschreibung/Autokorrektur
 * beim Benutzernamen, Passwort-Manager-Hinweise) duerfen bei einem Template-Umbau nicht stillschweigend verloren gehen.
 */
describe('LoginComponent Template (Mobil-Attribute)', () => {
  it('setzt autocomplete/autocapitalize/autocorrect/spellcheck auf den Eingabefeldern', async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {} },
        { provide: SnackbarService, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const user = el.querySelector('input[name="username"]')!;
    expect(user.getAttribute('autocomplete')).toBe('username');
    expect(user.getAttribute('autocapitalize')).toBe('none');
    expect(user.getAttribute('autocorrect')).toBe('off');
    expect(user.getAttribute('spellcheck')).toBe('false');
    expect(el.querySelector('input[name="password"]')!.getAttribute('autocomplete')).toBe('current-password');
  });
});

/** UX-033: auf LeagueHub sagt die Maske, dass die Seite nur für eine Gruppe ist und das RookHub-Konto gilt. */
describe('LoginComponent — LeagueHub-Hinweis (UX-033)', () => {
  async function render(legal?: object): Promise<HTMLElement> {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {} },
        { provide: SnackbarService, useValue: {} },
        ...(legal ? [{ provide: LEGAL_SITE, useValue: legal }] : []),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('LeagueHub: Hinweis auf Gruppe und RookHub-Konto', async () => {
    const el = await render({ contactEmail: 'x@y.z', imprint: true, kind: 'leaguehub' });
    expect(el.querySelector('.site-note')?.textContent).toContain('auth.login.leaguehubNote');
  });

  it('RookHub (Vorgabe) und KidHub: kein LeagueHub-Hinweis', async () => {
    expect((await render()).querySelector('.site-note')).toBeNull();
    TestBed.resetTestingModule();
    expect((await render({ contactEmail: 'x@y.z', imprint: false, kind: 'kidhub' })).querySelector('.site-note')).toBeNull();
  });
});
