import { of, throwError } from 'rxjs';
import { DeleteAccountCardComponent } from './delete-account-card.component';

function make(authOverride?: any) {
  const auth = authOverride ?? {
    deleteAccount: jasmine.createSpy('deleteAccount').and.returnValue(of({})),
  };
  const snackbar = { success: jasmine.createSpy('success'), info: jasmine.createSpy('info') };
  const translate = { instant: (k: string) => k };
  const c = new DeleteAccountCardComponent(auth as any, snackbar as any, translate as any);
  return { c, auth, snackbar };
}

describe('DeleteAccountCardComponent', () => {
  it('is a no-op without a password, and calls auth.deleteAccount otherwise', () => {
    const { c, auth } = make();
    c.deletePassword = '';
    c.deleteAccount();
    expect(auth.deleteAccount).not.toHaveBeenCalled();

    c.deletePassword = 'pw';
    c.deleteAccount();
    expect(auth.deleteAccount).toHaveBeenCalledWith('pw');
  });

  it('maps a 401 to the wrongPassword message and re-enables the button', () => {
    const auth = { deleteAccount: jasmine.createSpy('deleteAccount').and.returnValue(throwError(() => ({ status: 401 }))) };
    const { c, snackbar } = make(auth);
    c.deletePassword = 'pw';
    c.deleteAccount();
    expect(snackbar.info).toHaveBeenCalledWith('profile.delete.wrongPassword');
    expect(c.deleting).toBeFalse();
  });

  it('cancelDelete resets the confirm state', () => {
    const { c } = make();
    c.showDelete = true;
    c.deletePassword = 'pw';
    c.cancelDelete();
    expect(c.showDelete).toBeFalse();
    expect(c.deletePassword).toBe('');
  });
});
