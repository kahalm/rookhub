import { TestBed } from '@angular/core/testing';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { buildIcs } from '@rh/core/ics';
import { directoryCalendarEvent } from './directory-calendar-event';
import { DirectoryEntry } from './tournament-directory.model';

function entry(over: Partial<DirectoryEntry> = {}): DirectoryEntry {
  return {
    id: '1221888', chessResultsId: '1221888', name: 'Open Braunau 2026', federation: 'AUT',
    state: 'Oberösterreich', startDate: '2026-12-18', endDate: '2026-12-20', location: 'Ranshofen',
    timeControl: '90 min', speed: 'Standard', organizer: 'SK Braunau', director: null,
    chiefArbiter: null, rounds: 7, playerCount: 42, lat: 48.2, lon: 13.0, geoSource: 'City',
    geoPlaceName: 'Ranshofen', distanceKm: null, cancelled: false, subscribed: false,
    groupSize: 1, groups: [], venues: [], kind: 'Individual', isLeague: false, ageGroups: [],
    gender: 'Open', ignored: false, roundDates: [], sources: [], ...over,
  };
}

/**
 * Karte und Detailseite bauten den Termin je selbst — mit verschiedener Kennung
 * (directory-… gegen chess-results-…): wer ein Turnier einmal aus der Liste und einmal von der
 * Detailseite uebertrug, hatte es zweimal im Kalender (Codereview F6-009). Beide rufen jetzt diese
 * eine Abbildung; ihre Specs pruefen dieselbe Kennung.
 */
describe('directoryCalendarEvent', () => {
  let translate: TranslateService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTranslateService({ fallbackLang: 'en' })] });
    translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', {
      tournamentDirectory: { players: '{{count}} Teilnehmer', detail: { rounds: 'Runden', organizer: 'Veranstalter' } },
    });
    translate.use('de');
  });

  it('nimmt die Identitaet als Kennung — fuer chess-results- wie fuer FIDE-Turniere', () => {
    expect(directoryCalendarEvent(entry(), translate)?.uid).toBe('directory-1221888@rookhub');
    expect(directoryCalendarEvent(entry({ id: 'f123', chessResultsId: null }), translate)?.uid)
      .toBe('directory-f123@rookhub');
  });

  it('schreibt nur Bekanntes, mit uebersetzten Bezeichnungen und dem chess-results-Link', () => {
    const event = directoryCalendarEvent(entry(), translate)!;

    expect(event).toEqual({
      uid: 'directory-1221888@rookhub',
      title: 'Open Braunau 2026',
      start: '2026-12-18',
      end: '2026-12-20',
      location: 'Ranshofen',
      description: [
        '90 min', 'Runden: 7', '42 Teilnehmer', 'Veranstalter: SK Braunau',
        'https://chess-results.com/tnr1221888.aspx?lan=1',
      ].join('\n'),
      url: 'https://chess-results.com/tnr1221888.aspx?lan=1',
    });
  });

  it('laesst Fehlendes weg; ohne chess-results-Nummer kein Link', () => {
    const event = directoryCalendarEvent(entry({
      id: 'f123', chessResultsId: null, rounds: null, playerCount: null, organizer: null, endDate: null,
    }), translate)!;

    expect(event.description).toBe('90 min');
    expect(event.url).toBeNull();
    // Ein-Tages-Termin: buildIcs setzt DTEND auf den Folgetag des Starts.
    expect(buildIcs({ ...event, stamp: new Date(Date.UTC(2026, 9, 1)) })).toContain('DTEND;VALUE=DATE:20261219');
  });

  it('bietet ohne Startdatum keinen Termin an', () => {
    expect(directoryCalendarEvent(entry({ startDate: null }), translate)).toBeNull();
  });
});
