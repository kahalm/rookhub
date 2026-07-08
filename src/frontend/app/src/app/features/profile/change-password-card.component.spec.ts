import { of, throwError } from 'rxjs';
import { ChangePasswordCardComponent } from './change-password-card.component';

function make(authOverride?: any) {
  const auth = authOverride ?? {
    changePassword: jasmine.createSpy('changePassword').and.returnValue(of({})),
  };
  const snackbar = { success: jasmine.createSpy('success'), info: jasmine.createSpy('info') };
  const translate = { instant: (k: string) => k };
  const c = new ChangePasswordCardComponent(auth as any, snackbar as any, translate as any);
  return { c, auth, snackbar };
}

describe('ChangePasswordCardComponent', () => {
  it('refuses a mismatch without hitting the API', () => {
    const { c, auth, snackbar } = make();
    c.changePwdCurrent = 'old';
    c.changePwdNew = 'new1';
    c.changePwdConfirm = 'new2';
    c.changePassword();
    expect(auth.changePassword).not.toHaveBeenCalled();
    expect(snackbar.info).toHaveBeenCalledWith('profile.changePwd.mismatch');
  });

  it('calls auth.changePassword when the new passwords match', () => {
    const { c, auth, snackbar } = make();
    c.changePwdCurrent = 'old';
    c.changePwdNew = 'secret99';
    c.changePwdConfirm = 'secret99';
    c.changePassword();
    expect(auth.changePassword).toHaveBeenCalledWith('old', 'secret99');
    expect(snackbar.success).toHaveBeenCalledWith('profile.changePwd.done');
    expect(c.showChangePwd).toBeFalse();
  });

  it('maps a 401 to the wrongPassword message', () => {
    const auth = { changePassword: jasmine.createSpy('changePassword').and.returnValue(throwError(() => ({ status: 401 }))) };
    const { c, snackbar } = make(auth);
    c.changePwdCurrent = 'old';
    c.changePwdNew = 'secret99';
    c.changePwdConfirm = 'secret99';
    c.changePassword();
    expect(snackbar.info).toHaveBeenCalledWith('profile.changePwd.wrongPassword');
    expect(c.changingPwd).toBeFalse();
  });
});
