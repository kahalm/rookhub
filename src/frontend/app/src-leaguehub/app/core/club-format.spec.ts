import { HttpErrorResponse } from '@angular/common/http';
import { importSummary, normalizeResult, reasonText, scanAvailability, uploadErrorText, yearOf } from './club-format';
import { ScoresheetStatus } from './club.models';

describe('club-format', () => {
  it('Zusammenfassung des Imports: übernommen, als Schwaz, schon da, abgelehnt, abgeschnitten', () => {
    expect(importSummary({ added: 3, anonymized: 2, duplicates: 1, truncated: false, ids: [], failed: [{ index: 4, white: null, black: null, reason: 'illegal' }] }))
      .toBe('3 Partien übernommen (2 mit „Schwaz“), 1 schon da, 1 nicht übernommen.');
    expect(importSummary({ added: 1, anonymized: 0, duplicates: 0, truncated: true, ids: [1], failed: [] }))
      .toContain('1 Partie übernommen. Es wurden nur die ersten 500');
  });

  it('jeder Grund des Servers hat einen Satz', () => {
    for (const r of ['noLeaguePlayer', 'onlyOwnClub', 'notFound', 'fromPosition', 'illegal', 'noMoves', 'tooLong', 'duplicate', 'empty', 'tooLarge']) {
      expect(reasonText(r)).not.toBe('Nicht übernommen.');
    }
    expect(reasonText('onlyOwnClub')).toContain('Schwaz');
    expect(importSummary({ added: 2, duplicates: 0, anonymized: 2, truncated: false, ids: [], failed: [], remembered: 1 }))
      .toContain('1 Namens-Zuordnung gemerkt');
  });

  it('Einlesen gesperrt: nennt, ab wann es wieder geht', () => {
    const s: ScoresheetStatus = { available: true, dailyLimit: 1, usedToday: 1, nextAllowedAt: '2026-09-29T08:00:00Z', languages: [] };
    const r = scanAvailability(s, new Date('2026-09-28T10:00:00Z'));
    expect(r.ok).toBeFalse();
    expect(r.text).toContain('das nächste ab');
    expect(r.text).toContain('Eines je 24 Stunden');
    expect(scanAvailability({ ...s, dailyLimit: 10, usedToday: 3 }).text).toContain('10 je 24 Stunden (heute: 3 von 10)');
    expect(scanAvailability({ ...s, usedToday: 0, blocked: 'anonDailyLimit' }).ok).toBeFalse();
    expect(scanAvailability({ ...s, usedToday: 0 }).ok).toBeTrue();
    expect(scanAvailability({ ...s, unlimited: true }).ok).toBeTrue();
    expect(scanAvailability({ ...s, available: false }).text).toContain('nicht eingerichtet');
  });

  it('Absagen beim Hochladen', () => {
    expect(uploadErrorText(new HttpErrorResponse({ status: 400, error: { reason: 'dailyLimit' } }))).toContain('Tageslimit');
    expect(uploadErrorText(new HttpErrorResponse({ status: 400, error: { reason: 'anonDailyLimit' } }))).toContain('morgen');
    expect(uploadErrorText(new HttpErrorResponse({ status: 403 }))).toContain('Berechtigung');
  });

  it('Jahr aus gelesenen Datumsangaben, Ergebnis in PGN-Form', () => {
    const now = new Date('2026-09-28');
    expect(yearOf('2026-06-05', now)).toBe(2026);
    expect(yearOf('5.6.26', now)).toBe(2026);
    expect(yearOf('Juni 2019', now)).toBe(2019);
    expect(yearOf('2099', now)).toBeNull();
    expect(yearOf(null, now)).toBeNull();
    expect(normalizeResult('½-½')).toBe('1/2-1/2');
    expect(normalizeResult('1 - 0')).toBe('1-0');
    expect(normalizeResult('?')).toBe('*');
  });
});
