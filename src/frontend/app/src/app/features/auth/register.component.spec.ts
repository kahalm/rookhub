import { of, throwError } from 'rxjs';
import { RegisterComponent, PASSWORD_MIN_LENGTH, registerErrorOf } from './register.component';
import { AuthPrefillService } from '../../core/auth-prefill.service';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';

describe('RegisterComponent — optionale Email', () => {
  function make(email: string, prefill = new AuthPrefillService()) {
    const auth: any = { register: jasmine.createSpy('register').and.returnValue(of({})) };
    const router: any = { navigateByUrl: jasmine.createSpy('navigateByUrl') };
    const route: any = { snapshot: { queryParams: {} } };
    const c = new RegisterComponent(auth, prefill, router, route);
    c.username = 'user'; c.password = 'secret'; c.email = email;
    return { c, auth, prefill };
  }

  it('sendet null statt leerem String, wenn die Email leer ist', () => {
    const { c, auth } = make('');
    c.onSubmit();
    expect(auth.register).toHaveBeenCalledWith('user', null, 'secret');
  });

  it('trimmt und sendet die Email, wenn angegeben', () => {
    const { c, auth } = make('  a@b.co  ');
    c.onSubmit();
    expect(auth.register).toHaveBeenCalledWith('user', 'a@b.co', 'secret');
  });

  it('übernimmt Benutzername/Passwort aus dem geteilten Prefill (vom Login)', () => {
    const prefill = new AuthPrefillService();
    prefill.username = 'carried'; prefill.password = 'pw';
    const auth: any = { register: jasmine.createSpy('register').and.returnValue(of({})) };
    const c = new RegisterComponent(auth, prefill, { navigateByUrl: () => {} } as any,
      { snapshot: { queryParams: {} } } as any);
    expect(c.username).toBe('carried');
    expect(c.password).toBe('pw');
  });

  it('leert das Prefill nach erfolgreicher Registrierung', () => {
    const { c, prefill } = make('a@b.co');
    c.onSubmit();
    expect(prefill.username).toBe('');
    expect(prefill.email).toBe('');
    expect(prefill.password).toBe('');
  });
});

/**
 * Gerendertes Template: ohne autocapitalize="none" wurde der Benutzername am Handy als 'Kahalm' statt 'kahalm'
 * gespeichert; new-password laesst den Passwort-Manager ein starkes Passwort vorschlagen statt das alte einzufuellen.
 */
describe('RegisterComponent Template (Mobil-Attribute)', () => {
  it('setzt autocomplete/autocapitalize/autocorrect/spellcheck auf den Eingabefeldern', async () => {
    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const user = el.querySelector('input[name="username"]')!;
    expect(user.getAttribute('autocomplete')).toBe('username');
    expect(user.getAttribute('autocapitalize')).toBe('none');
    expect(user.getAttribute('autocorrect')).toBe('off');
    expect(user.getAttribute('spellcheck')).toBe('false');
    expect(el.querySelector('input[name="email"]')!.getAttribute('autocomplete')).toBe('email');
    expect(el.querySelector('input[name="password"]')!.getAttribute('autocomplete')).toBe('new-password');
  });
});

/**
 * Fehlerfälle. Auslöser (27.09., KidHub): ein Besucher mit Konto registrierte sich neu, bekam
 * „Username or email already in use." als verschwindende englische Snackbar und gab auf. Und der
 * Hinweis versprach 4 Zeichen, der Server verlangt 8.
 */
describe('RegisterComponent — Fehlerfälle', () => {
  function withError(err: any) {
    const auth: any = { register: jasmine.createSpy('register').and.returnValue(throwError(() => err)) };
    const router: any = { navigateByUrl: jasmine.createSpy('navigateByUrl') };
    const c = new RegisterComponent(auth, new AuthPrefillService(), router, { snapshot: { queryParams: {} } } as any);
    c.username = 'divbyzero'; c.password = 'geheim123'; c.email = '';
    return { c, router };
  }

  it('409 = Name ODER E-Mail vergeben — ohne zu verraten, welches (kein Enumeration-Oracle)', () => {
    const { c, router } = withError({ status: 409, error: { message: 'Username or email already in use.' } });
    c.onSubmit();
    expect(c.error()).toBe('taken');
    expect(c.errorKey('taken')).toBe('auth.register.taken');
    expect(c.loading()).toBeFalse();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('400 mit Passwort-Fehler (zu kurz / zu bekannt) wird als Passwort-Problem gemeldet', () => {
    expect(registerErrorOf({ status: 400, error: { errors: { Password: ['Password is too common'] } } })).toBe('passwordRejected');
    expect(registerErrorOf({ status: 400, error: { errors: { password: ['x'] } } })).toBe('passwordRejected');
  });

  it('andere 400 → „Angaben prüfen", alles Übrige → „fehlgeschlagen"', () => {
    expect(registerErrorOf({ status: 400, error: { errors: { Username: ['too short'] } } })).toBe('invalid');
    expect(registerErrorOf({ status: 500 })).toBe('failed');
    expect(registerErrorOf({ status: 0 })).toBe('failed');
  });

  it('ein neuer Versuch räumt den alten Fehler weg', () => {
    const { c } = withError({ status: 409 });
    c.onSubmit();
    expect(c.error()).toBe('taken');
    (c as any).auth.register.and.returnValue(of({}));
    c.onSubmit();
    expect(c.error()).toBeNull();
  });

  it('ungültiges Formular wird gar nicht erst gesendet', () => {
    const { c } = withError({ status: 500 });
    const form: any = { invalid: true, control: { markAllAsTouched: jasmine.createSpy('touch') } };
    c.onSubmit(form);
    expect((c as any).auth.register).not.toHaveBeenCalled();
    expect(form.control.markAllAsTouched).toHaveBeenCalled();
  });
});

describe('RegisterComponent Template (Längen + Fehlerblock)', () => {
  async function render() {
    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('prüft das Passwort auf dieselbe Mindestlänge wie der Server (8, nicht 4)', async () => {
    expect(PASSWORD_MIN_LENGTH).toBe(8);   // = PasswordPolicyAttribute.MinimumLength
    const el: HTMLElement = (await render()).nativeElement;
    expect(el.querySelector('input[name="password"]')!.getAttribute('minlength')).toBe('8');
    expect(el.querySelector('input[name="username"]')!.getAttribute('minlength')).toBe('3');
  });

  it('zeigt beim vergebenen Namen einen bleibenden Hinweis mit Weg zur Anmeldung', async () => {
    const fixture = await render();
    fixture.componentInstance.error.set('taken');
    fixture.detectChanges();
    const box: HTMLElement = fixture.nativeElement.querySelector('.form-error');
    expect(box).not.toBeNull();
    expect(box.getAttribute('role')).toBe('alert');
    expect(box.querySelector('a[href^="/login"]')).not.toBeNull();
  });

  it('bietet bei anderen Fehlern keine Anmeldung an', async () => {
    const fixture = await render();
    fixture.componentInstance.error.set('passwordRejected');
    fixture.detectChanges();
    const box: HTMLElement = fixture.nativeElement.querySelector('.form-error');
    expect(box).not.toBeNull();
    expect(box.querySelector('a[href^="/login"]')).toBeNull();
  });
});

/**
 * UX-002: wer ohne E-Mail registriert, kann ein vergessenes Passwort nie zuruecksetzen („Passwort vergessen“ nimmt
 * nur eine E-Mail an). Der Hinweis hiess nur „Optional“, und ein Tippfehler im einzigen Passwortfeld fiel erst beim
 * naechsten Anmelden auf.
 */
describe('RegisterComponent — ohne E-Mail kein Zurücksetzen (UX-002)', () => {
  async function render() {
    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
    return fixture;
  }

  async function loadLang(lang: string): Promise<any> {
    // Karma serviert public/ als Assets — je nach Version unter / oder /base/ (wie i18n-parity.spec.ts).
    for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
      const res = await fetch(url);
      if (res.ok) return res.json();
    }
    throw new Error(`${lang}.json nicht ladbar`);
  }

  it('der E-Mail-Hinweis nennt die Folge, nicht nur „Optional“', async () => {
    for (const [lang, word] of [['en', 'reset'], ['de', 'zurücksetzen']]) {
      const hint: string = (await loadLang(lang)).auth.register.emailHint;
      expect(hint).withContext(lang).not.toBe('Optional');
      expect(hint.toLowerCase()).withContext(lang).toContain(word);
    }
  });

  it('das Passwort lässt sich einblenden und wieder verbergen', async () => {
    const fixture = await render();
    const el: HTMLElement = fixture.nativeElement;
    const input = el.querySelector('input[name="password"]') as HTMLInputElement;
    const toggle = el.querySelector('button.pw-toggle') as HTMLButtonElement;
    expect(toggle).not.toBeNull();
    expect(toggle.type).toBe('button');                 // kein Absenden des Formulars
    expect(input.type).toBe('password');
    expect(toggle.getAttribute('aria-pressed')).toBe('false');
    expect(toggle.getAttribute('aria-label')).toBe('auth.register.showPassword');

    toggle.click();
    fixture.detectChanges();
    expect(input.type).toBe('text');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    expect(toggle.getAttribute('aria-label')).toBe('auth.register.hidePassword');

    toggle.click();
    fixture.detectChanges();
    expect(input.type).toBe('password');
  });
});

/**
 * UX-017: die Registrierung fragt Name, E-Mail und Passwort ab, verlinkte die Datenschutzerklaerung aber nicht —
 * nur /login hatte den Link (axe fand a[routerlink="/privacy"] auf /register auf keiner Oberflaeche).
 */
describe('RegisterComponent — Datenschutz-Link an der Maske (UX-017)', () => {
  it('verlinkt die Datenschutzerklärung unter dem Formular', async () => {
    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
    const link: HTMLAnchorElement = fixture.nativeElement.querySelector('.privacy-note a');
    expect(link).not.toBeNull();
    expect(link.getAttribute('href')).toBe('/privacy');
    expect(link.textContent!.trim()).toBe('auth.register.privacyNote');
  });
});
