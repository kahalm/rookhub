import { Injectable } from '@angular/core';
import { sharedCookieDomain } from './partner-site';

export type AppTheme = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'rookhub_app_theme';

/**
 * Derselbe Wert zusaetzlich als Cookie auf der ELTERNdomaene — RookHub und die Turnierseite sind
 * zwei Origins und teilen den localStorage nicht. Ein Cookie auf `.oberschmid.homes` sehen beide.
 * Es ist eine reine Anzeige-Einstellung, also bewusst lesbar (kein HttpOnly) und ohne jeden
 * Geheimnis-Charakter.
 */
const COOKIE_KEY = 'rookhub_theme';
const COOKIE_MAX_AGE = 60 * 60 * 24 * 365;

@Injectable({ providedIn: 'root' })
export class ThemeService {
  // Default = dark; eine gespeicherte Nutzerwahl (siehe Konstruktor) hat Vorrang.
  private _preference: AppTheme = 'dark';
  private _systemDark = false;
  private mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');

  get preference(): AppTheme { return this._preference; }
  get isDark(): boolean {
    return this._preference === 'dark' || (this._preference === 'system' && this._systemDark);
  }

  constructor() {
    if (this.mediaQuery) {
      this._systemDark = this.mediaQuery.matches;
      this.mediaQuery.addEventListener('change', e => {
        this._systemDark = e.matches;
        this.apply();
      });
    }

    // Das geteilte Cookie hat Vorrang vor dem geraetelokalen Wert: es ist der Modus, den der
    // Nutzer zuletzt auf EINER der beiden Seiten gewaehlt hat.
    const shared = readCookie();
    const stored = readLocal();
    if (shared) this._preference = shared;
    else if (stored) this._preference = stored;

    // Wechselt man zwischen zwei offenen Tabs der beiden Seiten hin und her, soll die
    // Umschaltung mitkommen — Cookies melden sich nicht von selbst.
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState !== 'visible') return;
      const current = readCookie();
      if (current && current !== this._preference) {
        this._preference = current;
        this.apply();
      }
    });

    this.apply();
  }

  setPreference(pref: AppTheme): void {
    this._preference = pref;
    try { localStorage.setItem(STORAGE_KEY, pref); } catch {}
    writeCookie(pref);
    this.apply();
  }

  toggle(): void {
    const next: Record<AppTheme, AppTheme> = { system: 'light', light: 'dark', dark: 'system' };
    this.setPreference(next[this._preference]);
  }

  private apply(): void {
    try { document.documentElement.classList.toggle('dark-theme', this.isDark); } catch {}
  }
}

function isTheme(value: string | null | undefined): value is AppTheme {
  return value === 'light' || value === 'dark' || value === 'system';
}

function readLocal(): AppTheme | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return isTheme(value) ? value : null;
  } catch { return null; }
}

function readCookie(): AppTheme | null {
  try {
    const hit = document.cookie.split(';')
      .map(c => c.trim())
      .find(c => c.startsWith(COOKIE_KEY + '='));
    const value = hit ? decodeURIComponent(hit.slice(COOKIE_KEY.length + 1)) : null;
    return isTheme(value) ? value : null;
  } catch { return null; }
}

function writeCookie(pref: AppTheme): void {
  const domain = sharedCookieDomain();
  if (!domain) return;                      // kein gemeinsamer Elternteil (IP/localhost)
  try {
    const secure = location.protocol === 'https:' ? '; Secure' : '';
    document.cookie =
      `${COOKIE_KEY}=${pref}; domain=${domain}; path=/; max-age=${COOKIE_MAX_AGE}; SameSite=Lax${secure}`;
  } catch {}
}
