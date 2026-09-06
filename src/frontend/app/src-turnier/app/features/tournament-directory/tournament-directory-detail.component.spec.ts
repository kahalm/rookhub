import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { TournamentDirectoryDetailComponent } from './tournament-directory-detail.component';
import { DirectoryEntry } from './tournament-directory.model';

function entry(id: string, over: Partial<DirectoryEntry> = {}): DirectoryEntry {
  return {
    chessResultsId: id, name: 'Open Braunau 2026', federation: 'AUT', state: 'Oberösterreich',
    startDate: '2026-12-18', endDate: '2026-12-20', location: 'Ranshofen',
    timeControl: '90 min', speed: 'Standard', organizer: 'SK Braunau', director: null,
    chiefArbiter: null, rounds: 7, playerCount: 42, lat: 48.2, lon: 13.0, geoSource: 'City',
    geoPlaceName: 'Ranshofen', distanceKm: null, cancelled: false, subscribed: false,
    groupSize: 1, groups: [], ...over,
  };
}

describe('TournamentDirectoryDetailComponent', () => {
  let fixture: ComponentFixture<TournamentDirectoryDetailComponent>;
  let component: TournamentDirectoryDetailComponent;
  let http: HttpTestingController;

  async function setup(id = '1457129') {
    await TestBed.configureTestingModule({
      imports: [TournamentDirectoryDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id })) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TournamentDirectoryDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  }

  /** Der Blick „liegt das Turnier schon geholt hier?" laeuft nach jedem geladenen Eintrag. */
  function flushImportLookup(id: string, body: Record<string, unknown> | null = null) {
    const req = http.expectOne(`/api/tournaments/${id}`);
    if (body) req.flush(body);
    else req.flush('nicht geholt', { status: 404, statusText: 'Not Found' });
  }

  it('lädt das Turnier zur Id aus der Adresse', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');

    expect(component.entry()?.name).toBe('Open Braunau 2026');
    expect(component.loading()).toBeFalse();
    expect(component.notFound()).toBeFalse();
    http.verify();
  });

  it('sagt es, wenn das Turnier nicht im Verzeichnis steht', async () => {
    await setup('999');
    http.expectOne('/api/tournament-directory/999')
      .flush('weg', { status: 404, statusText: 'Not Found' });

    expect(component.notFound()).toBeTrue();
    expect(component.entry()).toBeNull();
    // Ohne Eintrag wird auch nicht nach einem geholten Turnier gesucht.
    http.verify();
  });

  it('bietet den Sprung zu Teilnehmern und Ergebnissen, sobald das Turnier geholt ist', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129', { id: 12, chessResultsId: '1457129', name: 'Open Braunau 2026' });

    expect(component.imported()?.id).toBe(12);
  });

  it('nimmt kein fremdes Turnier, das nur zufällig auf die interne Nummer passt', async () => {
    // Die Crawler-Route loest erst die INTERNE Nummer auf. Beide Nummernkreise sind numerisch —
    // ohne Gegenprobe zeigte die Seite die Ergebnisse eines voellig anderen Turniers.
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129', { id: 1457129, chessResultsId: '888', name: 'Ganz anderes' });

    expect(component.imported()).toBeNull();
  });

  it('merkt das Turnier und schaltet die Anzeige sofort um', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');

    component.bookmark();
    http.expectOne({ method: 'POST', url: '/api/subscriptions' }).flush({ id: 1 });

    expect(component.entry()?.subscribed).toBeTrue();
    http.verify();
  });

  it('zeigt die Karte nur mit Koordinaten und passt sie auf den Ort ein', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');

    expect(component.pins().length).toBe(1);
    expect(component.mapCentre()).toEqual({ lat: 48.2, lon: 13.0, radiusKm: 6 });
    // Und derselbe Getter liefert dasselbe OBJEKT — ein frisches Literal je Zyklus liesse die
    // Karte in jeder Änderungserkennung neu einpassen (genau der Zoom-Fehler im Kalender).
    expect(component.mapCentre()).toBe(component.mapCentre());
  });

  it('lässt die Karte weg, wenn das Turnier nicht verortet ist', async () => {
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129')
      .flush(entry('1457129', { lat: null, lon: null, geoSource: 'None' }));
    flushImportLookup('1457129');

    expect(component.pins()).toEqual([]);
    expect(component.mapCentre()).toBeNull();
  });

  it('führt zurück in den Kalender statt in den Browserverlauf', async () => {
    // Der Einstieg kann eine Benachrichtigung gewesen sein — dann führte „zurück" aus der App.
    await setup('1457129');
    http.expectOne('/api/tournament-directory/1457129').flush(entry('1457129'));
    flushImportLookup('1457129');

    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    component.back();
    expect(navigate).toHaveBeenCalledWith(['/tournaments/calendar']);
  });
});
