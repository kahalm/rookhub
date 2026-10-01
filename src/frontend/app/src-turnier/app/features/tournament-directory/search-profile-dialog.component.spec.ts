import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { GeolocationService } from '../../core/geolocation.service';
import { SearchProfileDialogComponent } from './search-profile-dialog.component';
import { SearchProfile } from './tournament-directory.model';

describe('SearchProfileDialogComponent', () => {
  let fixture: ComponentFixture<SearchProfileDialogComponent>;
  let component: SearchProfileDialogComponent;
  let http: HttpTestingController;
  let closed: SearchProfile | null | undefined;

  async function setup(profile: SearchProfileDialogComponent['data']['profile'] = null) {
    closed = undefined;
    await TestBed.configureTestingModule({
      imports: [SearchProfileDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { profile } },
        { provide: MatDialogRef, useValue: { close: (v: SearchProfile | null) => (closed = v) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SearchProfileDialogComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  /**
   * Der Dialog speichert selbst (F6-003): die Eingabe steht im Rumpf der Anfrage, geschlossen
   * wird erst mit dem gespeicherten Profil.
   */
  function expectSave(method: 'POST' | 'PUT' = 'POST', id?: number) {
    const url = id === undefined ? '/api/tournament-search-profiles' : `/api/tournament-search-profiles/${id}`;
    return http.expectOne({ method, url });
  }

  function saved(body: Record<string, unknown>, id = 9): SearchProfile {
    return { ...(body as Omit<SearchProfile, 'id'>), id };
  }

  it('verweigert das Speichern ohne Namen', async () => {
    await setup();
    component.lat = 47.8;
    component.lon = 13.0;

    component.save();

    expect(closed).toBeUndefined();
    expect(component.error).toBe('tournamentDirectory.profile.errorName');
  });

  it('verweigert das Speichern ohne aufgelösten Ort', async () => {
    // Ein frei getippter Ortsname reicht nicht: der Server braucht Koordinaten, sonst kann
    // die nächtliche Umkreis-Meldung gar nicht rechnen.
    await setup();
    component.name = 'Zuhause';
    component.placeQuery = 'irgendwo';

    component.save();

    expect(closed).toBeUndefined();
    expect(component.error).toBe('tournamentDirectory.profile.errorPlace');
  });

  it('übernimmt Koordinaten aus einem Vorschlag und schliesst mit dem Profil', async () => {
    await setup();
    component.name = 'Zuhause';
    component.choose({ label: '5020 Salzburg (AT)', country: 'AT', postalCode: '5020', lat: 47.8, lon: 13.04 });
    component.radiusKm = 75;
    component.selectedSpeeds = ['Blitz'];

    component.save();

    const req = expectSave();
    expect(req.request.body).toEqual(jasmine.objectContaining({
      name: 'Zuhause', lat: 47.8, lon: 13.04, radiusKm: 75, speeds: ['Blitz'], notifyNew: true,
    }));
    expect(closed).toBeUndefined();
    req.flush(saved(req.request.body));
    expect(closed).toEqual(jasmine.objectContaining({ id: 9, name: 'Zuhause', lat: 47.8 }));
  });

  it('füllt das Formular beim Bearbeiten vor', async () => {
    await setup({
      id: 5, name: 'Ferienhaus', placeQuery: '9500 Villach', lat: 46.6, lon: 13.85, radiusKm: 40,
      federations: [], speeds: ['Rapid'], weekendOnly: true, minPlayers: 10,
      notifyNew: false, sortOrder: 2,
    });

    expect(component.name).toBe('Ferienhaus');
    expect(component.radiusKm).toBe(40);
    expect(component.weekendOnly).toBeTrue();
    expect(component.notifyNew).toBeFalse();

    component.save();
    const req = expectSave('PUT', 5);
    expect(req.request.body).toEqual(jasmine.objectContaining({ name: 'Ferienhaus', sortOrder: 2 }));
    req.flush(saved(req.request.body, 5));
    expect(closed).toEqual(jasmine.objectContaining({ id: 5, name: 'Ferienhaus' }));
  });

  it('fragt erst ab zwei Zeichen nach Ortsvorschlägen', async () => {
    await setup();

    component.onPlaceInput('S');
    http.expectNone(r => r.url === '/api/tournament-directory/places');
    expect(component.suggestions).toEqual([]);
  });

  it('rastet den Umkreis-Schieber in 25-km-Schritten ab 25 km', async () => {
    // 5-km-Schritte waren auf dem Handy nicht zu treffen: 99 Stufen auf ~260 px Spur sind
    // ~2,6 px je Stufe, der Finger sprang um +-20 km. Die Vorgabe 100 muss auf dem Raster liegen.
    await setup();

    const slider: HTMLElement = fixture.nativeElement.querySelector('mat-slider');
    expect(slider.getAttribute('min')).toBe('25');
    expect(slider.getAttribute('max')).toBe('500');
    expect(slider.getAttribute('step')).toBe('25');
    expect(component.radiusKm % 25).toBe(0);
  });

  it('macht aus einer Null-Teilnehmerzahl kein Filterkriterium', async () => {
    await setup();
    component.name = 'Zuhause';
    component.choose({ label: 'Wien', country: 'AT', postalCode: null, lat: 48.2, lon: 16.37 });
    component.minPlayers = 0;

    component.save();

    const req = expectSave();
    expect(req.request.body.minPlayers).toBeNull();
    req.flush(saved(req.request.body));
  });

  // ----- Ohne Lexikon-Treffer anlegbar, Suche mit Rueckmeldung (F6-007) --------

  it('nimmt den Browser-Standort samt nächstem Ort als Mittelpunkt', async () => {
    await setup();
    const geolocation = TestBed.inject(GeolocationService);
    spyOnProperty(geolocation, 'supported').and.returnValue(true);
    spyOn(geolocation, 'current').and.returnValue(of({ lat: 47.27, lon: 11.39, accuracyM: 30 }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('my_location');

    component.useCurrentLocation();
    http.expectOne(r => r.url === '/api/tournament-directory/places/nearest')
      .flush({ label: '6020 Innsbruck (AT)', country: 'AT', postalCode: '6020', lat: 47.26, lon: 11.4 });

    expect(component.placeQuery).toBe('6020 Innsbruck (AT)');
    expect(component.locating()).toBeFalse();
    component.name = 'Zuhause';
    component.save();
    // Mittelpunkt sind die Koordinaten des GERAETS, der Ort ist nur die Beschriftung.
    const req = expectSave();
    expect(req.request.body).toEqual(jasmine.objectContaining({ name: 'Zuhause', lat: 47.27, lon: 11.39, placeQuery: '6020 Innsbruck (AT)' }));
    req.flush(saved(req.request.body));
  });

  it('legt das Profil auch ohne Ort im Lexikon an — mit den Koordinaten als Beschriftung', async () => {
    await setup();
    const geolocation = TestBed.inject(GeolocationService);
    spyOnProperty(geolocation, 'supported').and.returnValue(true);
    spyOn(geolocation, 'current').and.returnValue(of({ lat: 59.91, lon: 10.75, accuracyM: 30 }));

    component.useCurrentLocation();
    http.expectOne(r => r.url === '/api/tournament-directory/places/nearest')
      .flush(null, { status: 204, statusText: 'No Content' });

    expect(component.placeQuery).toBe('59.910, 10.750');
    component.name = 'Oslo';
    component.save();
    const req = expectSave();
    expect(req.request.body).toEqual(jasmine.objectContaining({ name: 'Oslo', lat: 59.91, lon: 10.75 }));
    req.flush(saved(req.request.body));
  });

  it('meldet eine abgelehnte Standortfreigabe', async () => {
    await setup();
    const geolocation = TestBed.inject(GeolocationService);
    spyOnProperty(geolocation, 'supported').and.returnValue(true);
    spyOn(geolocation, 'current').and.returnValue(throwError(() => 'denied'));

    component.useCurrentLocation();
    fixture.detectChanges();

    expect(component.locating()).toBeFalse();
    expect(component.locationError()).toBe('denied');
    expect(fixture.nativeElement.textContent).toContain('tournamentDirectory.place.error.denied');
    expect(component.lat).toBeNull();
  });

  it('sagt „kein Ort gefunden" und übersteht einen Fehler der Ortssuche', async () => {
    await setup();

    component.onPlaceInput('Kleinstdorf');
    await new Promise(resolve => setTimeout(resolve, 300));
    http.expectOne(r => r.url === '/api/tournament-directory/places').flush([]);
    fixture.detectChanges();
    expect(component.noMatch).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('tournamentDirectory.place.noMatch');

    component.onPlaceInput('Kleinstdorfx');
    await new Promise(resolve => setTimeout(resolve, 300));
    http.expectOne(r => r.url === '/api/tournament-directory/places')
      .flush('kaputt', { status: 500, statusText: 'Server Error' });
    expect(component.searchFailed).toBeTrue();
    expect(component.searching).toBeFalse();

    // Der Such-Strom lebt noch: die naechste Eingabe fragt wieder.
    component.onPlaceInput('Salzburg');
    await new Promise(resolve => setTimeout(resolve, 300));
    http.expectOne(r => r.url === '/api/tournament-directory/places')
      .flush([{ label: '5020 Salzburg (AT)', country: 'AT', postalCode: '5020', lat: 47.8, lon: 13.04 }]);
    expect(component.suggestions.length).toBe(1);
    expect(component.searchFailed).toBeFalse();
  });

  // ----- Speichern im Dialog: Fehler lassen die Eingaben stehen (F6-003) ----------

  function fillValid() {
    component.name = 'Zuhause';
    component.choose({ label: '6380 St. Johann (AT)', country: 'AT', postalCode: '6380', lat: 47.52, lon: 12.42 });
    component.radiusKm = 150;
    component.selectedSpeeds = ['Rapid'];
    component.weekendOnly = true;
  }

  /**
   * Zweites Profil, wieder „Zuhause" genannt: vorher schloss der Dialog VOR dem Speichern, das 409
   * kam als „konnte nicht gespeichert werden", und Ort, Umkreis und Bedenkzeiten waren weg.
   */
  it('bleibt bei „Name schon vergeben" (409) offen, behält die Eingaben und nennt den Grund', async () => {
    await setup();
    fillValid();

    component.save();
    expect(component.saving).toBeTrue();
    expectSave().flush({ message: 'A search profile with this name already exists.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(closed).toBeUndefined();
    expect(component.saving).toBeFalse();
    expect(component.error).toBe('tournamentDirectory.profile.errorNameTaken');
    expect(fixture.nativeElement.textContent).toContain('tournamentDirectory.profile.errorNameTaken');
    expect(component.lat).toBe(47.52);
    expect(component.radiusKm).toBe(150);
    expect(component.selectedSpeeds).toEqual(['Rapid']);
    expect(component.weekendOnly).toBeTrue();

    // Neuer Name, zweiter Versuch — aus demselben Dialog.
    component.name = 'Zweitwohnsitz';
    component.save();
    const req = expectSave();
    expect(req.request.body).toEqual(jasmine.objectContaining({ name: 'Zweitwohnsitz', lat: 47.52 }));
    req.flush(saved(req.request.body, 21));
    expect(closed).toEqual(jasmine.objectContaining({ id: 21, name: 'Zweitwohnsitz' }));
    expect(component.error).toBeNull();
  });

  it('nennt beim 21. Profil die Obergrenze des Servers', async () => {
    await setup();
    fillValid();

    component.save();
    expectSave().flush({ message: 'At most 20 search profiles per user.' }, { status: 400, statusText: 'Bad Request' });

    expect(closed).toBeUndefined();
    expect(component.error).toBe('tournamentDirectory.profile.errorLimit');
    expect(component.errorParams).toEqual({ max: 20 });
  });

  it('meldet einen sonstigen Fehler allgemein und bleibt offen', async () => {
    await setup();
    fillValid();

    component.save();
    expectSave().flush('kaputt', { status: 500, statusText: 'Server Error' });

    expect(closed).toBeUndefined();
    expect(component.error).toBe('tournamentDirectory.profile.saveError');
    expect(component.name).toBe('Zuhause');
  });

  it('schickt bei einem Doppeltipp auf „Speichern" nur eine Anfrage', async () => {
    await setup();
    fillValid();

    component.save();
    component.save();
    fixture.detectChanges();
    const button = Array.from(fixture.nativeElement.querySelectorAll('.dialog-actions button') as NodeListOf<HTMLButtonElement>)
      .find(b => b.textContent?.includes('common.save'))!;
    expect(button.disabled).toBeTrue();

    const req = expectSave();
    req.flush(saved(req.request.body));
    expect(closed).toEqual(jasmine.objectContaining({ id: 9 }));
  });
});
