import { of, throwError } from 'rxjs';
import { LoginComponent } from './login.component';
import { AuthPrefillService } from '../../core/auth-prefill.service';

function make(queryParams: Record<string, string> = {}, prefill = new AuthPrefillService()) {
  const auth: any = { login: jasmine.createSpy('login').and.returnValue(of({})) };
  const router: any = { navigateByUrl: jasmine.createSpy('navigateByUrl') };
  const route: any = { snapshot: { queryParams } };
  const snackbar: any = { warn: jasmine.createSpy('warn') };
  const translate: any = { instant: (k: string) => k };
  return { c: new LoginComponent(auth, prefill, router, route, snackbar, translate), auth, router, snackbar, prefill };
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

  it('warns on login error and clears loading', () => {
    const { c, auth, router, snackbar } = make();
    auth.login.and.returnValue(throwError(() => ({ error: { message: 'nope' } })));
    c.onSubmit();
    expect(snackbar.warn).toHaveBeenCalledWith('nope');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(c.loading).toBeFalse();
  });
});
