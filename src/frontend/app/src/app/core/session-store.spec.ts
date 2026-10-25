import { TestBed } from '@angular/core/testing';
import { OfflineService } from './offline.service';
import { SessionStore } from './session-store';

function jwt(expSecondsFromNow: number): string {
  return `h.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + expSecondsFromNow }))}.s`;
}

describe('SessionStore (Zerlegung Z34)', () => {
  const user = { token: jwt(3600), username: 'u', userId: 1, isAdmin: false };

  beforeEach(() => { localStorage.clear(); TestBed.configureTestingModule({}); });
  afterEach(() => localStorage.clear());

  it('liest die gespeicherte Sitzung beim Aufbau; unlesbar → entfernt', () => {
    localStorage.setItem('rookhub_user', JSON.stringify(user));
    expect(TestBed.inject(SessionStore).value?.username).toBe('u');
    TestBed.resetTestingModule();
    localStorage.setItem('rookhub_user', '{ kaputt');
    expect(TestBed.inject(SessionStore).value).toBeNull();
    expect(localStorage.getItem('rookhub_user')).toBeNull();
  });

  it('validUser beendet eine abgelaufene Sitzung und räumt auf', () => {
    const store = TestBed.inject(SessionStore);
    const expired = { ...user, token: jwt(-1) };
    localStorage.setItem('rookhub_user', JSON.stringify(expired));
    localStorage.setItem('rookhub_admin_user', '{}');
    const clear = spyOn(TestBed.inject(OfflineService), 'clearOnLogout');
    store.set(expired);
    expect(store.validUser()).toBeNull();
    expect(store.value).toBeNull();
    expect(localStorage.getItem('rookhub_user')).toBeNull();
    expect(localStorage.getItem('rookhub_admin_user')).toBeNull();
    expect(clear).toHaveBeenCalled();
  });

  it('persist schreibt die Sitzung und setzt storageFull zurück', () => {
    const store = TestBed.inject(SessionStore);
    store.storageFull = true;
    expect(store.persist(user)).toBeTrue();
    expect(store.storageFull).toBeFalse();
    expect(JSON.parse(localStorage.getItem('rookhub_user')!).userId).toBe(1);
  });

  it('Merker für das ausstehende Ende der geteilten Anmeldung', () => {
    const store = TestBed.inject(SessionStore);
    expect(store.sessionEndPending).toBeFalse();
    store.setSessionEndPending(true);
    expect(localStorage.getItem(SessionStore.SessionEndPendingKey)).toBe('1');
    expect(store.sessionEndPending).toBeTrue();
    store.setSessionEndPending(false);
    expect(store.sessionEndPending).toBeFalse();
  });

  it('user$ meldet jede gesetzte Sitzung', () => {
    const store = TestBed.inject(SessionStore);
    const seen: (string | null)[] = [];
    store.user$.subscribe(u => seen.push(u?.username ?? null));
    store.set(user);
    store.set(null);
    expect(seen).toEqual([null, 'u', null]);
  });
});
