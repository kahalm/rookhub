import { DestroyRef } from '@angular/core';
import { of, throwError } from 'rxjs';
import { DirectoryPlaceController } from './directory-place';
import { DEFAULT_RADIUS_KM, DirectoryFilter, EMPTY_FILTER, SearchProfile } from './tournament-directory.model';

describe('DirectoryPlaceController', () => {
  const destroyRef = { onDestroy: () => () => {} } as unknown as DestroyRef;

  function make(profile: SearchProfile | null = null, geo: { supported?: boolean; fix?: { lat: number; lon: number }; fail?: string } = {}) {
    const filter: DirectoryFilter = { ...EMPTY_FILTER };
    const reload = jasmine.createSpy('reload');
    const directory: any = {
      places: () => of([]),
      nearestPlace: jasmine.createSpy('nearestPlace').and.returnValue(of({ label: 'Schwaz', lat: 47.3, lon: 11.7 })),
    };
    const geolocation: any = {
      supported: geo.supported ?? true,
      current: () => geo.fail ? throwError(() => geo.fail) : of(geo.fix ?? { lat: 47.35, lon: 11.71 }),
    };
    const place = new DirectoryPlaceController(directory, geolocation, destroyRef, {
      filter: () => filter, activeProfile: () => profile, reload,
    });
    return { place, filter, reload, directory };
  }

  it('ein gewaehlter Ort setzt den Mittelpunkt und ersetzt das Profil; Abtippen loest ihn', () => {
    const { place, filter, reload } = make();
    filter.profileId = 3;
    place.choose({ label: 'Innsbruck', lat: 47.26, lon: 11.39 } as any);
    expect(filter.lat).toBe(47.26);
    expect(filter.radiusKm).toBe(DEFAULT_RADIUS_KM);
    expect(filter.profileId).toBeNull();
    expect(reload).toHaveBeenCalledTimes(1);
    place.input('Innsbruck');
    expect(filter.lat).toBe(47.26);
    place.input('Inns');
    expect(filter.lat).toBeNull();
  });

  it('Ortung: Koordinaten sofort, Name danach; Fehler landet in locationError', () => {
    const { place, filter, reload } = make();
    place.useCurrentLocation();
    expect(filter.lat).toBe(47.35);
    expect(place.label).toBe('Schwaz');
    expect(place.locating()).toBeFalse();
    expect(reload).toHaveBeenCalled();
    const failing = make(null, { fail: 'denied' });
    failing.place.useCurrentLocation();
    expect(failing.place.locationError()).toBe('denied' as any);
    expect(failing.filter.lat).toBeNull();
  });

  it('Radius bei aktivem Profil uebernimmt dessen Mittelpunkt; 0 heisst ueberall', () => {
    const profile = { id: 3, name: 'Tirol', placeQuery: 'Wörgl', lat: 47.49, lon: 12.06, radiusKm: 50 } as SearchProfile;
    const { place, filter } = make(profile);
    filter.profileId = 3;
    place.radiusChange(100);
    expect(filter.profileId).toBeNull();
    expect(filter.lat).toBe(47.49);
    expect(filter.radiusKm).toBe(100);
    place.radiusChange(0);
    expect(filter.radiusKm).toBeNull();
  });

  it('Profil folgen zeigt dessen Ort und Radius; Zuruecksetzen leert das Feld', () => {
    const profile = { id: 3, name: 'Tirol', placeQuery: null, lat: 47.49, lon: 12.06, radiusKm: 50 } as unknown as SearchProfile;
    const { place, filter } = make(profile);
    filter.lat = 1;
    place.followProfile();
    expect(place.label).toBe('Tirol');
    expect(filter.radiusKm).toBe(50);
    expect(filter.lat).toBeNull();
    place.reset();
    expect(place.label).toBe('');
  });
});
