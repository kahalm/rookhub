import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TournamentDirectoryService } from './tournament-directory.service';
import { DirectoryFilter, EMPTY_FILTER } from './tournament-directory.model';

describe('TournamentDirectoryService', () => {
  let service: TournamentDirectoryService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TournamentDirectoryService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function filter(overrides: Partial<DirectoryFilter> = {}): DirectoryFilter {
    return { ...EMPTY_FILTER, ...overrides };
  }

  it('lässt leere Filter komplett aus der Query weg', () => {
    service.search(filter()).subscribe();
    const req = http.expectOne(r => r.url === '/api/tournament-directory');

    expect(req.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    req.flush({ items: [], total: 0, truncated: false });
  });

  it('schickt den Umkreis nur mit, wenn Mittelpunkt UND Radius gesetzt sind', () => {
    // Ein Radius ohne Mittelpunkt (oder umgekehrt) ist keine halbe Umkreissuche,
    // sondern eine, die der Server ablehnen würde.
    service.search(filter({ lat: 47.8, lon: 13.0 })).subscribe();
    let req = http.expectOne(r => r.url === '/api/tournament-directory');
    expect(req.request.params.has('lat')).toBeFalse();
    req.flush({ items: [], total: 0, truncated: false });

    service.search(filter({ lat: 47.8, lon: 13.0, radiusKm: 50 })).subscribe();
    req = http.expectOne(r => r.url === '/api/tournament-directory');
    expect(req.request.params.get('lat')).toBe('47.8');
    expect(req.request.params.get('radiusKm')).toBe('50');
    req.flush({ items: [], total: 0, truncated: false });
  });

  it('reicht Zeitraum, Föderation, Bedenkzeit, Text und Profil durch', () => {
    service.search(filter({
      from: '2026-09-01', to: '2026-12-31', federation: 'AUT',
      speed: 'Blitz', text: 'Braunau', weekendOnly: true, minPlayers: 20, profileId: 7,
    })).subscribe();

    const req = http.expectOne(r => r.url === '/api/tournament-directory');
    const p = req.request.params;
    expect(p.get('from')).toBe('2026-09-01');
    expect(p.get('to')).toBe('2026-12-31');
    expect(p.get('fed')).toBe('AUT');
    expect(p.get('speed')).toBe('Blitz');
    expect(p.get('q')).toBe('Braunau');
    expect(p.get('weekendOnly')).toBe('true');
    expect(p.get('minPlayers')).toBe('20');
    expect(p.get('profileId')).toBe('7');
    req.flush({ items: [], total: 0, truncated: false });
  });

  it('lässt beim Kartenaufruf den Umkreis weg — dort zählt der sichtbare Ausschnitt', () => {
    service.map(filter({ lat: 47.8, lon: 13.0, radiusKm: 50, federation: 'AUT' }),
      '47.0,12.0,48.0,14.0').subscribe();

    const req = http.expectOne(r => r.url === '/api/tournament-directory/map');
    expect(req.request.params.has('radiusKm')).toBeFalse();
    expect(req.request.params.has('lat')).toBeFalse();
    expect(req.request.params.get('bbox')).toBe('47.0,12.0,48.0,14.0');
    expect(req.request.params.get('fed')).toBe('AUT');
    req.flush([]);
  });

  it('lässt beim Kalender from/to weg — Jahr und Monat bestimmen den Zeitraum', () => {
    service.calendar(filter({ from: '2026-01-01', to: '2026-01-31', profileId: 3 }), 2026, 10).subscribe();

    const req = http.expectOne(r => r.url === '/api/tournament-directory/calendar');
    expect(req.request.params.has('from')).toBeFalse();
    expect(req.request.params.has('to')).toBeFalse();
    expect(req.request.params.get('year')).toBe('2026');
    expect(req.request.params.get('month')).toBe('10');
    expect(req.request.params.get('profileId')).toBe('3');
    req.flush([]);
  });

  it('holt ein einzelnes Turnier und Ortsvorschläge', () => {
    service.get('1457129').subscribe();
    http.expectOne('/api/tournament-directory/1457129').flush({});

    service.places('Salz').subscribe();
    const req = http.expectOne(r => r.url === '/api/tournament-directory/places');
    expect(req.request.params.get('q')).toBe('Salz');
    req.flush([]);
  });
});
