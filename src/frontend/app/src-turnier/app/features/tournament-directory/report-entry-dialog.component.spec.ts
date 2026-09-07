import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ReportEntryDialogComponent } from './report-entry-dialog.component';
import { DirectoryEntry } from './tournament-directory.model';

function entry(over: Partial<DirectoryEntry> = {}): DirectoryEntry {
  return {
    chessResultsId: '1405166', name: 'Frauenbundesliga', federation: 'AUT', state: 'Tirol',
    startDate: '2026-11-14', endDate: '2027-03-15', location: 'Mayrhofen, St.Veit',
    timeControl: '90 min', speed: 'Standard', organizer: null, director: null, chiefArbiter: null,
    rounds: 7, playerCount: 40, lat: 47.16, lon: 11.86, geoSource: 'City',
    geoPlaceName: 'St. Veit in Defereggen', distanceKm: null, cancelled: false, subscribed: false,
    groupSize: 1, groups: [], venues: [], kind: 'Team', isLeague: true,
    ageGroups: [], gender: 'Female', ...over,
  };
}

/**
 * „Falsches Event melden". Der eigentliche Gewinn sind nicht die Korrekturfelder, sondern die
 * zwei Lern-Fragen: Alter und Publikum stehen nur im Namen, und diese Namen sind regional.
 */
describe('ReportEntryDialogComponent', () => {
  let http: HttpTestingController;
  let closed: jasmine.Spy;

  function setup(over: Partial<DirectoryEntry> = {}) {
    closed = jasmine.createSpy('close');
    TestBed.configureTestingModule({
      imports: [ReportEntryDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: closed } },
        { provide: MAT_DIALOG_DATA, useValue: { entry: entry(over) } },
      ],
    });
    const fixture = TestBed.createComponent(ReportEntryDialogComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  afterEach(() => TestBed.resetTestingModule());

  /** Ein Knopfdruck ohne ein Wort ist eine gueltige Meldung („hier stimmt was nicht"). */
  it('sendet auch ohne jede Eingabe', () => {
    const component = setup();

    component.send();

    const req = http.expectOne('/api/tournament-directory/1405166/report');
    expect(req.request.body).toEqual({ message: null, namePattern: null, sourceLink: null });
    req.flush(null);

    expect(closed).toHaveBeenCalledWith(true);
    http.verify();
  });

  /**
   * Was der Dialog schickt, ist ein Satz Freitext plus die zwei Lern-Antworten. Strukturierte
   * Korrekturfelder (Ort, Art, Klasse, Bedenkzeit, Liga) standen hier einmal und sind wieder
   * weg: sie verlangten vom Melder die interne Wertetabelle und machten aus einer Rueckmeldung
   * ein Formular.
   */
  it('schickt Freitext und die beiden Lern-Antworten mit', () => {
    const component = setup();
    component.message = 'Gespielt wird in St. Veit an der Glan.';
    component.namePattern = 'Schachrallye = immer Nachwuchs';
    component.sourceLink = 'https://www.tiroler-schachverband.at/jugend';

    component.send();

    const body = http.expectOne('/api/tournament-directory/1405166/report').request.body;
    expect(body).toEqual({
      message: 'Gespielt wird in St. Veit an der Glan.',
      namePattern: 'Schachrallye = immer Nachwuchs',
      sourceLink: 'https://www.tiroler-schachverband.at/jugend',
    });
  });

  /**
   * Der IST-Stand steht mit im Dialog. Ohne ihn meldet jemand einen Ort, der schon so
   * gespeichert ist: auf der Seite steht nur der Ortstext von chess-results, nicht das Ergebnis
   * der Verortung.
   */
  it('zeigt den gespeicherten Stand an', () => {
    const component = setup();

    expect(component.summary).toContain('St. Veit in Defereggen');
  });

  it('lässt den Dialog nach einem Fehlschlag offen', () => {
    const component = setup();

    component.send();
    http.expectOne('/api/tournament-directory/1405166/report')
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(component.failed()).toBeTrue();
    expect(closed).not.toHaveBeenCalled();
  });
});
