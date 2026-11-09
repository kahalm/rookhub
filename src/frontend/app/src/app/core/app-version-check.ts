/**
 * Server-Version nachsehen, ohne den Service Worker (U1, „Seite nicht gefunden“ prüft die Server-Version): das vom Build
 * erzeugte Changelog-Asset (`scripts/changelog-json.mjs` → `/assets/changelog.json`, neuester Eintrag zuerst) frisch
 * holen. `cache: 'no-store'` umgeht den HTTP-Zwischenspeicher, `ngsw-bypass` den Service Worker (er hält das Asset in
 * seiner lazy-Gruppe `changelog` und lieferte sonst den Stand der alten Fassung).
 */
export const SERVER_CHANGELOG_URL = '/assets/changelog.json?ngsw-bypass=1';

/** Höchstens so lange auf die Antwort warten — danach gilt „nichts gefunden“ (normale Nicht-gefunden-Seite). */
export const SERVER_VERSION_TIMEOUT_MS = 5000;

/** `a` neuer als `b`? Dreiteilig numerisch (`0.936.0` > `0.935.12`); Unlesbares ist nie neuer. */
export function isNewerVersion(a: string, b: string): boolean {
  const parse = (v: string) => /^\d+\.\d+\.\d+$/.test(v) ? v.split('.').map(Number) : null;
  const x = parse(a);
  const y = parse(b);
  if (!x || !y) return false;
  for (let i = 0; i < 3; i++) {
    if (x[i] !== y[i]) return x[i] > y[i];
  }
  return false;
}

/** Oberste Version des Server-Changelogs, `null` bei jedem Fehler (offline, 404, SPA-Rückfall statt JSON, Zeitlimit). */
export async function fetchServerVersion(
  fetcher: typeof fetch = (...args) => fetch(...args), timeoutMs = SERVER_VERSION_TIMEOUT_MS): Promise<string | null> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const timeout = new Promise<null>(resolve => { timer = setTimeout(() => resolve(null), timeoutMs); });
  const load = (async () => {
    try {
      const res = await fetcher(SERVER_CHANGELOG_URL, { cache: 'no-store' });
      if (!res.ok) return null;
      const entries: unknown = await res.json();
      const top = Array.isArray(entries) ? (entries[0] as { version?: unknown } | undefined)?.version : null;
      return typeof top === 'string' ? top : null;
    } catch {
      return null;
    }
  })();
  try {
    return await Promise.race([load, timeout]);
  } finally {
    clearTimeout(timer);
  }
}
