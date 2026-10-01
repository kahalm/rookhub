import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { GeolocationService } from '../../core/geolocation.service';
import { TournamentDirectoryComponent } from './tournament-directory.component';
import { DirectoryEntry, SearchProfile } from './tournament-directory.model';
import { TournamentMapComponent } from './tournament-map.component';

function profile(id: number, name: string): SearchProfile {
  return {
    id, name, placeQuery: null, lat: 47.8, lon: 13.04, radiusKm: 100,
    federations: [], speeds: [], weekendOnly: false, minPlayers: null, notifyNew: true, sortOrder: 0,
  };
}

function entry(id: string, name = 'Open Braunau'): DirectoryEntry {
  return {
    id, chessResultsId: id, name, federation: 'AUT', state: 'Salzburg',
    startDate: '2026-12-18', endDate: '2026-12-20', location: 'Ranshofen',
    timeControl: '90 min', speed: 'Standard', organizer: null, director: null, chiefArbiter: null,
    rounds: 7, playerCount: 20, lat: 48.2, lon: 13.0, geoSource: 'City', geoPlaceName: 'Ranshofen',
    distanceKm: 12.5, cancelled: false, subscribed: false, groupSize: 1, groups: [], venues: [],
    kind: 'Individual', isLeague: false, ageGroups: [], gender: 'Open',
    ignored: false, roundDates: [], sources: [],
  };
}

/**
 * Statt der Leaflet-Karte: die echte meldet ihren Ausschnitt selbst (nach dem ersten Zeichnen und
 * bei jeder Groessenaenderung) und loeste damit eigene, zeitabhaengige Kartenabfragen aus. Die
 * Markerliste wird wie in `groupByPoint` durchlaufen — ein Objekt statt einer Liste fiele hier auf.
 */
@Component({ selector: 'app-tournament-map', template: '' })
class MapStubComponent {
  private _entries: DirectoryEntry[] = [];
  @Input() set entries(value: DirectoryEntry[]) { this._entries = [...value]; }
  get entries(): DirectoryEntry[] { return this._entries; }
  @Input() centre: unknown;
  @Input() colourBy: unknown;
  @Output() colourByChange = new EventEmitter<unknown>();
  @Output() boundsChanged = new EventEmitter<string>();
  @Output() tilesFailed = new EventEmitter<void>();
  @Output() entryIgnored = new EventEmitter<void>();
  @Output() entrySelected = new EventEmitter<DirectoryEntry>();
}

describe('TournamentDirectoryComponent', () => {
  let fixture: ComponentFixture<TournamentDirectoryComponent>;
  let component: TournamentDirectoryComponent;
  let http: HttpTestingController;
  let navigate: jasmine.Spy;

  /** Die angemeldete Kennung der Tests — die gemerkte Ansicht liegt je Nutzer. */
  const ME = 1;
  const LOCAL_KEY = TournamentDirectoryComponent.viewKeyFor(ME)!;

  // Die Ansicht ueberlebt einen Seitenwechsel im localStorage — ohne Aufraeumen faerbte der
  // Zustand eines Tests auf den naechsten ab.
  const clearViewKeys = () => {
    for (const k of Object.keys(localStorage))
      if (k.startsWith(TournamentDirectoryComponent.ViewKey)) localStorage.removeItem(k);
  };
  beforeEach(clearViewKeys);
  afterEach(clearViewKeys);

  async function setup(queryParams: Record<string, string> = {}, user: { id: number; impersonating?: boolean } = { id: ME }) {
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

    const auth = TestBed.inject(AuthService);
    spyOnProperty(auth, 'currentUser', 'get').and.returnValue({
      token: 't', username: `u${user.id}`, userId: user.id, isAdmin: false, impersonating: !!user.impersonating,
    });
    spyOnProperty(auth, 'isImpersonating', 'get').and.returnValue(!!user.impersonating);

    fixture = TestBed.createComponent(TournamentDirectoryComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture.detectChanges();
    flushViewState();
  }

  /**
   * Die Filterleiste fragt beim Start den beim NUTZER gespeicherten Zustand ab. In den Tests
   * antwortet er mit 204 („nichts gespeichert") — der interessante Fall ist der eigene Test
   * weiter unten, hier wuerde er nur jede Erwartung verschieben.
   *
   * <p>Auf das 204 folgt ein PUT (der lokale Zustand wird hinaufgeschoben) — der ist gedrosselt
   * und faellt in den Tests nie an, weil dort keine Zeit vergeht.</p>
   */
  function flushViewState(state: Record<string, unknown> | null = null) {
    const req = http.expectOne('/api/view-state/turnier.directory');
    if (state) req.flush(state);
    else req.flush(null, { status: 204, statusText: 'No Content' });
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

    expect(component.profiles().length).toBe(1);
    expect(component.entries().length).toBe(1);
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

  it('führt ein per Deep-Link gemeldetes Turnier direkt auf seine Detailseite', async () => {
    // Das Turnier aus einer Absage- oder Änderungsmeldung liegt womöglich ausserhalb des
    // Umkreises oder ist abgesagt — in der gefilterten Liste wäre es nicht zu finden.
    await setup({ t: '1457129' });
    flushProfiles([]);

    expect(navigate).toHaveBeenCalledWith(['/tournaments/calendar', '1457129']);
    // Und die Liste darunter wird gar nicht erst geholt — man bleibt nicht hier.
    http.verify();
  });

  it('öffnet ein angeklicktes Turnier als eigene Seite', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    component.select(entry('42'));
    expect(navigate).toHaveBeenCalledWith(['/tournaments/calendar', '42']);
    http.verify();
  });

  it('baut nach der Rückkehr dieselbe Ansicht wieder auf', async () => {
    // Ohne das fiele der Weg „Turnier öffnen → zurück" auf die Vorgabefilter zurück.
    await setup();
    flushProfiles([profile(3, 'Zuhause'), profile(4, 'Ferienhaus')]);
    flushList([]);

    component.filter.profileId = 4;
    component.searchText = 'Braunau';
    component.applyRangePreset('year');
    http.expectOne(r => r.url === '/api/tournament-directory').flush({ items: [], total: 0, truncated: false });
    http.verify();

    // Seite verlassen und neu betreten — der TestBed muss dafuer wirklich zurueckgesetzt werden.
    TestBed.resetTestingModule();
    await setup();
    flushProfiles([profile(3, 'Zuhause'), profile(4, 'Ferienhaus')]);
    const req = flushList([]);

    expect(component.filter.profileId).toBe(4);
    expect(component.searchText).toBe('Braunau');
    expect(component.rangePreset).toBe('year');
    expect(req.request.params.get('q')).toBe('Braunau');
    http.verify();
  });

  it('vergisst ein gelöschtes Suchprofil und nimmt das erste, das es noch gibt', async () => {
    // Sonst suchte die Seite weiter um Koordinaten, zu denen es kein Profil mehr gibt.
    localStorage.setItem(LOCAL_KEY, JSON.stringify({ profileId: 99 }));
    await setup();
    flushProfiles([profile(3, 'Zuhause')]);
    flushList([]);

    expect(component.filter.profileId).toBe(3);
    http.verify();
  });

  it('behält „kein Umkreis" als getroffene Wahl bei', async () => {
    // Null ist hier etwas anderes als „noch nichts gewählt" — sonst schnappt die Vorauswahl
    // bei jeder Rückkehr wieder zu.
    localStorage.setItem(LOCAL_KEY, JSON.stringify({ profileId: null }));
    await setup();
    flushProfiles([profile(3, 'Zuhause')]);
    const req = flushList([]);

    expect(component.filter.profileId).toBeNull();
    expect(req.request.params.has('profileId')).toBeFalse();
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
    expect(component.entries().length).toBe(2);

    component.searchText = 'Braunau';
    component.reload();
    req = http.expectOne(r => r.url === '/api/tournament-directory');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('q')).toBe('Braunau');
    req.flush({ items: [entry('3')], total: 1, truncated: false });
    // Seite 1 ersetzt, statt an die alte Liste anzuhängen.
    expect(component.entries().length).toBe(1);
    http.verify();
  });

  it('lässt eine überholte Antwort die Liste NICHT ersetzen', async () => {
    // „Mehr laden" (Seite 2) anstossen, sofort den Filter wechseln: die alte Antwort traf früher
    // auf `page === 1` und ersetzte die Liste durch Seite 2 des vorigen Filters.
    await setup();
    flushProfiles([]);
    flushList([entry('1')], 200);

    component.loadMore();
    const stale = http.expectOne(r => r.url === '/api/tournament-directory');
    expect(stale.request.params.get('page')).toBe('2');

    component.searchText = 'Braunau';
    component.reload();
    const fresh = http.expectOne(r => r.url === '/api/tournament-directory');

    // Erst die ÜBERHOLTE Antwort, dann die aktuelle.
    stale.flush({ items: [entry('alt')], total: 200, truncated: false });
    fresh.flush({ items: [entry('neu')], total: 1, truncated: false });

    expect(component.entries().map(e => e.id)).toEqual(['neu']);
    expect(component.total()).toBe(1);
    http.verify();
  });

  it('hängt weitere Seiten an, statt sie zu ersetzen', async () => {
    await setup();
    flushProfiles([]);
    flushList([entry('1')], 100);

    component.loadMore();
    http.expectOne(r => r.url === '/api/tournament-directory')
      .flush({ items: [entry('2')], total: 100, truncated: false });

    expect(component.entries().map(e => e.id)).toEqual(['1', '2']);
    http.verify();
  });

  it('meldet den Kartenausschnitt und lädt nur dann Pins', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    component.onBoundsChanged('47.0,12.0,48.0,14.0');
    const req = http.expectOne(r => r.url === '/api/tournament-directory/map');
    expect(req.request.params.get('bbox')).toBe('47.0,12.0,48.0,14.0');
    req.flush({ items: [entry('1')], truncated: false });

    expect(component.pins().length).toBe(1);
    expect(component.mapTruncated()).toBeFalse();
    http.verify();
  });

  /**
   * Die Karte kappt nach Startdatum. Unter einer gekappten Karte stand bisher nur „N Turniere im
   * Ausschnitt" — ganze Monate wirkten leer. Jetzt steht dort derselbe Hinweis wie in der Liste,
   * und er verschwindet wieder, sobald der Ausschnitt passt.
   */
  it('zeigt unter der Karte einen Hinweis, wenn der Ausschnitt gekappt ist', async () => {
    TestBed.overrideComponent(TournamentDirectoryComponent, {
      remove: { imports: [TournamentMapComponent] },
      add: { imports: [MapStubComponent] },
    });
    await setup();
    flushProfiles([]);
    flushList([]);

    component.onTabChange(1);
    fixture.detectChanges();
    await fixture.whenStable();
    const hint = () => (fixture.nativeElement as HTMLElement).querySelector('.map-truncated');

    component.onBoundsChanged('46.0,5.0,55.0,17.0');
    http.expectOne(r => r.url === '/api/tournament-directory/map')
      .flush({ items: [entry('1'), entry('2')], truncated: true });
    fixture.detectChanges();

    expect(component.pins().map(e => e.id)).toEqual(['1', '2']);
    expect(component.mapTruncated()).toBeTrue();
    expect(hint()?.textContent).toContain('tournamentDirectory.mapTruncated');

    component.onBoundsChanged('47.0,12.0,48.0,14.0');
    http.expectOne(r => r.url === '/api/tournament-directory/map')
      .flush({ items: [entry('1')], truncated: false });
    fixture.detectChanges();

    expect(component.mapTruncated()).toBeFalse();
    expect(hint()).toBeNull();
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
    req.flush({ tournaments: [], days: [] });
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

  /**
   * Codereview UX-039: der Server liefert erst, was im Zeitraum BEGINNT, dann was schon laeuft.
   * Die Liste setzt den zweiten Teil als eigenen Block „laeuft bereits" ab — sonst sehen die
   * Saisonligen wie Termine der naechsten Wochen aus.
   */
  it('zeigt bereits laufende Wettbewerbe als eigenen Block unter den kommenden', async () => {
    await setup();
    flushProfiles([]);
    flushList([entry('111', 'Open Braunau'), { ...entry('222', 'Landesliga'), ongoing: true }], 30);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const block = root.querySelector('.ongoing-block') as HTMLElement;
    expect(block).withContext('Block „laeuft bereits"').not.toBeNull();
    expect(block.querySelector('.block-title')?.textContent?.trim()).toBe('tournamentDirectory.ongoing.title');
    expect([...block.querySelectorAll('.tc-name')].map(n => n.textContent?.trim())).toEqual(['Landesliga']);
    const upcoming = root.querySelector('.tab-body > .entry-grid') as HTMLElement;
    expect([...upcoming.querySelectorAll('.tc-name')].map(n => n.textContent?.trim())).toEqual(['Open Braunau']);
    // Die Zahl darueber zaehlt beide Bloecke zusammen.
    expect(component.entries().length).toBe(2);
    http.verify();
  });

  it('zeigt ohne laufende Wettbewerbe keinen zweiten Block', async () => {
    await setup();
    flushProfiles([]);
    flushList([entry('111')]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ongoing-block')).toBeNull();
    http.verify();
  });

  /**
   * Codereview UX-039: unplausible Laufzeiten sind standardmaessig aus; der Schalter holt sie
   * zurueck, zaehlt als Zusatzfilter und ueberlebt den Seitenwechsel wie „Ligen ausblenden".
   */
  it('blendet unplausible Zeiträume standardmäßig aus und holt sie auf Wunsch zurück', async () => {
    await setup();
    flushProfiles([]);
    const first = flushList([]);
    expect(first.request.params.has('includeImplausible')).toBeFalse();
    expect(component.activeExtraFilters).toBe(0);

    component.onIncludeImplausibleChange(true);
    const req = http.expectOne(r => r.url === '/api/tournament-directory');
    expect(req.request.params.get('includeImplausible')).toBe('true');
    req.flush({ items: [], total: 0, truncated: false });

    expect(component.activeExtraFilters).toBe(1);
    expect(JSON.parse(localStorage.getItem(LOCAL_KEY)!).includeImplausible).toBeTrue();
    http.verify();
  });

  it('übernimmt „unplausible zeigen" aus der gemerkten Ansicht', async () => {
    localStorage.setItem(LOCAL_KEY, JSON.stringify({ tab: 'list', includeImplausible: true }));

    await setup();
    flushProfiles([]);
    const req = flushList([]);

    expect(component.filter.includeImplausible).toBeTrue();
    expect(req.request.params.get('includeImplausible')).toBe('true');
    http.verify();
  });

  /**
   * Codereview UX-039: die Vorgabe heisst „Naechste drei Monate", denn so rechnet `rangeFor`
   * (heute + 3 Monate). „Naechstes Quartal" las sich wie das Kalenderquartal Q4.
   */
  it('beschriftet die Vorgabe als die nächsten drei Monate, nicht als Quartal', async () => {
    const expected: Record<string, RegExp> = { en: /three months/i, de: /drei Monate/i, hr: /tri mjeseca/i, hu: /három hónap/i };
    for (const [lang, pattern] of Object.entries(expected)) {
      let json: { tournamentDirectory: { range: { quarter: string } } } | null = null;
      for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
        const res = await fetch(url);
        if (res.ok) { json = await res.json(); break; }
      }
      expect(json?.tournamentDirectory.range.quarter).withContext(lang).toMatch(pattern);
    }
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

    expect(component.tilesFailed()).toBeFalse();
    component.onTilesFailed();
    expect(component.tilesFailed()).toBeTrue();
    http.verify();
  });

  it('verkraftet einen Fehler beim Laden der Profile und zeigt trotzdem die Liste', async () => {
    await setup();
    http.expectOne('/api/tournament-search-profiles').flush('kaputt', { status: 500, statusText: 'Server Error' });
    flushList([entry('1')]);

    expect(component.entries().length).toBe(1);
    http.verify();
  });

  // ----- Fehler sehen nicht wie „leer" aus (Codereview UX-040) ---------------

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const serverError = { status: 500, statusText: 'Server Error' };

  it('zeigt bei gescheiterter Liste „Erneut versuchen" statt „keine Turniere"', async () => {
    await setup();
    flushProfiles([profile(1, 'Zuhause')]);
    http.expectOne(r => r.url === '/api/tournament-directory').flush('kaputt', serverError);
    fixture.detectChanges();

    expect(component.listFailed()).toBeTrue();
    expect(text()).toContain('tournamentDirectory.loadError');
    expect(text()).not.toContain('tournamentDirectory.empty');

    const retry = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.load-failed button');
    expect(retry).withContext('Erneut-versuchen-Knopf fehlt').toBeTruthy();
    retry!.click();
    flushList([entry('1')]);
    fixture.detectChanges();

    expect(component.listFailed()).toBeFalse();
    expect(component.entries().length).toBe(1);
    expect((fixture.nativeElement as HTMLElement).querySelector('.load-failed')).toBeNull();
    http.verify();
  });

  it('räumt bei gescheitertem Filterwechsel die Liste des alten Filters ab', async () => {
    await setup();
    flushProfiles([]);
    flushList([entry('1')]);

    component.searchText = 'Braunau';
    component.reload();
    http.expectOne(r => r.url === '/api/tournament-directory').flush('kaputt', serverError);

    expect(component.entries()).toEqual([]);
    expect(component.listFailed()).toBeTrue();
    http.verify();
  });

  it('behält die Liste, wenn nur „Mehr anzeigen" scheitert', async () => {
    await setup();
    flushProfiles([]);
    flushList([entry('1')], 100);

    component.loadMore();
    http.expectOne(r => r.url === '/api/tournament-directory').flush('kaputt', serverError);

    expect(component.entries().map(e => e.id)).toEqual(['1']);
    expect(component.listFailed()).toBeFalse();
    http.verify();
  });

  it('zeigt „Noch kein Suchprofil" nur, wenn die Profile wirklich geladen wurden', async () => {
    await setup();
    http.expectOne('/api/tournament-search-profiles').flush('kaputt', serverError);
    flushList([entry('1')]);
    fixture.detectChanges();

    expect(component.profilesLoaded()).toBeFalse();
    expect(text()).not.toContain('tournamentDirectory.noProfileHint');
    http.verify();
  });

  it('zeigt „Noch kein Suchprofil" bei geladener, leerer Profilliste', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);
    fixture.detectChanges();

    expect(text()).toContain('tournamentDirectory.noProfileHint');
    http.verify();
  });

  it('meldet einen gescheiterten Kalendermonat mit „Erneut versuchen"', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    component.onTabChange(2);
    http.expectOne(r => r.url === '/api/tournament-directory/calendar').flush('kaputt', serverError);
    fixture.detectChanges();

    expect(component.calendarFailed()).toBeTrue();
    const retry = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.load-failed button');
    expect(retry).withContext('Erneut-versuchen-Knopf fehlt').toBeTruthy();
    expect(text()).not.toContain('tournamentDirectory.calendar.empty');

    retry!.click();
    http.expectOne(r => r.url === '/api/tournament-directory/calendar').flush({ tournaments: [], days: [] });
    expect(component.calendarFailed()).toBeFalse();
    http.verify();
  });

  it('meldet einen gescheiterten Kartenausschnitt statt „0 Turniere im Ausschnitt"', async () => {
    TestBed.overrideComponent(TournamentDirectoryComponent, {
      remove: { imports: [TournamentMapComponent] },
      add: { imports: [MapStubComponent] },
    });
    await setup();
    flushProfiles([]);
    flushList([]);

    component.onTabChange(1);
    component.onBoundsChanged('47.0,12.0,48.0,14.0');
    http.expectOne(r => r.url === '/api/tournament-directory/map').flush('kaputt', serverError);
    fixture.detectChanges();

    expect(component.mapFailed()).toBeTrue();
    expect(text()).toContain('tournamentDirectory.loadError');
    expect(text()).not.toContain('tournamentDirectory.pins');
    http.verify();
  });

  // ----- Ort, Umkreis und Standort ---------------------------------------

  it('nimmt einen Ort aus der Ortsliste als Suchmittelpunkt', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    component.onPlaceInput('Hallein');
    // Die Ortssuche laeuft entprellt — ohne Warten liegt noch keine Anfrage vor.
    await new Promise(resolve => setTimeout(resolve, 300));
    const suggest = http.expectOne(r => r.url === '/api/tournament-directory/places');
    expect(suggest.request.params.get('q')).toBe('Hallein');
    suggest.flush([{ label: '5400 Hallein (AT)', country: 'AT', postalCode: '5400', lat: 47.68, lon: 13.1 }]);

    component.choosePlace(component.placeSuggestions()[0]);
    const req = flushList([]);

    expect(req.request.params.get('lat')).toBe('47.68');
    expect(req.request.params.get('lon')).toBe('13.1');
    // Ohne Vorgabe waere ein Mittelpunkt ohne Radius eine Angabe ohne Wirkung.
    expect(req.request.params.get('radiusKm')).toBe('100');
    http.verify();
  });

  /**
   * Ein selbst gewaehlter Ort ERSETZT das Profil. Liefe beides mit, gewaenne serverseitig das
   * Profil — das Ortsfeld behauptete dann etwas anderes, als die Liste darunter zeigt.
   */
  it('legt bei eigener Ortswahl das Suchprofil ab', async () => {
    await setup();
    flushProfiles([profile(3, 'Zuhause')]);
    flushList([]);
    expect(component.filter.profileId).toBe(3);

    component.choosePlace({ label: 'Wien (AT)', country: 'AT', postalCode: null, lat: 48.21, lon: 16.37 });
    const req = flushList([]);

    expect(component.filter.profileId).toBeNull();
    expect(req.request.params.get('profileId')).toBeNull();
    expect(req.request.params.get('lat')).toBe('48.21');
    http.verify();
  });

  /**
   * Text im Ortsfeld, der nicht zum gewaehlten Treffer passt, entwertet die Koordinaten. Ohne
   * das sucht die Seite weiter um Wien, waehrend „Berlin" im Feld steht.
   */
  it('verwirft die Koordinaten, sobald der Ortstext abweicht', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    component.choosePlace({ label: 'Wien (AT)', country: 'AT', postalCode: null, lat: 48.21, lon: 16.37 });
    flushList([]);

    component.onPlaceInput('Berl');
    await new Promise(resolve => setTimeout(resolve, 300));
    http.expectOne(r => r.url === '/api/tournament-directory/places').flush([]);

    expect(component.filter.lat).toBeNull();
    expect(component.filter.lon).toBeNull();
    http.verify();
  });

  it('übernimmt den Browser-Standort und trägt den nächsten Ort als Namen ein', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    const geolocation = TestBed.inject(GeolocationService);
    spyOnProperty(geolocation, 'supported').and.returnValue(true);
    spyOn(geolocation, 'current').and.returnValue(of({ lat: 47.27, lon: 11.39, accuracyM: 30 }));

    component.useCurrentLocation();

    // Die Suche laeuft SOFORT mit den Koordinaten — der Ortsname ist Beiwerk.
    const req = flushList([]);
    expect(req.request.params.get('lat')).toBe('47.27');
    expect(req.request.params.get('radiusKm')).toBe('100');

    const nearest = http.expectOne(r => r.url === '/api/tournament-directory/places/nearest');
    nearest.flush({ label: '6020 Innsbruck (AT)', country: 'AT', postalCode: '6020', lat: 47.27, lon: 11.39 });

    expect(component.placeLabel).toBe('6020 Innsbruck (AT)');
    http.verify();
  });

  /**
   * Kein Ort im Lexikon in Reichweite (Server: 204) darf die Suche nicht anhalten — die
   * Koordinaten sind ja da. Dann stehen sie selbst im Feld.
   */
  it('behält die Koordinaten, wenn kein Ortsname gefunden wird', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    const geolocation = TestBed.inject(GeolocationService);
    spyOnProperty(geolocation, 'supported').and.returnValue(true);
    spyOn(geolocation, 'current').and.returnValue(of({ lat: -33.86, lon: 151.2, accuracyM: 50 }));

    component.useCurrentLocation();
    flushList([]);
    http.expectOne(r => r.url === '/api/tournament-directory/places/nearest')
      .flush(null, { status: 204, statusText: 'No Content' });

    expect(component.filter.lat).toBe(-33.86);
    expect(component.placeLabel).toBe('-33.860, 151.200');
    http.verify();
  });

  it('meldet eine abgelehnte Standortfreigabe als solche', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    const geolocation = TestBed.inject(GeolocationService);
    spyOn(geolocation, 'current').and.returnValue(throwError(() => 'denied'));

    component.useCurrentLocation();

    // Unterschieden von „geht gerade nicht": nur hier hilft die Browser-Einstellung.
    expect(component.locationError()).toBe('denied');
    expect(component.locating()).toBeFalse();
    http.verify();
  });

  it('schickt einen selbst gewählten Umkreis mit und löst das Profil ab', async () => {
    await setup();
    flushProfiles([profile(3, 'Zuhause')]);
    flushList([]);

    component.onRadiusChange(25);
    const req = flushList([]);

    // Der Server nimmt bei gesetztem profileId DESSEN Radius — das Feld zeigte dann 25 km und
    // die Liste 100.
    expect(req.request.params.get('profileId')).toBeNull();
    expect(req.request.params.get('radiusKm')).toBe('25');
    expect(req.request.params.get('lat')).toBe('47.8');
    http.verify();
  });

  // ----- Publikum und Format ---------------------------------------------

  it('schickt Turnierart, Klassen und Geschlecht kommagetrennt mit', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    component.onKindsChange(['Team']);
    flushList([]);
    component.onAgeGroupsChange(['U12', 'U14']);
    flushList([]);
    component.onGendersChange(['Female']);
    const req = flushList([]);

    expect(req.request.params.get('kinds')).toBe('Team');
    expect(req.request.params.get('ageGroups')).toBe('U12,U14');
    expect(req.request.params.get('genders')).toBe('Female');
    http.verify();
  });

  /**
   * „Nur Erwachsene" und eine gewaehlte Jugendklasse ergeben zwingend eine leere Liste — das
   * meint niemand, also schliessen sie sich gegenseitig aus.
   */
  it('schließt „nur Erwachsene" und eine Jugendklasse gegenseitig aus', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);

    component.onAgeGroupsChange(['U12']);
    flushList([]);
    component.onAdultsOnlyChange(true);
    let req = flushList([]);

    expect(component.filter.ageGroups).toEqual([]);
    expect(req.request.params.get('adultsOnly')).toBe('true');
    expect(req.request.params.get('ageGroups')).toBeNull();

    component.onAgeGroupsChange(['U12']);
    req = flushList([]);
    expect(component.filter.adultsOnly).toBeFalse();
    expect(req.request.params.get('adultsOnly')).toBeNull();
    http.verify();
  });

  it('sendet leere Filterlisten gar nicht', async () => {
    await setup();
    flushProfiles([]);
    const req = flushList([]);

    // Ein `kinds=` beantwortet die Frage „welche Arten" mit „keine" statt mit „alle".
    expect(req.request.params.get('kinds')).toBeNull();
    expect(req.request.params.get('ageGroups')).toBeNull();
    expect(req.request.params.get('genders')).toBeNull();
    http.verify();
  });

  /**
   * Ein Wert, den eine spaetere Fassung nicht mehr kennt, wuerde als Filter weiterlaufen, die
   * Liste unerklaerlich leer halten — und der Server wiese ihn mit 400 ab.
   */
  it('verwirft unbekannte Filterwerte aus der gemerkten Ansicht', async () => {
    localStorage.setItem(LOCAL_KEY, JSON.stringify({
      tab: 'list', ageGroups: ['U12', 'U13'], kinds: ['Team', 'Doubles'], genders: ['Mixed'],
    }));

    await setup();
    flushProfiles([]);
    const req = flushList([]);

    expect(component.filter.ageGroups).toEqual(['U12']);
    expect(component.filter.kinds).toEqual(['Team']);
    expect(req.request.params.get('genders')).toBeNull();
    http.verify();
  });

  it('merkt einen selbst gesetzten Ort über den Seitenwechsel hinweg', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);
    component.choosePlace({ label: 'Wien (AT)', country: 'AT', postalCode: null, lat: 48.21, lon: 16.37 });
    flushList([]);

    // Zweiter Aufbau, wie nach „Turnier oeffnen → zurueck".
    TestBed.resetTestingModule();
    await setup();
    flushProfiles([]);
    const req = flushList([]);

    expect(component.placeLabel).toBe('Wien (AT)');
    expect(req.request.params.get('lat')).toBe('48.21');
    http.verify();
  });

  /**
   * Der Hinweis steht UNTER den Reitern, gilt also fuer alle drei Ansichten. Die Luecke ist in
   * jeder gleich unsichtbar: das Verzeichnis speist sich aus chess-results, und wer dort nicht
   * ausschreibt, kommt hier nicht vor.
   */
  it('zeigt unter allen drei Ansichten den Hinweis auf ein fehlendes Turnier', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);
    fixture.detectChanges();

    const row = fixture.nativeElement.querySelector('.missing-row');
    expect(row).withContext('Hinweiszeile fehlt').toBeTruthy();
    expect(row.querySelector('button')).toBeTruthy();

    for (const tab of [1, 2]) {
      component.onTabChange(tab);
      if (tab === 2) {
        http.expectOne(r => r.url === '/api/tournament-directory/calendar')
          .flush({ tournaments: [], days: [] });
      }
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.missing-row')).toBeTruthy();
    }
    http.verify();
  });
  /**
   * Die Filtereinstellung gehoert dem NUTZER, nicht dem Browser: der Umkreis, den man am Rechner
   * eingestellt hat, war am Handy weg (nur `localStorage`). Weicht der beim Nutzer gespeicherte
   * Zustand ab, ist ER der juengere — geschrieben hat ihn das Geraet, an dem zuletzt gefiltert
   * wurde.
   */
  it('übernimmt den beim Nutzer gespeicherten Zustand und lädt damit neu', async () => {
    await TestBed.configureTestingModule({
      imports: [TournamentDirectoryComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({}) } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TournamentDirectoryComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture.detectChanges();

    // Der Server hat einen Zustand: Reiter „Karte", eigener Umkreis, Text gesetzt.
    http.expectOne('/api/view-state/turnier.directory').flush({
      tab: 'map', rangePreset: 'quarter', federation: 'GER', text: 'open',
      lat: 47.8, lon: 13.04, radiusKm: 50, placeLabel: 'Salzburg',
      kinds: [], ageGroups: [], genders: [],
    });
    flushProfiles([]);

    expect(component.tab).toBe('map');
    expect(component.filter.federation).toBe('GER');
    expect(component.filter.text).toBe('open');
    expect(component.filter.radiusKm).toBe(50);
    expect(component.filter.lat).toBe(47.8);
  });

  /**
   * Hat der Server nichts, wird der geraetelokale Zustand hinaufgeschoben — beim ersten Aufruf
   * nach dieser Aenderung steht dort noch nichts, und die bestehende Einstellung soll nicht
   * verloren gehen. Der Weg ist gedrosselt, gepruefft wird deshalb der ausgeloeste Wunsch.
   */
  it('lädt ohne gespeicherten Zustand einfach mit der Vorgabe', async () => {
    await setup();
    flushProfiles([]);

    // Kein zweiter Ladevorgang: nichts weicht ab, also nichts nachzuziehen.
    const list = flushList([]);
    expect(list.request.params.has('from')).toBeTrue();
    http.verify();
  });

  // ----- Gemerkte Ansicht gehoert dem NUTZER (W3 F6-002) ----------------------

  /**
   * Geteiltes Geraet: A stellt einen Ort ein und meldet sich ab, B meldet sich an. B bekam vorher
   * A's Umkreis samt Koordinaten angezeigt — und schob ihn in SEIN Konto.
   */
  it('zeigt dem naechsten Nutzer desselben Geraets nicht den Ort des vorigen', async () => {
    await setup();
    flushProfiles([]);
    flushList([]);
    component.choosePlace({ label: '6130 Schwaz (AT)', country: 'AT', postalCode: '6130', lat: 47.35, lon: 11.71 });
    flushList([]);
    expect(component.placeLabel).toBe('6130 Schwaz (AT)');

    TestBed.resetTestingModule();
    await setup({}, { id: 2 });
    flushProfiles([]);
    const req = flushList([]);

    expect(component.placeLabel).toBe('');
    expect(component.filter.lat).toBeNull();
    expect(req.request.params.has('lat')).toBeFalse();
    http.verify();
  });

  /** Ein alter, nutzerloser Eintrag gehoert irgendwem — er wird weder gelesen noch liegen gelassen. */
  it('liest den alten nutzerlosen Schluessel nicht mehr und entfernt ihn', async () => {
    localStorage.setItem(TournamentDirectoryComponent.ViewKey, JSON.stringify({ placeLabel: 'Fremd', lat: 47.35, lon: 11.71, radiusKm: 25 }));
    await setup();
    flushProfiles([]);
    const req = flushList([]);

    expect(component.placeLabel).toBe('');
    expect(req.request.params.has('lat')).toBeFalse();
    expect(localStorage.getItem(TournamentDirectoryComponent.ViewKey)).toBeNull();
    http.verify();
  });

  /** Gegenprobe: ohne Einstieg-als-Nutzer geht der Zustand gedrosselt zum Server. */
  it('schreibt den Zustand gedrosselt zum Server', async () => {
    // Die Uhr VOR dem Aufbau: schon der Start (204 → hinaufschieben) stellt den Drossel-Timer. Und
    // mit Datum: debounceTime misst die Ruhezeit ueber scheduler.now(), nicht ueber den Timer allein.
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(2026, 8, 30, 12, 0, 0));
    try {
      await setup();
      flushProfiles([]);
      flushList([]);
      jasmine.clock().tick(1300);
      const put = http.expectOne(r => r.method === 'PUT' && r.url === '/api/view-state/turnier.directory');
      expect(put.request.body).toEqual(jasmine.objectContaining({ tab: component.tab, rangePreset: 'quarter' }));
      put.flush(null, { status: 204, statusText: 'No Content' });
    } finally {
      jasmine.clock().uninstall();
    }
    http.verify();
  });

  /**
   * Einstieg als Nutzer: der Admin sieht sich nur um — nichts davon (auch nicht die Vorgabe des
   * ersten Aufbaus) darf im Konto des Nutzers landen, der dort noch nichts gespeichert hat.
   */
  it('schreibt beim Einstieg als Nutzer nichts in das fremde Konto', async () => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(2026, 8, 30, 12, 0, 0));
    try {
      await setup({}, { id: 7, impersonating: true });
      flushProfiles([]);
      flushList([]);
      component.choosePlace({ label: 'Wien (AT)', country: 'AT', postalCode: null, lat: 48.21, lon: 16.37 });
      flushList([]);
      jasmine.clock().tick(1300);
      expect(component.placeLabel).toBe('Wien (AT)');
      http.expectNone(r => r.method === 'PUT' && r.url === '/api/view-state/turnier.directory');
    } finally {
      jasmine.clock().uninstall();
    }
    http.verify();
  });
});
