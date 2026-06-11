import { of, throwError } from 'rxjs';
import { ForgotPasswordComponent } from './forgot-password.component';

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
