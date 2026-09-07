import { Injectable } from '@angular/core';
import {
  onSharedPreferenceChange, readSharedPreference, writeSharedPreference,
} from './shared-preference';

export type AppTheme = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'rookhub_app_theme';

/** Geteilt mit der Turnierseite — Mechanismus in `shared-preference.ts`. */
const COOKIE_KEY = 'rookhub_theme';

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
    onSharedPreferenceChange(COOKIE_KEY, () => this._preference, value => {
      if (!isTheme(value)) return;
      this._preference = value;
      this.apply();
    });

    this.apply();
  }

  setPreference(pref: AppTheme): void {
    this._preference = pref;
    try { localStorage.setItem(STORAGE_KEY, pref); } catch {}
    writeSharedPreference(COOKIE_KEY, pref);
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
  const value = readSharedPreference(COOKIE_KEY);
  return isTheme(value) ? value : null;
}
