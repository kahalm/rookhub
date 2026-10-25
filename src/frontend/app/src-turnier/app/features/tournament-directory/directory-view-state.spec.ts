import { DirectoryViewHost, applyStoredView, snapshotView } from './directory-view-state';
import { DirectoryRangePreset, EMPTY_FILTER, TournamentSpeed, rangeFor } from './tournament-directory.model';

/** Eine Leiste wie die der Komponente — nur die Felder, die der gemerkte Zustand traegt. */
function host(): DirectoryViewHost {
  const h: DirectoryViewHost = {
    tab: 'list', rangePreset: 'quarter', filter: { ...EMPTY_FILTER, kinds: [], ageGroups: [], genders: [] },
    placeLabel: '', calendarYear: 2026, calendarMonth: 10, mapColourBy: 'speed', mapView: null,
    speeds: ['Standard', 'Rapid', 'Blitz'] as TournamentSpeed[],
    applyRangePreset(preset: DirectoryRangePreset) {
      h.rangePreset = preset;
      if (preset !== 'custom') Object.assign(h.filter, rangeFor(preset));
    },
  };
  return h;
}

describe('directory-view-state (Golden: zurückgelesen = gemerkt)', () => {
  it('liest einen voll gesetzten Zustand unverändert zurück', () => {
    const a = host();
    a.tab = 'map';
    a.applyRangePreset('custom');
    Object.assign(a.filter, {
      from: '2026-11-01', to: '2026-12-31', federation: 'AUT', speed: 'Rapid', text: 'Open',
      weekendOnly: true, minPlayers: 20, profileId: 3, lat: 47.27, lon: 11.39, radiusKm: 50,
      kinds: ['Team'], ageGroups: ['U12'], genders: ['Female'], adultsOnly: false, hideLeagues: true,
      includeIgnored: true, includeImplausible: true,
    });
    a.placeLabel = '6020 Innsbruck (AT)';
    a.calendarYear = 2027;
    a.calendarMonth = 2;
    a.mapColourBy = 'gender';
    a.mapView = { lat: 47.3, lon: 11.4, zoom: 9, centre: '47.27|11.39|50' };
    const stored = JSON.parse(JSON.stringify(snapshotView(a)));

    const b = host();
    const applied = applyStoredView(b, stored);

    // Bis auf das Suchprofil: das legt erst `applyQueryParams` an, wenn die Liste es noch kennt —
    // hier kommt es als `storedProfileId` zurueck.
    expect(snapshotView(b)).toEqual({ ...stored, profileId: null });
    expect(applied).toEqual({ placeRestored: true, storedProfileId: 3, hasStoredProfile: true });
  });

  it('liest die Vorgabe-Leiste unverändert zurück', () => {
    const a = host();
    a.applyRangePreset('quarter');
    const stored = JSON.parse(JSON.stringify(snapshotView(a)));

    const b = host();
    applyStoredView(b, stored);

    expect(snapshotView(b)).toEqual(stored);
  });

  it('verwirft Werte, die es nicht (mehr) gibt, statt sie als Filter weiterlaufen zu lassen', () => {
    const b = host();
    applyStoredView(b, {
      tab: 'grid', rangePreset: 'decade', speed: 'Bullet', kinds: ['Team', 'Simul'], mapColourBy: 'rainbow',
      mapView: { lat: 95, lon: 0, zoom: 3 }, calendarMonth: 13, calendarYear: 2027, minPlayers: '20',
    });

    const s = snapshotView(b);
    expect([s['tab'], s['rangePreset'], s['speed'], s['kinds'], s['mapColourBy'], s['mapView'], s['minPlayers']])
      .toEqual(['list', 'quarter', null, ['Team'], 'speed', null, null]);
    expect([s['calendarYear'], s['calendarMonth']]).toEqual([2026, 10]);
    expect(applyStoredView(b, null)).toBeNull();
  });
});
