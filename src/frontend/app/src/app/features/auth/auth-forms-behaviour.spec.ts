import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { LoginComponent } from './login.component';
import { RegisterComponent } from './register.component';
import { ForgotPasswordComponent } from './forgot-password.component';
import { ResetPasswordComponent } from './reset-password.component';

/**
 * Codereview W5 UX-052 (c): die vier Auth-Formulare verhielten sich verschieden — /login und /register schickten
 * leere Formulare ab, /forgot-password und /reset-password sperrten den Knopf bis zur Gültigkeit (grau gefüllt, im
 * M3-Thema kräftiger als jeder aktive Knopf), und nur /register hatte keinen Autofokus. Jetzt EIN Verhalten: Knopf
 * immer aktiv, ein unvollständiges Formular geht nicht raus, das Feld sagt, was fehlt.
 */
describe('Auth-Formulare: ein gemeinsames Verhalten (UX-052)', () => {
  /** Nur die vier Aufrufe, um die es geht — jeder antwortet sofort mit Erfolg. */
  let auth: Record<'login' | 'register' | 'forgotPassword' | 'resetPassword', jasmine.Spy>;

  async function render<T>(cmp: Type<T>, prepare?: (c: T) => void) {
    const ok = () => of({});
    auth = {
      login: jasmine.createSpy('login').and.callFake(ok),
      register: jasmine.createSpy('register').and.callFake(ok),
      forgotPassword: jasmine.createSpy('forgotPassword').and.callFake(ok),
      resetPassword: jasmine.createSpy('resetPassword').and.callFake(ok),
    };
    TestBed.configureTestingModule({
      imports: [cmp],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: auth },
        { provide: SnackbarService, useValue: { warn: () => {}, success: () => {} } },
      ],
    });
    const fixture = TestBed.createComponent(cmp);
    prepare?.(fixture.componentInstance);
    fixture.detectChanges();
    // ngModel meldet seine Controls erst nach einem Microtask am NgForm an — vorher waere das Formular „gueltig".
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const submit = el.querySelector<HTMLButtonElement>('form button[type="submit"]')!;
    return { fixture, el, submit };
  }

  afterEach(() => TestBed.resetTestingModule());

  const cases: { name: string; cmp: Type<unknown>; prepare?: (c: any) => void; called: () => jasmine.Spy }[] = [
    { name: 'Anmelden', cmp: LoginComponent, called: () => auth.login },
    { name: 'Registrieren', cmp: RegisterComponent, called: () => auth.register },
    { name: 'Passwort vergessen', cmp: ForgotPasswordComponent, called: () => auth.forgotPassword },
    { name: 'Passwort zurücksetzen', cmp: ResetPasswordComponent, prepare: c => c.token = 'tok', called: () => auth.resetPassword },
  ];

  for (const tc of cases) {
    it(`${tc.name}: Knopf ist bei leerem Formular aktiv, Absenden schickt nichts und markiert die Felder`, async () => {
      const { fixture, el, submit } = await render(tc.cmp, tc.prepare);
      expect(submit.disabled).withContext('Knopf aktiv').toBeFalse();

      submit.click();
      fixture.detectChanges();

      expect(tc.called()).not.toHaveBeenCalled();
      expect(el.querySelectorAll('mat-error').length).withContext('Fehler am Feld').toBeGreaterThan(0);
    });
  }

  it('alle vier setzen den Fokus ins erste Feld — auch Registrieren', async () => {
    for (const tc of cases) {
      const { el } = await render(tc.cmp, tc.prepare);
      const first = el.querySelector('form input')!;
      expect(first.hasAttribute('autofocus')).withContext(tc.name).toBeTrue();
      TestBed.resetTestingModule();
    }
  });
});
