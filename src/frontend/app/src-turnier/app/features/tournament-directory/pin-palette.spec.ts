import { DirectoryEntry } from './tournament-directory.model';
import { MIXED, categoryKeyOf, categoryOf, commonCategory, legendOf } from './pin-palette';

function entry(over: Partial<DirectoryEntry> = {}): DirectoryEntry {
  return {
    id: '1', chessResultsId: '1', name: 'Turnier', federation: 'AUT', state: null,
    startDate: '2026-10-10', endDate: '2026-10-12', location: 'Salzburg', timeControl: null,
    speed: 'Standard', organizer: null, director: null, chiefArbiter: null,
    rounds: null, playerCount: null, lat: 47.8, lon: 13.04, geoSource: 'City', geoPlaceName: null,
    distanceKm: null, cancelled: false, subscribed: false, groupSize: 1, groups: [], venues: [],
    kind: 'Individual', isLeague: false, ageGroups: [], gender: 'Open',
    ignored: false, roundDates: [], sources: [], ...over,
  };
}

describe('pin-palette', () => {
  it('nimmt die einwertigen Merkmale direkt vom Eintrag', () => {
    expect(categoryKeyOf(entry({ speed: 'Rapid' }), 'speed')).toBe('Rapid');
    expect(categoryKeyOf(entry({ kind: 'Team' }), 'kind')).toBe('Team');
    expect(categoryKeyOf(entry({ gender: 'Female' }), 'gender')).toBe('Female');
  });

  /**
   * Die Altersklassen sind eine MENGE — ein Turnier fasst oft U8 bis U18 zusammen. Welche der
   * acht Klassen die Farbe bekaeme, waere eine willkuerliche Wahl; die Reduktion auf „Nachwuchs"
   * ist die, die noch stimmt.
   */
  it('macht aus IRGENDEINER Jugendklasse „Nachwuchs"', () => {
    expect(categoryKeyOf(entry({ ageGroups: ['U8', 'U10', 'U12'] }), 'youth')).toBe('youth');
    expect(categoryKeyOf(entry({ ageGroups: ['YouthUnspecified'] }), 'youth')).toBe('youth');
  });

  it('stellt Senioren NEBEN den Nachwuchs, nicht darunter', () => {
    // Seniorenschach ist Erwachsenenschach — aber eine eigene Klasse.
    expect(categoryKeyOf(entry({ ageGroups: ['Senior'] }), 'youth')).toBe('senior');
    expect(categoryKeyOf(entry({ ageGroups: [] }), 'youth')).toBe('adult');
  });

  it('faerbt ein Buendel nur, wenn ALLE seine Turniere dieselbe Klasse haben', () => {
    const rapid = [entry({ speed: 'Rapid' }), entry({ speed: 'Rapid' })];
    expect(commonCategory(rapid, 'speed').key).toBe('Rapid');

    // Auf einem Punkt mit einem Blitz- und vier Turnierschachturnieren waere jede der beiden
    // Farben eine Falschaussage.
    const gemischt = [entry({ speed: 'Rapid' }), entry({ speed: 'Blitz' })];
    expect(commonCategory(gemischt, 'speed')).toBe(MIXED);
  });

  it('faellt bei einer unbekannten Klasse auf „gemischt" zurueck statt zu raten', () => {
    // Eine Klasse, die der Server neu vergibt, bevor diese Tafel sie kennt: lieber „keine
    // Aussage" als eine falsche Farbe.
    expect(categoryOf('speed', 'Armageddon')).toBe(MIXED);
  });

  it('gibt jeder Klasse eine eigene Farbe', () => {
    for (const scheme of ['speed', 'kind', 'youth', 'gender'] as const) {
      const fills = legendOf(scheme).map(c => c.fill);
      expect(new Set(fills).size).withContext(`${scheme}: doppelte Farbe`).toBe(fills.length);
    }
  });
});
