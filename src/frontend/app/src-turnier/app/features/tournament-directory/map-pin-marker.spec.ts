import * as L from 'leaflet';
import { MapPinMarker } from './map-pin-marker';

/**
 * Die Pin-Form ist keine Dekoration: die SPITZE sitzt auf dem Ort, der klickbare Kopf steht
 * darueber. Zeichnet man nur die Form um und laesst die Trefferpruefung beim geerbten Kreis um
 * den Ankerpunkt, liegt der klickbare Bereich zur Haelfte unter dem Pin im Leeren — und der Kopf,
 * auf den jeder zielt, ragt oben heraus.
 */
describe('MapPinMarker', () => {
  let map: L.Map;
  let host: HTMLDivElement;

  beforeEach(() => {
    host = document.createElement('div');
    host.style.width = '400px';
    host.style.height = '400px';
    document.body.appendChild(host);
    map = L.map(host, { preferCanvas: true, center: [47.8, 13.04], zoom: 10 });
  });

  afterEach(() => {
    map.remove();
    host.remove();
  });

  function pin(radius = 7): MapPinMarker & { _point: L.Point; _containsPoint(p: L.Point): boolean } {
    const marker = new MapPinMarker([47.8, 13.04], { radius });
    marker.addTo(map);
    return marker as MapPinMarker & { _point: L.Point; _containsPoint(p: L.Point): boolean };
  }

  it('nimmt einen Klick auf den Kopf ÜBER dem Ort an', () => {
    const marker = pin();
    const anchor = marker._point;

    // Kopfmitte: 1,55 Kopfradien ueber der Spitze.
    expect(marker._containsPoint(anchor.subtract(L.point(0, 11)))).toBeTrue();
  });

  it('nimmt einen Klick auf die Spitze an', () => {
    const marker = pin();

    expect(marker._containsPoint(marker._point)).toBeTrue();
  });

  /** Unter der Spitze ist nichts mehr — dort liegt der geerbte Kreis, aber kein Pin. */
  it('weist einen Klick unter der Spitze ab', () => {
    const marker = pin();

    expect(marker._containsPoint(marker._point.add(L.point(0, 8)))).toBeFalse();
  });

  /** Der Schwanz laeuft zusammen: auf halber Hoehe ist er schmaler als der Kopf. */
  it('weist einen Klick neben dem schmalen Schwanz ab', () => {
    const marker = pin();
    const nearTip = marker._point.subtract(L.point(0, 2));

    expect(marker._containsPoint(nearTip.add(L.point(7, 0)))).toBeFalse();
  });

  /**
   * Die Ausdehnung fuer den Renderer muss nach OBEN ueber Kopf und Schwanz reichen. Bliebe sie
   * das geerbte Quadrat um den Anker, schnitte der Renderer den halben Pin weg, sobald er am Rand
   * des neu zu zeichnenden Bereichs liegt.
   */
  it('reicht mit ihrer Ausdehnung über den ganzen Pin', () => {
    const marker = pin();
    const internals = marker as unknown as { _point: L.Point; _pxBounds: L.Bounds };

    const top = internals._pxBounds.min!.y;
    const bottom = internals._pxBounds.max!.y;
    expect(internals._point.y - top).toBeGreaterThanOrEqual(MapPinMarker.heightAbove(7));
    // Nach unten nur bis zur Spitze plus Klick-Toleranz — nicht um einen ganzen Radius.
    expect(bottom - internals._point.y).toBeLessThan(7);
  });
});
