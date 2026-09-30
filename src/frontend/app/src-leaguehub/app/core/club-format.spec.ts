import { HttpErrorResponse } from '@angular/common/http';
import { importSummary, normalizeResult, reasonText, scanAvailability, sheetPgn, sheetPgnFileName, shortDateTime, uploadErrorText, yearOf } from './club-format';
import { ScoresheetStatus } from './club.models';

describe('club-format', () => {
  it('Zusammenfassung des Imports: übernommen, als Schwaz, schon da, abgelehnt, abgeschnitten', () => {
    expect(importSummary({ added: 3, anonymized: 2, duplicates: 1, truncated: false, ids: [], failed: [{ index: 4, white: null, black: null, reason: 'illegal' }] }))
      .toBe('3 Partien übernommen (2 mit „Schwaz“), 1 schon da, 1 nicht übernommen.');
    expect(importSummary({ added: 1, anonymized: 0, duplicates: 0, truncated: true, ids: [1], failed: [] }))
      .toContain('1 Partie übernommen. Es wurden nur die ersten 500');
  });

  it('jeder Grund des Servers hat einen Satz', () => {
    for (const r of ['noLeaguePlayer', 'onlyOwnClub', 'notFound', 'fromPosition', 'illegal', 'noMoves', 'tooLong', 'duplicate', 'empty', 'tooLarge', 'shareLimit']) {
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

  it('PGN einer geprüften Partie: Kopf, nummerierte Züge, Zeilen bis 80 Zeichen, Dateiname', () => {
    const g = { moves: ['e4', 'c5', 'Nf3', 'd6'], white: 'Oberschmid, Patrik', black: 'Hengl "Phil"', result: '1/2-1/2', event: null, year: 2025 };
    expect(sheetPgn(g)).toBe('[Event "?"]\n[Site "?"]\n[Date "2025.??.??"]\n[Round "?"]\n[White "Oberschmid, Patrik"]\n'
      + '[Black "Hengl \\"Phil\\""]\n[Result "1/2-1/2"]\n\n1. e4 c5 2. Nf3 d6 1/2-1/2\n');
    const long = sheetPgn({ ...g, moves: Array.from({ length: 60 }, (_, i) => i % 2 ? 'Nf6' : 'Nf3'), result: 'x', year: null });
    expect(long).toContain('[Date "????.??.??"]');
    expect(long.trimEnd().endsWith(' *')).toBeTrue();
    expect(long.split('\n').every(l => l.length <= 80)).toBeTrue();
    expect(sheetPgnFileName(g)).toBe('Oberschmid_HenglPhil_2025.pgn');
    expect(sheetPgnFileName({ ...g, white: null, year: null })).toBe('Partie_HenglPhil.pgn');
  });

  it('Datum und Uhrzeit aus der API (ohne Zone = UTC) in Ortszeit', () => {
    const d = new Date(Date.UTC(2026, 8, 28, 8, 52));
    const two = (n: number) => String(n).padStart(2, '0');
    expect(shortDateTime('2026-09-28T08:52:00')).toBe(`${two(d.getDate())}.${two(d.getMonth() + 1)}., ${two(d.getHours())}:${two(d.getMinutes())}`);
    expect(shortDateTime(null)).toBe('');
  });
});
