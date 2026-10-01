import { registerLocaleData } from '@angular/common';
import localeDe from '@angular/common/locales/de';
import localeHr from '@angular/common/locales/hr';
import localeHu from '@angular/common/locales/hu';

/**
 * Angular-Locale-Daten je Sprache aus `FORMAT_LOCALES` (locale.service.ts) — EINE Stelle fuer
 * RookHub, Turnierseite und KidHub. `resolveStartupLocale()` liefert jede FORMAT_LOCALE als
 * LOCALE_ID; fehlen ihre Daten, wirft jede DatePipe/DecimalPipe NG0701 „Missing locale data" und
 * die Seite bricht. `locale-data.spec.ts` bindet die Schluessel an FORMAT_LOCALES: wer dort eine
 * Sprache eintraegt, muss sie hier ergaenzen (Codereview F8-014). 'en' ist in Angular eingebaut.
 */
export const FORMAT_LOCALE_DATA: Readonly<Record<string, readonly unknown[] | null>> = {
  en: null,
  de: localeDe,
  hr: localeHr,
  hu: localeHu,
};

/** Registriert die Locale-Daten aller FORMAT_LOCALES (einmal beim Laden der App-Konfiguration). */
export function registerFormatLocaleData(): void {
  for (const data of Object.values(FORMAT_LOCALE_DATA)) {
    if (data) registerLocaleData(data);
  }
}
