import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { TournamentDirectoryComponent } from './tournament-directory.component';
import { DirectoryEntry, SearchProfile } from './tournament-directory.model';

function profile(id: number, name: string): SearchProfile {
  return {
    id, name, placeQuery: null, lat: 47.8, lon: 13.04, radiusKm: 100,
    federations: [], speeds: [], weekendOnly: false, minPlayers: null, notifyNew: true, sortOrder: 0,
  };
}

function entry(id: string, name = 'Open Braunau'): DirectoryEntry {
  return {
    chessResultsId: id, name, federation: 'AUT', state: 'Salzburg',
    startDate: '2026-12-18', endDate: '2026-12-20', location: 'Ranshofen',
    timeControl: '90 min', speed: 'Standard', organizer: null, director: null, chiefArbiter: null,
    rounds: 7, playerCount: 20, lat: 48.2, lon: 13.0, geoSource: 'City', geoPlaceName: 'Ranshofen',
    distanceKm: 12.5, cancelled: false, subscribed: false, groupSize: 1, groups: [],
  };
}

describe('TournamentDirectoryComponent', () => {
  let fixture: ComponentFixture<TournamentDirectoryComponent>;
  let component: TournamentDirectoryComponent;
  let http: HttpTestingController;

  async function setup(queryParams: Record<string, string> = {}) {
    await TestBed.configureTestingModule({
      imports: [TournamentDirectoryComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TournamentDirectoryComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  }

  function flushProfiles(profiles: SearchProfile[]) {
    http.expectOne('/api/tournament-search-profiles').flush(profiles);
  }

  function flushList(items: DirectoryEntry[], total = items.length, truncated = false) {
    const req = http.expectOne(r => r.url === '/api/tournament-directory');
    req.flush({ items, total, truncated });
    return req;
  }

  it('lädt Profile und danach die Liste', async () => {
    await setup();
    flushProfiles([profile(1, 'Zuhause')]);
    flushList([entry('111')]);

    expect(component.profiles.length).toBe(1);
    expect(component.entries.length).toBe(1);
    http.verify();
  });

  it('wählt ohne Deep-Link das erste Suchprofil vor', async () => {
    // Ohne Vorauswahl sähe man beim ersten Öffnen alle Turniere Europas — der Umkreis ist
    // der eigentliche Zweck der Seite.
    await setup();
    flushProfiles([profile(3, 'Zuhause'), profile(4, 'Ferienhaus')]);
    const req = flushList([]);

    expect(component.filter.profileId).toBe(3);
    expect(req.request.params.get('profileId')).toBe('3');
    http.verify();
  });

  it('übernimmt ein Suchprofil aus dem Deep-Link der Umkreis-Meldung', async () => {
    await setup({ profile: '4' });
    flushProfiles([profile(3, 'Zuhause'), profile(4, 'Ferienhaus')]);
    flushList([]);

    expect(component.filter.profileId).toBe(4);
    http.verify();
  });

  it('ignoriert ein fremdes Profil im Deep-Link und fällt auf das erste zurück', async () => {
    await setup({ profile: '999' });
    flushProfiles([profile(3, 'Zuhause')]);
    flushList([]);

    expect(component.filter.profileId).toBe(3);
    http.verify();
  });

  it('lädt ein per Deep-Link gemeldetes Turnier einzeln nach', async () => {
    // Das Turnier aus einer Absage- oder Änderungsmeldung liegt womöglich ausserhalb des
    // Umkreises oder ist abgesagt — in der gefilterten Liste wäre es nicht zu finden.
    await setup({ t: '1457129' });
    flushProfiles([]);
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129', 'Gemeldetes Turnier'));
    flushList([]);

    expect(component.selected?.name).toBe('Gemeldetes Turnier');
    http.verify();
  });

  it('setzt beim Filterwechsel wieder auf Seite 1', async () => {
    await setup();
    flushProfiles([]);
    flushList([entry('1')], 200);

    component.loadMore();
    let req = http.expectOne(r => r.url === '/api/tournament-directory');
    expect(req.request.params.get('page')).toBe('2');
    req.flush({ items: [entry('2')], total: 200, truncated: false });
    expect(component.entries.length).toBe(2);

    component.searchText = 'Braunau';
    component.reload();
    req = http.expectOne(r => r.url === '/api/tournament-directory');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('q')).toBe('Braunau');
    req.flush({ items: [entry('3')], total: 1, truncated: false });
    // Seite 1 ersetzt, statt an die alte Liste anzuhängen.
    expect(component.entries.length).toBe(1);
    http.verify();
  });

  it('hängt weitere Seiten an, statt sie zu ersetzen', async () => {
    await setup();
    flushProfiles([]);
    flushList([entry('1')], 100);

    component.loadMore();
    http.expectOne(r => r.url === '/api/tournament-directory')
      .flush({ items: [entry('2')], total: 100, truncated: false });

    expect(component.entries.map(e => e.chessResultsId)).toEqual(['1', '2']);
    http.verify();
  });

  it('meldet den Kartenausschnitt und lädt nur dann Pins', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    component.onBoundsChanged('47.0,12.0,48.0,14.0');
    const req = http.expectOne(r => r.url === '/api/tournament-directory/map');
    expect(req.request.params.get('bbox')).toBe('47.0,12.0,48.0,14.0');
    req.flush([entry('1')]);

    expect(component.pins.length).toBe(1);
    http.verify();
  });

  it('lädt beim Monatswechsel den Kalender neu', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    component.onMonthChanged({ year: 2027, month: 1 });
    const req = http.expectOne(r => r.url === '/api/tournament-directory/calendar');
    expect(req.request.params.get('year')).toBe('2027');
    expect(req.request.params.get('month')).toBe('1');
    req.flush([]);
    http.verify();
  });

  it('markiert ein gemerktes Turnier sofort, ohne die Liste neu zu laden', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    const target = entry('111');
    component.bookmark(target);
    http.expectOne({ method: 'POST', url: '/api/subscriptions' }).flush({ id: 1 });

    expect(target.subscribed).toBeTrue();
    http.verify();
  });

  it('schränkt standardmäßig aufs kommende Quartal ein', async () => {
    // Ohne Vorgabe stehen über tausend Turniere bis weit ins nächste Jahr in der Liste.
    await setup();
    flushProfiles([]);
    const req = flushList([]);

    const from = new Date(req.request.params.get('from')!);
    const to = new Date(req.request.params.get('to')!);
    const monate = (to.getFullYear() - from.getFullYear()) * 12 + (to.getMonth() - from.getMonth());
    expect(component.rangePreset).toBe('quarter');
    expect(monate).toBe(3);
    http.verify();
  });

  it('lässt „Alles Kommende" das Enddatum weg', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    component.applyRangePreset('all');
    const req = http.expectOne(r => r.url === '/api/tournament-directory');
    expect(req.request.params.has('from')).toBeTrue();
    expect(req.request.params.has('to')).toBeFalse();
    req.flush({ items: [], total: 0, truncated: false });
    http.verify();
  });

  it('zählt nur die eingeklappten Zusatzfilter', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    expect(component.activeExtraFilters).toBe(0);
    component.filter.speed = 'Blitz';
    component.filter.weekendOnly = true;
    expect(component.activeExtraFilters).toBe(2);
    // Der Zeitraum gehört zur immer sichtbaren Zeile und zählt deshalb nicht mit.
    component.applyRangePreset('year');
    http.expectOne(r => r.url === '/api/tournament-directory').flush({ items: [], total: 0, truncated: false });
    expect(component.activeExtraFilters).toBe(2);
    http.verify();
  });

  it('setzt den Filter zurück und landet wieder beim Quartal', async () => {
    await setup();
    flushProfiles([profile(1, 'Zuhause')]);
    flushList([]);

    component.filter.text = 'Braunau';
    component.filter.speed = 'Blitz';
    component.applyRangePreset('all');
    http.expectOne(r => r.url === '/api/tournament-directory').flush({ items: [], total: 0, truncated: false });

    component.resetFilter();
    const req = http.expectOne(r => r.url === '/api/tournament-directory');

    expect(component.filter.speed).toBeNull();
    expect(component.filter.profileId).toBeNull();
    expect(component.rangePreset).toBe('quarter');
    expect(req.request.params.has('to')).toBeTrue();
    req.flush({ items: [], total: 0, truncated: false });
    http.verify();
  });

  it('meldet fehlgeschlagene Kartenkacheln, statt schwarz zu bleiben', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    expect(component.tilesFailed).toBeFalse();
    component.onTilesFailed();
    expect(component.tilesFailed).toBeTrue();
    http.verify();
  });

  it('verkraftet einen Fehler beim Laden der Profile und zeigt trotzdem die Liste', async () => {
    await setup();
    http.expectOne('/api/tournament-search-profiles').flush('kaputt', { status: 500, statusText: 'Server Error' });
    flushList([entry('1')]);

    expect(component.entries.length).toBe(1);
    http.verify();
  });
});
