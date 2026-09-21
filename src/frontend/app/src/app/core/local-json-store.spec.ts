import { BoundedMapStore, hasKey, keysWithPrefix, localStore, readJson, readRaw, removeKey, sessionStore, writeJson, writeRaw }
  from './local-json-store';

/** Ein Speicher, der beim Schreiben wirft — Privatmodus/volles Kontingent. */
function quotaStorage(): Storage {
  return {
    length: 0,
    clear: () => { /* noop */ },
    getItem: () => null,
    key: () => null,
    removeItem: () => { throw new DOMException('blocked'); },
    setItem: () => { throw new DOMException('QuotaExceededError'); },
  } as unknown as Storage;
}

describe('local-json-store', () => {
  const KEY = 'rookhub_test_ljs';
  const PREFIX = 'rookhub_test_ljs_p_';

  beforeEach(() => {
    localStorage.removeItem(KEY);
    for (const k of keysWithPrefix(localStorage, PREFIX)) localStorage.removeItem(k);
  });
  afterEach(() => {
    localStorage.removeItem(KEY);
    for (const k of keysWithPrefix(localStorage, PREFIX)) localStorage.removeItem(k);
  });

  it('writes and reads JSON round-trip', () => {
    expect(writeJson(localStore(), KEY, { a: 1, b: ['x'] })).toBeTrue();
    expect(readJson<{ a: number; b: string[] }>(localStore(), KEY)).toEqual({ a: 1, b: ['x'] });
  });

  it('returns null for a missing key', () => {
    expect(readJson(localStore(), KEY)).toBeNull();
  });

  it('returns null for broken JSON instead of throwing', () => {
    localStorage.setItem(KEY, '{kaputt');
    expect(readJson(localStore(), KEY)).toBeNull();
  });

  it('returns null when the stored value is literally null', () => {
    localStorage.setItem(KEY, 'null');
    expect(readJson(localStore(), KEY)).toBeNull();
  });

  it('reports false when the storage refuses to write (quota)', () => {
    expect(writeJson(quotaStorage(), KEY, { a: 1 })).toBeFalse();
    expect(writeRaw(quotaStorage(), KEY, '1')).toBeFalse();
  });

  it('treats a missing storage as empty rather than failing', () => {
    expect(readJson(null, KEY)).toBeNull();
    expect(writeJson(null, KEY, 1)).toBeFalse();
    expect(hasKey(null, KEY)).toBeFalse();
    expect(keysWithPrefix(null, PREFIX)).toEqual([]);
    expect(() => removeKey(null, KEY)).not.toThrow();
  });

  it('removeKey does not throw on a locked storage', () => {
    expect(() => removeKey(quotaStorage(), KEY)).not.toThrow();
  });

  it('reads and writes raw markers and answers hasKey', () => {
    expect(hasKey(localStore(), KEY)).toBeFalse();
    expect(writeRaw(localStore(), KEY, '1')).toBeTrue();
    expect(readRaw(localStore(), KEY)).toBe('1');
    expect(hasKey(localStore(), KEY)).toBeTrue();
    removeKey(localStore(), KEY);
    expect(hasKey(localStore(), KEY)).toBeFalse();
  });

  it('lists exactly the keys carrying the prefix', () => {
    localStorage.setItem(PREFIX + 'a', '1');
    localStorage.setItem(PREFIX + 'b', '2');
    localStorage.setItem(KEY, '3');
    expect(keysWithPrefix(localStore(), PREFIX).sort()).toEqual([PREFIX + 'a', PREFIX + 'b']);
  });

  it('sessionStore is a different storage than localStore', () => {
    expect(writeJson(sessionStore(), KEY, 7)).toBeTrue();
    expect(readJson(sessionStore(), KEY)).toBe(7);
    expect(readJson(localStore(), KEY)).toBeNull();
    removeKey(sessionStore(), KEY);
  });

  describe('BoundedMapStore', () => {
    it('keeps only the newest entries by the default (lexicographic) order', () => {
      const store = new BoundedMapStore<number>(KEY, 3);
      for (const d of ['20260701', '20260702', '20260703', '20260704']) store.set(d, 1);
      expect(store.get('20260701')).toBeUndefined();
      expect(Object.keys(store.read()).sort()).toEqual(['20260702', '20260703', '20260704']);
    });

    it('uses the supplied eviction order', () => {
      // Ältester = kleinstes `at`, NICHT der kleinste Schlüssel.
      const store = new BoundedMapStore<{ at: number }>(
        KEY, 2, e => Object.keys(e).sort((a, b) => e[a].at - e[b].at));
      store.set('a', { at: 30 });
      store.set('b', { at: 10 });
      store.set('c', { at: 20 });
      expect(store.get('b')).toBeUndefined();       // ältester Schreibzeitpunkt raus
      expect(Object.keys(store.read()).sort()).toEqual(['a', 'c']);
    });

    it('remove deletes one entry and leaves the rest', () => {
      const store = new BoundedMapStore<number>(KEY, 5);
      store.set('a', 1);
      store.set('b', 2);
      expect(store.remove('a')).toBeTrue();
      expect(store.get('a')).toBeUndefined();
      expect(store.get('b')).toBe(2);
    });

    it('remove of a missing entry succeeds without touching the storage', () => {
      const store = new BoundedMapStore<number>(KEY, 5);
      expect(store.remove('nope')).toBeTrue();
      expect(localStorage.getItem(KEY)).toBeNull();
    });

    it('starts empty on corrupt content and overwrites it on the next write', () => {
      localStorage.setItem(KEY, '{kaputt');
      const store = new BoundedMapStore<number>(KEY, 5);
      expect(store.read()).toEqual({});
      store.set('a', 1);
      expect(store.get('a')).toBe(1);
    });

    it('ignores a stored array (the map must be an object)', () => {
      localStorage.setItem(KEY, '[1,2,3]');
      expect(new BoundedMapStore<number>(KEY, 5).read()).toEqual({});
    });
  });
});
