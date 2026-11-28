import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';
import { HandoffReady } from './handoff-ready';

describe('authGuard', () => {
  function configure(loggedIn: boolean) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
      ],
    });
  }

  const run = (url: string) =>
    TestBed.runInInjectionContext(() => authGuard({} as any, { url } as any));

  it('lets logged-in users through', () => {
    configure(true);
    expect(run('/weekly')).toBe(true);
  });

  it('redirects logged-out users to /login with returnUrl + authRequired flag', () => {
    configure(false);
    const res = run('/weekly/5') as UrlTree;
    expect(res instanceof UrlTree).toBe(true);
    expect(res.toString()).toContain('/login');
    expect(res.queryParams['returnUrl']).toBe('/weekly/5');
    expect(res.queryParams['authRequired']).toBe('1');
  });

  describe('während der Start-Übernahme der Anmeldung (Fund 2026-11-28, LeagueHub)', () => {
    // Die Wurzelkomponente startet consumeIncoming ohne zu warten, die erste Navigation läuft parallel. Entschied der
    // Guard sofort, stand der Gast mit geteilter Anmeldung auf /login, obwohl rh-session einen Moment später 200 gab.
    let auth: { isLoggedIn: boolean };
    let finish!: () => void;

    beforeEach(() => {
      auth = { isLoggedIn: false };
      TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: AuthService, useValue: auth }] });
      TestBed.inject(HandoffReady).track(new Promise<void>(r => finish = r));
    });

    it('wartet auf die laufende Übernahme und lässt danach durch, statt auf /login zu schicken', async () => {
      const res = run('/verein');
      expect(res instanceof Promise).withContext('entscheidet erst nach der Übernahme').toBeTrue();
      auth.isLoggedIn = true;                       // rh-session → 200 → adoptSession
      finish();
      expect(await res).toBe(true);
    });

    it('schickt auf die Maske, wenn die Übernahme ohne Anmeldung endet (204)', async () => {
      const res = run('/verein') as Promise<UrlTree>;
      finish();
      const tree = await res;
      expect(tree.toString()).toContain('/login');
      expect(tree.queryParams['returnUrl']).toBe('/verein');
    });

    it('wartet nicht endlos: nach HandoffReady.GuardWaitMs geht es auf die Maske', async () => {
      jasmine.clock().install();
      try {
        let out: unknown;
        (run('/verein') as Promise<unknown>).then(v => out = v);
        jasmine.clock().tick(HandoffReady.GuardWaitMs - 1);
        await Promise.resolve();
        expect(out).withContext('vor dem Zeitlimit').toBeUndefined();
        jasmine.clock().tick(1);
        for (let i = 0; i < 3; i++) await Promise.resolve();
        expect(out instanceof UrlTree).toBeTrue();
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('bleibt synchron, sobald die Übernahme fertig ist', async () => {
      finish();
      await TestBed.inject(HandoffReady).settled();
      expect(run('/verein') instanceof UrlTree).toBeTrue();
    });
  });
});
