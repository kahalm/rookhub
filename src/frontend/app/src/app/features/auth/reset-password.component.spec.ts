import { of, throwError } from 'rxjs';
import { ResetPasswordComponent, resetErrorOf } from './reset-password.component';
import { PASSWORD_MIN_LENGTH } from './register.component';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';

describe('ResetPasswordComponent', () => {
  function make(token: string, resetReturn = of(void 0)) {
    const auth: any = { resetPassword: jasmine.createSpy('resetPassword').and.returnValue(resetReturn) };
    const router: any = { navigate: jasmine.createSpy('navigate') };
    const route: any = { snapshot: { queryParams: token ? { token } : {} } };
    const snackbar: any = { warn: jasmine.createSpy('warn'), success: jasmine.createSpy('success') };
    const translate: any = { instant: (k: string) => k };
    const c = new ResetPasswordComponent(auth, router, route, snackbar, translate);
    return { c, auth, router, snackbar };
  }

  it('liest das Token aus der Query', () => {
    const { c } = make('tok123');
    expect(c.token).toBe('tok123');
  });

  // UX-018: hier galten 4 Zeichen, der Server verlangt 8 (ResetPasswordDto: MinLength + PasswordPolicy) — ein
  // 6-stelliges Passwort liess den Knopf zu und scheiterte dann mit einem englischen Servertext.
  it('canSubmit nur bei übereinstimmenden Passwörtern ab der Server-Mindestlänge (8)', () => {
    expect(PASSWORD_MIN_LENGTH).toBe(8);
    const { c } = make('tok');
    c.password = 'läufer1'; c.confirm = 'läufer1';
    expect(c.canSubmit).toBeFalse();          // 7 Zeichen: zu kurz
    c.password = 'läufer12'; c.confirm = 'läufer13';
    expect(c.canSubmit).toBeFalse();          // ungleich
    c.password = 'läufer12'; c.confirm = 'läufer12';
    expect(c.canSubmit).toBeTrue();
  });

  it('blockt Submit bei abweichender Bestätigung und sagt es im Formular', () => {
    const { c, auth, snackbar } = make('tok');
    c.password = 'läufer12'; c.confirm = 'läufer13';
    c.onSubmit();
    expect(auth.resetPassword).not.toHaveBeenCalled();
    expect(c.error()).toBe('mismatch');
    expect(snackbar.warn).not.toHaveBeenCalled();
  });

  it('setzt das Passwort und navigiert bei Erfolg zum Login', () => {
    const { c, auth, router, snackbar } = make('tok', of(void 0));
    c.password = 'läufer12'; c.confirm = 'läufer12';
    c.onSubmit();
    expect(auth.resetPassword).toHaveBeenCalledWith('tok', 'läufer12');
    expect(snackbar.success).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('abgelaufener Link: eigener Fehler statt des englischen Servertexts als Snackbar', () => {
    const { c, snackbar, router } = make('tok',
      throwError(() => ({ status: 400, error: { message: 'Invalid or expired reset token.' } })));
    c.password = 'läufer12'; c.confirm = 'läufer12';
    c.onSubmit();
    expect(c.error()).toBe('expired');
    expect(c.errorKey('expired')).toBe('auth.reset.expired');
    expect(snackbar.warn).not.toHaveBeenCalled();
    expect(c.loading()).toBeFalse();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('ein neuer Versuch räumt den alten Fehler weg', () => {
    const { c, auth } = make('tok', throwError(() => ({ status: 500 })));
    c.password = 'läufer12'; c.confirm = 'läufer12';
    c.onSubmit();
    expect(c.error()).toBe('failed');
    auth.resetPassword.and.returnValue(of(void 0));
    c.onSubmit();
    expect(c.error()).toBeNull();
  });
});

describe('resetErrorOf', () => {
  it('400 mit Feldfehler am neuen Passwort (zu kurz / zu bekannt) → Passwort abgelehnt', () => {
    const modelState = { status: 400, error: { errors: {
      NewPassword: ['Password must be at least 8 characters long.',
        "The field NewPassword must be a string or array type with a minimum length of '8'."] } } };
    expect(resetErrorOf(modelState)).toBe('passwordRejected');
    expect(resetErrorOf({ status: 400, error: { errors: { newPassword: ['x'] } } })).toBe('passwordRejected');
  });

  it('jede andere 400 betrifft den Link (abgelaufen, benutzt, fehlend) → abgelaufen', () => {
    expect(resetErrorOf({ status: 400, error: { message: 'Invalid or expired reset token.' } })).toBe('expired');
    expect(resetErrorOf({ status: 400, error: { errors: { Token: ['The Token field is required.'] } } })).toBe('expired');
  });

  it('alles Übrige (Netz weg, 429, 500) → fehlgeschlagen', () => {
    expect(resetErrorOf({ status: 0 })).toBe('failed');
    expect(resetErrorOf({ status: 429 })).toBe('failed');
    expect(resetErrorOf({ status: 500, error: { message: 'boom' } })).toBe('failed');
  });
});

/**
 * Gerendertes Template: beide Passwortfelder tragen new-password, damit der Passwort-Manager ein neues Passwort
 * vorschlaegt und nicht das alte einfuellt.
 */
describe('ResetPasswordComponent Template (Mobil-Attribute)', () => {
  it('setzt autocomplete="new-password" auf beiden Passwortfeldern', async () => {
    await TestBed.configureTestingModule({
      imports: [ResetPasswordComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {} },
        { provide: SnackbarService, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ResetPasswordComponent);
    // Ohne Token zeigt die Karte nur den Hinweis; das Formular braucht ein Token.
    fixture.componentInstance.token = 'tok';
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('input[name="password"]')!.getAttribute('autocomplete')).toBe('new-password');
    expect(el.querySelector('input[name="confirm"]')!.getAttribute('autocomplete')).toBe('new-password');
  });
});

describe('ResetPasswordComponent Template (Länge + Fehlerblock, UX-018)', () => {
  async function render() {
    await TestBed.configureTestingModule({
      imports: [ResetPasswordComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {} },
        { provide: SnackbarService, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ResetPasswordComponent);
    fixture.componentInstance.token = 'tok';
    fixture.detectChanges();
    return fixture;
  }

  it('prüft beide Felder auf die Server-Mindestlänge (8, nicht 4)', async () => {
    const el: HTMLElement = (await render()).nativeElement;
    expect(el.querySelector('input[name="password"]')!.getAttribute('minlength')).toBe('8');
    expect(el.querySelector('input[name="confirm"]')!.getAttribute('minlength')).toBe('8');
  });

  it('zeigt einen abgelaufenen Link als bleibenden Hinweis mit Weg zu einem neuen Link', async () => {
    const fixture = await render();
    fixture.componentInstance.error.set('expired');
    fixture.detectChanges();
    const box: HTMLElement = fixture.nativeElement.querySelector('.form-error');
    expect(box).not.toBeNull();
    expect(box.getAttribute('role')).toBe('alert');
    expect(box.querySelector('a[href="/forgot-password"]')).not.toBeNull();
  });

  it('bietet beim abgelehnten Passwort keinen neuen Link an', async () => {
    const fixture = await render();
    fixture.componentInstance.error.set('passwordRejected');
    fixture.detectChanges();
    const box: HTMLElement = fixture.nativeElement.querySelector('.form-error');
    expect(box).not.toBeNull();
    expect(box.querySelector('a[href="/forgot-password"]')).toBeNull();
  });
});
