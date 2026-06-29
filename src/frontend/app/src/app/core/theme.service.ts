import { Injectable } from '@angular/core';

export type AppTheme = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'rookhub_app_theme';

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

    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored === 'light' || stored === 'dark' || stored === 'system') {
        this._preference = stored;
      }
    } catch {}

    this.apply();
  }

  setPreference(pref: AppTheme): void {
    this._preference = pref;
    try { localStorage.setItem(STORAGE_KEY, pref); } catch {}
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
