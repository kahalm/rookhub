import { DestroyRef } from '@angular/core';
import { fakeAsync, tick } from '@angular/core/testing';
import { Observable, of } from 'rxjs';
import { ViewStateService } from '@rh/core/view-state.service';
import { DirectoryViewSync } from './directory-view-state';
import {
  DirectoryViewAuth, DirectoryViewStateHost, DirectoryViewStateSync,
} from './directory-view-state-sync';

/** Ein Abgleich mit Attrappen fuer Server, Anmeldung und Komponente. */
function setup(opts: {
  userId?: number | null; loggedIn?: boolean; impersonating?: boolean;
  remote?: Record<string, unknown> | null | undefined; ready?: boolean;
} = {}) {
  let state: Record<string, unknown> = { tab: 'list' };
  const applied: (Record<string, unknown> | null)[] = [];
  const saved: unknown[] = [];
  let reloads = 0;
  const destroyCallbacks: (() => void)[] = [];

  const auth = {
    isLoggedIn: opts.loggedIn ?? true,
    isImpersonating: opts.impersonating ?? false,
    currentUser: opts.userId === null ? null : { userId: opts.userId ?? 7 },
  } as unknown as DirectoryViewAuth;
  const viewStates = {
    get: () => of(opts.remote) as Observable<Record<string, unknown> | null | undefined>,
    save: (_key: string, s: unknown) => { saved.push(s); return of(true); },
  } as unknown as ViewStateService;
  const destroyRef = { onDestroy: (cb: () => void) => { destroyCallbacks.push(cb); return () => undefined; } } as unknown as DestroyRef;
  const host: DirectoryViewStateHost = {
    snapshot: () => state,
    apply: stored => applied.push(stored),
    ready: () => opts.ready ?? true,
    reload: () => reloads++,
  };
  const sync = new DirectoryViewStateSync(auth, viewStates, destroyRef, host);
  sync.start();
  return {
    sync, applied, saved, reloads: () => reloads,
    setState: (s: Record<string, unknown>) => (state = s),
  };
}

describe('DirectoryViewStateSync', () => {
  const clear = () => Object.keys(localStorage)
    .filter(k => k.startsWith(DirectoryViewStateSync.ViewKey)).forEach(k => localStorage.removeItem(k));
  beforeEach(clear);
  afterEach(clear);

  it('bildet den lokalen Schlüssel je Nutzer, ohne Anmeldung einen Gast-Schlüssel', () => {
    expect(DirectoryViewStateSync.viewKeyFor(7)).toBe('rh.turnier.directoryView.7');
    expect(DirectoryViewStateSync.viewKeyFor(null)).toBe('rh.turnier.directoryView.guest');
    expect(DirectoryViewStateSync.viewKeyFor(undefined)).toBe('rh.turnier.directoryView.guest');
    expect(DirectoryViewStateSync.StateKey).toBe('turnier.directory');
  });

  it('legt den gemerkten Zustand des Nutzers auf die Leiste und räumt den nutzerlosen Altschlüssel ab', () => {
    localStorage.setItem('rh.turnier.directoryView', '{"tab":"map"}');
    localStorage.setItem('rh.turnier.directoryView.7', '{"tab":"calendar"}');
    const t = setup();

    t.sync.restore();

    expect(t.applied).toEqual([{ tab: 'calendar' }]);
    expect(localStorage.getItem('rh.turnier.directoryView')).toBeNull();
  });

  it('gibt bei fehlendem oder kaputtem Eintrag null weiter', () => {
    const t = setup();
    t.sync.restore();
    localStorage.setItem('rh.turnier.directoryView.7', '{kaputt');
    t.sync.restore();

    expect(t.applied).toEqual([null, null]);
  });

  it('sichert lokal sofort und beim Nutzer erst nach dem Abgleich und gedrosselt', fakeAsync(() => {
    const t = setup({ remote: { tab: 'list' } });
    t.setState({ tab: 'map' });

    t.sync.store();
    expect(localStorage.getItem('rh.turnier.directoryView.7')).toBe('{"tab":"map"}');
    tick(DirectoryViewSync.PersistDebounceMs);
    expect(t.saved).withContext('vor dem Abgleich zum Server').toEqual([]);

    t.sync.syncRemote();
    t.sync.store();
    tick(DirectoryViewSync.PersistDebounceMs - 1);
    expect(t.saved).toEqual([]);
    tick(1);
    expect(t.saved).toEqual([{ tab: 'map' }]);
  }));

  it('schickt für Gäste und beim Einstieg als Nutzer nichts zum Server', fakeAsync(() => {
    for (const opts of [{ loggedIn: false, userId: null }, { impersonating: true }]) {
      const t = setup({ ...opts, remote: null });
      t.sync.syncRemote();
      t.sync.store();
      tick(DirectoryViewSync.PersistDebounceMs);
      expect(t.saved).toEqual([]);
    }
    expect(localStorage.getItem('rh.turnier.directoryView.guest')).not.toBeNull();
  }));

  it('schiebt den lokalen Zustand hinauf, wenn der Server nichts hat', fakeAsync(() => {
    const t = setup({ remote: null });

    t.sync.syncRemote();
    tick(DirectoryViewSync.PersistDebounceMs);

    expect(t.saved).toEqual([{ tab: 'list' }]);
    expect(t.applied).toEqual([]);
  }));

  it('übernimmt einen abweichenden Server-Stand und lädt neu, sobald die Seite bereit ist', () => {
    const bereit = setup({ remote: { tab: 'calendar' } });
    bereit.sync.syncRemote();
    expect(bereit.applied).toEqual([{ tab: 'calendar' }]);
    expect(bereit.reloads()).toBe(1);

    const imAufbau = setup({ remote: { tab: 'calendar' }, ready: false });
    imAufbau.sync.syncRemote();
    expect(imAufbau.applied).toEqual([{ tab: 'calendar' }]);
    expect(imAufbau.reloads()).toBe(0);

    const gleich = setup({ remote: { tab: 'list' } });
    gleich.sync.syncRemote();
    expect(gleich.applied).toEqual([]);
    expect(gleich.reloads()).toBe(0);
  });

  it('schickt nach gescheitertem Abruf nur, was der Nutzer danach geändert hat', fakeAsync(() => {
    const t = setup({ remote: undefined });
    t.sync.syncRemote();

    t.sync.store();
    tick(DirectoryViewSync.PersistDebounceMs);
    expect(t.saved).withContext('unveränderter Aufbau ging hinauf').toEqual([]);

    t.setState({ tab: 'map' });
    t.sync.store();
    tick(DirectoryViewSync.PersistDebounceMs);
    expect(t.saved).toEqual([{ tab: 'map' }]);
  }));

  it('zieht die Vergleichsbasis nach, wenn der Abruf vor dem fertigen Aufbau scheiterte', fakeAsync(() => {
    const t = setup({ remote: undefined, ready: false });
    t.sync.syncRemote();
    t.setState({ tab: 'map' });            // applyQueryParams setzt noch Profil/Ort
    t.sync.captureBaselineIfFailed();

    t.sync.store();
    tick(DirectoryViewSync.PersistDebounceMs);
    expect(t.saved).toEqual([]);

    t.setState({ tab: 'calendar' });
    t.sync.store();
    tick(DirectoryViewSync.PersistDebounceMs);
    expect(t.saved).toEqual([{ tab: 'calendar' }]);
  }));
});
