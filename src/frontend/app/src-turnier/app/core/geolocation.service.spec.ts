import { TestBed } from '@angular/core/testing';
import { GeolocationFailure, GeolocationService } from './geolocation.service';

/**
 * Die Huelle um `navigator.geolocation`. Der Grund, dass sie existiert, ist genau dieser Test:
 * mitten in einer Komponente laesst sich die Browser-API nicht ersetzen — `navigator.geolocation`
 * ist schreibgeschuetzt, und ein echter Aufruf im Testlauf oeffnet eine Berechtigungsabfrage, die
 * niemand beantwortet.
 */
describe('GeolocationService', () => {
  let service: GeolocationService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(GeolocationService);
  });

  function stub(behaviour: Partial<Geolocation>): void {
    spyOnProperty(navigator, 'geolocation', 'get').and.returnValue(behaviour as Geolocation);
  }

  it('liefert Koordinaten samt Genauigkeit', async () => {
    stub({
      getCurrentPosition: (success) => success({
        coords: { latitude: 47.27, longitude: 11.39, accuracy: 25 },
      } as GeolocationPosition),
    });

    const fix = await new Promise<{ lat: number; lon: number; accuracyM: number }>(
      (resolve, reject) => service.current().subscribe({ next: resolve, error: reject }));

    expect(fix).toEqual({ lat: 47.27, lon: 11.39, accuracyM: 25 });
  });

  /**
   * Die Zahlencodes des Browsers werden in benannte Faelle uebersetzt. Ohne das muesste jeder
   * Aufrufer sie selbst deuten — und „du hast das abgelehnt" braucht in der Oberflaeche eine
   * andere Antwort als „geht gerade nicht": im ersten Fall hilft nur die Browser-Einstellung.
   */
  it('übersetzt die Fehlercodes in benannte Fälle', async () => {
    // EIN Spion fuer alle Faelle: Jasmine laesst dieselbe Eigenschaft nicht zweimal ausspaehen,
    // und der Code wechselt zwischen den Durchlaeufen ueber die geschlossene Variable.
    let code = 0;
    stub({
      getCurrentPosition: (_success, error) =>
        error?.({ code, message: '' } as GeolocationPositionError),
    });

    const cases: [number, GeolocationFailure][] = [[1, 'denied'], [2, 'unavailable'], [3, 'timeout']];
    for (const [errorCode, expected] of cases) {
      code = errorCode;

      const failure = await new Promise<GeolocationFailure>(resolve =>
        service.current().subscribe({ error: resolve }));

      expect(failure).withContext(`Code ${errorCode}`).toBe(expected);
    }
  });

  it('meldet einen Browser ohne Standort-Unterstützung als solchen', async () => {
    spyOnProperty(navigator, 'geolocation', 'get').and.returnValue(undefined as unknown as Geolocation);

    expect(service.supported).toBeFalse();
    const failure = await new Promise<GeolocationFailure>(resolve =>
      service.current().subscribe({ error: resolve }));
    expect(failure).toBe('unsupported');
  });

  /**
   * Eine Minute alte Ortung ist fuer eine Umkreissuche ueber 100 km taufrisch und kommt sofort;
   * GPS-Genauigkeit kostet auf dem Handy Sekunden und Akku fuer einen Unterschied, den niemand
   * sieht.
   */
  it('fragt ohne GPS-Genauigkeit und nimmt eine gecachte Ortung an', () => {
    let options: PositionOptions | undefined;
    stub({
      getCurrentPosition: (_success, _error, opts) => { options = opts; },
    });

    service.current().subscribe({ error: () => { /* kommt nie */ } });

    expect(options?.enableHighAccuracy).toBeFalse();
    expect(options?.maximumAge).toBeGreaterThan(0);
    expect(options?.timeout).toBeGreaterThan(0);
  });
});
