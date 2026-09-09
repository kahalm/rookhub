import * as L from 'leaflet';
import { MapPinMarker, countLabel, pinRadiusFor } from './map-pin-marker';

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
   * Auf Touch-Geraeten gibt die Karte dem Canvas-Renderer 8 px Toleranz (tournament-map): ein
   * Tipp knapp neben dem 14-px-Kopf soll den Pin treffen statt ins Leere zu gehen. Die Marke
   * muss diese Toleranz vom RENDERER uebernehmen — eine eigene Konstante wuesste nichts davon.
   */
  it('nimmt mit Renderer-Toleranz einen Tipp neben dem Kopf an', () => {
    map.remove();
    map = L.map(host, {
      preferCanvas: true, renderer: L.canvas({ tolerance: 8 }), center: [47.8, 13.04], zoom: 10,
    });
    const marker = pin();
    const head = marker._point.subtract(L.point(0, 11));

    expect(marker._containsPoint(head.add(L.point(12, 0)))).toBeTrue();
  });

  it('weist ohne Renderer-Toleranz denselben Tipp neben dem Kopf ab', () => {
    // Maus: praezise — und der Hover-Hinweis soll nicht 8 px neben dem Pin aufgehen.
    const marker = pin();
    const head = marker._point.subtract(L.point(0, 11));

    expect(marker._containsPoint(head.add(L.point(12, 0)))).toBeFalse();
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

  // ----- Die Anzahl im Kopf ---------------------------------------------------------

  /**
   * Zeichnet den Pin mit einem gefaelschten Renderer und gibt zurueck, was als TEXT in die
   * Leinwand ging. Der echte Canvas-Renderer zeichnet erst im naechsten Bildaufbau — hier geht es
   * um den Inhalt, nicht um den Zeitpunkt.
   */
  function drawnText(count: number): string[] {
    const marker = new MapPinMarker([47.8, 13.04], { radius: 7, count });
    marker.addTo(map);

    const drawn: string[] = [];
    const ctx = {
      beginPath: () => {}, arc: () => {}, lineTo: () => {}, closePath: () => {},
      save: () => {}, restore: () => {}, fillText: (t: string) => drawn.push(t),
    };
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const self = marker as any;
    const echt = self._renderer;
    self._renderer = {
      _drawing: true, _ctx: ctx, _fillStroke: () => {},
      _bounds: { intersects: () => true },
    };
    self._updatePath();
    // Zurueckgeben, sonst scheitert das Abraeumen der Karte am gefaelschten Renderer.
    self._renderer = echt;
    return drawn;
  }

  it('schreibt die Anzahl in den Kopf, sobald mehrere Turniere auf dem Punkt liegen', () => {
    // Ohne die Zahl sieht ein Punkt mit fünf Turnieren aus wie einer mit einem — und vier davon
    // sind unerreichbar, weil ihre Pins exakt darunter liegen.
    expect(drawnText(5)).toEqual(['5']);
  });

  it('lässt den Kopf eines einzelnen Turniers leer', () => {
    // „1" auf jedem Punkt wäre nur Rauschen.
    expect(drawnText(1)).toEqual([]);
  });

  it('lässt den Kopf mit der Stellenzahl wachsen', () => {
    // „111" in einem 14 px grossen Kopf wäre nicht zu lesen.
    expect(pinRadiusFor(7, 1)).toBe(7);
    expect(pinRadiusFor(7, 9)).toBeLessThan(pinRadiusFor(7, 99));
    expect(pinRadiusFor(7, 99)).toBeLessThan(pinRadiusFor(7, 111));
  });

  it('kürzt vierstellige Anzahlen ab, statt sie unlesbar zu quetschen', () => {
    expect(countLabel(999)).toBe('999');
    expect(countLabel(1000)).toBe('999+');
  });

  /**
   * Die Zahl muss auf dem Handy lesbar bleiben: mit dem alten Faktor stand „111" in 9 px und
   * „999+" in 8 px. Gemessen an der Schrift, die in die Leinwand geht — und sie darf den Kopf
   * trotzdem nicht ueberragen.
   */
  it('schreibt die Anzahl mindestens 11 px groß und trotzdem in den Kopf', () => {
    for (const count of [5, 12, 111, 1000]) {
      const radius = pinRadiusFor(7, count);
      const marker = new MapPinMarker([47.8, 13.04], { radius, count });
      const font = (marker as unknown as { labelFont: string }).labelFont;
      const px = Number(/(\d+)px/.exec(font)![1]);

      expect(px).withContext(`Anzahl ${count}`).toBeGreaterThanOrEqual(11);
      // ~0,6 em je fetter Ziffer.
      expect(px * 0.6 * countLabel(count).length).withContext(`Anzahl ${count}`)
        .toBeLessThanOrEqual(2 * radius);
    }
  });
});
