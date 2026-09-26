import { formatQuietUntil } from './quiet-hours.util';

describe('formatQuietUntil (Sperrzeiten der Spark, 0.546.0)', () => {
  it('leer bei fehlender oder unlesbarer Angabe', () => {
    expect(formatQuietUntil(null, 'de')).toBe('');
    expect(formatQuietUntil(undefined, 'de')).toBe('');
    expect(formatQuietUntil('kein Datum', 'de')).toBe('');
  });

  it('Wochentag und Uhrzeit in der Sprache der Oberfläche', () => {
    const iso = '2026-09-25T12:00:00Z';   // Freitag
    const expected = new Date(iso).toLocaleString('de', { weekday: 'short', hour: '2-digit', minute: '2-digit' });
    expect(formatQuietUntil(iso, 'de')).toBe(expected);
    expect(formatQuietUntil(iso, 'de')).toContain('Fr');
  });

  it('unbekannte Sprache fällt auf die des Browsers zurück, statt zu werfen', () => {
    expect(formatQuietUntil('2026-09-25T12:00:00Z', 'xx-invalid-!!')).not.toBe('');
  });
});
