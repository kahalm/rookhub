import { TestBed } from '@angular/core/testing';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { TournamentDatePipe, formatTournamentDates, formatTournamentDay } from './tournament-date';

/** ICU setzt um den Bis-Strich teils schmale Leerzeichen — verglichen wird der Text, nicht die Breite. */
const plain = (text: string) => text.replace(/\s/g, ' ');

/**
 * Codereview F6-010: die Turnierseite zeigte jeden Termin roh als „2026-12-18 – 2026-12-20",
 * waehrend der Kalender daneben „Sa., 3. Okt." schreibt.
 */
describe('Turniertermine', () => {
  it('schreibt einen Tag in der Sprache der Oberfläche, nicht als ISO-Datum', () => {
    expect(formatTournamentDay('2026-12-18', 'de')).toBe('18. Dez. 2026');
    expect(formatTournamentDay('2026-12-18', 'en')).toBe('Dec 18, 2026');
    expect(formatTournamentDay('2026-12-18', 'hr')).not.toContain('2026-12-18');
    expect(formatTournamentDay('2026-12-18', 'hu')).not.toContain('2026-12-18');
  });

  it('fasst einen Zeitraum zusammen', () => {
    expect(plain(formatTournamentDates('2026-12-18', '2026-12-20', 'de'))).toBe('18.–20. Dez. 2026');
    expect(plain(formatTournamentDates('2026-12-18', '2026-12-20', 'en'))).toBe('Dec 18 – 20, 2026');
    expect(plain(formatTournamentDates('2026-12-18', '2027-01-03', 'de'))).toBe('18. Dez. 2026 – 3. Jan. 2027');
  });

  it('nennt nur einen Tag, wenn Beginn und Ende gleich sind oder eines fehlt', () => {
    expect(formatTournamentDates('2026-12-18', '2026-12-18', 'de')).toBe('18. Dez. 2026');
    expect(formatTournamentDates('2026-12-18', null, 'de')).toBe('18. Dez. 2026');
    expect(formatTournamentDates(null, '2026-12-20', 'de')).toBe('20. Dez. 2026');
    expect(formatTournamentDates(null, null, 'de')).toBe('');
  });

  it('liest den Tag als Kalendertag, nicht als Zeitpunkt in der Ortszeit', () => {
    // Mitternacht in der Ortszeit gelesen, wuerde der 1. Januar westlich von Greenwich zum
    // 31. Dezember des Vorjahres.
    expect(formatTournamentDay('2026-01-01', 'en')).toBe('Jan 1, 2026');
  });

  it('zeigt Spieltermine mit Wochentag und ohne Jahr', () => {
    expect(formatTournamentDay('2026-09-26', 'de', 'playDay')).toBe('Sa., 26. Sept.');
  });

  it('lässt fremde Formate stehen und übersteht eine unbekannte Sprachkennung', () => {
    expect(formatTournamentDay('irgendwann', 'de')).toBe('irgendwann');
    expect(formatTournamentDates('2026-12-18', 'bald', 'de')).toBe('2026-12-18 – bald');
    expect(() => formatTournamentDay('2026-12-18', 'nicht/gueltig')).not.toThrow();
  });

  describe('Pipe', () => {
    let pipe: TournamentDatePipe;
    let translate: TranslateService;

    beforeEach(() => {
      TestBed.configureTestingModule({ providers: [provideTranslateService({ fallbackLang: 'en' })] });
      translate = TestBed.inject(TranslateService);
      pipe = TestBed.runInInjectionContext(() => new TournamentDatePipe());
    });

    it('folgt einem Sprachwechsel, obwohl sich das Datum nicht ändert', () => {
      translate.use('de');
      expect(plain(pipe.transform('2026-12-18', '2026-12-20'))).toBe('18.–20. Dez. 2026');

      translate.use('en');
      expect(plain(pipe.transform('2026-12-18', '2026-12-20'))).toBe('Dec 18 – 20, 2026');
    });

    it('formatiert Spieltermine auf Wunsch mit Wochentag', () => {
      translate.use('de');
      expect(pipe.transform('2026-09-26', null, 'playDay')).toBe('Sa., 26. Sept.');
    });
  });
});
