import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TournamentDirectoryService } from './tournament-directory.service';
import {
  DirectoryCalendarDay, DirectoryEntry, DirectoryFilter, DirectoryMapResponse, EMPTY_FILTER,
} from './tournament-directory.model';

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

  /**
   * Codereview UX-039: unplausible Laufzeiten blendet der Server standardmaessig aus — in Liste,
   * Karte UND Kalender. Der Schalter muss deshalb in allen drei Abfragen ankommen, sonst zeigt die
   * Karte etwas anderes als die Liste darueber.
   */
  it('schickt „unplausible zeigen" an Liste, Karte und Kalender, sonst gar nicht', () => {
    service.search(filter()).subscribe();
    const off = http.expectOne(r => r.url === '/api/tournament-directory');
    expect(off.request.params.has('includeImplausible')).toBeFalse();
    off.flush({ items: [], total: 0, truncated: false });

    const on = filter({ includeImplausible: true });
    service.search(on).subscribe();
    service.map(on, '47.0,12.0,48.0,14.0').subscribe();
    service.calendar(on, 2026, 10).subscribe();

    const list = http.expectOne(r => r.url === '/api/tournament-directory');
    const map = http.expectOne(r => r.url === '/api/tournament-directory/map');
    const calendar = http.expectOne(r => r.url === '/api/tournament-directory/calendar');
    for (const req of [list, map, calendar]) {
      expect(req.request.params.get('includeImplausible')).withContext(req.request.url).toBe('true');
    }
    list.flush({ items: [], total: 0, truncated: false });
    map.flush({ items: [], truncated: false });
    calendar.flush({ tournaments: [], days: [] });
  });

  it('lässt beim Kartenaufruf den Umkreis weg — dort zählt der sichtbare Ausschnitt', () => {
    service.map(filter({ lat: 47.8, lon: 13.0, radiusKm: 50, federation: 'AUT' }),
      '47.0,12.0,48.0,14.0').subscribe();

    const req = http.expectOne(r => r.url === '/api/tournament-directory/map');
    expect(req.request.params.has('radiusKm')).toBeFalse();
    expect(req.request.params.has('lat')).toBeFalse();
    expect(req.request.params.get('bbox')).toBe('47.0,12.0,48.0,14.0');
    expect(req.request.params.get('fed')).toBe('AUT');
    req.flush({ items: [], truncated: false });
  });

  /**
   * Die Karte kappt nach Startdatum. Bis 0.606.0 war die Antwort eine nackte Liste, und unter einer
   * gekappten Karte stand nur „N Turniere im Ausschnitt" — die spaeten Monate fehlten still.
   */
  it('reicht beim Kartenaufruf die Marker UND das Kappungs-Kennzeichen durch', () => {
    let res: DirectoryMapResponse | undefined;
    service.map(filter(), '47.0,12.0,48.0,14.0').subscribe(r => (res = r));

    const pin = { id: '1', name: 'Open Braunau' } as DirectoryEntry;
    http.expectOne(r => r.url === '/api/tournament-directory/map').flush({ items: [pin], truncated: true });

    expect(res).toEqual({ items: [pin], truncated: true });
  });

  /** Kommt die Turnierseite vor der API heraus, darf die Karte an der alten Form nicht zerbrechen. */
  it('liest die alte Antwortform (nackte Liste) als ungekappt', () => {
    let res: DirectoryMapResponse | undefined;
    service.map(filter(), '47.0,12.0,48.0,14.0').subscribe(r => (res = r));

    const pin = { id: '1', name: 'Open Braunau' } as DirectoryEntry;
    http.expectOne(r => r.url === '/api/tournament-directory/map').flush([pin]);

    expect(res).toEqual({ items: [pin], truncated: false });
  });

  it('lässt beim Kalender from/to weg — Jahr und Monat bestimmen den Zeitraum', () => {
    service.calendar(filter({ from: '2026-01-01', to: '2026-01-31', profileId: 3 }), 2026, 10).subscribe();

    const req = http.expectOne(r => r.url === '/api/tournament-directory/calendar');
    expect(req.request.params.has('from')).toBeFalse();
    expect(req.request.params.has('to')).toBeFalse();
    expect(req.request.params.get('year')).toBe('2026');
    expect(req.request.params.get('month')).toBe('10');
    expect(req.request.params.get('profileId')).toBe('3');
    req.flush({ tournaments: [], days: [] });
  });

  it('setzt den Monat aus Turnieren und Tagesnummern wieder zusammen', () => {
    // Der Server schickt jedes Turnier EINMAL. Voll ausgeschrieben waren das auf dem Dev-Server
    // 5962 Einträge für 200 verschiedene Turniere — 3 MB je Monat, 97 % davon Wiederholung.
    let days: DirectoryCalendarDay[] = [];
    service.calendar(filter({}), 2026, 10).subscribe(d => (days = d));

    http.expectOne(r => r.url === '/api/tournament-directory/calendar').flush({
      tournaments: [{ id: '1', name: 'Dreitäger' }, { id: '2', name: 'Eintäger' }],
      days: [
        { date: '2026-10-10', ids: ['1'] },
        { date: '2026-10-11', ids: ['1', '2'] },
        { date: '2026-10-12', ids: [] },
      ],
    });

    expect(days.map(d => d.items.map(i => i.name))).toEqual([['Dreitäger'], ['Dreitäger', 'Eintäger'], []]);
    // Dasselbe Turnier an mehreren Tagen ist DASSELBE Objekt — Kopien wären genau die
    // Verschwendung, die auf der Leitung gerade abgeschafft wurde.
    expect(days[0].items[0]).toBe(days[1].items[0]);
  });

  it('übergeht eine Tagesnummer ohne Beschreibung, statt eine Lücke zu rendern', () => {
    let days: DirectoryCalendarDay[] = [];
    service.calendar(filter({}), 2026, 10).subscribe(d => (days = d));

    http.expectOne(r => r.url === '/api/tournament-directory/calendar').flush({
      tournaments: [{ id: '1', name: 'Da' }],
      days: [{ date: '2026-10-10', ids: ['1', 'fehlt'] }],
    });

    expect(days[0].items.map(i => i.name)).toEqual(['Da']);
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
