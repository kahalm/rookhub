import { Injectable, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import {
  onSharedPreferenceChange, readSharedPreference, writeSharedPreference,
} from './shared-preference';

const LANG_KEY = 'rookhub_lang';

/**
 * Dieselbe Sprache auf BEIDEN Oberflaechen. RookHub und die Turnierseite sind zwei Origins und
 * teilen den `localStorage` nicht — wer hier Deutsch waehlte, sass dort weiter in Englisch. Das
 * Cookie auf der gemeinsamen Elterndomaene sehen beide; Mechanismus in `shared-preference.ts`
 * (dasselbe Verfahren wie beim Design-Modus).
 */
const LANG_COOKIE = 'rookhub_lang';

// Weltweit relevante Sprachen. Übersetzungen: public/i18n/<code>.json
// (fehlende Keys fallen automatisch auf 'en' zurück).
export const SUPPORTED_LANGS = [
  'en', 'de', 'hr', 'es', 'fr', 'it', 'pt', 'nl', 'sv', 'pl', 'cs', 'ro', 'hu',
  'el', 'tr', 'ru', 'uk', 'ar', 'fa', 'hi', 'id', 'vi', 'zh', 'ja', 'ko',
] as const;
export type AppLang = (typeof SUPPORTED_LANGS)[number];

// Rechts-nach-links-Sprachen (Layout-Richtung via <html dir>).
const RTL_LANGS: readonly AppLang[] = ['ar', 'fa'];

// Sprachen, für die wir Angular-Locale-Daten registrieren (für DatePipe/DecimalPipe etc.).
// Nur die tatsächlich übersetzten Sprachen; alle anderen formatieren über 'en'
// (vermeidet „Missing locale data"-Fehler bei nicht registrierten Locales). 'en' ist
// in Angular eingebaut und muss nicht registriert werden.
export const FORMAT_LOCALES: readonly string[] = ['en', 'de', 'hr'];

/**
 * Ermittelt die Start-Locale für `LOCALE_ID` (Bootstrap) — gleiche Quelle wie
 * {@link LocaleService.resolveInitial} (gespeichert → Browser → en), aber ohne
 * TranslateService, damit es als reine LOCALE_ID-Factory beim Bootstrap läuft.
 * Gibt nur eine in {@link FORMAT_LOCALES} registrierte Locale zurück.
 */
export function resolveStartupLocale(): string {
  // Die GETEILTE Wahl zuerst: sie ist die, die der Nutzer zuletzt auf einer der beiden Seiten
  // getroffen hat. Sonst der geraetelokale Wert, sonst der Browser.
  let lang: string | null = readSharedPreference(LANG_COOKIE);
  if (!lang) { try { lang = localStorage.getItem(LANG_KEY); } catch {} }
  if (!lang && typeof navigator !== 'undefined' && navigator.language) {
    lang = navigator.language.split('-')[0];
  }
  return lang && FORMAT_LOCALES.includes(lang) ? lang : 'en';
}

export interface LangOption { code: AppLang; label: string; }

/**
 * Kapselt ngx-translate: registriert die Sprachen, ermittelt die Startsprache
 * (gespeichert → Browser → en), persistiert die Auswahl und setzt <html lang/dir>.
 */
@Injectable({ providedIn: 'root' })
export class LocaleService {
  private translate = inject(TranslateService);

  // Label = Eigenbezeichnung der Sprache (im Switcher angezeigt).
  // en/de/hr zuerst (Haupt-/Heimsprachen), danach die übrigen nach globaler
  // Reichweite (grobe Sprecherzahl) — große Märkte oben.
  readonly languages: LangOption[] = [
    { code: 'en', label: 'English' },
    { code: 'de', label: 'Deutsch' },
    { code: 'hr', label: 'Hrvatski' },
    { code: 'es', label: 'Español' },
    { code: 'zh', label: '中文' },
    { code: 'hi', label: 'हिन्दी' },
    { code: 'ar', label: 'العربية' },
    { code: 'pt', label: 'Português' },
    { code: 'fr', label: 'Français' },
    { code: 'ru', label: 'Русский' },
    { code: 'ja', label: '日本語' },
    { code: 'id', label: 'Bahasa Indonesia' },
    { code: 'it', label: 'Italiano' },
    { code: 'tr', label: 'Türkçe' },
    { code: 'ko', label: '한국어' },
    { code: 'vi', label: 'Tiếng Việt' },
    { code: 'fa', label: 'فارسی' },
    { code: 'pl', label: 'Polski' },
    { code: 'uk', label: 'Українська' },
    { code: 'ro', label: 'Română' },
    { code: 'nl', label: 'Nederlands' },
    { code: 'el', label: 'Ελληνικά' },
    { code: 'hu', label: 'Magyar' },
    { code: 'cs', label: 'Čeština' },
    { code: 'sv', label: 'Svenska' },
  ];

  /** In AppComponent vor dem Rendern aufrufen. */
  init(): void {
    this.translate.addLangs([...SUPPORTED_LANGS]);
    this.translate.setFallbackLang('en');
    const lang = this.resolveInitial();
    this.translate.use(lang);
    this.applyHtmlAttrs(lang);

    // Wechselt man zwischen zwei offenen Tabs der beiden Seiten, soll die Sprachwahl mitkommen —
    // Cookies melden sich nicht von selbst.
    onSharedPreferenceChange(LANG_COOKIE, () => this.current, value => {
      const next = this.normalize(value);
      if (next) this.apply(next);
    });
  }

  get current(): AppLang {
    return this.normalize(this.translate.getCurrentLang()) ?? 'en';
  }

  use(lang: AppLang): void {
    this.apply(lang);
    try { localStorage.setItem(LANG_KEY, lang); } catch {}
    // Damit die andere Oberflaeche dieselbe Sprache zeigt.
    writeSharedPreference(LANG_COOKIE, lang);
  }

  /** Umschalten OHNE zu speichern — der Weg fuer eine anderswo getroffene Wahl. */
  private apply(lang: AppLang): void {
    this.translate.use(lang);
    this.applyHtmlAttrs(lang);
  }

  /** Setzt <html lang> und dir (rtl für Arabisch/Persisch, sonst ltr). */
  private applyHtmlAttrs(lang: AppLang): void {
    const el = document.documentElement;
    el.lang = lang;
    el.dir = RTL_LANGS.includes(lang) ? 'rtl' : 'ltr';
  }

  /**
   * Reihenfolge: die GETEILTE Wahl (zuletzt auf einer der beiden Seiten getroffen) → der
   * geraetelokale Wert → die Browsersprache → Englisch.
   */
  private resolveInitial(): AppLang {
    const shared = this.normalize(readSharedPreference(LANG_COOKIE));
    if (shared) return shared;
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
