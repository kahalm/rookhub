import { of, throwError } from 'rxjs';
import { ForgotPasswordComponent } from './forgot-password.component';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';

describe('ForgotPasswordComponent', () => {
  function make(forgotReturn = of(void 0)) {
    const auth: any = { forgotPassword: jasmine.createSpy('forgotPassword').and.returnValue(forgotReturn) };
    const snackbar: any = { warn: jasmine.createSpy('warn') };
    const translate: any = { instant: (k: string) => k };
    const c = new ForgotPasswordComponent(auth, snackbar, translate);
    return { c, auth, snackbar };
  }

  it('trimmt die Email und ruft den Service', () => {
    const { c, auth } = make();
    c.email = '  user@test.com  ';
    c.onSubmit();
    expect(auth.forgotPassword).toHaveBeenCalledWith('user@test.com');
  });

  it('zeigt nach Erfolg die neutrale Bestätigung statt des Formulars', () => {
    const { c } = make(of(void 0));
    c.email = 'user@test.com';
    c.onSubmit();
    expect(c.sent).toBeTrue();
    expect(c.loading).toBeFalse();
  });

  it('warnt bei Fehler und bleibt im Formular', () => {
    const { c, snackbar } = make(throwError(() => ({ error: { message: 'boom' } })));
    c.email = 'user@test.com';
    c.onSubmit();
    expect(c.sent).toBeFalse();
    expect(snackbar.warn).toHaveBeenCalledWith('boom');
  });
});

/**
 * Gerendertes Template: autocomplete="email" laesst die Handy-Tastatur/den Passwort-Manager die Adresse anbieten.
 */
describe('ForgotPasswordComponent Template (Mobil-Attribute)', () => {
  it('setzt autocomplete="email" auf dem E-Mail-Feld', async () => {
    await TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {} },
        { provide: SnackbarService, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ForgotPasswordComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('input[name="email"]')!.getAttribute('autocomplete')).toBe('email');
  });
});
