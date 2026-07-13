import { of } from 'rxjs';
import { RegisterComponent } from './register.component';
import { AuthPrefillService } from '../../core/auth-prefill.service';

describe('RegisterComponent — optionale Email', () => {
  function make(email: string, prefill = new AuthPrefillService()) {
    const auth: any = { register: jasmine.createSpy('register').and.returnValue(of({})) };
    const router: any = { navigateByUrl: jasmine.createSpy('navigateByUrl') };
    const route: any = { snapshot: { queryParams: {} } };
    const snackbar: any = { warn: jasmine.createSpy('warn') };
    const translate: any = { instant: (k: string) => k };
    const c = new RegisterComponent(auth, prefill, router, route, snackbar, translate);
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
      { snapshot: { queryParams: {} } } as any, { warn: () => {} } as any, { instant: (k: string) => k } as any);
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
