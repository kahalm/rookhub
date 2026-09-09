import { of } from 'rxjs';
import { RegisterComponent } from './register.component';
import { AuthPrefillService } from '../../core/auth-prefill.service';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';

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

/**
 * Gerendertes Template: ohne autocapitalize="none" wurde der Benutzername am Handy als 'Kahalm' statt 'kahalm'
 * gespeichert; new-password laesst den Passwort-Manager ein starkes Passwort vorschlagen statt das alte einzufuellen.
 */
describe('RegisterComponent Template (Mobil-Attribute)', () => {
  it('setzt autocomplete/autocapitalize/autocorrect/spellcheck auf den Eingabefeldern', async () => {
    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {} },
        { provide: SnackbarService, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const user = el.querySelector('input[name="username"]')!;
    expect(user.getAttribute('autocomplete')).toBe('username');
    expect(user.getAttribute('autocapitalize')).toBe('none');
    expect(user.getAttribute('autocorrect')).toBe('off');
    expect(user.getAttribute('spellcheck')).toBe('false');
    expect(el.querySelector('input[name="email"]')!.getAttribute('autocomplete')).toBe('email');
    expect(el.querySelector('input[name="password"]')!.getAttribute('autocomplete')).toBe('new-password');
  });
});
