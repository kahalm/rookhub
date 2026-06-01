import { Injectable, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

const LANG_KEY = 'rookhub_lang';

export const SUPPORTED_LANGS = ['en', 'de', 'hr'] as const;
export type AppLang = (typeof SUPPORTED_LANGS)[number];

export interface LangOption { code: AppLang; label: string; }

/**
 * Kapselt ngx-translate: registriert die Sprachen, ermittelt die Startsprache
 * (gespeichert → Browser → en) und persistiert die Auswahl in localStorage.
 */
@Injectable({ providedIn: 'root' })
export class LocaleService {
  private translate = inject(TranslateService);

  readonly languages: LangOption[] = [
    { code: 'en', label: 'English' },
    { code: 'de', label: 'Deutsch' },
    { code: 'hr', label: 'Hrvatski' },
  ];

  /** In AppComponent vor dem Rendern aufrufen. */
  init(): void {
    this.translate.addLangs([...SUPPORTED_LANGS]);
    this.translate.setFallbackLang('en');
    this.translate.use(this.resolveInitial());
  }

  get current(): AppLang {
    return this.normalize(this.translate.getCurrentLang()) ?? 'en';
  }

  use(lang: AppLang): void {
    this.translate.use(lang);
    try { localStorage.setItem(LANG_KEY, lang); } catch {}
  }

  private resolveInitial(): AppLang {
    try {
      const stored = this.normalize(localStorage.getItem(LANG_KEY));
      if (stored) return stored;
    } catch {}
    return this.normalize(this.translate.getBrowserLang()) ?? 'en';
  }

  private normalize(lang: string | null | undefined): AppLang | null {
    return lang && (SUPPORTED_LANGS as readonly string[]).includes(lang) ? (lang as AppLang) : null;
  }
}
