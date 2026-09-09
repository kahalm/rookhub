import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { SearchProfileDialogComponent } from './search-profile-dialog.component';
import { SearchProfileInput } from './tournament-directory.model';

describe('SearchProfileDialogComponent', () => {
  let fixture: ComponentFixture<SearchProfileDialogComponent>;
  let component: SearchProfileDialogComponent;
  let http: HttpTestingController;
  let closed: SearchProfileInput | null | undefined;

  async function setup(profile: SearchProfileDialogComponent['data']['profile'] = null) {
    closed = undefined;
    await TestBed.configureTestingModule({
      imports: [SearchProfileDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { profile } },
        { provide: MatDialogRef, useValue: { close: (v: SearchProfileInput | null) => (closed = v) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SearchProfileDialogComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

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

    expect(closed).toEqual(jasmine.objectContaining({
      name: 'Zuhause', lat: 47.8, lon: 13.04, radiusKm: 75, speeds: ['Blitz'], notifyNew: true,
    }));
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
    expect(closed).toEqual(jasmine.objectContaining({ name: 'Ferienhaus', sortOrder: 2 }));
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

    expect(closed!.minPlayers).toBeNull();
  });
});
