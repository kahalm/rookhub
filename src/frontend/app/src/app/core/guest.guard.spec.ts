import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree } from '@angular/router';
import { guestGuard } from './guest.guard';
import { AuthService } from './auth.service';

describe('guestGuard', () => {
  function configure(loggedIn: boolean) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
      ],
    });
  }

  const run = (queryParams: Record<string, string> = {}) =>
    TestBed.runInInjectionContext(() => guestGuard({ queryParams } as any, { url: '/login' } as any));

  it('lässt Abgemeldete auf die Anmeldemaske', () => {
    configure(false);
    expect(run()).toBe(true);
  });

  it('schickt Angemeldete auf ihr Rücksprungziel statt auf die Maske', () => {
    // Der Fall vom 2026-09-09: Token gültig, Maske trotzdem offen → Passwort umsonst getippt.
    configure(true);
    const res = run({ returnUrl: '/courses/340?mode=sequential' }) as UrlTree;
    expect(res instanceof UrlTree).toBeTrue();
    expect(res.toString()).toBe('/courses/340?mode=sequential');
  });

  it('schickt Angemeldete ohne Ziel auf die Wurzel der App', () => {
    configure(true);
    expect((run() as UrlTree).toString()).toBe('/');
  });

  it('lässt Angemeldete mit ?switch=1 bewusst an die Maske (Konto-Wechsel)', () => {
    configure(true);
    expect(run({ switch: '1' })).toBe(true);
  });

  it('folgt keinem fremden Rücksprungziel (offene Weiterleitung)', () => {
    configure(true);
    expect((run({ returnUrl: '//evil.example' }) as UrlTree).toString()).toBe('/');
    expect((run({ returnUrl: 'https://evil.example/x' }) as UrlTree).toString()).toBe('/');
  });
});
