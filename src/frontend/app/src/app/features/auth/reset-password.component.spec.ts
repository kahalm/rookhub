import { of, throwError } from 'rxjs';
import { ResetPasswordComponent } from './reset-password.component';

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

  it('canSubmit nur bei übereinstimmenden Passwörtern ab 4 Zeichen', () => {
    const { c } = make('tok');
    c.password = 'abc'; c.confirm = 'abc';
    expect(c.canSubmit).toBeFalse();          // zu kurz
    c.password = 'abcd'; c.confirm = 'abce';
    expect(c.canSubmit).toBeFalse();          // ungleich
    c.password = 'abcd'; c.confirm = 'abcd';
    expect(c.canSubmit).toBeTrue();
  });

  it('blockt Submit bei abweichender Bestätigung', () => {
    const { c, auth, snackbar } = make('tok');
    c.password = 'abcd'; c.confirm = 'xyzw';
    c.onSubmit();
    expect(auth.resetPassword).not.toHaveBeenCalled();
    expect(snackbar.warn).toHaveBeenCalledWith('auth.reset.mismatch');
  });

  it('setzt das Passwort und navigiert bei Erfolg zum Login', () => {
    const { c, auth, router, snackbar } = make('tok', of(void 0));
    c.password = 'abcd'; c.confirm = 'abcd';
    c.onSubmit();
    expect(auth.resetPassword).toHaveBeenCalledWith('tok', 'abcd');
    expect(snackbar.success).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('warnt bei Fehler', () => {
    const { c, snackbar } = make('tok', throwError(() => ({ error: { message: 'expired' } })));
    c.password = 'abcd'; c.confirm = 'abcd';
    c.onSubmit();
    expect(snackbar.warn).toHaveBeenCalledWith('expired');
    expect(c.loading).toBeFalse();
  });
});
