import { formatDate } from '@angular/common';
import { registerFormatLocaleData } from './locale-data';

/**
 * DESIGN.md 5.13 / Fund e1-rk-date-fixed-pattern: Datum und Uhrzeit in der App-Sprache. Feste DatePipe-Muster
 * wie `date:'dd.MM.yyyy'` oder `date:'HH:mm'` zeigten auf Englisch „Thu, 09.07.2026 · 21:00“ und „11.10. 00:20“
 * (liest sich als 10. November). Erlaubt sind die Locale-Formate (`short`, `mediumDate`, `shortTime` …) und der
 * reine Wochentag `EEE`; ein festes Muster mit Tag/Monat/Jahr/Stunde/Minute fällt hier auf.
 *
 * Die Quelltexte liefert karma.conf.js aus (files, included: false); gelesen werden sie per fetch.
 */
type Served = { __karma__?: { files?: Record<string, unknown> } };

const DATE_PIPE = /\|\s*date\s*:\s*(['"])([^'"]*)\1/g;
const FIXED_PART = /d|M|y|H|h|m/;
const NAMED = new Set(['short', 'medium', 'long', 'full', 'shortDate', 'mediumDate', 'longDate', 'fullDate',
  'shortTime', 'mediumTime', 'longTime', 'fullTime']);

/** Feste DatePipe-Muster im Quelltext (benannte Locale-Formate zählen nicht). */
function fixedDatePatterns(source: string): string[] {
  const out: string[] = [];
  for (const m of source.matchAll(DATE_PIPE)) {
    const fmt = m[2];
    if (!NAMED.has(fmt) && FIXED_PART.test(fmt)) out.push(fmt);
  }
  return out;
}

function sourceUrls(): string[] {
  const served = (globalThis as Served).__karma__?.files ?? {};
  return Object.keys(served).filter(u => /^\/base\/src[\w-]*\/.+\.(ts|html)$/.test(u) && !u.endsWith('.spec.ts'));
}

describe('Datumsformate (DESIGN 5.13)', () => {
  it('erkennt feste Muster und lässt Locale-Formate und den Wochentag durch', () => {
    expect(fixedDatePatterns(`{{ a | date:'dd.MM. HH:mm' }} {{ b | date: 'HH:mm' }}`)).toEqual(['dd.MM. HH:mm', 'HH:mm']);
    expect(fixedDatePatterns(`{{ a | date:'EEE' }}, {{ a | date:'mediumDate' }} · {{ a | date:'shortTime' }} {{ c | date:'short' }}`))
      .toEqual([]);
  });

  it('kein Quelltext benutzt ein festes DatePipe-Muster', async () => {
    const urls = sourceUrls();
    expect(urls.length).withContext('karma liefert keine Quelltexte aus').toBeGreaterThan(100);
    const hits: string[] = [];
    await Promise.all(urls.map(async u => {
      const text = await (await fetch(u)).text();
      for (const fmt of fixedDatePatterns(text)) hits.push(`${u}: ${fmt}`);
    }));
    expect(hits).toEqual([]);
  });

  it('Wochenpost-Format: deutsch „Do., 09.07.2026 · 21:00“, englisch „Thu, Jul 9, 2026 · 9:00 PM“', () => {
    registerFormatLocaleData();
    const at = new Date(2026, 6, 9, 21, 0);
    const line = (l: string) => `${formatDate(at, 'EEE', l)}, ${formatDate(at, 'mediumDate', l)} · ${formatDate(at, 'shortTime', l)}`;
    expect(line('de')).toBe('Do., 09.07.2026 · 21:00');
    expect(line('en').replace(/ /g, ' ')).toBe('Thu, Jul 9, 2026 · 9:00 PM');
    // Listen (Statistik, CI-Läufe): `short`.
    const t = new Date(2026, 9, 11, 0, 20);
    expect(formatDate(t, 'short', 'de')).toBe('11.10.26, 00:20');
    expect(formatDate(t, 'short', 'en').replace(/ /g, ' ')).toBe('10/11/26, 12:20 AM');
  });
});
