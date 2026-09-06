import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
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
    await TestBed.configureTestingModule({
      imports: [TournamentMapComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    fixture = TestBed.createComponent(TournamentMapComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => fixture.destroy());

  it('meldet den sichtbaren Ausschnitt im Serverformat', async () => {
    let bounds: string | null = null;
    component.boundsChanged.subscribe(b => (bounds = b));

    fixture.detectChanges();   // ngAfterViewInit legt die Karte an
    // Der erste Bericht kommt bewusst erst NACH dem laufenden Durchlauf (sonst NG0100 beim
    // Elternteil, der daraufhin sein Ladeflag setzt).
    await Promise.resolve();

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

  it('zeigt beim Start den GANZEN Umkreis, nicht einen Ausschnitt davon', async () => {
    // Passt Leaflet auf eine Flaeche der Groesse 0 ein, rechnet es die groesstmoegliche
    // Vergroesserung aus — man landet tief in einer Strasse statt beim ganzen Umkreis.
    const lat = 47.8, lon = 13.04, radiusKm = 100;
    let bounds: string | null = null;
    component.boundsChanged.subscribe(b => (bounds = b));
    component.centre = { lat, lon, radiusKm };

    fixture.detectChanges();
    await Promise.resolve();

    const [minLat, minLon, maxLat, maxLon] = bounds!.split(',').map(Number);
    const dLat = radiusKm / 111.32;
    const dLon = radiusKm / (111.32 * Math.cos((lat * Math.PI) / 180));
    expect(minLat).withContext('Suedrand des Kreises abgeschnitten').toBeLessThanOrEqual(lat - dLat);
    expect(maxLat).withContext('Nordrand des Kreises abgeschnitten').toBeGreaterThanOrEqual(lat + dLat);
    expect(minLon).withContext('Westrand des Kreises abgeschnitten').toBeLessThanOrEqual(lon - dLon);
    expect(maxLon).withContext('Ostrand des Kreises abgeschnitten').toBeGreaterThanOrEqual(lon + dLon);
  });

  it('holt die Kacheln von der eigenen Herkunft, nicht direkt von OpenStreetMap', () => {
    // Direkt zu laden setzt voraus, dass jeder Betrachter selbst ins offene Netz kommt.
    fixture.detectChanges();
    const src = fixture.nativeElement.querySelector('img.leaflet-tile')?.getAttribute('src') ?? '';
    expect(src.startsWith('/tiles/')).toBeTrue();
  });

  // ----- Klick auf einen Punkt: erst das Popup, dann die Detailseite -----------------

  /**
   * Klickt den Punkt in der MITTE der Karte an und gibt den Popup-Inhalt zurück.
   *
   * Die Punkte liegen auf einer Leinwand (`preferCanvas`), es gibt sie also NICHT als eigene
   * DOM-Knoten — der Klick muss an die richtige Pixelstelle. Deshalb setzen die Tests den
   * Mittelpunkt auf dieselben Koordinaten wie das Turnier: dann liegt der Punkt genau mittig.
   */
  function openPopupAtCentre(): HTMLElement {
    const host: HTMLElement = fixture.nativeElement;
    const canvas = host.querySelector<HTMLCanvasElement>('canvas.leaflet-zoom-animated');
    expect(canvas).withContext('keine Zeichenfläche für die Punkte').not.toBeNull();

    const box = canvas!.getBoundingClientRect();
    const at = { clientX: box.left + box.width / 2, clientY: box.top + box.height / 2, bubbles: true };
    // Leaflet erkennt einen Klick erst nach mousedown/mouseup auf derselben Stelle.
    canvas!.dispatchEvent(new MouseEvent('mousedown', at));
    canvas!.dispatchEvent(new MouseEvent('mouseup', at));
    canvas!.dispatchEvent(new MouseEvent('click', at));

    const popup = host.querySelector<HTMLElement>('.leaflet-popup-content .tm-popup');
    expect(popup).withContext('kein Popup geöffnet').not.toBeNull();
    return popup!;
  }

  /** Turnier und Kartenmittelpunkt auf dieselbe Stelle legen — der Punkt sitzt dann mittig. */
  function centredOn(e: DirectoryEntry): void {
    component.entries = [e];
    component.centre = { lat: e.lat!, lon: e.lon!, radiusKm: 5 };
    fixture.detectChanges();
  }

  it('öffnet beim Klick auf einen Punkt ein Popup, statt die Karte zu verlassen', () => {
    // Wer auf der Karte sucht, vergleicht — ein Klick, der wegnavigiert, reißt den Faden ab.
    let selected: DirectoryEntry | null = null;
    component.entrySelected.subscribe(e => (selected = e));
    centredOn(entry('1', 47.8, 13.04));

    const popup = openPopupAtCentre();

    expect(popup.querySelector('.tm-popup-title')?.textContent).toBe('Turnier 1');
    expect(selected).withContext('darf beim bloßen Anklicken NICHT weiterführen').toBeNull();
  });

  it('führt erst der Klick auf den Titel im Popup zur Detailseite', () => {
    let selected: DirectoryEntry | null = null;
    component.entrySelected.subscribe(e => (selected = e));
    centredOn(entry('1', 47.8, 13.04));

    openPopupAtCentre().querySelector<HTMLButtonElement>('.tm-popup-title')!.click();

    expect(selected).not.toBeNull();
    expect(selected!.chessResultsId).toBe('1');
  });

  it('zeigt Termin, Ort und die Kurzangaben im Popup', () => {
    const e = entry('1', 47.8, 13.04);
    e.playerCount = 42;
    e.cancelled = true;
    centredOn(e);

    const popup = openPopupAtCentre();
    const lines = [...popup.querySelectorAll('.tm-popup-line')].map(n => n.textContent);
    expect(lines).toEqual(['2026-10-10 – 2026-10-12', 'Salzburg']);
    expect(popup.querySelectorAll('.tm-badge').length).toBeGreaterThanOrEqual(3);
    expect(popup.querySelector('.tm-badge-warn')).withContext('abgesagt fehlt').not.toBeNull();
  });

  it('räumt die Karte beim Zerstören ab', () => {
    fixture.detectChanges();
    expect(() => fixture.destroy()).not.toThrow();
  });
});
