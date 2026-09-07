import { sharedCookieDomain } from './partner-site';

/**
 * Anzeige-Einstellungen, die BEIDE Oberflaechen teilen.
 *
 * <p>RookHub und die Turnierseite sind zwei Origins und teilen den `localStorage` nicht — wer auf
 * der einen Seite Deutsch waehlt, sass auf der anderen weiter in Englisch. Ein Cookie auf der
 * gemeinsamen Elterndomaene (`.oberschmid.homes`) sehen beide.</p>
 *
 * <p>Bewusst LESBAR (kein HttpOnly) und ohne Geheimnis-Charakter: es sind Anzeige-Einstellungen,
 * kein Nachweis. Der Wert wird zusaetzlich geraetelokal gespeichert — ohne gemeinsame
 * Elterndomaene (localhost, IP, Vorschau-Build) gibt es kein Cookie, und dann muss die
 * Einstellung trotzdem einen Seitenaufruf ueberleben.</p>
 *
 * <p>Diese Datei ist die EINE Stelle fuer den Mechanismus. Vorher trug der Design-Modus seine
 * eigene Cookie-Lesefunktion; eine zweite Einstellung haette sie kopiert.</p>
 */
const MaxAgeSeconds = 60 * 60 * 24 * 365;

/** Der geteilte Wert, oder `null` — auch bei gesperrten Cookies. */
export function readSharedPreference(cookieName: string): string | null {
  try {
    const hit = document.cookie.split(';')
      .map(c => c.trim())
      .find(c => c.startsWith(cookieName + '='));
    return hit ? decodeURIComponent(hit.slice(cookieName.length + 1)) : null;
  } catch { return null; }
}

/**
 * Schreibt den geteilten Wert. Ohne gemeinsame Elterndomaene passiert NICHTS (statt ein Cookie
 * auf einen geratenen Bereich zu setzen) — der Aufrufer speichert ohnehin auch lokal.
 */
export function writeSharedPreference(cookieName: string, value: string): void {
  const domain = sharedCookieDomain();
  if (!domain) return;
  try {
    const secure = location.protocol === 'https:' ? '; Secure' : '';
    document.cookie = `${cookieName}=${encodeURIComponent(value)}; domain=${domain}; path=/; `
      + `max-age=${MaxAgeSeconds}; SameSite=Lax${secure}`;
  } catch { /* Cookies gesperrt — der geraetelokale Wert traegt weiter */ }
}

/**
 * Meldet, wenn der Tab wieder sichtbar wird UND sich der geteilte Wert inzwischen geaendert hat.
 * Gebraucht beim Hin- und Herwechseln zwischen zwei offenen Tabs der beiden Seiten: Cookies
 * melden sich nicht von selbst.
 */
export function onSharedPreferenceChange(
  cookieName: string, current: () => string | null, apply: (value: string) => void): void {
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState !== 'visible') return;
    const shared = readSharedPreference(cookieName);
    if (shared && shared !== current()) apply(shared);
  });
}
