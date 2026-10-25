import { DirectoryEntry } from './tournament-directory.model';
import { MIXED, PinCategory } from './pin-palette';
import {
  groupByPoint, groupStyle, hasSubscribed, isVague, pinStyle, placeName, placeOf, venuesOf,
} from './map-pin-groups';

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

const RAPID: PinCategory = { key: 'Rapid', fill: '#009e73', stroke: '#00664a', label: 'x' };

describe('map-pin-groups', () => {
  it('bündelt bei EXAKT gleicher Koordinate auf fünf Nachkommastellen', () => {
    const groups = groupByPoint([
      entry({ id: 'a', lat: 47.2178, lon: 11.6411 }),
      entry({ id: 'b', lat: 47.217801, lon: 11.641104 }),   // rundet auf denselben Punkt
      entry({ id: 'c', lat: 47.21782, lon: 11.6411 }),      // gut zwei Meter daneben
    ]);

    expect(groups.map(g => g.members.map(m => m.entry.id))).toEqual([['a', 'b'], ['c']]);
    expect(groups[0].lat).toBe(47.2178);
    expect(groups[0].lon).toBe(11.6411);
    expect(groups[0].category).toBe(MIXED);
  });

  it('zeichnet je Spielort einen Punkt und überspringt Turniere ohne Koordinaten', () => {
    const liga = entry({
      id: 'liga',
      venues: [
        { name: 'Mayrhofen', lat: 47.1667, lon: 11.8667, geoSource: 'City' },
        { name: 'St.Veit', lat: 46.7667, lon: 14.3667, geoSource: 'City' },
      ],
    });
    const groups = groupByPoint([liga, entry({ id: 'ohne', lat: null, lon: null })]);

    expect(groups.length).toBe(2);
    expect(groups.map(g => g.members[0].spot.name)).toEqual(['Mayrhofen', 'St.Veit']);
  });

  it('legt Punkte mit gemerkten Turnieren ans Ende, sonst in Server-Reihenfolge', () => {
    const groups = groupByPoint([
      entry({ id: 'g', lat: 47.0, lon: 13.0, subscribed: true }),
      entry({ id: 'x', lat: 47.1, lon: 13.1 }),
      entry({ id: 'y', lat: 47.2, lon: 13.2 }),
    ]);

    expect(groups.map(g => g.members[0].entry.id)).toEqual(['x', 'y', 'g']);
    expect(groups.map(hasSubscribed)).toEqual([false, false, true]);
  });

  it('nimmt ohne Spielort-Liste den Ort des Eintrags selbst', () => {
    expect(venuesOf(entry({ geoPlaceName: 'Salzburg Stadt', geoSource: 'Region' })))
      .toEqual([{ name: 'Salzburg Stadt', lat: 47.8, lon: 13.04, geoSource: 'Region' }]);
    expect(venuesOf(entry({ geoPlaceName: null, location: null })))
      .toEqual([{ name: '', lat: 47.8, lon: 13.04, geoSource: 'City' }]);
    expect(venuesOf(entry({ lat: null }))).toEqual([]);
  });

  it('gibt einem Punkt ohne Spielort-Liste keinen Platzhalter als Namen', () => {
    expect(venuesOf(entry({ geoPlaceName: null, location: 'various', geoSource: 'Region' })))
      .toEqual([{ name: '', lat: 47.8, lon: 13.04, geoSource: 'Region' }]);
    expect(placeName('verschiedene Spielorte')).toBe('');
    expect(placeName('Innsbruck')).toBe('Innsbruck');
    expect(placeName(null)).toBe('');
  });

  it('nennt einen Punkt nur ungefähr, wenn ALLE Spielorte Regionsmitten sind', () => {
    const [vage] = groupByPoint([entry({ geoSource: 'Region' }), entry({ id: '2', geoSource: 'Region' })]);
    const [gemischt] = groupByPoint([entry({ geoSource: 'Region' }), entry({ id: '2', geoSource: 'City' })]);

    expect(isVague(vage)).toBeTrue();
    expect(isVague(gemischt)).toBeFalse();
  });

  it('nimmt als Ortsnamen Spielort, Lexikon-Ort oder Ortstext — einen Platzhalter nie', () => {
    const name = (over: Partial<DirectoryEntry>) => placeOf(groupByPoint([entry(over)])[0]);

    expect(name({ geoPlaceName: 'Hallein' })).toBe('Hallein');
    expect(name({ geoPlaceName: null, location: 'Tirol' })).toBe('Tirol');
    // P15/4e: ohne Spielort-Liste trug der Punkt den Ortstext als Namen — auch „various".
    expect(name({ geoPlaceName: null, location: 'various' })).toBe('');
    expect(name({ geoPlaceName: null, location: 'Div.' })).toBe('');
    const benannt = { name: 'various', lat: 47.8, lon: 13.04, geoSource: 'City' as const };
    expect(name({ geoPlaceName: null, location: 'Tirol', venues: [benannt] })).toBe('Tirol');
    const unbenannt = { name: '', lat: 47.8, lon: 13.04, geoSource: 'City' as const };
    expect(name({ geoPlaceName: null, location: 'various', venues: [unbenannt] })).toBe('');
    expect(name({ geoPlaceName: null, location: 'Tirol', venues: [unbenannt] })).toBe('Tirol');
  });

  it('zeigt die Klasse als Füllung, „gemerkt" als dicken Ring und „ungefähr" abgeschwächt', () => {
    expect(pinStyle(RAPID, false, false))
      .toEqual({ color: '#00664a', fillColor: '#009e73', weight: 2, fillOpacity: 0.85 });
    expect(pinStyle(RAPID, true, false))
      .toEqual({ color: '#202124', fillColor: '#009e73', weight: 4, fillOpacity: 0.85 });
    expect(pinStyle(RAPID, true, true))
      .toEqual({ color: '#202124', fillColor: '#009e73', weight: 4, fillOpacity: 0.45 });
  });

  it('leitet den Stil des Bündels aus Klasse, gemerkt und ungefähr ab', () => {
    const [group] = groupByPoint([entry({ geoSource: 'Region', subscribed: true })]);
    group.category = RAPID;

    expect(groupStyle(group)).toEqual(pinStyle(RAPID, true, true));
  });
});
