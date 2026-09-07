import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
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
    distanceKm: null, cancelled: false, subscribed: false, groupSize: 1, groups: [], venues: [],
    kind: 'Individual', isLeague: false, ageGroups: [], gender: 'Open',
    ignored: false, roundDates: [], sources: [],
  };
}

describe('TournamentMapComponent', () => {
  let fixture: ComponentFixture<TournamentMapComponent>;
  let component: TournamentMapComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TournamentMapComponent],
      providers: [
        provideTranslateService({ fallbackLang: 'en' }),
        // Das Popup ist inzwischen die gemeinsame Kurzansicht; die spricht mit dem Server
        // (merken, ausblenden) und braucht deshalb einen HttpClient.
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
      ],
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

    const popup = host.querySelector<HTMLElement>('.leaflet-popup-content .tc');
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

    expect(popup.querySelector('.tc-name')?.textContent?.trim()).toBe('Turnier 1');
    expect(selected).withContext('darf beim bloßen Anklicken NICHT weiterführen').toBeNull();
  });

  it('führt erst der Klick auf den Titel im Popup zur Detailseite', () => {
    let selected: DirectoryEntry | null = null;
    component.entrySelected.subscribe(e => (selected = e));
    centredOn(entry('1', 47.8, 13.04));

    openPopupAtCentre().querySelector<HTMLButtonElement>('.tc-name')!.click();

    expect(selected).not.toBeNull();
    expect(selected!.chessResultsId).toBe('1');
  });

  it('zeigt Termin, Ort und die Kurzangaben im Popup', () => {
    const e = entry('1', 47.8, 13.04);
    e.playerCount = 42;
    e.cancelled = true;
    centredOn(e);

    const popup = openPopupAtCentre();
    // Der Text enthaelt die Symbolnamen des mat-icon davor — geprueft wird deshalb auf
    // Enthaltensein, nicht auf Gleichheit.
    const lines = [...popup.querySelectorAll('.tc-line')].map(n => n.textContent ?? '');
    expect(lines.length).toBe(2);
    expect(lines[0]).toContain('2026-10-10 – 2026-10-12');
    expect(lines[1]).toContain('Salzburg');
    expect(popup.querySelectorAll('.badge').length).toBeGreaterThanOrEqual(3);
    expect(popup.querySelector('.badge.warn')).withContext('abgesagt fehlt').not.toBeNull();

    // Und die vier Aktionen, die die Kurzansicht ueberall gleich anbietet.
    expect(popup.querySelectorAll('.tc-actions button').length).toBe(4);
  });

  it('zoomt auf Wunsch eine Stufe weiter heraus als der eingepasste Ausschnitt', async () => {
    // Auf der Detailseite sitzt der eingepasste Ausschnitt so knapp um den Ort, dass die
    // Umgebung fehlt, an der man ihn erkennt.
    const centre = { lat: 47.8, lon: 13.04, radiusKm: 6 };

    let eng: string | null = null;
    component.boundsChanged.subscribe(b => (eng = b));
    component.centre = centre;
    fixture.detectChanges();
    await Promise.resolve();
    const engSpan = Number(eng!.split(',')[2]) - Number(eng!.split(',')[0]);

    fixture.destroy();
    fixture = TestBed.createComponent(TournamentMapComponent);
    component = fixture.componentInstance;
    let weit: string | null = null;
    component.boundsChanged.subscribe(b => (weit = b));
    component.centre = centre;
    component.zoomOutSteps = 1;
    fixture.detectChanges();
    await Promise.resolve();
    const weitSpan = Number(weit!.split(',')[2]) - Number(weit!.split(',')[0]);

    // Eine Zoomstufe = doppelter Ausschnitt.
    expect(weitSpan / engSpan).toBeCloseTo(2, 1);
  });

  it('zeichnet je Spielort einen Punkt, nicht nur den Hauptort', () => {
    // Bei Ligen nennt chess-results mehrere („Mayrhofen, St.Veit"). Mit nur dem Hauptort
    // verschwände ein Turnier die Hälfte seiner Orte.
    const liga = entry('1', 47.17, 11.87);
    liga.venues = [
      { name: 'Mayrhofen', lat: 47.17, lon: 11.87, geoSource: 'City' },
      { name: 'St. Veit an der Glan', lat: 46.77, lon: 14.36, geoSource: 'PostalCode' },
    ];
    component.entries = [liga];
    // Weit genug herausgezoomt, dass beide Orte im Bild sind.
    component.centre = { lat: 47.0, lon: 13.1, radiusKm: 200 };

    fixture.detectChanges();

    // Die Punkte liegen auf der Leinwand — gezählt wird über die Leaflet-Ebene.
    const layers = (component as unknown as { markerLayer: { getLayers(): unknown[] } }).markerLayer;
    expect(layers.getLayers().length).toBe(2);
  });

  it('zeichnet ohne Spielort-Liste weiterhin den einen Ort des Eintrags', () => {
    component.entries = [entry('1', 47.8, 13.04)];
    fixture.detectChanges();

    const layers = (component as unknown as { markerLayer: { getLayers(): unknown[] } }).markerLayer;
    expect(layers.getLayers().length).toBe(1);
  });

  it('räumt die Karte beim Zerstören ab', () => {
    fixture.detectChanges();
    expect(() => fixture.destroy()).not.toThrow();
  });
});
