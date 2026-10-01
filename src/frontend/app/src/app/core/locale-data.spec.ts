import { formatDate, formatNumber } from '@angular/common';
import { FORMAT_LOCALES } from './locale.service';
import { FORMAT_LOCALE_DATA, registerFormatLocaleData } from './locale-data';

/**
 * resolveStartupLocale() gibt jede FORMAT_LOCALE als LOCALE_ID heraus. Fehlen ihre Locale-Daten,
 * wirft jede DatePipe/DecimalPipe NG0701 — frueher hingen die Daten in drei app.config.ts von
 * Hand an der Liste, ohne Test (Codereview F8-014).
 */
describe('locale-data', () => {
  it('fuehrt genau die FORMAT_LOCALES — eine neue Sprache dort ohne Locale-Daten faellt hier auf', () => {
    expect(Object.keys(FORMAT_LOCALE_DATA).sort()).toEqual([...FORMAT_LOCALES].sort());
  });

  it('hat fuer jede FORMAT_LOCALE ausser en die Daten GENAU dieser Locale', () => {
    for (const locale of FORMAT_LOCALES.filter(l => l !== 'en')) {
      const data = FORMAT_LOCALE_DATA[locale];
      expect(data).withContext(locale).toBeTruthy();
      // Index 0 der Angular-Locale-Daten ist die Locale-Kennung.
      expect(data?.[0]).withContext(locale).toBe(locale);
    }
  });

  it('nach registerFormatLocaleData formatieren Datum und Zahl in jeder FORMAT_LOCALE ohne NG0701', () => {
    registerFormatLocaleData();
    const day = new Date(2026, 9, 1);
    for (const locale of FORMAT_LOCALES) {
      expect(() => formatDate(day, 'mediumDate', locale)).withContext(locale).not.toThrow();
      expect(() => formatNumber(1234.5, locale)).withContext(locale).not.toThrow();
    }
    expect(formatNumber(1234.5, 'de')).toBe('1.234,5');
  });
});
