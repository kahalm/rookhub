import { Injectable } from '@angular/core';
import { MAT_NATIVE_DATE_FORMATS, MatDateFormats, NativeDateAdapter } from '@angular/material/core';

/** Reihenfolge von Tag/Monat/Jahr in der Locale (de: day, month, year; en: month, day, year; hu: year, month, day). */
export function dateFieldOrder(locale: string): string[] {
  return new Intl.DateTimeFormat(locale, { year: 'numeric', month: '2-digit', day: '2-digit' })
    .formatToParts(new Date(2017, 0, 2))
    .map(p => p.type)
    .filter(t => t === 'year' || t === 'month' || t === 'day');
}

/**
 * NativeDateAdapter, der getippte Daten in der Reihenfolge der App-Sprache liest („16.07.2026" in de) —
 * der eingebaute nimmt `Date.parse` und verstünde nur das US-Format. Seit b1-rk-edit-datum-us-format geteilt
 * (vorher nur in der Wochenpost): Material-Datumsfelder statt `<input type="date">`, das nach der BROWSERsprache
 * formatiert („10/10/2026" in der deutschen Oberfläche, DESIGN 5.13).
 */
@Injectable()
export class LocaleNativeDateAdapter extends NativeDateAdapter {
  override parse(value: unknown, parseFormat?: unknown): Date | null {
    if (typeof value === 'string') {
      const nums = value.match(/\d+/g);
      if (nums && nums.length === 3) {
        const order = dateFieldOrder(this.locale);
        const part = (k: string) => Number(nums[order.indexOf(k)]);
        let year = part('year');
        if (year < 100) year += 2000;
        const month = part('month') - 1, day = part('day');
        const d = new Date(year, month, day);
        return year >= 1000 && d.getMonth() === month && d.getDate() === day ? d : this.invalid();
      }
    }
    return super.parse(value, parseFormat);
  }
}

/** Datum zweistellig in der Reihenfolge der Sprache: de „16.07.2026", en „07/16/2026", hu „2026. 07. 16.". */
export const LOCALE_DATE_FORMATS: MatDateFormats = {
  ...MAT_NATIVE_DATE_FORMATS,
  display: {
    ...MAT_NATIVE_DATE_FORMATS.display,
    dateInput: { year: 'numeric', month: '2-digit', day: '2-digit' },
  },
};

/** "YYYY-MM-DD" → lokales Datum (null bei leer/ungültig). */
export function parseYmd(s: string | null | undefined): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s || '');
  return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null;
}

/** Lokales Datum → "YYYY-MM-DD" ('' bei leer/ungültig). */
export function formatYmd(d: Date | null | undefined): string {
  if (!d || isNaN(d.getTime())) return '';
  const p2 = (n: number) => n.toString().padStart(2, '0');
  return `${d.getFullYear()}-${p2(d.getMonth() + 1)}-${p2(d.getDate())}`;
}
