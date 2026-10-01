import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { TournamentCardDialogComponent } from './tournament-card-dialog.component';
import { TournamentCardComponent } from './tournament-card.component';
import { DirectoryEntry } from './tournament-directory.model';

function entry(): DirectoryEntry {
  return {
    id: '1457129', chessResultsId: '1457129', name: 'Open Braunau', federation: 'AUT',
    state: 'Salzburg',
    startDate: '2026-12-18', endDate: '2026-12-20', location: 'Ranshofen',
    timeControl: '90 min', speed: 'Standard', organizer: null, director: null, chiefArbiter: null,
    rounds: 7, playerCount: 42, lat: 48.2, lon: 13.0, geoSource: 'City', geoPlaceName: 'Ranshofen',
    distanceKm: null, cancelled: false, subscribed: false, groupSize: 1, groups: [], venues: [],
    kind: 'Individual', isLeague: false, ageGroups: [], gender: 'Open',
    ignored: false, roundDates: [], sources: [],
  };
}

/**
 * Das Kalender-Fenster reicht „gemerkt" an den Kalender weiter (Codereview 2026-09-29, F6-008):
 * vorher hoerte es nur auf (selected) und (ignoredChanged), und das naechste Oeffnen desselben
 * Turniers zeigte wieder „Merken".
 */
describe('TournamentCardDialogComponent', () => {
  function setup() {
    TestBed.configureTestingModule({
      imports: [TournamentCardDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: jasmine.createSpy('close') } },
        { provide: MAT_DIALOG_DATA, useValue: { entry: entry() } },
      ],
    });
    const fixture = TestBed.createComponent(TournamentCardDialogComponent);
    fixture.detectChanges();
    const card = fixture.debugElement.query(By.directive(TournamentCardComponent))
      .componentInstance as TournamentCardComponent;
    return { dialog: fixture.componentInstance, card };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('merkt sich, dass in der Kurzansicht gemerkt wurde', () => {
    const { dialog, card } = setup();
    expect(dialog.subscribed).toBeNull();

    card.subscribedChanged.emit({ entry: card.entry, subscribed: true });
    expect(dialog.subscribed).toBeTrue();

    card.subscribedChanged.emit({ entry: card.entry, subscribed: false });
    expect(dialog.subscribed).toBeFalse();
  });
});
