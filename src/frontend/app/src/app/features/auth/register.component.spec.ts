import { of } from 'rxjs';
import { RegisterComponent } from './register.component';

describe('RegisterComponent — optionale Email', () => {
  function make(email: string) {
    const auth: any = { register: jasmine.createSpy('register').and.returnValue(of({})) };
    const router: any = { navigateByUrl: jasmine.createSpy('navigateByUrl') };
    const route: any = { snapshot: { queryParams: {} } };
    const snackbar: any = { warn: jasmine.createSpy('warn') };
    const translate: any = { instant: (k: string) => k };
    const c = new RegisterComponent(auth, router, route, snackbar, translate);
    c.username = 'user'; c.password = 'secret'; c.email = email;
    return { c, auth };
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
});
