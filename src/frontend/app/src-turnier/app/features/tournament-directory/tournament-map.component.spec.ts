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
    id, chessResultsId: id, name: `Turnier ${id}`, federation: 'AUT', state: null,
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
  function clickCentre(): void {
    const host: HTMLElement = fixture.nativeElement;
    const canvas = host.querySelector<HTMLCanvasElement>('canvas.leaflet-zoom-animated');
    expect(canvas).withContext('keine Zeichenfläche für die Punkte').not.toBeNull();

    const box = canvas!.getBoundingClientRect();
    const at = { clientX: box.left + box.width / 2, clientY: box.top + box.height / 2, bubbles: true };
    // Leaflet erkennt einen Klick erst nach mousedown/mouseup auf derselben Stelle.
    canvas!.dispatchEvent(new MouseEvent('mousedown', at));
    canvas!.dispatchEvent(new MouseEvent('mouseup', at));
    canvas!.dispatchEvent(new MouseEvent('click', at));
  }

  /** Die Kurzansicht im geöffneten Popup (ein Punkt, der für genau EIN Turnier steht). */
  function openPopupAtCentre(): HTMLElement {
    clickCentre();
    const host: HTMLElement = fixture.nativeElement;
    const popup = host.querySelector<HTMLElement>('.leaflet-popup-content .tc');
    expect(popup).withContext('kein Popup geöffnet').not.toBeNull();
    return popup!;
  }

  /** Der Popup-Inhalt eines GEBÜNDELTEN Punktes — die Liste bzw. die Ansicht darunter. */
  function openGroupAtCentre(): HTMLElement {
    clickCentre();
    const host: HTMLElement = fixture.nativeElement;
    const popup = host.querySelector<HTMLElement>('.leaflet-popup-content .tm-group');
    expect(popup).withContext('kein Popup geöffnet').not.toBeNull();
    return popup!;
  }

  /** Turnier und Kartenmittelpunkt auf dieselbe Stelle legen — der Punkt sitzt dann mittig. */
  function centredOn(...es: DirectoryEntry[]): void {
    component.entries = es;
    component.centre = { lat: es[0].lat!, lon: es[0].lon!, radiusKm: 5 };
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
    expect(selected!.id).toBe('1');
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

  // ----- Handy: Popup-Breite und Trefferflaeche --------------------------------------

  /** Karte so breit wie ein kleines Handy bzw. ein Desktop. Der Host ist ein Inline-Element. */
  function mapWidth(px: number): void {
    const host: HTMLElement = fixture.nativeElement;
    host.style.display = 'block';
    host.style.width = `${px}px`;
  }

  it('macht das Popup auf einer schmalen Karte schmaler als die Karte', () => {
    // Auf 360 px war es 3 px breiter als die Karte: rechter Rand und ein Drittel des
    // Schliessen-X abgeschnitten.
    mapWidth(344);
    centredOn(entry('1', 47.8, 13.04));

    const [popup] = popupOptions(component);
    expect(popup.maxWidth!).toBeLessThanOrEqual(344 - 60);
    expect(popup.minWidth!).toBeLessThanOrEqual(popup.maxWidth!);
  });

  it('lässt die Popup-Breite am Desktop unverändert', () => {
    mapWidth(800);
    centredOn(entry('1', 47.8, 13.04));

    const [popup] = popupOptions(component);
    expect(popup.minWidth).toBe(220);
    expect(popup.maxWidth).toBe(300);
  });

  /**
   * Auf Touch-Geraeten bekommt der Canvas-Renderer 8 px Klick-Toleranz: ein einzelner Pin ist
   * ~16 px breit und mit dem Finger kaum zu treffen. Mit der Maus nicht — die ist praezise, und
   * der Hover-Hinweis soll nicht neben dem Pin aufgehen.
   */
  it('gibt dem Renderer auf Touch-Geräten 8 px Klick-Toleranz', () => {
    fakePointer(true);
    fixture.detectChanges();

    expect(rendererTolerance(component)).toBe(8);
  });

  it('lässt die Klick-Toleranz mit der Maus bei 0', () => {
    fakePointer(false);
    fixture.detectChanges();

    expect(rendererTolerance(component)).toBe(0);
  });

  /** Taeuscht das Zeigegeraet vor; alle anderen Medienabfragen laufen weiter ans Original. */
  function fakePointer(coarse: boolean): void {
    const original = window.matchMedia.bind(window);
    spyOn(window, 'matchMedia').and.callFake((query: string) =>
      query === '(pointer: coarse)' ? ({ matches: coarse } as MediaQueryList) : original(query));
  }

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

  // ----- Mehrere Turniere auf DERSELBEN Koordinate ----------------------------------
  //
  // Gemeldet an zwei Tiroler Ligen, deren Ortstext nur „Tirol" lautet: beide sitzen auf der
  // Landesmitte, und der zweite Pin lag exakt unter dem ersten — unerreichbar, ohne dass etwas
  // darauf hindeutete.

  it('fasst Punkte auf derselben Koordinate zu EINEM Pin mit Anzahl zusammen', () => {
    component.entries = [entry('1', 47.2178, 11.6411, 'Region'),
                         entry('2', 47.2178, 11.6411, 'Region'),
                         entry('3', 47.2178, 11.6411, 'Region')];
    fixture.detectChanges();

    const pins = markerOptions(component);
    expect(pins.length).withContext('drei Pins übereinander statt einem').toBe(1);
    expect(pins[0].count).toBe(3);
  });

  it('lässt Turniere auf verschiedenen Koordinaten einzeln', () => {
    component.entries = [entry('1', 47.8, 13.04), entry('2', 47.9, 13.1)];
    fixture.detectChanges();

    const pins = markerOptions(component);
    expect(pins.length).toBe(2);
    // Ohne Bündelung trägt der Pin keine Zahl — „1" auf jedem Punkt wäre nur Rauschen.
    expect(pins.every(p => p.count === 1)).toBeTrue();
  });

  it('nennt im Popup JEDES Turnier des Punktes, nicht nur das oberste', () => {
    centredOn(entry('1', 47.2178, 11.6411, 'Region'),
              entry('2', 47.2178, 11.6411, 'Region'));

    const rows = [...openGroupAtCentre().querySelectorAll('.tm-group-name')]
      .map(n => n.textContent?.trim());

    expect(rows).toEqual(['Turnier 1', 'Turnier 2']);
  });

  it('führt von der Liste in die Kurzansicht und wieder zurück', () => {
    centredOn(entry('1', 47.2178, 11.6411, 'Region'),
              entry('2', 47.2178, 11.6411, 'Region'));
    const popup = openGroupAtCentre();

    popup.querySelectorAll<HTMLButtonElement>('.tm-group-row')[1].click();

    expect(popup.querySelector('.tc-name')?.textContent?.trim()).toBe('Turnier 2');

    popup.querySelector<HTMLButtonElement>('.tm-group-back')!.click();

    expect(popup.querySelectorAll('.tm-group-row').length).toBe(2);
    expect(popup.querySelector('.tc')).withContext('Kurzansicht nicht abgeräumt').toBeNull();
  });

  it('hebt den gebündelten Punkt hervor, sobald EINES seiner Turniere gemerkt ist', () => {
    const a = entry('1', 47.2178, 11.6411, 'Region');
    const b = entry('2', 47.2178, 11.6411, 'Region');
    component.entries = [a, b];
    fixture.detectChanges();
    const vorher = markerStyles(component)[0].color;

    component.applySubscribed(b, true);

    // Ein gemerktes Turnier unter fünf anderen ginge sonst unter — der Punkt gehört ihnen allen.
    expect(markerStyles(component)[0].color).not.toBe(vorher);
  });

  // ----- Einfärben nach einem Merkmal --------------------------------------------------

  it('färbt die Punkte nach dem gewählten Merkmal, nicht nach „gemerkt"', () => {
    // „Wo ist ein Schnellschachturnier" soll ohne einen einzigen Klick zu beantworten sein.
    component.entries = [entry('1', 47.8, 13.04), { ...entry('2', 47.9, 13.1), speed: 'Rapid' }];
    fixture.detectChanges();

    const [standard, rapid] = markerStyles(component);
    expect(standard.fillColor).not.toBe(rapid.fillColor);
  });

  it('schaltet auf ein anderes Merkmal um und meldet die Wahl nach draußen', () => {
    const gewaehlt: string[] = [];
    component.colourByChange.subscribe(c => gewaehlt.push(c));
    component.entries = [entry('1', 47.8, 13.04), { ...entry('2', 47.9, 13.1), kind: 'Team' }];
    fixture.detectChanges();
    const vorher = markerStyles(component).map(s => s.fillColor);

    const select: HTMLSelectElement =
      fixture.nativeElement.querySelector('.colour-by select');
    select.value = 'kind';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(gewaehlt).toEqual(['kind']);
    // Nach Turnierart unterscheiden sich die beiden, nach Bedenkzeit taten sie es nicht.
    expect(vorher[0]).toBe(vorher[1]);
    const nachher = markerStyles(component).map(s => s.fillColor);
    expect(nachher[0]).not.toBe(nachher[1]);
  });

  it('behauptet auf einem gemischten Bündel keine Klasse', () => {
    component.entries = [{ ...entry('1', 47.2178, 11.6411), speed: 'Rapid' },
                         { ...entry('2', 47.2178, 11.6411), speed: 'Blitz' }];
    fixture.detectChanges();

    expect(markerStyles(component)[0].fillColor).toBe('#79808a');
    // Und die Legende erklärt die graue Marke, sobald es eine gibt.
    fixture.nativeElement.querySelector('.legend-toggle').click();
    fixture.detectChanges();
    const legende: string = fixture.nativeElement.querySelector('.legend').textContent;
    expect(legende).toContain('tournamentDirectory.map.legend.mixed');
  });

  it('zeigt „gemerkt" als Ring, damit die Füllung die Klasse behalten kann', () => {
    const gemerkt = { ...entry('1', 47.8, 13.0), subscribed: true };
    const offen = entry('2', 47.9, 13.1);
    component.entries = [offen, gemerkt];
    fixture.detectChanges();

    const stile = markerStyles(component);
    const g = stile.find(s => s.weight === 4);
    expect(g).withContext('gemerkt ohne dicken Ring').toBeDefined();
    // Dieselbe Bedenkzeit, also dieselbe Füllung — unterschieden wird über den Rand.
    expect(stile[0].fillColor).toBe(stile[1].fillColor);
    expect(g!.color).not.toBe(stile.find(s => s.weight === 2)!.color);
  });

  it('listet in der Legende die Klassen des gewählten Merkmals', () => {
    fixture.detectChanges();
    fixture.nativeElement.querySelector('.legend-toggle').click();
    fixture.detectChanges();
    const zeilen = [...fixture.nativeElement.querySelectorAll('.legend li')]
      .map((n: Element) => n.textContent?.trim());

    expect(zeilen[0]).toContain('tournamentDirectory.speed.Standard');
    // „gemerkt" und „nur ungefähr" liegen ÜBER dem Merkmal und stehen deshalb immer dabei.
    expect(zeilen.at(-2)).toContain('tournamentDirectory.map.legend.bookmarked');
    expect(zeilen.at(-1)).toContain('tournamentDirectory.map.legend.approximate');
  });

  it('hält die Legende zugeklappt, bis jemand sie aufschlägt', () => {
    // Sie erklärt eine Bildsprache, die man EINMAL nachliest — dauerhaft aufgeklappt verdeckt sie
    // auf einem Handy ein Viertel der Karte, also genau das, wofür man sie aufgeschlagen hat.
    fixture.detectChanges();
    const host: HTMLElement = fixture.nativeElement;

    expect(host.querySelector('.legend')).withContext('Legende steht offen').toBeNull();
    expect(host.querySelector('.legend-toggle')!.getAttribute('aria-expanded')).toBe('false');
    // Die AUSWAHL bleibt sichtbar — sie ist das Bedienelement, nicht die Erklärung.
    expect(host.querySelector('.colour-by select')).not.toBeNull();

    host.querySelector<HTMLButtonElement>('.legend-toggle')!.click();
    fixture.detectChanges();

    expect(host.querySelectorAll('.legend li').length).toBeGreaterThan(0);
    expect(host.querySelector('.legend-toggle')!.getAttribute('aria-expanded')).toBe('true');
  });

  it('blendet Auswahl und Legende aus, wenn die Karte ohne Zubehör gewünscht ist', () => {
    // Die Detailseite zeigt genau EINEN Pin: die Legende erklaert dort nichts, die Auswahl faerbt
    // nichts um — das Bedienfeld verdeckte auf dem Handy aber ein Viertel der kleinen Karte.
    fixture.componentRef.setInput('showChrome', false);
    fixture.detectChanges();
    const chrome = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.map-chrome');

    // Im DOM bleibt es (statischer ViewChild, disableClickPropagation), zu sehen ist es nicht.
    expect(chrome).withContext('Zubehoer aus dem DOM entfernt statt ausgeblendet').not.toBeNull();
    expect(chrome!.hidden).toBeTrue();
    expect(getComputedStyle(chrome!).display).toBe('none');
  });

  /**
   * Leaflet schiebt die Karte selbst, damit ein Popup am Rand ins Bild passt. Meldet man dieses
   * Schieben als neuen Ausschnitt, lädt der Elternteil die Punkte neu — und wirft damit genau das
   * Popup weg, für das die Karte eben gerückt ist.
   */
  it('meldet Leaflets eigenes Rücken für ein Popup NICHT als neuen Ausschnitt', async () => {
    fixture.detectChanges();
    await Promise.resolve();

    let gemeldet = 0;
    component.boundsChanged.subscribe(() => gemeldet++);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const map = (component as any).map;

    map.fire('autopanstart');
    map.fire('moveend');
    expect(gemeldet).withContext('das Rücken fürs Popup wurde gemeldet').toBe(0);

    // Die NÄCHSTE echte Bewegung meldet wieder.
    map.fire('moveend');
    expect(gemeldet).toBe(1);
  });

  it('räumt die Karte beim Zerstören ab', () => {
    fixture.detectChanges();
    expect(() => fixture.destroy()).not.toThrow();
  });
  /**
   * Der Punkt zeigt „gemerkt" mit an — vorher unterschied er es GAR NICHT, die Auskunft stand nur
   * im Popup, also erst nach dem Klick auf den Punkt, den man ohne die Auskunft nicht kennt.
   *
   * <p>Getragen wird es vom RING (Farbe UND Dicke), seit die Fuellung die Klasse des gewaehlten
   * Merkmals zeigt. Zwei Kanaele bleiben es damit weiterhin.</p>
   */
  it('hebt gemerkte Turniere auf der Karte hervor', () => {
    const gemerkt = { ...entry('1', 47.8, 13.0), subscribed: true };
    const offen = entry('2', 47.9, 13.1);
    fixture.componentRef.setInput('entries', [gemerkt, offen]);
    fixture.detectChanges();

    const stile = markerStyles(component);
    expect(stile.length).toBe(2);
    const [a, b] = stile;
    expect(a.color).not.toBe(b.color);
    expect(a.weight).not.toBe(b.weight);
  });

  /**
   * Wird im Popup gemerkt, faerbt sich der Punkt SOFORT um — ein Neuladen des Ausschnitts wuerde
   * das offene Popup zuschlagen.
   */
  it('färbt den Punkt nach dem Merken um, ohne neu zu laden', () => {
    const e = entry('1', 47.8, 13.0);
    fixture.componentRef.setInput('entries', [e]);
    fixture.detectChanges();
    const vorher = markerStyles(component)[0].color;

    component.applySubscribed(e, true);

    expect(e.subscribed).toBeTrue();
    // Der RING wechselt; die Fuellung gehoert dem eingefaerbten Merkmal und bleibt.
    expect(markerStyles(component)[0].color).not.toBe(vorher);
  });
});

/** Die Popup-Optionen, mit denen die Punkte gebunden wurden. */
function popupOptions(component: TournamentMapComponent): { minWidth?: number; maxWidth?: number }[] {
  const options: { minWidth?: number; maxWidth?: number }[] = [];
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (component as any).markerLayer?.eachLayer((l: any) => options.push(l.getPopup().options));
  return options;
}

/** Die Klick-Toleranz des Canvas-Renderers, den die Karte bekommen hat. */
function rendererTolerance(component: TournamentMapComponent): number {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return (component as any).map.options.renderer.options.tolerance;
}

/** Die Marker-Optionen inkl. Anzahl der Turniere auf dem Punkt. */
function markerOptions(component: TournamentMapComponent): { count?: number }[] {
  const options: { count?: number }[] = [];
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (component as any).markerLayer?.eachLayer((l: any) => options.push(l.options));
  return options;
}

/** Die gezeichneten Stile der Marker — Leaflet haelt sie in `options`. */
function markerStyles(
  component: TournamentMapComponent,
): { fillColor?: string; weight?: number; color?: string }[] {
  const styles: { fillColor?: string; weight?: number; color?: string }[] = [];
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (component as any).markerLayer?.eachLayer((l: any) => styles.push(l.options));
  return styles;
}
