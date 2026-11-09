import { SERVER_VERSION_URL, fetchServerVersion, isNewerVersion } from './app-version-check';

describe('isNewerVersion (U1)', () => {
  it('vergleicht dreiteilig numerisch, nicht als Text', () => {
    expect(isNewerVersion('0.936.0', '0.935.12')).toBeTrue();
    expect(isNewerVersion('0.935.10', '0.935.9')).toBeTrue();
    expect(isNewerVersion('1.0.0', '0.999.99')).toBeTrue();
    expect(isNewerVersion('0.935.0', '0.935.0')).toBeFalse();
    expect(isNewerVersion('0.934.9', '0.935.0')).toBeFalse();
  });

  it('Unlesbares ist nie neuer', () => {
    expect(isNewerVersion('', '0.1.0')).toBeFalse();
    expect(isNewerVersion('v0.936.0', '0.935.0')).toBeFalse();
    expect(isNewerVersion('0.936', '0.935.0')).toBeFalse();
  });
});

describe('fetchServerVersion (U1)', () => {
  const json = (body: unknown, ok = true) => Promise.resolve({ ok, json: () => Promise.resolve(body) } as Response);

  it('holt version.json ohne Zwischenspeicher und am Service Worker vorbei (nicht das ganze Changelog)', async () => {
    const fetcher = jasmine.createSpy('fetch').and.returnValue(json({ version: '0.940.0' }));
    expect(await fetchServerVersion(fetcher)).toBe('0.940.0');
    const [url, init] = fetcher.calls.mostRecent().args;
    expect(url).toBe(SERVER_VERSION_URL);
    expect(url).toContain('version.json');
    expect(url).toContain('ngsw-bypass');
    expect(init).toEqual({ cache: 'no-store' });
  });

  it('null bei Fehlerstatus, Nicht-JSON (SPA-Rückfall), Netzfehler und Zeitlimit', async () => {
    expect(await fetchServerVersion(() => json({ version: '0.940.0' }, false))).toBeNull();
    expect(await fetchServerVersion(() => json([{ version: '0.940.0' }]))).toBeNull();
    expect(await fetchServerVersion(() => Promise.resolve({ ok: true, json: () => Promise.reject(new SyntaxError('<')) } as Response))).toBeNull();
    expect(await fetchServerVersion(() => Promise.reject(new TypeError('offline')))).toBeNull();
    expect(await fetchServerVersion(() => new Promise<Response>(() => {}), 1)).toBeNull();
  });
});
