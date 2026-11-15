import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import {
  VISITOR_DAYS, VISITOR_SURFACES, VISITOR_VIEWS, VisitorOriginsService, WorldMap, visitorStep,
} from './visitor-origins.service';

describe('VisitorOriginsService (Besucher-Herkunft)', () => {
  let service: VisitorOriginsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(VisitorOriginsService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('Oberflächen und Zeiträume sind wörtlich die des Servers (VisitorOriginRecorder.Surfaces, AllowedDays)', () => {
    expect([...VISITOR_SURFACES]).toEqual(['rookhub', 'leaguehub', 'kidhub', 'clubhub', 'turnier']);
    expect([...VISITOR_DAYS]).toEqual([7, 30, 90, 365]);
    expect([...VISITOR_VIEWS]).toEqual(['world', 'eu', 'na', 'sa', 'af', 'as', 'oc']);
  });

  it('get: ohne Auswahl keine Parameter, mit Auswahl days + surface', () => {
    service.get().subscribe();
    const plain = http.expectOne(r => r.url === '/api/visitor-origins');
    expect(plain.request.params.keys()).toEqual([]);
    plain.flush({});

    service.get(30, 'leaguehub').subscribe();
    const filtered = http.expectOne(r => r.url === '/api/visitor-origins');
    expect(filtered.request.params.get('days')).toBe('30');
    expect(filtered.request.params.get('surface')).toBe('leaguehub');
    filtered.flush({});
  });

  it('map: einmal geladen, danach aus dem Speicher; nach resetMap neu', () => {
    const got: unknown[] = [];
    service.map().subscribe(m => got.push(m));
    service.map().subscribe(m => got.push(m));
    http.expectOne('/assets/geo/world-countries.json').flush({ w: 960, h: 400, views: {}, countries: [] });
    expect(got.length).toBe(2);
    service.map().subscribe(m => got.push(m));
    expect(got.length).toBe(3);
    http.expectNone('/assets/geo/world-countries.json');

    service.resetMap();
    service.map().subscribe();
    http.expectOne('/assets/geo/world-countries.json').flush({});
  });

  it('visitorStep: fünf Stufen, logarithmisch gegen den größten Wert; 0 = kein Wert', () => {
    expect(visitorStep(0, 200)).toBe(0);
    expect(visitorStep(5, 0)).toBe(0);
    expect(visitorStep(200, 200)).toBe(5);
    expect(visitorStep(1, 1)).toBe(5);
    expect(visitorStep(1, 200)).toBe(1);
    // kleine Länder bleiben neben einem großen unterscheidbar: 3, 12 und 50 von 200 liegen in drei Stufen
    const steps = [3, 12, 50].map(n => visitorStep(n, 200));
    expect(new Set(steps).size).toBe(3);
    expect(steps).toEqual([...steps].sort((a, b) => a - b));
  });
});

/**
 * Das ausgelieferte Karten-Asset (`public/assets/geo/world-countries.json`, erzeugt von `tools/visitor-map/build_map.py`):
 * klein genug, jeder Ausschnitt da, jedes Land mit Kontinent und Pfad.
 */
describe('Karten-Asset world-countries.json', () => {
  let text = '';
  let map: WorldMap;

  beforeAll(async () => {
    for (const url of ['/assets/geo/world-countries.json', '/base/assets/geo/world-countries.json']) {
      const res = await fetch(url);
      if (res.ok) { text = await res.text(); break; }
    }
    map = JSON.parse(text) as WorldMap;
  });

  it('bleibt unter 300 KB', () => {
    expect(text.length).toBeGreaterThan(50_000);
    expect(text.length).toBeLessThan(300_000);
  });

  it('hat die Welt und alle sechs Kontinente als Ausschnitt innerhalb der Karte', () => {
    expect(Object.keys(map.views).sort()).toEqual([...VISITOR_VIEWS].sort());
    for (const view of VISITOR_VIEWS) {
      const [x, y, w, h] = map.views[view];
      expect(x).withContext(view).toBeGreaterThanOrEqual(0);
      expect(y).withContext(view).toBeGreaterThanOrEqual(0);
      expect(x + w).withContext(view).toBeLessThanOrEqual(map.w + 0.5);
      expect(y + h).withContext(view).toBeLessThanOrEqual(map.h + 0.5);
      expect(w * h).withContext(view).toBeGreaterThan(0);
    }
  });

  it('jedes Land: ISO-Kürzel einmalig, Kontinent bekannt, Pfad vorhanden', () => {
    const ids = map.countries.map(c => c.id);
    expect(new Set(ids).size).toBe(ids.length);
    expect(ids.length).toBeGreaterThan(190);
    for (const c of map.countries) {
      expect(c.id).toMatch(/^[A-Z]{2}$/);
      expect(VISITOR_VIEWS.slice(1) as readonly string[]).withContext(c.id).toContain(c.c);
      expect(c.d).withContext(c.id).toMatch(/^M[-\d.]+,[-\d.]+l/);
    }
  });

  it('feste Zuordnung der Länder auf zwei Kontinenten und die kleinen Länder Europas', () => {
    const continent = new Map(map.countries.map(c => [c.id, c.c]));
    expect(continent.get('RU')).toBe('eu');
    expect(continent.get('TR')).toBe('as');
    expect(continent.get('KZ')).toBe('as');
    expect(continent.get('CY')).toBe('eu');
    expect(continent.get('EG')).toBe('af');
    expect(continent.get('MX')).toBe('na');
    expect(continent.get('AU')).toBe('oc');
    for (const id of ['AT', 'CH', 'LI', 'LU', 'BE', 'NL', 'SI', 'HR', 'HU', 'DE']) {
      expect(continent.get(id)).withContext(id).toBe('eu');
    }
  });

  it('Österreich, Schweiz und Benelux bleiben im Europa-Ausschnitt erkennbar (genug Stützpunkte)', () => {
    const points = (id: string) => (map.countries.find(c => c.id === id)!.d.match(/-?\d+(\.\d+)?,-?\d+(\.\d+)?/g) ?? []).length;
    for (const id of ['AT', 'CH', 'BE', 'NL']) expect(points(id)).withContext(id).toBeGreaterThanOrEqual(12);
    expect(points('LU')).toBeGreaterThanOrEqual(4);
  });
});
