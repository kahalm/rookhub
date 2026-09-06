import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TournamentMapComponent } from './tournament-map.component';
import { DirectoryEntry } from './tournament-directory.model';

function entry(id: string, lat: number | null, lon: number | null,
               geoSource: DirectoryEntry['geoSource'] = 'City'): DirectoryEntry {
  return {
    chessResultsId: id, name: `Turnier ${id}`, federation: 'AUT', state: null,
    startDate: '2026-10-10', endDate: '2026-10-12', location: 'Salzburg', timeControl: null,
    speed: 'Standard', organizer: null, director: null, chiefArbiter: null,
    rounds: null, playerCount: null, lat, lon, geoSource, geoPlaceName: null,
    distanceKm: null, cancelled: false, subscribed: false, groupSize: 1, groups: [],
  };
}

describe('TournamentMapComponent', () => {
  let fixture: ComponentFixture<TournamentMapComponent>;
  let component: TournamentMapComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TournamentMapComponent] }).compileComponents();
    fixture = TestBed.createComponent(TournamentMapComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => fixture.destroy());

  it('meldet den sichtbaren Ausschnitt im Serverformat', () => {
    let bounds: string | null = null;
    component.boundsChanged.subscribe(b => (bounds = b));

    fixture.detectChanges();   // ngAfterViewInit legt die Karte an

    expect(bounds).not.toBeNull();
    expect(bounds!).toMatch(/^-?\d+\.\d+,-?\d+\.\d+,-?\d+\.\d+,-?\d+\.\d+$/);
  });

  it('überspringt Einträge ohne Koordinaten, statt an ihnen zu scheitern', () => {
    component.entries = [entry('1', 47.8, 13.04), entry('2', null, null)];

    expect(() => fixture.detectChanges()).not.toThrow();
  });

  it('zeichnet den Umkreis, wenn ein Mittelpunkt gesetzt ist', () => {
    component.centre = { lat: 47.8, lon: 13.04, radiusKm: 50 };

    expect(() => fixture.detectChanges()).not.toThrow();
  });

  it('holt die Kacheln von der eigenen Herkunft, nicht direkt von OpenStreetMap', () => {
    // Direkt zu laden setzt voraus, dass jeder Betrachter selbst ins offene Netz kommt.
    fixture.detectChanges();
    const src = fixture.nativeElement.querySelector('img.leaflet-tile')?.getAttribute('src') ?? '';
    expect(src.startsWith('/tiles/')).toBeTrue();
  });

  it('räumt die Karte beim Zerstören ab', () => {
    fixture.detectChanges();
    expect(() => fixture.destroy()).not.toThrow();
  });
});
